using System;
using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class SnapshotPathBuilderTests
{
    [Fact]
    public void BuildSnapshotDirectory_UsesTimestampFolderUnderSnapshots()
    {
        var targetDirectory = @"C:\work\vice";
        var timestamp = new DateTime(2026, 3, 11, 9, 4, 5);

        var snapshotDirectory = SnapshotPathBuilder.BuildSnapshotDirectory(targetDirectory, timestamp);

        Assert.Equal(
            @"C:\work\vice\snapshots\2026-03-11T09-04-05",
            snapshotDirectory);
    }

    [Fact]
    public void BuildSnapshotArchivePath_UsesTimestampZipUnderSnapshots()
    {
        var targetDirectory = @"C:\work\vice";
        var timestamp = new DateTime(2026, 3, 11, 9, 4, 5);

        var snapshotArchivePath = SnapshotPathBuilder.BuildSnapshotArchivePath(targetDirectory, timestamp);

        Assert.Equal(
            @"C:\work\vice\snapshots\2026-03-11T09-04-05.zip",
            snapshotArchivePath);
    }

    [Fact]
    public void GetRelativePath_PreservesNestedLayout()
    {
        var rootDirectory = @"C:\work\vice";
        var fullPath = @"C:\work\vice\drawings\detail\vice.idw";

        var relativePath = PathUtilities.GetRelativePath(rootDirectory, fullPath);

        Assert.Equal(@"drawings\detail\vice.idw", relativePath);
    }
}
