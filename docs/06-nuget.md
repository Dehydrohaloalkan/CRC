# NuGet Package — Crc.Custom

## Install

```bash
dotnet add package Crc.Custom
```

Or add to your `.csproj`:
```xml
<PackageReference Include="Crc.Custom" Version="1.0.0" />
```

Target framework: **net10.0**.

---

## API Reference

All public types are in namespace `Crc`.

### `Crc32` — static class

```csharp
// Single-threaded — span or array
uint Crc32.Compute(ReadOnlySpan<byte> data)
uint Crc32.Compute(byte[] data)

// Single-threaded — streaming (any file size, 1 MB buffer)
uint Crc32.Compute(Stream stream)

// Multi-threaded — large array
uint Crc32.ComputeParallel(byte[] data, int degreeOfParallelism = 0)

// Multi-threaded — large stream (producer-consumer, bounded memory)
uint Crc32.ComputeParallel(Stream stream, int degreeOfParallelism = 0)
```

`degreeOfParallelism = 0` means `Environment.ProcessorCount`.

### `CrcService` — static class

```csharp
// Process a file or directory; returns one FileCrcInfo per file.
IReadOnlyList<FileCrcInfo> CrcService.Process(string path, CrcOptions? options = null)
```

**`CrcOptions`**

| Property | Default | Description |
|---|---|---|
| `LargeFileThreshold` | 4 MB | Files at or above this size use intra-file parallelism |
| `DegreeOfParallelism` | 0 (CPU count) | Thread count for parallel operations |

### `CrcProtocol` — static class

```csharp
// Write results to a file: "path=XXXXXXXX" per line (UTF-8 by default).
void CrcProtocol.Write(
    IReadOnlyList<FileCrcInfo> results,
    string outputPath,
    CrcProtocolOptions? options = null)
```

**`CrcProtocolOptions`**

| Property | Default | Description |
|---|---|---|
| `LegacyLineEndings` | `false` | Use CRLF and backslash separators |
| `LegacyEncoding` | `false` | Write Windows-1251 instead of UTF-8 |
| `LegacyPathCase` | `false` | Uppercase directory, lowercase filename |

### `FileCrcInfo` — record

```csharp
string FileName     // file name only
string RelativePath // path relative to the root processed
string Crc          // "XXXXXXXX" — 8 uppercase hex digits
```

---

## Usage example

```csharp
using Crc;

// Single file
uint crc = Crc32.Compute(File.ReadAllBytes("data.bin"));
Console.WriteLine(crc.ToString("X8")); // e.g. 22896B0A

// Large file
using var fs = File.OpenRead("large.bin");
uint crc2 = Crc32.ComputeParallel(fs);

// Directory
var results = CrcService.Process("/path/to/dir");
CrcProtocol.Write(results, "output.txt");

// Custom options
var results2 = CrcService.Process("/path/to/dir", new CrcOptions
{
    LargeFileThreshold    = 8 * 1024 * 1024, // 8 MB
    DegreeOfParallelism   = 4
});
```

---

## Reference value

```
Input:  "123456789" (ASCII)
Output: 0x22896B0A
```

This differs from the standard zlib CRC-32 (`0xCBF43926`) due to the custom convention —
see [02-math.md](02-math.md) for the full explanation.
