namespace InventorAutosave.Core;

internal static class AutosaveDefaults
{
    public const int DefaultIntervalMinutes = 5;
    public const int DefaultDeferredSaveMinutes = 1;

    private static readonly string[] DefaultIgnorePatternsValue =
    {
        "*.bak",
        "*.lck",
        "*.tmp",
        "~*",
        "OldVersions/**",
        "**/OldVersions/**",
    };

    public static string[] CreateDefaultIgnorePatterns()
    {
        return (string[])DefaultIgnorePatternsValue.Clone();
    }
}
