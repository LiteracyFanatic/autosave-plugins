namespace InventorAutosave.Core.Logic;

internal enum OpenDocumentDisposition
{
    EligibleForSave,
    SkipClean,
    SkipUnsaved,
}

internal sealed class OpenDocumentAutosaveInfo
{
    public string DisplayName { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public bool IsDirty { get; init; }

    public bool IsTrackedDirty { get; init; }
}

internal sealed class OpenDocumentClassification
{
    public OpenDocumentDisposition Disposition { get; init; }

    public bool ShouldCheckEditEnvironmentFirst { get; init; }
}

internal static class OpenDocumentClassifier
{
    public static OpenDocumentClassification Classify(OpenDocumentAutosaveInfo document)
    {
        var shouldCheckEditEnvironmentFirst = document.IsDirty || document.IsTrackedDirty;

        if (!shouldCheckEditEnvironmentFirst)
        {
            return new OpenDocumentClassification { Disposition = OpenDocumentDisposition.SkipClean };
        }

        if (string.IsNullOrWhiteSpace(document.FullPath))
        {
            return new OpenDocumentClassification { Disposition = OpenDocumentDisposition.SkipUnsaved };
        }

        return new OpenDocumentClassification
        {
            Disposition = OpenDocumentDisposition.EligibleForSave,
            ShouldCheckEditEnvironmentFirst = shouldCheckEditEnvironmentFirst,
        };
    }
}
