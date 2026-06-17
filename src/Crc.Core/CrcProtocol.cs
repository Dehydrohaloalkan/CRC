using System.Text;

namespace Crc;

/// <summary>Options for writing the CRC protocol file.</summary>
public sealed class CrcProtocolOptions
{
    /// <summary>
    /// Use Windows path separators and CRLF line endings for legacy compatibility.
    /// When false (default), forward slashes and LF are used.
    /// </summary>
    public bool LegacyLineEndings { get; init; } = false;

    /// <summary>
    /// Encode the output file as Windows-1251 instead of UTF-8.
    /// </summary>
    public bool LegacyEncoding { get; init; } = false;

    /// <summary>
    /// Convert paths to uppercase directory + lowercase filename (legacy format).
    /// </summary>
    public bool LegacyPathCase { get; init; } = false;
}

/// <summary>
/// Writes CRC-32 results to a text file in the format:
///   <c>relative/path/file.ext=XXXXXXXX</c>
/// One line per file, UTF-8 (or Windows-1251 in legacy mode).
/// </summary>
public static class CrcProtocol
{
    /// <summary>
    /// Write <paramref name="results"/> to <paramref name="outputPath"/>.
    /// </summary>
    public static void Write(
        IReadOnlyList<FileCrcInfo> results,
        string outputPath,
        CrcProtocolOptions? options = null)
    {
        options ??= new CrcProtocolOptions();

        Encoding enc = options.LegacyEncoding
            ? GetLegacyEncoding()
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        using var writer = new StreamWriter(outputPath, append: false, enc);
        foreach (var f in results)
        {
            string path = FormatPath(f.RelativePath, options);
            writer.Write(path);
            writer.Write('=');
            writer.Write(f.Crc);
            // Always write LF here; replace below if legacy CRLF needed.
            writer.Write('\n');
        }

        // Legacy mode: re-read and normalise line endings + path separators.
        if (options.LegacyLineEndings)
        {
            string content = File.ReadAllText(outputPath, enc);
            content = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            File.WriteAllText(outputPath, content, enc);
        }
    }

    // -------------------------------------------------------------------------

    private static string FormatPath(string relativePath, CrcProtocolOptions options)
    {
        if (options.LegacyPathCase)
        {
            string dir = Path.GetDirectoryName(relativePath) ?? string.Empty;
            string file = Path.GetFileName(relativePath);
            relativePath = string.IsNullOrEmpty(dir)
                ? file.ToLowerInvariant()
                : Path.Combine(dir.ToUpperInvariant(), file.ToLowerInvariant());
        }

        if (options.LegacyLineEndings)
            relativePath = relativePath.Replace('/', '\\');

        return relativePath;
    }

    private static Encoding GetLegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(1251); }
        catch { return Encoding.UTF8; }
    }
}
