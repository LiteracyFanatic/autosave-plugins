using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using InventorAutosave.Core;
using InventorAutosave.Core.Logic;
using Inventor;

namespace InventorAutosave.Services;

internal sealed class InventorAutosaveService : IDisposable
{
    private const string PluginCommandPrefix = "InventorAutosave.";

    private readonly Application _application;
    private readonly object _trackedDirtyDocumentsGate = new();
    private readonly HashSet<string> _trackedDirtyDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileAccessEvents? _fileAccessEvents;

    public InventorAutosaveService(Application application)
    {
        _application = application;
        try
        {
            _fileAccessEvents = _application.FileAccessEvents;
            _fileAccessEvents.OnFileDirty += OnFileDirty;
        }
        catch
        {
        }
    }

    public SnapshotRunResult RunSnapshot(
        string targetDirectory,
        bool includeHashDiffFromPreviousSnapshot,
        bool keepSnapshotDirectories)
    {
        var normalizedTarget = PathUtilities.NormalizeDirectory(targetDirectory);
        var result = new SnapshotRunResult();

        var openDocuments = ReadOpenDocuments();
        foreach (var openDocument in openDocuments)
        {
            var classification = OpenDocumentClassifier.Classify(openDocument.Info);
            switch (classification.Disposition)
            {
                case OpenDocumentDisposition.EligibleForSave:
                    var label = GetDocumentLabel(normalizedTarget, openDocument.Info);
                    if (classification.ShouldCheckEditEnvironmentFirst)
                    {
                        result.DirtyDocumentsDetected.Add(label);
                    }

                    AttemptSave(openDocument.Document, label, classification.ShouldCheckEditEnvironmentFirst, result);
                    break;
                case OpenDocumentDisposition.SkipUnsaved:
                    result.SkippedUnsavedDocuments.Add(openDocument.Info.DisplayName);
                    break;
            }
        }

        var snapshotTimestamp = DateTime.Now;
        var snapshotDirectory = SnapshotPathBuilder.BuildSnapshotDirectory(normalizedTarget, snapshotTimestamp);
        var snapshotArchivePath = SnapshotPathBuilder.BuildSnapshotArchivePath(normalizedTarget, snapshotTimestamp);
        Directory.CreateDirectory(snapshotDirectory);

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
            }
        }

        result.SnapshotDirectory = snapshotDirectory;
        if (includeHashDiffFromPreviousSnapshot)
        {
            var diffResult = SnapshotHashDiffComparer.CompareWithPreviousSnapshot(normalizedTarget, snapshotDirectory);
            result.PreviousSnapshotPath = diffResult.PreviousSnapshotPath;
            result.FilesDifferentFromPreviousSnapshot.AddRange(diffResult.DifferingFiles);
        }

        if (TryCreateSnapshotArchive(snapshotDirectory, snapshotArchivePath, result))
        {
            result.SnapshotArchivePath = snapshotArchivePath;

            if (!keepSnapshotDirectories && TryDeleteSnapshotDirectory(snapshotDirectory, result))
            {
                result.SnapshotDirectory = string.Empty;
            }
        }

        return result;
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
        if (shouldCheckEditEnvironmentFirst)
        {
            PrepareDocumentForSave(document);
        }

        if (TrySilentSave(document))
        {
            result.SavedDocumentCount++;
            result.SavedDocuments.Add(label);
            ClearTrackedDirty(document);
            return;
        }

        if (!shouldCheckEditEnvironmentFirst || IsNonDefaultCommandActive(document))
        {
            PrepareDocumentForSave(document);
            if (TrySilentSave(document))
            {
                result.SavedDocumentCount++;
                result.SavedDocuments.Add(label);
                ClearTrackedDirty(document);
                return;
            }
        }

        PrepareDocumentForSave(document);
        if (TryInteractiveSave(document))
        {
            result.SavedDocumentCount++;
            result.SavedDocuments.Add(label);
            ClearTrackedDirty(document);
            return;
        }

        result.FailedDocuments.Add(label);
    }

    private bool TrySilentSave(Document document)
    {
        var previousSilentState = false;
        var saveAttemptState = CaptureSaveAttemptState(document);

        try
        {
            previousSilentState = _application.SilentOperation;
        }
        catch
        {
        }

        try
        {
            _application.SilentOperation = true;
            document.Save2(false);
            return DidSaveSucceed(document, saveAttemptState);
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                _application.SilentOperation = previousSilentState;
            }
            catch
            {
            }
        }
    }

    private bool TryInteractiveSave(Document document)
    {
        var saveAttemptState = CaptureSaveAttemptState(document);

        try
        {
            document.Save();
            return DidSaveSucceed(document, saveAttemptState);
        }
        catch
        {
            return false;
        }
    }

    private void TryExitEditEnvironment(Document document)
    {
        var editObject = TryGetEditObject(document);
        if (editObject == null)
        {
            return;
        }

        ExitEditOnObject(editObject);
    }

    private void PrepareDocumentForSave(Document document)
    {
        TryExitEditEnvironment(document);

        if (IsNonDefaultCommandActive(document))
        {
            TryStopActiveCommand();
        }
    }

    private object? TryGetEditObject(Document document)
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
        catch
        {
        }

        try
        {
            return document.ActivatedObject;
        }
        catch
        {
            return null;
        }
    }

    private static void ExitEditOnObject(object editObject)
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
        catch
        {
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
        catch
        {
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
        catch
        {
            return false;
        }
    }

    private void TryStopActiveCommand()
    {
        try
        {
            _application.CommandManager.StopActiveCommand();
        }
        catch
        {
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

    private static bool TryCreateSnapshotArchive(
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
            return true;
        }
        catch (Exception ex)
        {
            result.CopyFailures.Add($"Archive creation failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryDeleteSnapshotDirectory(string snapshotDirectory, SnapshotRunResult result)
    {
        try
        {
            Directory.Delete(snapshotDirectory, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            result.CopyFailures.Add($"Archive cleanup failed: {ex.Message}");
            return false;
        }
    }

    private readonly record struct SaveAttemptState(int FileSaveCounter, DateTime? LastWriteTimeUtc);
}
