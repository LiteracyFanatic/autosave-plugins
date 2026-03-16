using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace InventorAutosave.Core.Logic;

internal static class SnapshotFileFilter
{
    public static bool IsSupportedSnapshotFile(string path)
    {
        return !string.IsNullOrWhiteSpace(Path.GetFileName(path));
    }

    public static bool IsExcludedArtifact(string path, IEnumerable<string>? ignorePatterns = null)
    {
        var normalizedPath = NormalizeForPatternMatching(path);
        foreach (var pattern in EnumerateIgnorePatterns(ignorePatterns))
        {
            if (MatchesPattern(normalizedPath, pattern))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsInsideSnapshotsDirectory(string targetDirectory, string path)
    {
        if (!PathUtilities.IsPathUnderDirectory(targetDirectory, path))
        {
            return false;
        }

        var relativePath = PathUtilities.GetRelativePath(targetDirectory, path);
        return IsRelativePathInsideSnapshotsDirectory(relativePath);
    }

    public static bool ShouldCopyFile(string targetDirectory, string path, IEnumerable<string>? ignorePatterns = null)
    {
        if (!PathUtilities.IsPathUnderDirectory(targetDirectory, path))
        {
            return false;
        }

        var relativePath = PathUtilities.GetRelativePath(targetDirectory, path);
        return IsSupportedSnapshotFile(path)
            && !IsExcludedArtifact(relativePath, ignorePatterns)
            && !IsRelativePathInsideSnapshotsDirectory(relativePath);
    }

    private static IEnumerable<string> EnumerateIgnorePatterns(IEnumerable<string>? ignorePatterns)
    {
        if (ignorePatterns == null)
        {
            ignorePatterns = AutosaveDefaults.CreateDefaultIgnorePatterns();
        }

        foreach (var pattern in ignorePatterns)
        {
            var trimmed = pattern?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            yield return NormalizeForPatternMatching(trimmed);
        }
    }

    private static bool MatchesPattern(string normalizedPath, string normalizedPattern)
    {
        var regex = "^" + ConvertGlobToRegex(normalizedPattern) + "$";
        if (Regex.IsMatch(normalizedPath, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (normalizedPattern.Contains('/'))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalizedPath);
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ConvertGlobToRegex(string pattern)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '*')
            {
                var isDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (isDoubleStar)
                {
                    builder.Append(".*");
                    index++;
                }
                else
                {
                    builder.Append(@"[^/]*");
                }

                continue;
            }

            if (current == '?')
            {
                builder.Append(@"[^/]");
                continue;
            }

            builder.Append(Regex.Escape(current.ToString()));
        }

        return builder.ToString();
    }

    private static string NormalizeForPatternMatching(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private static bool IsRelativePathInsideSnapshotsDirectory(string relativePath)
    {
        var normalizedRelativePath = NormalizeForPatternMatching(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return false;
        }

        var pathSegments = normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return pathSegments.Length > 0
            && string.Equals(pathSegments[0], "snapshots", StringComparison.OrdinalIgnoreCase);
    }
}
