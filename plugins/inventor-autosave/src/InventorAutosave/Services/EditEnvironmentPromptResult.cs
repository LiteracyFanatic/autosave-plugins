namespace InventorAutosave.Services;

internal enum EditEnvironmentPromptChoice
{
    SaveNow,
    Ignore,
    Delay,
}

internal readonly record struct EditEnvironmentPromptResult(EditEnvironmentPromptChoice Choice, int DelayMinutes);
