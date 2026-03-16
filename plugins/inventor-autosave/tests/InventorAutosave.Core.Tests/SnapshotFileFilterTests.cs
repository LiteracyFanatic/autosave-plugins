using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class SnapshotFileFilterTests
{
    [Fact]
    public void ShouldCopyFile_AllowsAnyFileInTargetDirectory()
    {
        var targetDirectory = @"C:\work\vice";
        var candidatePath = @"C:\work\vice\assemblies\notes.txt";

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
    public void ShouldCopyFile_ExcludesSnapshotArchiveInSnapshotsRoot()
    {
        var targetDirectory = @"C:\work\vice";
        var candidatePath = @"C:\work\vice\snapshots\2026-03-11T10-00-00.zip";

        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, candidatePath));
    }

    [Fact]
    public void ShouldCopyFile_ExcludesSnapshotsDirectoryWithMixedSeparators()
    {
        var targetDirectory = @"C:\work\vice";
        var candidatePath = @"C:\work\vice/snapshots\2026-03-11T10-00-00\vice.iam";

        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, candidatePath));
    }

    [Fact]
    public void ShouldCopyFile_UsesDefaultIgnorePatterns()
    {
        var targetDirectory = @"C:\work\vice";

        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\base.lck"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\base.tmp"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\base.bak"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\~base.ipt"));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\OldVersions\base.ipt"));
    }

    [Fact]
    public void ShouldCopyFile_UsesConfiguredIgnorePatterns()
    {
        var targetDirectory = @"C:\work\vice";
        var ignorePatterns = new[]
        {
            "*.txt",
            "**/cache/**",
        };

        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\notes.txt", ignorePatterns));
        Assert.False(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\build\cache\result.bin", ignorePatterns));
        Assert.True(SnapshotFileFilter.ShouldCopyFile(targetDirectory, @"C:\work\vice\designs\base.ipt", ignorePatterns));
    }
}
