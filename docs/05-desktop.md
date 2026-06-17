# Desktop Application

## Technology

Built with **Avalonia UI 11** — a cross-platform .NET UI framework that renders natively
on Windows, Linux (X11/Wayland) and macOS from a single codebase.
MVVM pattern via **CommunityToolkit.Mvvm**.

## Usage

1. Launch `Crc.Desktop` (or `Crc.Desktop.exe` on Windows).
2. Click **Browse…** to pick a file or directory, or type the path directly.
3. Optionally check **Legacy mode** for Windows-1251 encoding and CRLF line endings.
4. Click **Compute** — results appear in the table (relative path + CRC-32).
5. Click **Save…** to write the results to a text file.

## Publishing

```bash
# Windows self-contained
dotnet publish src/Crc.Desktop/Crc.Desktop.csproj \
  -c Release -r win-x64 --self-contained \
  -o dist/desktop/win-x64

# Linux self-contained
dotnet publish src/Crc.Desktop/Crc.Desktop.csproj \
  -c Release -r linux-x64 --self-contained \
  -o dist/desktop/linux-x64
```

Or use `build/build-all.ps1` / `build/build-all.sh` — they include both platforms.

## Linux prerequisites

On Linux, Avalonia requires one of:
- **X11**: `libx11`, `libxrandr`, `libxi`, `libxcursor`, `libxcb`
- **Wayland**: `libwayland-client`
- **Font rendering**: `fontconfig`, `freetype`

Install on Debian/Ubuntu:
```bash
sudo apt-get install -y \
  libx11-6 libxrandr2 libxi6 libxcursor1 libxcb1 \
  libfontconfig1 libfreetype6
```
