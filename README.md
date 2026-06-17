# CRC-32 Toolkit

CRC-32 implementation with a custom convention (see [docs/02-math.md](docs/02-math.md)).
Reference value: `"123456789"` → `0x22896B0A`.

## Components

| Component | Description |
|---|---|
| **Crc.Core** | NuGet library: Slice-by-16 algorithm + multi-threaded parallel compute |
| **Crc.Cli** | Cross-platform console utility (Windows + Linux) |
| **Crc.Api** | ASP.NET Core REST API |
| **Crc.Desktop** | Avalonia desktop application (Windows + Linux) |

## Quick start

```bash
# Build everything into dist/
./build/build-all.sh          # Linux
./build/build-all.ps1         # Windows (PowerShell 7+)

# CLI
dist/cli/linux-x64/crc file.bin
dist/cli/linux-x64/crc /some/directory/ -o results.txt
```

## Documentation

See [docs/](docs/README.md) for full documentation:
mathematics, CLI reference, API deployment, desktop guide, NuGet API.

## Development

```bash
dotnet build Crc.slnx
dotnet test tests/Crc.Core.Tests/

# Benchmarks (Release required)
dotnet run -c Release --project benchmarks/Crc.Benchmarks/
```
