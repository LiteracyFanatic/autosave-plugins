using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private readonly Func<string, int, AutosavePromptChoice> _promptForModalSave;
    private readonly object _trackedDirtyDocumentsGate = new();
    private readonly HashSet<string> _trackedDirtyDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _deferredSaveUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileAccessEvents? _fileAccessEvents;

    public InventorAutosaveService(
        Application application,
        ILogger<InventorAutosaveService> logger,
        Func<string, int, AutosavePromptChoice> promptForModalSave)
    {
        _application = application;
        _logger = logger;
        _promptForModalSave = promptForModalSave;
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

    public AutosaveRunResult RunAutosave(int deferredSaveMinutes)
    {
        var result = new AutosaveRunResult();
        var stopwatch = Stopwatch.StartNew();
        var effectiveDeferredSaveMinutes = Math.Max(1, deferredSaveMinutes);

        try
        {
            _logger.LogInformation(
                "Starting autosave run. DeferredSaveMinutes {DeferredSaveMinutes}.",
                effectiveDeferredSaveMinutes);

            var openDocuments = ReadOpenDocuments();
            _logger.LogInformation("Discovered {OpenDocumentCount} open document(s) before autosave.", openDocuments.Count);

            foreach (var openDocument in openDocuments)
            {
                var label = GetDocumentLabel(openDocument.Info);
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
                        result.DirtyDocumentsDetected.Add(label);
                        AttemptSave(
                            openDocument.Document,
                            label,
                            classification.ShouldCheckEditEnvironmentFirst,
                            result,
                            effectiveDeferredSaveMinutes);
                        break;
                    case OpenDocumentDisposition.SkipUnsaved:
                        result.SkippedUnsavedDocuments.Add(openDocument.Info.DisplayName);
                        _logger.LogInformation("Skipping unsaved document {DocumentDisplayName}.", openDocument.Info.DisplayName);
                        break;
                }
            }

            _logger.LogInformation(
                "Autosave run completed in {ElapsedMilliseconds} ms. DirtyDetectedCount {DirtyDetectedCount}. SavedDocumentCount {SavedDocumentCount}. FailedDocumentCount {FailedDocumentCount}. SkippedUnsavedCount {SkippedUnsavedCount}. IgnoredDocumentCount {IgnoredDocumentCount}. DelayedDocumentCount {DelayedDocumentCount}.",
                stopwatch.ElapsedMilliseconds,
                result.DirtyDocumentsDetected.Count,
                result.SavedDocuments.Count,
                result.FailedDocuments.Count,
                result.SkippedUnsavedDocuments.Count,
                result.IgnoredDocuments.Count,
                result.DelayedDocuments.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Autosave run failed after {ElapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private List<(Document Document, OpenDocumentAutosaveInfo Info)> ReadOpenDocuments()
    {
        var results = new List<(Document Document, OpenDocumentAutosaveInfo Info)>();

        foreach (Document document in _application.Documents)
        {
            var info = new OpenDocumentAutosaveInfo
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
        AutosaveRunResult result,
        int deferredSaveMinutes)
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

        if (!TryPrepareModalCommandForSave(
                document,
                label,
                shouldCheckEditEnvironmentFirst,
                result,
                trackedPath,
                deferredSaveMinutes))
        {
            return;
        }

        if (TrySilentSave(document, label, "initial"))
        {
            CompleteSuccessfulSave(document, label, trackedPath, result, "initial silent save");
            return;
        }

        if (!TryPrepareModalCommandForSave(
                document,
                label,
                shouldCheckEditEnvironmentFirst,
                result,
                trackedPath,
                deferredSaveMinutes))
        {
            return;
        }

        if (TrySilentSave(document, label, "post-prompt"))
        {
            CompleteSuccessfulSave(document, label, trackedPath, result, "silent save after modal prompt");
            return;
        }

        result.FailedDocuments.Add(label);
        _logger.LogWarning("Failed to save document {DocumentLabel} after all save attempts.", label);
    }

    private bool TryPrepareModalCommandForSave(
        Document document,
        string label,
        bool shouldCheckEditEnvironmentFirst,
        AutosaveRunResult result,
        string trackedPath,
        int deferredSaveMinutes)
    {
        if (!shouldCheckEditEnvironmentFirst || !NeedsPreparationForSave(document, label))
        {
            return true;
        }

        AutosavePromptDecision decision;
        try
        {
            var choice = _promptForModalSave(label, deferredSaveMinutes);
            decision = AutosavePromptPolicy.Resolve(choice, deferredSaveMinutes);
        }
        catch (Exception ex)
        {
            decision = AutosavePromptPolicy.Resolve(AutosavePromptChoice.Delay, deferredSaveMinutes);
            _logger.LogWarning(
                ex,
                "Could not show autosave prompt for {DocumentLabel}. ActiveCommand {ActiveCommand}. ActiveEnvironment {ActiveEnvironment}. The document will be delayed instead of interrupting the active command.",
                label,
                GetActiveCommandName(),
                GetActiveEnvironmentDescription());
        }

        switch (decision.Disposition)
        {
            case AutosavePromptDisposition.SkipDocument:
                result.IgnoredDocuments.Add(label);
                _logger.LogInformation("User ignored autosave for document {DocumentLabel}.", label);
                return false;
            case AutosavePromptDisposition.DelayDocument:
                SetDeferredSave(trackedPath, decision.DelayMinutes);
                result.DelayedDocuments.Add(label);
                _logger.LogInformation(
                    "User delayed autosave for document {DocumentLabel} by {DelayMinutes} minute(s).",
                    label,
                    decision.DelayMinutes);
                return false;
            default:
                PrepareDocumentForSave(document, label);
                return true;
        }
    }

    private void CompleteSuccessfulSave(
        Document document,
        string label,
        string trackedPath,
        AutosaveRunResult result,
        string phase)
    {
        result.SavedDocumentCount++;
        result.SavedDocuments.Add(label);
        ClearTrackedDirty(document);
        ClearDeferredSave(trackedPath);
        _logger.LogInformation("Saved document {DocumentLabel} via {Phase}.", label, phase);
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
        _logger.LogDebug(
            "Preparing document {DocumentLabel} for save. ActiveCommand {ActiveCommand}. ActiveEnvironment {ActiveEnvironment}.",
            label,
            GetActiveCommandName(),
            GetActiveEnvironmentDescription());
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
                if (activeEditObject != null && !IsDocumentObject(activeEditObject, document))
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
            var activatedObject = document.ActivatedObject;
            return activatedObject != null && !IsDocumentObject(activatedObject, document)
                ? activatedObject
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not inspect ActivatedObject for {DocumentLabel}.", label);
            return null;
        }
    }

    private static bool IsDocumentObject(object candidate, Document document)
    {
        var documentPath = SafeGet(() => document.FullFileName, string.Empty);
        var candidatePath = SafeGet(() => Convert.ToString(((dynamic)candidate).FullFileName) ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(documentPath)
            && !string.IsNullOrWhiteSpace(candidatePath)
            && string.Equals(
                PathUtilities.NormalizePath(documentPath),
                PathUtilities.NormalizePath(candidatePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var documentDisplayName = SafeGet(() => document.DisplayName, string.Empty);
        var candidateDisplayName = SafeGet(() => Convert.ToString(((dynamic)candidate).DisplayName) ?? string.Empty, string.Empty);
        var documentObjectType = SafeGet(() => Convert.ToInt32(document.Type), int.MinValue);
        var candidateObjectType = SafeGet(() => Convert.ToInt32(((dynamic)candidate).Type), int.MaxValue);

        return documentObjectType == candidateObjectType
            && !string.IsNullOrWhiteSpace(documentDisplayName)
            && string.Equals(documentDisplayName, candidateDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private bool NeedsPreparationForSave(Document document, string label)
    {
        return TryGetEditObject(document, label) != null
            || IsPromptRequiredEnvironmentActive(document, label)
            || IsNonDefaultCommandActive(document);
    }

    private bool IsPromptRequiredEnvironmentActive(Document document, string label)
    {
        try
        {
            var activeDocument = _application.ActiveDocument;
            if (!ReferenceEquals(activeDocument, document))
            {
                return false;
            }

            var activeEnvironment = _application.UserInterfaceManager.ActiveEnvironment;
            var displayName = SafeGet(() => activeEnvironment.DisplayName, string.Empty);
            var internalName = SafeGet(() => activeEnvironment.InternalName, string.Empty);
            var isPromptRequired = ActiveInventorEnvironmentClassifier.IsPromptRequired(displayName, internalName);
            if (isPromptRequired)
            {
                _logger.LogDebug(
                    "Prompt-required Inventor environment active for {DocumentLabel}. ActiveEnvironment {ActiveEnvironment}.",
                    label,
                    FormatEnvironment(displayName, internalName));
            }

            return isPromptRequired;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not inspect active Inventor environment for {DocumentLabel}.", label);
            return false;
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

    private string GetActiveCommandName()
    {
        try
        {
            return _application.CommandManager.ActiveCommand ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetActiveEnvironmentDescription()
    {
        try
        {
            var activeEnvironment = _application.UserInterfaceManager.ActiveEnvironment;
            var displayName = SafeGet(() => activeEnvironment.DisplayName, string.Empty);
            var internalName = SafeGet(() => activeEnvironment.InternalName, string.Empty);
            return FormatEnvironment(displayName, internalName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatEnvironment(string displayName, string internalName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return internalName;
        }

        if (string.IsNullOrWhiteSpace(internalName))
        {
            return displayName;
        }

        return $"{displayName} ({internalName})";
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

    private static string GetDocumentLabel(OpenDocumentAutosaveInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.FullPath))
        {
            return info.FullPath;
        }

        return info.DisplayName;
    }

    private readonly record struct SaveAttemptState(int FileSaveCounter, DateTime? LastWriteTimeUtc);
}
