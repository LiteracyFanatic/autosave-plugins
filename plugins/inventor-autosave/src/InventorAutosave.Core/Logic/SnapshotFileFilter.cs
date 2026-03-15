using System;
using System.Collections.Generic;
using System.IO;

namespace InventorAutosave.Core.Logic;

internal static class SnapshotFileFilter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ipt",
        ".iam",
        ".idw",
        ".ipn",
        ".dwg",
        ".ipj",
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bak",
        ".lck",
        ".tmp",
    };

    public static bool IsSupportedSnapshotFile(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsExcludedArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        if (ExcludedExtensions.Contains(extension))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("~", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return PathUtilities.ContainsDirectorySegment(path, "OldVersions");
    }

    public static bool IsInsideSnapshotsDirectory(string targetDirectory, string path)
    {
        var snapshotRoot = PathUtilities.CombineUnderDirectory(targetDirectory, "snapshots");
        return PathUtilities.IsPathUnderDirectory(snapshotRoot, path);
    }

    public static bool ShouldCopyFile(string targetDirectory, string path)
    {
        return PathUtilities.IsPathUnderDirectory(targetDirectory, path)
            && IsSupportedSnapshotFile(path)
            && !IsExcludedArtifact(path)
            && !IsInsideSnapshotsDirectory(targetDirectory, path);
    }

}
