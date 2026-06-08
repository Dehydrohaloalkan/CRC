namespace CRC.Models;
public class FileCrcInfo {
    public required string FileName { get; set; }
    public required string FilePath { get; set; }
    public required string ProtocolPath { get; set; }
    public required string Crc { get; set; }
}