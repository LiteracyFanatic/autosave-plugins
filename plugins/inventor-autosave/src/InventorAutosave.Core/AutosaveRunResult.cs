using System.Collections.Generic;

namespace InventorAutosave.Core;

internal sealed class AutosaveRunResult
{
    public int SavedDocumentCount { get; set; }

    public List<string> DirtyDocumentsDetected { get; } = new();

    public List<string> SavedDocuments { get; } = new();

    public List<string> FailedDocuments { get; } = new();

    public List<string> SkippedUnsavedDocuments { get; } = new();

    public List<string> IgnoredDocuments { get; } = new();

    public List<string> DelayedDocuments { get; } = new();
}
