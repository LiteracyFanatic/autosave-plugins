using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class OpenDocumentClassifierTests
{
    [Fact]
    public void Classify_ReturnsSkipUnsavedForDirtyUnsavedDocument()
    {
        var classification = OpenDocumentClassifier.Classify(
            new OpenDocumentSnapshotInfo
            {
                DisplayName = "Unsaved Part",
                FullPath = string.Empty,
                IsDirty = true,
            });

        Assert.Equal(OpenDocumentDisposition.SkipUnsaved, classification.Disposition);
    }

    [Fact]
    public void Classify_ReturnsEligibleForSaveForTrackedDirtySavedDocument()
    {
        var classification = OpenDocumentClassifier.Classify(
            new OpenDocumentSnapshotInfo
            {
                DisplayName = "vice.iam",
                FullPath = @"C:\work\vice\vice.iam",
                IsDirty = false,
                IsTrackedDirty = true,
            });

        Assert.Equal(OpenDocumentDisposition.EligibleForSave, classification.Disposition);
        Assert.True(classification.ShouldCheckEditEnvironmentFirst);
    }

    [Fact]
    public void Classify_ReturnsEligibleForSaveForSavedDirtyTargetDocument()
    {
        var classification = OpenDocumentClassifier.Classify(
            new OpenDocumentSnapshotInfo
            {
                DisplayName = "vice.iam",
                FullPath = @"C:\work\vice\vice.iam",
                IsDirty = true,
                IsTrackedDirty = false,
            });

        Assert.Equal(OpenDocumentDisposition.EligibleForSave, classification.Disposition);
        Assert.True(classification.ShouldCheckEditEnvironmentFirst);
    }

    [Fact]
    public void Classify_ReturnsSkipCleanForSavedCleanTargetDocument()
    {
        var classification = OpenDocumentClassifier.Classify(
            new OpenDocumentSnapshotInfo
            {
                DisplayName = "vice.iam",
                FullPath = @"C:\work\vice\vice.iam",
                IsDirty = false,
                IsTrackedDirty = false,
            });

        Assert.Equal(OpenDocumentDisposition.SkipClean, classification.Disposition);
        Assert.False(classification.ShouldCheckEditEnvironmentFirst);
    }

    [Fact]
    public void Classify_ReturnsEligibleForSaveForSavedDocumentOutsideTargetDirectory()
    {
        var classification = OpenDocumentClassifier.Classify(
            new OpenDocumentSnapshotInfo
            {
                DisplayName = "vice.iam",
                FullPath = @"\\server\share\vice\vice.iam",
                IsDirty = true,
                IsTrackedDirty = false,
            });

        Assert.Equal(OpenDocumentDisposition.EligibleForSave, classification.Disposition);
        Assert.True(classification.ShouldCheckEditEnvironmentFirst);
    }

    [Fact]
    public void Classify_ReturnsEligibleForSaveForDirtySavedSnapshotDocument()
    {
        var classification = OpenDocumentClassifier.Classify(
            new OpenDocumentSnapshotInfo
            {
                DisplayName = "vice.iam",
                FullPath = @"C:\work\vice\snapshots\2026-03-11T09-04-05\vice.iam",
                IsDirty = true,
                IsTrackedDirty = false,
            });

        Assert.Equal(OpenDocumentDisposition.EligibleForSave, classification.Disposition);
        Assert.True(classification.ShouldCheckEditEnvironmentFirst);
    }
}
