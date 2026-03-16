using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace InventorAutosave.Core.Logic;

internal sealed class SnapshotHashDiffResult
{
    public string PreviousSnapshotPath { get; set; } = string.Empty;

    public List<string> DifferingFiles { get; } = new();
}

internal static class SnapshotHashDiffComparer
{
    public static SnapshotHashDiffResult CompareWithPreviousSnapshot(
        string targetDirectory,
        string currentSnapshotPath,
        IEnumerable<string>? ignorePatterns = null)
    {
        var result = new SnapshotHashDiffResult();
        var previousSnapshotPath = FindPreviousSnapshotPath(targetDirectory, currentSnapshotPath);
        if (string.IsNullOrWhiteSpace(previousSnapshotPath))
        {
            return result;
        }

        result.PreviousSnapshotPath = previousSnapshotPath;

        var currentFiles = EnumerateSnapshotFiles(currentSnapshotPath, ignorePatterns);
        var previousFiles = EnumerateSnapshotFiles(previousSnapshotPath, ignorePatterns);
        var allRelativePaths = new SortedSet<string>(currentFiles.Keys, StringComparer.OrdinalIgnoreCase);
        allRelativePaths.UnionWith(previousFiles.Keys);

        foreach (var relativePath in allRelativePaths)
        {
            if (!currentFiles.TryGetValue(relativePath, out var currentHash))
            {
                result.DifferingFiles.Add($"Removed: {relativePath}");
                continue;
            }

            if (!previousFiles.TryGetValue(relativePath, out var previousHash))
            {
                result.DifferingFiles.Add($"New: {relativePath}");
                continue;
            }

            if (!string.Equals(currentHash, previousHash, StringComparison.Ordinal))
            {
                result.DifferingFiles.Add($"Changed: {relativePath}");
            }
        }

        return result;
    }

    public static string FindPreviousSnapshotPath(string targetDirectory, string currentSnapshotPath)
    {
        var snapshotRoot = PathUtilities.CombineUnderDirectory(targetDirectory, "snapshots");
        if (!Directory.Exists(snapshotRoot))
        {
            return string.Empty;
        }

        var normalizedCurrentSnapshot = PathUtilities.NormalizePath(currentSnapshotPath);
        var currentSnapshotName = GetSnapshotName(normalizedCurrentSnapshot);
        SnapshotCandidate? previousSnapshot = null;

        foreach (var candidate in EnumerateSnapshotCandidates(snapshotRoot))
        {
            if (string.Equals(candidate.Path, normalizedCurrentSnapshot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Compare(candidate.Name, currentSnapshotName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            if (IsBetterCandidate(candidate, previousSnapshot))
            {
                previousSnapshot = candidate;
            }
        }

        return previousSnapshot?.Path ?? string.Empty;
    }

    private static IEnumerable<SnapshotCandidate> EnumerateSnapshotCandidates(string snapshotRoot)
    {
        foreach (var candidateDirectory in Directory.EnumerateDirectories(snapshotRoot))
        {
            var normalizedCandidate = PathUtilities.NormalizeDirectory(candidateDirectory);
            yield return new SnapshotCandidate(
                normalizedCandidate,
                GetSnapshotName(normalizedCandidate),
                IsDirectory: true);
        }

        foreach (var candidateArchive in Directory.EnumerateFiles(snapshotRoot, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var normalizedCandidate = PathUtilities.NormalizePath(candidateArchive);
            yield return new SnapshotCandidate(
                normalizedCandidate,
                GetSnapshotName(normalizedCandidate),
                IsDirectory: false);
        }
    }

    private static bool IsBetterCandidate(SnapshotCandidate candidate, SnapshotCandidate? currentBest)
    {
        if (!currentBest.HasValue)
        {
            return true;
        }

        var nameComparison = string.Compare(
            candidate.Name,
            currentBest.Value.Name,
            StringComparison.OrdinalIgnoreCase);
        if (nameComparison > 0)
        {
            return true;
        }

        return nameComparison == 0 && candidate.IsDirectory && !currentBest.Value.IsDirectory;
    }

    private static Dictionary<string, string> EnumerateSnapshotFiles(
        string snapshotPath,
        IEnumerable<string>? ignorePatterns)
    {
        if (Directory.Exists(snapshotPath))
        {
            return EnumerateDirectorySnapshotFiles(snapshotPath, ignorePatterns);
        }

        if (File.Exists(snapshotPath)
            && string.Equals(Path.GetExtension(snapshotPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return EnumerateArchiveSnapshotFiles(snapshotPath, ignorePatterns);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> EnumerateDirectorySnapshotFiles(
        string snapshotDirectory,
        IEnumerable<string>? ignorePatterns)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(snapshotDirectory, "*", SearchOption.AllDirectories))
        {
            if (!SnapshotFileFilter.IsSupportedSnapshotFile(filePath)
                || SnapshotFileFilter.IsExcludedArtifact(
                    NormalizeRelativePath(PathUtilities.GetRelativePath(snapshotDirectory, filePath)),
                    ignorePatterns))
            {
                continue;
            }

            var relativePath = NormalizeRelativePath(PathUtilities.GetRelativePath(snapshotDirectory, filePath));
            files[relativePath] = ComputeSha256(filePath);
        }

        return files;
    }

    private static Dictionary<string, string> EnumerateArchiveSnapshotFiles(
        string snapshotArchivePath,
        IEnumerable<string>? ignorePatterns)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(snapshotArchivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var relativePath = NormalizeRelativePath(entry.FullName);
            if (!SnapshotFileFilter.IsSupportedSnapshotFile(relativePath)
                || SnapshotFileFilter.IsExcludedArtifact(relativePath, ignorePatterns))
            {
                continue;
            }

            using var stream = entry.Open();
            files[relativePath] = ComputeSha256(stream);
        }

        return files;
    }

    private static string GetSnapshotName(string snapshotPath)
    {
        var fileName = Path.GetFileName(snapshotPath);
        return string.Equals(Path.GetExtension(fileName), ".zip", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeSha256(stream);
    }

    private static string ComputeSha256(Stream stream)
    {
        using var hashAlgorithm = SHA256.Create();
        return Convert.ToHexString(hashAlgorithm.ComputeHash(stream));
    }

    private readonly record struct SnapshotCandidate(string Path, string Name, bool IsDirectory);
}
