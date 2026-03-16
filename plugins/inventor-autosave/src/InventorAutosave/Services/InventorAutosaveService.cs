using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using InventorAutosave.Core;
using InventorAutosave.Core.Logic;
using Inventor;
using Microsoft.Extensions.Logging;

namespace InventorAutosave.Services;

internal sealed class InventorAutosaveService : IDisposable
{
    private const string PluginCommandPrefix = "InventorAutosave.";

    private readonly Application _application;
    private readonly ILogger<InventorAutosaveService> _logger;
    private readonly Func<string, int, bool, EditEnvironmentPromptResult> _promptForEditEnvironmentSave;
    private readonly object _trackedDirtyDocumentsGate = new();
    private readonly HashSet<string> _trackedDirtyDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _deferredSaveUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileAccessEvents? _fileAccessEvents;

    public InventorAutosaveService(
        Application application,
        ILogger<InventorAutosaveService> logger,
        Func<string, int, bool, EditEnvironmentPromptResult> promptForEditEnvironmentSave)
    {
        _application = application;
        _logger = logger;
        _promptForEditEnvironmentSave = promptForEditEnvironmentSave;
        try
        {
            _fileAccessEvents = _application.FileAccessEvents;
            _fileAccessEvents.OnFileDirty += OnFileDirty;
            _logger.LogInformation("Subscribed to Inventor file dirty events.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to Inventor file dirty events.");
        }
    }

    public SnapshotRunResult RunSnapshot(
        string targetDirectory,
        bool includeHashDiffFromPreviousSnapshot,
        bool keepSnapshotDirectories,
        IEnumerable<string>? ignorePatterns,
        EditEnvironmentSaveBehavior editEnvironmentSaveBehavior,
        int deferredSaveMinutes,
        bool allowDelayDuringPrompt)
    {
        var normalizedTarget = PathUtilities.NormalizeDirectory(targetDirectory);
        var result = new SnapshotRunResult();
        var stopwatch = Stopwatch.StartNew();
        var effectiveIgnorePatterns = ignorePatterns?.ToArray() ?? Array.Empty<string>();
        var effectiveDeferredSaveMinutes = Math.Max(1, deferredSaveMinutes);

        try
        {
            _logger.LogInformation(
                "Starting snapshot run. TargetDirectory {TargetDirectory}. IncludeHashDiffFromPreviousSnapshot {IncludeHashDiffFromPreviousSnapshot}. KeepSnapshotDirectories {KeepSnapshotDirectories}. IgnorePatternCount {IgnorePatternCount}. EditEnvironmentSaveBehavior {EditEnvironmentSaveBehavior}. DeferredSaveMinutes {DeferredSaveMinutes}. AllowDelayDuringPrompt {AllowDelayDuringPrompt}.",
                normalizedTarget,
                includeHashDiffFromPreviousSnapshot,
                keepSnapshotDirectories,
                effectiveIgnorePatterns.Length,
                editEnvironmentSaveBehavior,
                effectiveDeferredSaveMinutes,
                allowDelayDuringPrompt);

            var openDocuments = ReadOpenDocuments();
            _logger.LogInformation("Discovered {OpenDocumentCount} open document(s) before snapshot.", openDocuments.Count);

            foreach (var openDocument in openDocuments)
            {
                var label = GetDocumentLabel(normalizedTarget, openDocument.Info);
                if (IsOutsideTargetDirectory(normalizedTarget, openDocument.Info))
                {
                    result.SkippedOutsideTargetDocuments.Add(label);
                    _logger.LogInformation(
                        "Skipping open document outside target directory {DocumentLabel}. FullPath {FullPath}.",
                        label,
                        openDocument.Info.FullPath);
                    continue;
                }

                var classification = OpenDocumentClassifier.Classify(openDocument.Info);
                _logger.LogDebug(
                    "Evaluated document {DocumentLabel}. FullPath {FullPath}. IsDirty {IsDirty}. IsTrackedDirty {IsTrackedDirty}. Disposition {Disposition}. ShouldCheckEditEnvironmentFirst {ShouldCheckEditEnvironmentFirst}.",
                    label,
                    openDocument.Info.FullPath,
                    openDocument.Info.IsDirty,
                    openDocument.Info.IsTrackedDirty,
                    classification.Disposition,
                    classification.ShouldCheckEditEnvironmentFirst);

                switch (classification.Disposition)
                {
                    case OpenDocumentDisposition.EligibleForSave:
                        if (classification.ShouldCheckEditEnvironmentFirst)
                        {
                            result.DirtyDocumentsDetected.Add(label);
                        }

                        AttemptSave(
                            openDocument.Document,
                            label,
                            classification.ShouldCheckEditEnvironmentFirst,
                            result,
                            editEnvironmentSaveBehavior,
                            effectiveDeferredSaveMinutes,
                            allowDelayDuringPrompt);
                        break;
                    case OpenDocumentDisposition.SkipUnsaved:
                        result.SkippedUnsavedDocuments.Add(openDocument.Info.DisplayName);
                        _logger.LogInformation("Skipping unsaved document {DocumentDisplayName}.", openDocument.Info.DisplayName);
                        break;
                }
            }

            var sourceFiles = EnumerateSourceFiles(normalizedTarget, effectiveIgnorePatterns);

            var snapshotTimestamp = DateTime.Now;
            var snapshotDirectory = SnapshotPathBuilder.BuildSnapshotDirectory(normalizedTarget, snapshotTimestamp);
            var snapshotArchivePath = SnapshotPathBuilder.BuildSnapshotArchivePath(normalizedTarget, snapshotTimestamp);
            Directory.CreateDirectory(snapshotDirectory);
            _logger.LogInformation(
                "Snapshot output prepared. SnapshotDirectory {SnapshotDirectory}. SnapshotArchivePath {SnapshotArchivePath}. SourceFileCount {SourceFileCount}.",
                snapshotDirectory,
                snapshotArchivePath,
                sourceFiles.Length);

            foreach (var sourceFile in sourceFiles)
            {
                var relativePath = PathUtilities.GetRelativePath(normalizedTarget, sourceFile);
                var destinationPath = System.IO.Path.Combine(snapshotDirectory, relativePath);
                var destinationDirectory = System.IO.Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                try
                {
                    System.IO.File.Copy(sourceFile, destinationPath, overwrite: true);
                    result.CopiedFileCount++;
                }
                catch (Exception ex)
                {
                    result.CopyFailures.Add($"{relativePath}: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to copy snapshot file {RelativePath}.", relativePath);
                }
            }

            result.SnapshotDirectory = snapshotDirectory;
            if (includeHashDiffFromPreviousSnapshot)
            {
                var diffResult = SnapshotHashDiffComparer.CompareWithPreviousSnapshot(
                    normalizedTarget,
                    snapshotDirectory,
                    effectiveIgnorePatterns);
                result.PreviousSnapshotPath = diffResult.PreviousSnapshotPath;
                result.FilesDifferentFromPreviousSnapshot.AddRange(diffResult.DifferingFiles);
                _logger.LogInformation(
                    "Snapshot hash diff completed. PreviousSnapshotPath {PreviousSnapshotPath}. DifferingFileCount {DifferingFileCount}.",
                    result.PreviousSnapshotPath,
                    result.FilesDifferentFromPreviousSnapshot.Count);
            }

            if (TryCreateSnapshotArchive(snapshotDirectory, snapshotArchivePath, result))
            {
                result.SnapshotArchivePath = snapshotArchivePath;

                if (!keepSnapshotDirectories && TryDeleteSnapshotDirectory(snapshotDirectory, result))
                {
                    result.SnapshotDirectory = string.Empty;
                }
            }

            _logger.LogInformation(
                "Snapshot run completed in {ElapsedMilliseconds} ms. SavedDocumentCount {SavedDocumentCount}. FailedDocumentCount {FailedDocumentCount}. SkippedOutsideTargetCount {SkippedOutsideTargetCount}. SkippedUnsavedCount {SkippedUnsavedCount}. DirtyDetectedCount {DirtyDetectedCount}. CopiedFileCount {CopiedFileCount}. CopyFailureCount {CopyFailureCount}. HashDifferenceCount {HashDifferenceCount}. SnapshotArchivePath {SnapshotArchivePath}. SnapshotDirectory {SnapshotDirectory}. PreviousSnapshotPath {PreviousSnapshotPath}.",
                stopwatch.ElapsedMilliseconds,
                result.SavedDocuments.Count,
                result.FailedDocuments.Count,
                result.SkippedOutsideTargetDocuments.Count,
                result.SkippedUnsavedDocuments.Count,
                result.DirtyDocumentsDetected.Count,
                result.CopiedFileCount,
                result.CopyFailures.Count,
                result.FilesDifferentFromPreviousSnapshot.Count,
                result.SnapshotArchivePath,
                result.SnapshotDirectory,
                result.PreviousSnapshotPath);

            return result;
        }
        catch (SnapshotCancelledException ex)
        {
            result.Cancelled = true;
            result.CancellationReason = ex.Message;
            _logger.LogInformation("Snapshot run cancelled. Reason: {CancellationReason}", ex.Message);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Snapshot run failed after {ElapsedMilliseconds} ms. TargetDirectory {TargetDirectory}.",
                stopwatch.ElapsedMilliseconds,
                normalizedTarget);
            throw;
        }
    }

    private List<(Document Document, OpenDocumentSnapshotInfo Info)> ReadOpenDocuments()
    {
        var results = new List<(Document Document, OpenDocumentSnapshotInfo Info)>();

        foreach (Document document in _application.Documents)
        {
            var info = new OpenDocumentSnapshotInfo
            {
                DisplayName = SafeGet(() => document.DisplayName, "Untitled"),
                FullPath = SafeGet(() => document.FullFileName, string.Empty),
                IsDirty = SafeGet(() => document.Dirty, false),
                IsTrackedDirty = IsTrackedDirty(SafeGet(() => document.FullFileName, string.Empty)),
            };

            results.Add((document, info));
        }

        return results;
    }

    private void AttemptSave(
        Document document,
        string label,
        bool shouldCheckEditEnvironmentFirst,
        SnapshotRunResult result,
        EditEnvironmentSaveBehavior editEnvironmentSaveBehavior,
        int deferredSaveMinutes,
        bool allowDelayDuringPrompt)
    {
        _logger.LogDebug(
            "Attempting to save document {DocumentLabel}. ShouldCheckEditEnvironmentFirst {ShouldCheckEditEnvironmentFirst}.",
            label,
            shouldCheckEditEnvironmentFirst);

        var trackedPath = NormalizeTrackedPath(SafeGet(() => document.FullFileName, string.Empty), documentObject: null);
        if (TryGetDeferredSaveUntilUtc(trackedPath, out var deferredUntilUtc))
        {
            result.DelayedDocuments.Add(label);
            _logger.LogInformation(
                "Skipping save for {DocumentLabel} because it is deferred until {DeferredUntilUtc}.",
                label,
                deferredUntilUtc);
            return;
        }

        if (shouldCheckEditEnvironmentFirst)
        {
            var needsPreparation = NeedsPreparationForSave(document, label);
            if (needsPreparation && editEnvironmentSaveBehavior == EditEnvironmentSaveBehavior.Prompt)
            {
                var promptResult = _promptForEditEnvironmentSave(label, deferredSaveMinutes, allowDelayDuringPrompt);
                switch (promptResult.Choice)
                {
                    case EditEnvironmentPromptChoice.Ignore:
                        result.IgnoredDocuments.Add(label);
                        _logger.LogInformation("User ignored save for document {DocumentLabel}.", label);
                        throw new SnapshotCancelledException(
                            $"Autosave cancelled. No snapshot was created because {label} was ignored.");
                    case EditEnvironmentPromptChoice.Delay:
                        if (!allowDelayDuringPrompt)
                        {
                            result.IgnoredDocuments.Add(label);
                            throw new SnapshotCancelledException(
                                "Autosave cancelled. Delay is only available while automatic autosaves are running.");
                        }

                        var effectiveDelayMinutes = Math.Max(1, promptResult.DelayMinutes);
                        SetDeferredSave(trackedPath, effectiveDelayMinutes);
                        result.DelayedDocuments.Add(label);
                        _logger.LogInformation(
                            "User delayed save for document {DocumentLabel} by {DelayMinutes} minute(s).",
                            label,
                            effectiveDelayMinutes);
                        throw new SnapshotCancelledException(
                            $"Autosave delayed for {label} by {effectiveDelayMinutes} minute(s). No snapshot was created.");
                }
            }

            PrepareDocumentForSave(document, label);
        }

        if (TrySilentSave(document, label, "initial"))
        {
            result.SavedDocumentCount++;
            result.SavedDocuments.Add(label);
            ClearTrackedDirty(document);
            ClearDeferredSave(trackedPath);
            _logger.LogInformation("Saved document {DocumentLabel} via initial silent save.", label);
            return;
        }

        if (!shouldCheckEditEnvironmentFirst || IsNonDefaultCommandActive(document))
        {
            PrepareDocumentForSave(document, label);
            if (TrySilentSave(document, label, "post-prepare"))
            {
                result.SavedDocumentCount++;
                result.SavedDocuments.Add(label);
                ClearTrackedDirty(document);
                ClearDeferredSave(trackedPath);
                _logger.LogInformation("Saved document {DocumentLabel} via silent save after preparation.", label);
                return;
            }
        }

        PrepareDocumentForSave(document, label);
        if (TryInteractiveSave(document, label))
        {
            result.SavedDocumentCount++;
            result.SavedDocuments.Add(label);
            ClearTrackedDirty(document);
            ClearDeferredSave(trackedPath);
            _logger.LogInformation("Saved document {DocumentLabel} via interactive save.", label);
            return;
        }

        result.FailedDocuments.Add(label);
        _logger.LogWarning("Failed to save document {DocumentLabel} after all save attempts.", label);
    }

    private bool TrySilentSave(Document document, string label, string phase)
    {
        var previousSilentState = false;
        var saveAttemptState = CaptureSaveAttemptState(document);

        try
        {
            previousSilentState = _application.SilentOperation;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read SilentOperation before saving {DocumentLabel}. Phase {Phase}.", label, phase);
        }

        try
        {
            _application.SilentOperation = true;
            document.Save2(false);
            var succeeded = DidSaveSucceed(document, saveAttemptState);
            _logger.LogDebug(
                "Silent save attempt finished for {DocumentLabel}. Phase {Phase}. Succeeded {Succeeded}.",
                label,
                phase,
                succeeded);
            return succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Silent save attempt threw for {DocumentLabel}. Phase {Phase}.", label, phase);
            return false;
        }
        finally
        {
            try
            {
                _application.SilentOperation = previousSilentState;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore SilentOperation after saving {DocumentLabel}. Phase {Phase}.", label, phase);
            }
        }
    }

    private bool TryInteractiveSave(Document document, string label)
    {
        var saveAttemptState = CaptureSaveAttemptState(document);

        try
        {
            document.Save();
            var succeeded = DidSaveSucceed(document, saveAttemptState);
            _logger.LogDebug("Interactive save attempt finished for {DocumentLabel}. Succeeded {Succeeded}.", label, succeeded);
            return succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Interactive save attempt failed for {DocumentLabel}.", label);
            return false;
        }
    }

    private void TryExitEditEnvironment(Document document, string label)
    {
        var editObject = TryGetEditObject(document, label);
        if (editObject == null)
        {
            return;
        }

        ExitEditOnObject(editObject, label);
    }

    private void PrepareDocumentForSave(Document document, string label)
    {
        _logger.LogDebug("Preparing document {DocumentLabel} for save.", label);
        TryExitEditEnvironment(document, label);

        if (IsNonDefaultCommandActive(document))
        {
            _logger.LogDebug("Stopping active command before saving {DocumentLabel}.", label);
            TryStopActiveCommand();
        }
    }

    private object? TryGetEditObject(Document document, string label)
    {
        try
        {
            var activeDocument = _application.ActiveDocument;
            if (ReferenceEquals(activeDocument, document))
            {
                var activeEditObject = _application.ActiveEditObject;
                if (activeEditObject != null)
                {
                    return activeEditObject;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not inspect ActiveEditObject for {DocumentLabel}.", label);
        }

        try
        {
            return document.ActivatedObject;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not inspect ActivatedObject for {DocumentLabel}.", label);
            return null;
        }
    }

    private bool NeedsPreparationForSave(Document document, string label)
    {
        return TryGetEditObject(document, label) != null || IsNonDefaultCommandActive(document);
    }

    private void ExitEditOnObject(object editObject, string label)
    {
        switch (editObject)
        {
            case Sketch sketch:
                sketch.ExitEdit();
                return;
            case PlanarSketch planarSketch:
                planarSketch.ExitEdit();
                return;
            case Sketch3D sketch3D:
                sketch3D.ExitEdit();
                return;
        }

        try
        {
            ((dynamic)editObject).ExitEdit();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not exit edit environment for {DocumentLabel}. EditObjectType {EditObjectType}.", label, editObject.GetType().FullName);
        }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_fileAccessEvents == null)
        {
            return;
        }

        try
        {
            _fileAccessEvents.OnFileDirty -= OnFileDirty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to unsubscribe from Inventor file dirty events.");
        }
    }

    private void OnFileDirty(
        string relativeFileName,
        string libraryName,
        ref byte[] customLogicalName,
        string fullFileName,
        _Document documentObject,
        EventTimingEnum beforeOrAfter,
        NameValueMap context,
        out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;

        if (beforeOrAfter != EventTimingEnum.kAfter)
        {
            return;
        }

        var trackedPath = NormalizeTrackedPath(fullFileName, documentObject);
        if (string.IsNullOrWhiteSpace(trackedPath))
        {
            return;
        }

        lock (_trackedDirtyDocumentsGate)
        {
            _trackedDirtyDocuments.Add(trackedPath);
        }

        _logger.LogDebug("Tracked dirty document: {TrackedPath}", trackedPath);
    }

    private bool IsTrackedDirty(string fullPath)
    {
        var trackedPath = NormalizeTrackedPath(fullPath, documentObject: null);
        if (string.IsNullOrWhiteSpace(trackedPath))
        {
            return false;
        }

        lock (_trackedDirtyDocumentsGate)
        {
            return _trackedDirtyDocuments.Contains(trackedPath);
        }
    }

    private void ClearTrackedDirty(Document document)
    {
        ClearTrackedDirty(SafeGet(() => document.FullFileName, string.Empty));
    }

    private void ClearTrackedDirty(string fullPath)
    {
        var trackedPath = NormalizeTrackedPath(fullPath, documentObject: null);
        if (string.IsNullOrWhiteSpace(trackedPath))
        {
            return;
        }

        lock (_trackedDirtyDocumentsGate)
        {
            _trackedDirtyDocuments.Remove(trackedPath);
        }
    }

    private bool TryGetDeferredSaveUntilUtc(string trackedPath, out DateTime deferredUntilUtc)
    {
        deferredUntilUtc = default;
        if (string.IsNullOrWhiteSpace(trackedPath))
        {
            return false;
        }

        lock (_trackedDirtyDocumentsGate)
        {
            if (!_deferredSaveUntilUtc.TryGetValue(trackedPath, out deferredUntilUtc))
            {
                return false;
            }

            if (DateTime.UtcNow >= deferredUntilUtc)
            {
                _deferredSaveUntilUtc.Remove(trackedPath);
                deferredUntilUtc = default;
                return false;
            }

            return true;
        }
    }

    private void SetDeferredSave(string trackedPath, int delayMinutes)
    {
        if (string.IsNullOrWhiteSpace(trackedPath))
        {
            return;
        }

        lock (_trackedDirtyDocumentsGate)
        {
            _deferredSaveUntilUtc[trackedPath] = DateTime.UtcNow.AddMinutes(Math.Max(1, delayMinutes));
        }
    }

    private void ClearDeferredSave(string trackedPath)
    {
        if (string.IsNullOrWhiteSpace(trackedPath))
        {
            return;
        }

        lock (_trackedDirtyDocumentsGate)
        {
            _deferredSaveUntilUtc.Remove(trackedPath);
        }
    }

    private bool IsNonDefaultCommandActive(Document document)
    {
        try
        {
            var activeDocument = _application.ActiveDocument;
            if (!ReferenceEquals(activeDocument, document))
            {
                return false;
            }

            var activeCommand = _application.CommandManager.ActiveCommand ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activeCommand)
                || activeCommand.StartsWith(PluginCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var defaultCommand = SafeGet(() => document.DefaultCommand, string.Empty);
            return string.IsNullOrWhiteSpace(defaultCommand)
                || !string.Equals(activeCommand, defaultCommand, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to inspect the active command state.");
            return false;
        }
    }

    private void TryStopActiveCommand()
    {
        try
        {
            _application.CommandManager.StopActiveCommand();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop the active Inventor command.");
        }
    }

    private SaveAttemptState CaptureSaveAttemptState(Document document)
    {
        var fullPath = SafeGet(() => document.FullFileName, string.Empty);
        return new SaveAttemptState(
            SafeGet(() => document.FileSaveCounter, -1),
            GetLastWriteTimeUtc(fullPath));
    }

    private bool DidSaveSucceed(Document document, SaveAttemptState saveAttemptState)
    {
        var currentCounter = SafeGet(() => document.FileSaveCounter, saveAttemptState.FileSaveCounter);
        if (saveAttemptState.FileSaveCounter >= 0 && currentCounter > saveAttemptState.FileSaveCounter)
        {
            return true;
        }

        var fullPath = SafeGet(() => document.FullFileName, string.Empty);
        var currentWriteTime = GetLastWriteTimeUtc(fullPath);
        if (saveAttemptState.LastWriteTimeUtc.HasValue
            && currentWriteTime.HasValue
            && currentWriteTime.Value > saveAttemptState.LastWriteTimeUtc.Value)
        {
            return true;
        }

        return !SafeGet(() => document.Dirty, true);
    }

    private static DateTime? GetLastWriteTimeUtc(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !System.IO.File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return System.IO.File.GetLastWriteTimeUtc(fullPath);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeTrackedPath(string fullFileName, _Document? documentObject)
    {
        var path = fullFileName;
        if (string.IsNullOrWhiteSpace(path) && documentObject != null)
        {
            path = SafeGet(() => documentObject.FullFileName, string.Empty);
        }

        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : PathUtilities.NormalizePath(path);
    }

    private static string GetDocumentLabel(string normalizedTarget, OpenDocumentSnapshotInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.FullPath)
            && PathUtilities.IsPathUnderDirectory(normalizedTarget, info.FullPath))
        {
            return PathUtilities.GetRelativePath(normalizedTarget, info.FullPath);
        }

        if (!string.IsNullOrWhiteSpace(info.FullPath))
        {
            return info.FullPath;
        }

        return info.DisplayName;
    }

    private static bool IsOutsideTargetDirectory(string normalizedTarget, OpenDocumentSnapshotInfo info)
    {
        return !string.IsNullOrWhiteSpace(info.FullPath)
            && !PathUtilities.IsPathUnderDirectory(normalizedTarget, info.FullPath);
    }

    private bool TryCreateSnapshotArchive(
        string snapshotDirectory,
        string snapshotArchivePath,
        SnapshotRunResult result)
    {
        try
        {
            if (System.IO.File.Exists(snapshotArchivePath))
            {
                System.IO.File.Delete(snapshotArchivePath);
            }

            ZipFile.CreateFromDirectory(
                snapshotDirectory,
                snapshotArchivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
            _logger.LogInformation("Created snapshot archive at {SnapshotArchivePath}.", snapshotArchivePath);
            return true;
        }
        catch (Exception ex)
        {
            result.CopyFailures.Add($"Archive creation failed: {ex.Message}");
            _logger.LogWarning(ex, "Failed to create snapshot archive at {SnapshotArchivePath}.", snapshotArchivePath);
            return false;
        }
    }

    private bool TryDeleteSnapshotDirectory(string snapshotDirectory, SnapshotRunResult result)
    {
        try
        {
            PrepareDirectoryForDeletion(snapshotDirectory);
            Directory.Delete(snapshotDirectory, recursive: true);
            _logger.LogDebug("Deleted temporary snapshot directory {SnapshotDirectory}.", snapshotDirectory);
            return true;
        }
        catch (Exception ex)
        {
            result.CopyFailures.Add($"Archive cleanup failed: {ex.Message}");
            _logger.LogWarning(ex, "Failed to delete temporary snapshot directory {SnapshotDirectory}.", snapshotDirectory);
            return false;
        }
    }

    private string[] EnumerateSourceFiles(string normalizedTarget, string[] effectiveIgnorePatterns)
    {
        var sourceFiles = new List<string>();
        var skippedSnapshotFiles = new List<string>();

        foreach (var sourceFile in Directory.EnumerateFiles(normalizedTarget, "*", SearchOption.AllDirectories))
        {
            var relativePath = PathUtilities.GetRelativePath(normalizedTarget, sourceFile);
            if (IsInsideSnapshotsRoot(relativePath))
            {
                skippedSnapshotFiles.Add(relativePath);
                continue;
            }

            if (!SnapshotFileFilter.ShouldCopyFile(normalizedTarget, sourceFile, effectiveIgnorePatterns))
            {
                continue;
            }

            sourceFiles.Add(sourceFile);
        }

        if (skippedSnapshotFiles.Count > 0)
        {
            _logger.LogInformation(
                "Excluded {SkippedSnapshotFileCount} file(s) from the snapshots directory before copying. Sample: {SkippedSnapshotFiles}.",
                skippedSnapshotFiles.Count,
                string.Join(" | ", skippedSnapshotFiles.Take(5)));
        }

        return sourceFiles.ToArray();
    }

    private static bool IsInsideSnapshotsRoot(string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').Trim();
        return normalizedRelativePath.Equals("snapshots", StringComparison.OrdinalIgnoreCase)
            || normalizedRelativePath.StartsWith("snapshots/", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrepareDirectoryForDeletion(string snapshotDirectory)
    {
        if (!Directory.Exists(snapshotDirectory))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(snapshotDirectory, "*", SearchOption.AllDirectories))
        {
            TrySetAttributesNormal(filePath);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(snapshotDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            TrySetAttributesNormal(directoryPath);
        }

        TrySetAttributesNormal(snapshotDirectory);
    }

    private static void TrySetAttributesNormal(string path)
    {
        try
        {
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        }
        catch
        {
        }
    }

    private readonly record struct SaveAttemptState(int FileSaveCounter, DateTime? LastWriteTimeUtc);

    private sealed class SnapshotCancelledException : Exception
    {
        public SnapshotCancelledException(string message)
            : base(message)
        {
        }
    }
}
