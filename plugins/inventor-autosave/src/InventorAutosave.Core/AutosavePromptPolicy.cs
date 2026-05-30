namespace InventorAutosave.Core;

internal enum AutosavePromptChoice
{
    SaveNow,
    Ignore,
    Delay,
}

internal enum AutosavePromptDisposition
{
    SaveNow,
    SkipDocument,
    DelayDocument,
}

internal readonly record struct AutosavePromptDecision(
    AutosavePromptDisposition Disposition,
    int DelayMinutes);

internal static class AutosavePromptPolicy
{
    public static AutosavePromptDecision Resolve(AutosavePromptChoice choice, int requestedDelayMinutes)
    {
        return choice switch
        {
            AutosavePromptChoice.Delay => new AutosavePromptDecision(
                AutosavePromptDisposition.DelayDocument,
                SanitizeDelayMinutes(requestedDelayMinutes)),
            AutosavePromptChoice.Ignore => new AutosavePromptDecision(
                AutosavePromptDisposition.SkipDocument,
                SanitizeDelayMinutes(requestedDelayMinutes)),
            _ => new AutosavePromptDecision(
                AutosavePromptDisposition.SaveNow,
                SanitizeDelayMinutes(requestedDelayMinutes)),
        };
    }

    private static int SanitizeDelayMinutes(int requestedDelayMinutes)
    {
        return requestedDelayMinutes >= 1
            ? requestedDelayMinutes
            : AutosaveDefaults.DefaultDeferredSaveMinutes;
    }
}
