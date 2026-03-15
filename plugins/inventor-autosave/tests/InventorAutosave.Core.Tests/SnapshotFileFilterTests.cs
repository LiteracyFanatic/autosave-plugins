using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class SnapshotFileFilterTests
{
    [Fact]
    public void ShouldCopyFile_AllowsSupportedInventorFilesInTargetDirectory()
    {
        var targetDirectory = @"C:\work\vice";
        var candidatePath = @"C:\work\vice\assemblies\vice.iam";

        Assert.True(SnapshotFileFilter.ShouldCopyFile(targetDirectory, candidatePath));
    }

    [Fact]
    public void ShouldCopyFile_ExcludesSnapshotsDirectory()
    {
        var targetDirectory = @"C:\work\vice";
        var candidatePath = @"C:\work\vice\snapshots\2026-03-11T10-00-00\vice.iam";

        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, candidatePath));
    }

    [Fact]
    public void ShouldCopyFile_ExcludesLockTempAndBackupArtifacts()
    {
        var targetDirectory = @"C:\work\vice";

        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\base.lck"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\base.tmp"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\base.bak"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\OldVersions\base.ipt"));
    }
}
