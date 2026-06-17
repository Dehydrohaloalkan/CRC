namespace Crc;

/// <summary>Options for CRC computation.</summary>
public sealed class CrcOptions
{
    /// <summary>
    /// File size threshold (bytes) above which parallel intra-file computation is used.
    /// For directories with many small files, each file is computed sequentially while
    /// files themselves are processed in parallel. Default: 4 MB.
    /// </summary>
    public long LargeFileThreshold { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Maximum degree of parallelism for parallel operations (0 = CPU count).
    /// </summary>
    public int DegreeOfParallelism { get; init; } = 0;
}

/// <summary>
/// Computes CRC-32 for a file or a directory tree.
///
/// Strategy for parallelism:
///   • Single large file  → intra-file parallel chunks (ComputeParallel).
///   • Directory          → files processed concurrently with Parallel.ForEach;
///                          each individual file uses single-threaded streaming.
/// </summary>
public static class CrcService
{
    /// <summary>
    /// Compute CRC-32 for a file path or directory path.
    /// Returns one <see cref="FileCrcInfo"/> per file, sorted deterministically
    /// (directories first, then files, both alphabetically case-insensitive).
    /// </summary>
    /// <param name="path">Absolute or relative path to a file or directory.</param>
    /// <param name="options">Computation options (optional).</param>
    public static IReadOnlyList<FileCrcInfo> Process(string path, CrcOptions? options = null)
    {
        options ??= new CrcOptions();
        var attr = File.GetAttributes(path);

        if (attr.HasFlag(FileAttributes.Directory))
            return ProcessDirectory(path, options);

        var result = ProcessFile(path, Path.GetDirectoryName(path) ?? path, options);
        return result is null ? [] : [result];
    }

    // -------------------------------------------------------------------------

    private static IReadOnlyList<FileCrcInfo> ProcessDirectory(string rootDir, CrcOptions options)
    {
        var relativePaths = GetFilesRecursive(rootDir).ToList();
        var results = new FileCrcInfo[relativePaths.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.DegreeOfParallelism <= 0
                ? Environment.ProcessorCount
                : options.DegreeOfParallelism
        };

        Parallel.For(0, relativePaths.Count, parallelOptions, i =>
        {
            string rel = relativePaths[i];
            string abs = Path.Combine(rootDir, rel);
            using var fs = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            uint crc = Crc32.Compute(fs);
            results[i] = new FileCrcInfo
            {
                FileName = Path.GetFileName(abs),
                RelativePath = rel,
                Crc = crc.ToString("X8")
            };
        });

        return results;
    }

    private static FileCrcInfo? ProcessFile(string filePath, string rootDir, CrcOptions options)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return null;

        uint crc;
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        crc = info.Length >= options.LargeFileThreshold
            ? Crc32.ComputeParallel(fs, options.DegreeOfParallelism)
            : Crc32.Compute(fs);

        string rel = Path.GetRelativePath(rootDir, filePath);

        return new FileCrcInfo
        {
            FileName = info.Name,
            RelativePath = rel,
            Crc = crc.ToString("X8")
        };
    }

    /// <summary>
    /// Recursively enumerate files under <paramref name="rootPath"/>, sorted
    /// case-insensitively: subdirectories first (recursed), then files.
    /// Returns paths relative to <paramref name="rootPath"/>.
    /// </summary>
    internal static IEnumerable<string> GetFilesRecursive(string rootPath)
        => GetFilesRecursive(new DirectoryInfo(rootPath), "");

    private static IEnumerable<string> GetFilesRecursive(DirectoryInfo dir, string relativePath)
    {
        var result = new List<string>();

        foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            string subRel = string.IsNullOrEmpty(relativePath)
                ? sub.Name
                : Path.Combine(relativePath, sub.Name);
            result.AddRange(GetFilesRecursive(sub, subRel));
        }

        foreach (var file in dir.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(string.IsNullOrEmpty(relativePath)
                ? file.Name
                : Path.Combine(relativePath, file.Name));
        }

        return result;
    }
}
