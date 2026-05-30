namespace InventorAutosave.Core;

internal static class AutosaveCompletionPolicy
{
    public static bool ShouldShowWarning(AutosaveRunResult result, bool warnAboutUnsavedFiles)
    {
        return result.FailedDocuments.Count > 0
            || (warnAboutUnsavedFiles && result.SkippedUnsavedDocuments.Count > 0)
            || result.DelayedDocuments.Count > 0;
    }
}
