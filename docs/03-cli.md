# CLI Usage

## Install

Download the self-contained binary from `dist/cli/<platform>/`:

```
dist/cli/win-x64/crc.exe     # Windows
dist/cli/linux-x64/crc       # Linux (chmod +x first)
```

## Basic usage

```bash
# Single file — prints CRC to stdout
crc path/to/file.bin
# → 22896B0A

# Directory — prints one "relative/path=CRC" line per file
crc path/to/directory/

# Explicit flags
crc -f path/to/file.bin
crc -d path/to/directory/

# Save output to file (in addition to stdout)
crc path/to/directory/ -o results.txt

# Legacy mode: Windows-1251 encoding, CRLF line endings, backslash separators, uppercase dir
crc path/to/directory/ -o results.txt --legacy
```

## Pipeline examples

```bash
# Compare CRC of two files
[ "$(crc file1.bin)" = "$(crc file2.bin)" ] && echo "match" || echo "differ"

# Capture CRC into a variable
CRC=$(crc important.bin)
echo "CRC is $CRC"

# Process all .dat files and build a manifest
for f in *.dat; do
  echo "$f=$(crc "$f")"
done > manifest.txt

# Verify a file matches an expected CRC
EXPECTED="22896B0A"
ACTUAL=$(crc data.bin)
if [ "$ACTUAL" = "$EXPECTED" ]; then echo OK; else echo "FAIL: got $ACTUAL"; fi
```

## Arguments

| Flag | Long form | Description |
|---|---|---|
| (positional) | | Path to file or directory |
| `-f <path>` | `--file <path>` | Explicit file path |
| `-d <path>` | `--dir <path>` | Explicit directory path |
| `-o <path>` | `--out <path>` | Save output to file |
| | `--legacy` | Legacy encoding/endings/path format |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Error (path not found, permission denied, etc.) |

## Output format

- **Single file**: one line with the 8-character uppercase hex CRC value, no extras.
- **Directory**: one line per file — `relative/path=XXXXXXXX`.
- Informational messages (progress, save confirmation) go to **stderr** so they
  don't interfere with piping stdout.
