using System;
using System.Globalization;
using System.IO;

namespace InventorAutosave.Core.Logic;

internal static class SnapshotPathBuilder
{
    public static string BuildSnapshotDirectory(string targetDirectory, DateTime timestamp)
    {
        return PathUtilities.CombineUnderDirectory(
            targetDirectory,
            "snapshots",
            BuildSnapshotName(timestamp));
    }

    public static string BuildSnapshotArchivePath(string targetDirectory, DateTime timestamp)
    {
        return PathUtilities.CombineUnderDirectory(
            targetDirectory,
            "snapshots",
            BuildSnapshotName(timestamp) + ".zip");
    }

    private static string BuildSnapshotName(DateTime timestamp)
    {
        return timestamp.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture);
    }
}
