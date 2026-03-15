using System;
using System.IO;

namespace InventorAutosave.Core.Logic;

internal static class PathUtilities
{
    public static string NormalizeDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (IsWindowsStylePath(normalizedPath))
        {
            return normalizedPath.TrimEnd('\\', '/');
        }

        return normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string NormalizePath(string path)
    {
        if (IsWindowsStylePath(path))
        {
            return path.Replace('/', '\\')
                .TrimEnd('\\')
                .TrimEnd('/');
        }

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsPathUnderDirectory(string directoryPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var normalizedDirectory = EnsureTrailingSeparator(NormalizeDirectory(directoryPath));
        var normalizedCandidate = NormalizePath(candidatePath);

        return normalizedCandidate.StartsWith(
            normalizedDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsDirectorySegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var normalized = NormalizePath(path);
        var parts = normalized.Split('\\', '/');
        foreach (var part in parts)
        {
            if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetRelativePath(string rootDirectory, string fullPath)
    {
        var normalizedRoot = NormalizeDirectory(rootDirectory);
        var normalizedFullPath = NormalizePath(fullPath);

        if (string.Equals(normalizedRoot, normalizedFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (IsPathUnderDirectory(normalizedRoot, normalizedFullPath))
        {
            return normalizedFullPath.Substring(normalizedRoot.Length + 1);
        }

        return Path.GetRelativePath(normalizedRoot, normalizedFullPath);
    }

    public static string CombineUnderDirectory(string directoryPath, params string[] parts)
    {
        var normalizedDirectory = NormalizeDirectory(directoryPath);
        var separator = IsWindowsStylePath(normalizedDirectory) ? "\\" : Path.DirectorySeparatorChar.ToString();
        var combined = normalizedDirectory;

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            combined += separator + part.Trim('\\', '/');
        }

        return combined;
    }

    private static bool IsWindowsStylePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3)
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (IsWindowsStylePath(path))
        {
            return path.EndsWith("\\", StringComparison.Ordinal) ? path : path + "\\";
        }

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
