namespace Crc;

/// <summary>Result of computing CRC-32 for a single file.</summary>
public sealed class FileCrcInfo
{
    /// <summary>File name without path.</summary>
    public required string FileName { get; init; }

    /// <summary>Path relative to the root directory that was processed.</summary>
    public required string RelativePath { get; init; }

    /// <summary>CRC-32 value formatted as 8 uppercase hex digits.</summary>
    public required string Crc { get; init; }
}
