using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    private readonly object _trackedDirtyDocumentsGate = new();
    private readonly HashSet<string> _trackedDirtyDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileAccessEvents? _fileAccessEvents;

    public InventorAutosaveService(Application application, ILogger<InventorAutosaveService> logger)
    {
        _application = application;
        _logger = logger;
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
        bool keepSnapshotDirectories)
    {
        var normalizedTarget = PathUtilities.NormalizeDirectory(targetDirectory);
        var result = new SnapshotRunResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Starting snapshot run. TargetDirectory {TargetDirectory}. IncludeHashDiffFromPreviousSnapshot {IncludeHashDiffFromPreviousSnapshot}. KeepSnapshotDirectories {KeepSnapshotDirectories}.",
                normalizedTarget,
                includeHashDiffFromPreviousSnapshot,
                keepSnapshotDirectories);

            var openDocuments = ReadOpenDocuments();
            _logger.LogInformation("Discovered {OpenDocumentCount} open document(s) before snapshot.", openDocuments.Count);

            foreach (var openDocument in openDocuments)
            {
                var label = GetDocumentLabel(normalizedTarget, openDocument.Info);
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

                        AttemptSave(openDocument.Document, label, classification.ShouldCheckEditEnvironmentFirst, result);
                        break;
                    case OpenDocumentDisposition.SkipUnsaved:
                        result.SkippedUnsavedDocuments.Add(openDocument.Info.DisplayName);
                        _logger.LogInformation("Skipping unsaved document {DocumentDisplayName}.", openDocument.Info.DisplayName);
                        break;
                }
            }

            var snapshotTimestamp = DateTime.Now;
            var snapshotDirectory = SnapshotPathBuilder.BuildSnapshotDirectory(normalizedTarget, snapshotTimestamp);
            var snapshotArchivePath = SnapshotPathBuilder.BuildSnapshotArchivePath(normalizedTarget, snapshotTimestamp);
            Directory.CreateDirectory(snapshotDirectory);
            _logger.LogInformation(
                "Snapshot output prepared. SnapshotDirectory {SnapshotDirectory}. SnapshotArchivePath {SnapshotArchivePath}.",
                snapshotDirectory,
                snapshotArchivePath);

            foreach (var sourceFile in Directory.EnumerateFiles(normalizedTarget, "*", SearchOption.AllDirectories))
            {
                if (!SnapshotFileFilter.ShouldCopyFile(normalizedTarget, sourceFile))
                {
                    continue;
                }

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
                var diffResult = SnapshotHashDiffComparer.CompareWithPreviousSnapshot(normalizedTarget, snapshotDirectory);
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
                "Snapshot run completed in {ElapsedMilliseconds} ms. SavedDocumentCount {SavedDocumentCount}. FailedDocumentCount {FailedDocumentCount}. SkippedUnsavedCount {SkippedUnsavedCount}. DirtyDetectedCount {DirtyDetectedCount}. CopiedFileCount {CopiedFileCount}. CopyFailureCount {CopyFailureCount}. HashDifferenceCount {HashDifferenceCount}. SnapshotArchivePath {SnapshotArchivePath}. SnapshotDirectory {SnapshotDirectory}. PreviousSnapshotPath {PreviousSnapshotPath}.",
                stopwatch.ElapsedMilliseconds,
                result.SavedDocuments.Count,
                result.FailedDocuments.Count,
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
        SnapshotRunResult result)
    {
        _logger.LogDebug(
            "Attempting to save document {DocumentLabel}. ShouldCheckEditEnvironmentFirst {ShouldCheckEditEnvironmentFirst}.",
            label,
            shouldCheckEditEnvironmentFirst);

        if (shouldCheckEditEnvironmentFirst)
        {
            PrepareDocumentForSave(document, label);
        }

        if (TrySilentSave(document, label, "initial"))
        {
            result.SavedDocumentCount++;
            result.SavedDocuments.Add(label);
            ClearTrackedDirty(document);
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

        return info.DisplayName;
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

    private readonly record struct SaveAttemptState(int FileSaveCounter, DateTime? LastWriteTimeUtc);
}
