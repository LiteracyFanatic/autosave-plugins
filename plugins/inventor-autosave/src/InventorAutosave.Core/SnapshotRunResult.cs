using System.Collections.Generic;

namespace InventorAutosave.Core;

internal sealed class SnapshotRunResult
{
    public string SnapshotDirectory { get; set; } = string.Empty;

    public string SnapshotArchivePath { get; set; } = string.Empty;

    public string PreviousSnapshotPath { get; set; } = string.Empty;

    public int SavedDocumentCount { get; set; }

    public int CopiedFileCount { get; set; }

    public List<string> DirtyDocumentsDetected { get; } = new();

    public List<string> SavedDocuments { get; } = new();

    public List<string> FailedDocuments { get; } = new();

    public List<string> SkippedUnsavedDocuments { get; } = new();

    public List<string> CopyFailures { get; } = new();

    public List<string> FilesDifferentFromPreviousSnapshot { get; } = new();
}
