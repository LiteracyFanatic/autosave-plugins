using InventorAutosave.Core;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class AutosavePromptPolicyTests
{
    [Fact]
    public void Resolve_ReturnsSaveNowForSaveChoice()
    {
        var decision = AutosavePromptPolicy.Resolve(AutosavePromptChoice.SaveNow, 3);

        Assert.Equal(AutosavePromptDisposition.SaveNow, decision.Disposition);
        Assert.Equal(3, decision.DelayMinutes);
    }

    [Fact]
    public void Resolve_ReturnsPerDocumentSkipForIgnoreChoice()
    {
        var decision = AutosavePromptPolicy.Resolve(AutosavePromptChoice.Ignore, 3);

        Assert.Equal(AutosavePromptDisposition.SkipDocument, decision.Disposition);
        Assert.NotEqual(AutosavePromptDisposition.DelayDocument, decision.Disposition);
    }

    [Fact]
    public void Resolve_ReturnsPerDocumentDelayForDelayChoice()
    {
        var decision = AutosavePromptPolicy.Resolve(AutosavePromptChoice.Delay, 3);

        Assert.Equal(AutosavePromptDisposition.DelayDocument, decision.Disposition);
        Assert.Equal(3, decision.DelayMinutes);
    }

    [Fact]
    public void Resolve_DefaultsInvalidDelayToOneMinute()
    {
        var decision = AutosavePromptPolicy.Resolve(AutosavePromptChoice.Delay, 0);

        Assert.Equal(AutosavePromptDisposition.DelayDocument, decision.Disposition);
        Assert.Equal(AutosaveDefaults.DefaultDeferredSaveMinutes, decision.DelayMinutes);
    }
}
