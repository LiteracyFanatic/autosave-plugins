using System;
using System.IO;
using System.IO.Compression;
using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public sealed class SnapshotHashDiffComparerTests : IDisposable
{
    private readonly string _tempRoot;

    public SnapshotHashDiffComparerTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "InventorAutosave.Core.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void FindPreviousSnapshotPath_ReturnsLatestEarlierSnapshot()
    {
        var targetDirectory = Path.Combine(_tempRoot, "project");
        var snapshotRoot = Path.Combine(targetDirectory, "snapshots");
        Directory.CreateDirectory(snapshotRoot);

        Directory.CreateDirectory(Path.Combine(snapshotRoot, "2026-03-11T10-00-00"));
        var currentSnapshotDirectory = Path.Combine(snapshotRoot, "2026-03-11T10-05-00");
        Directory.CreateDirectory(currentSnapshotDirectory);
        Directory.CreateDirectory(Path.Combine(snapshotRoot, "2026-03-11T09-55-00"));

        var previousSnapshotPath = SnapshotHashDiffComparer.FindPreviousSnapshotPath(
            targetDirectory,
            currentSnapshotDirectory);

        Assert.Equal(
            PathUtilities.NormalizeDirectory(Path.Combine(snapshotRoot, "2026-03-11T10-00-00")),
            previousSnapshotPath);
    }

    [Fact]
    public void FindPreviousSnapshotPath_ReturnsZipWhenDirectoryIsNotAvailable()
    {
        var targetDirectory = Path.Combine(_tempRoot, "project");
        var snapshotRoot = Path.Combine(targetDirectory, "snapshots");
        Directory.CreateDirectory(snapshotRoot);

        var previousSnapshotDirectory = Path.Combine(snapshotRoot, "2026-03-11T10-00-00");
        Directory.CreateDirectory(previousSnapshotDirectory);
        WriteFile(previousSnapshotDirectory, "base.ipt", "content");
        var previousSnapshotArchive = previousSnapshotDirectory + ".zip";
        ZipFile.CreateFromDirectory(previousSnapshotDirectory, previousSnapshotArchive, CompressionLevel.Optimal, includeBaseDirectory: false);
        Directory.Delete(previousSnapshotDirectory, recursive: true);

        var currentSnapshotDirectory = Path.Combine(snapshotRoot, "2026-03-11T10-05-00");
        Directory.CreateDirectory(currentSnapshotDirectory);

        var previousSnapshotPath = SnapshotHashDiffComparer.FindPreviousSnapshotPath(
            targetDirectory,
            currentSnapshotDirectory);

        Assert.Equal(
            PathUtilities.NormalizePath(previousSnapshotArchive),
            previousSnapshotPath);
    }

    [Fact]
    public void CompareWithPreviousSnapshot_ReturnsChangedNewAndRemovedFiles()
    {
        var targetDirectory = Path.Combine(_tempRoot, "project");
        var snapshotRoot = Path.Combine(targetDirectory, "snapshots");
        var previousSnapshotDirectory = Path.Combine(snapshotRoot, "2026-03-11T10-00-00");
        var currentSnapshotDirectory = Path.Combine(snapshotRoot, "2026-03-11T10-05-00");
        Directory.CreateDirectory(previousSnapshotDirectory);
        Directory.CreateDirectory(currentSnapshotDirectory);

        WriteFile(previousSnapshotDirectory, "changed.ipt", "old-content");
        WriteFile(previousSnapshotDirectory, "same.iam", "same-content");
        WriteFile(previousSnapshotDirectory, "removed.idw", "removed-content");
        WriteFile(currentSnapshotDirectory, "changed.ipt", "new-content");
        WriteFile(currentSnapshotDirectory, "same.iam", "same-content");
        WriteFile(currentSnapshotDirectory, "new.ipn", "new-content");
        WriteFile(currentSnapshotDirectory, "ignored.txt", "ignore-me");

        var result = SnapshotHashDiffComparer.CompareWithPreviousSnapshot(
            targetDirectory,
            currentSnapshotDirectory);

        Assert.Equal(PathUtilities.NormalizeDirectory(previousSnapshotDirectory), result.PreviousSnapshotPath);
        Assert.Equal(
            new[]
            {
                "Changed: changed.ipt",
                "New: new.ipn",
                "Removed: removed.idw",
            },
            result.DifferingFiles);
    }

    [Fact]
    public void CompareWithPreviousSnapshot_ReturnsNoDifferencesWhenNoPreviousSnapshotExists()
    {
        var targetDirectory = Path.Combine(_tempRoot, "project");
        var currentSnapshotDirectory = Path.Combine(targetDirectory, "snapshots", "2026-03-11T10-05-00");
        Directory.CreateDirectory(currentSnapshotDirectory);
        WriteFile(currentSnapshotDirectory, "base.ipt", "content");

        var result = SnapshotHashDiffComparer.CompareWithPreviousSnapshot(
            targetDirectory,
            currentSnapshotDirectory);

        Assert.Equal(string.Empty, result.PreviousSnapshotPath);
        Assert.Empty(result.DifferingFiles);
    }

    [Fact]
    public void CompareWithPreviousSnapshot_SupportsZipArchives()
    {
        var targetDirectory = Path.Combine(_tempRoot, "project");
        var snapshotRoot = Path.Combine(targetDirectory, "snapshots");
        var previousSnapshotDirectory = Path.Combine(snapshotRoot, "2026-03-11T10-00-00");
        var currentSnapshotArchive = Path.Combine(snapshotRoot, "2026-03-11T10-05-00.zip");
        Directory.CreateDirectory(previousSnapshotDirectory);
        WriteFile(previousSnapshotDirectory, "changed.ipt", "old-content");
        WriteFile(previousSnapshotDirectory, "removed.idw", "removed-content");

        var currentSnapshotDirectory = Path.Combine(_tempRoot, "current");
        Directory.CreateDirectory(currentSnapshotDirectory);
        WriteFile(currentSnapshotDirectory, "changed.ipt", "new-content");
        WriteFile(currentSnapshotDirectory, "new.ipn", "new-content");
        WriteFile(currentSnapshotDirectory, "ignored.txt", "ignore-me");
        ZipFile.CreateFromDirectory(currentSnapshotDirectory, currentSnapshotArchive, CompressionLevel.Optimal, includeBaseDirectory: false);

        var result = SnapshotHashDiffComparer.CompareWithPreviousSnapshot(
            targetDirectory,
            currentSnapshotArchive);

        Assert.Equal(PathUtilities.NormalizeDirectory(previousSnapshotDirectory), result.PreviousSnapshotPath);
        Assert.Equal(
            new[]
            {
                "Changed: changed.ipt",
                "New: new.ipn",
                "Removed: removed.idw",
            },
            result.DifferingFiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void WriteFile(string rootDirectory, string relativePath, string contents)
    {
        var filePath = Path.Combine(rootDirectory, relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, contents);
    }
}
