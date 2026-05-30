using InventorAutosave.Core;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class AutosaveCompletionPolicyTests
{
    [Fact]
    public void ShouldShowWarning_ReturnsFalseForIgnoredDocumentsOnly()
    {
        var result = new AutosaveRunResult();
        result.IgnoredDocuments.Add(@"Z:\src\cad\mini-fridge\Mini Fridge - Use Case Rendering_final\Rug.ipt");

        Assert.False(AutosaveCompletionPolicy.ShouldShowWarning(result, warnAboutUnsavedFiles: true));
    }

    [Fact]
    public void ShouldShowWarning_ReturnsTrueForFailedDocuments()
    {
        var result = new AutosaveRunResult();
        result.FailedDocuments.Add("part.ipt");

        Assert.True(AutosaveCompletionPolicy.ShouldShowWarning(result, warnAboutUnsavedFiles: true));
    }

    [Fact]
    public void ShouldShowWarning_HonorsUnsavedFileWarningSetting()
    {
        var result = new AutosaveRunResult();
        result.SkippedUnsavedDocuments.Add("Unsaved1");

        Assert.True(AutosaveCompletionPolicy.ShouldShowWarning(result, warnAboutUnsavedFiles: true));
        Assert.False(AutosaveCompletionPolicy.ShouldShowWarning(result, warnAboutUnsavedFiles: false));
    }
}
