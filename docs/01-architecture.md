# Architecture

## Project layout

```
CRC/
  src/
    Crc.Core/          NuGet library — algorithm + service + protocol
    Crc.Cli/           Cross-platform console utility
    Crc.Api/           ASP.NET Core Minimal API
    Crc.Desktop/       Avalonia desktop application (Windows + Linux)
  benchmarks/
    Crc.Benchmarks/    Speed comparison of all algorithm variants
  tests/
    Crc.Core.Tests/    Unit + integration tests (xUnit)
  build/
    build-all.ps1      Windows build script → dist/
    build-all.sh       Linux build script → dist/
  docs/                This documentation
```

## Dependency graph

```
Crc.Core         (library, no external deps except System.Text.Encoding.CodePages)
    ↑
    ├── Crc.Cli        (console entry point)
    ├── Crc.Api        (ASP.NET Core web host)
    ├── Crc.Desktop    (Avalonia UI)
    └── Crc.Benchmarks (includes LegacyVariants.cs — all other algorithm variants)
         ↑
         Crc.Core.Tests
```

## Crc.Core components

| File | Responsibility |
|---|---|
| `Crc32.cs` | Public API: `Compute()`, `ComputeParallel()`. Slice-by-16 inner loop. |
| `Crc32Combine.cs` | GF(2) matrix arithmetic for stitching parallel chunks. |
| `CrcService.cs` | File/directory traversal; threading strategy selection. |
| `CrcProtocol.cs` | Writing `path=CRC` lines to a text file (UTF-8 or legacy). |
| `Models/FileCrcInfo.cs` | Result record: `FileName`, `RelativePath`, `Crc`. |

## Threading strategy

`CrcService.Process` selects based on input shape:

- **Single small file** (< 4 MB by default): `Crc32.Compute(Stream)` — single-threaded.
- **Single large file** (≥ 4 MB): `Crc32.ComputeParallel(Stream)` — parallel chunks + GF(2) stitch.
- **Directory**: `Parallel.For` over files; each file uses single-threaded streaming.
  Many small files benefit most from this mode; for a directory containing
  large files the per-file parallel mode can be enabled by lowering the threshold.

Threshold is configurable via `CrcOptions.LargeFileThreshold`.

## Build artefacts

| Path | Contents |
|---|---|
| `dist/nuget/` | `Crc.Custom.*.nupkg` |
| `dist/cli/win-x64/` | `crc.exe` single-file self-contained |
| `dist/cli/linux-x64/` | `crc` single-file self-contained |
| `dist/api/` | Framework-dependent API publish |
| `dist/desktop/win-x64/` | Desktop self-contained |
| `dist/desktop/linux-x64/` | Desktop self-contained |
