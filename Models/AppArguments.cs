namespace CRC.Models;
internal class AppArguments {
    public bool BackwardCompatibility { get; set; } = true;
    public bool SilentMode { get; set; }
    
    public string? Target { get; set; }
    public string? ProtocolDir { get; set; }

    public string ProtocolFile { get; set; } = "csum.mrk";
    public string CrcFile { get; set; } = "csum.crc";
}