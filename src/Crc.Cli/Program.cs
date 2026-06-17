using Crc;

// --------------------------------------------------------------------
// crc — CRC-32 command-line utility
//
// Usage:
//   crc <path>                     compute CRC for file or directory
//   crc -f <file>                  explicit file flag
//   crc -d <directory>             explicit directory flag
//   crc <path> -o <output.txt>     also save results to file
//   crc <path> --legacy            legacy line endings / encoding / path case
//
// Output (stdout):
//   single file  → XXXXXXXX
//   directory    → relative/path=XXXXXXXX  (one line per file)
//
// Informational messages go to stderr so stdout stays pipe-friendly.
// Exit codes: 0 = success, 1 = error.
// --------------------------------------------------------------------

string? target = null;
string? outputFile = null;
bool legacy = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-f" or "--file":
            target = NextArg(args, ref i, "-f"); break;
        case "-d" or "--dir":
            target = NextArg(args, ref i, "-d"); break;
        case "-o" or "--out":
            outputFile = NextArg(args, ref i, "-o"); break;
        case "--legacy":
            legacy = true; break;
        default:
            if (!args[i].StartsWith('-') && target is null)
                target = args[i];
            else
                return Fail($"Unknown argument: {args[i]}");
            break;
    }
}

if (string.IsNullOrWhiteSpace(target))
    return Fail("Usage: crc <file|directory> [-o output.txt] [--legacy]");

if (!Path.Exists(target))
    return Fail($"Path not found: {target}");

try
{
    IReadOnlyList<FileCrcInfo> results = CrcService.Process(target);

    bool isDir = File.GetAttributes(target).HasFlag(FileAttributes.Directory);
    var lines = FormatResults(results, isDir);

    // Write to stdout.
    foreach (string line in lines)
        Console.WriteLine(line);

    // Optionally save to file.
    if (outputFile is not null)
    {
        var opts = new CrcProtocolOptions
        {
            LegacyLineEndings = legacy,
            LegacyEncoding    = legacy,
            LegacyPathCase    = legacy,
        };
        CrcProtocol.Write(results, outputFile, opts);
        Console.Error.WriteLine($"Saved: {outputFile}");
    }

    return 0;
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static IEnumerable<string> FormatResults(IReadOnlyList<FileCrcInfo> results, bool isDir)
{
    foreach (var f in results)
        yield return isDir ? $"{f.RelativePath}={f.Crc}" : f.Crc;
}

static string NextArg(string[] args, ref int i, string flag)
{
    if (++i >= args.Length)
    {
        Console.Error.WriteLine($"Missing value for {flag}");
        Environment.Exit(1);
    }
    return args[i];
}

static int Fail(string message)
{
    Console.Error.WriteLine($"Error: {message}");
    return 1;
}
