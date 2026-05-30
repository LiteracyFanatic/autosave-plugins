using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class ActiveInventorEnvironmentClassifierTests
{
    [Fact]
    public void IsPromptRequired_RecognizesFeaEnvironmentInternalName()
    {
        Assert.True(ActiveInventorEnvironmentClassifier.IsPromptRequired(
            "Analysis",
            "FEA Environment Internal Name"));
    }

    [Fact]
    public void IsPromptRequired_RecognizesInventorStudioDisplayName()
    {
        Assert.True(ActiveInventorEnvironmentClassifier.IsPromptRequired(
            "Inventor Studio",
            string.Empty));
    }

    [Fact]
    public void IsPromptRequired_DoesNotTreatDefaultPartEnvironmentAsPromptRequired()
    {
        Assert.False(ActiveInventorEnvironmentClassifier.IsPromptRequired(
            "Part",
            "PMxPartEnvironment"));
    }
}
