using System;

namespace InventorAutosave.Core.Logic;

internal static class ActiveInventorEnvironmentClassifier
{
    public static bool IsPromptRequired(string? displayName, string? internalName)
    {
        return Contains(internalName, "FEA")
            || Contains(internalName, "Studio")
            || Contains(displayName, "Inventor Studio")
            || Contains(displayName, "Stress")
            || IsAnalysisEnvironment(displayName);
    }

    private static bool IsAnalysisEnvironment(string? displayName)
    {
        return string.Equals(displayName?.Trim(), "Analysis", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? value, string marker)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
