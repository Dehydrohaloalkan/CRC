using System.Diagnostics;
using Crc;
using Crc.Benchmarks;

// ------------------------------------------------------------------
// CRC-32 variant benchmark.
//
//   dotnet run -c Release                        # 100 MB random, 3 passes
//   dotnet run -c Release -- --size 200          # 200 MB
//   dotnet run -c Release -- --iter 5            # 5 passes
//   dotnet run -c Release -- --file C:\big.dat   # real file
//   dotnet run -c Release -- --no-bitwise        # skip slow reference
//
// IMPORTANT: run in Release mode only (-c Release).
// ------------------------------------------------------------------

int sizeMB = 100;
int iterations = 3;
string? filePath = null;
bool runBitwise = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--size" or "-s": sizeMB = int.Parse(args[++i]); break;
        case "--iter" or "-i": iterations = int.Parse(args[++i]); break;
        case "--file" or "-f": filePath = args[++i]; break;
        case "--no-bitwise": runBitwise = false; break;
        default: Console.Error.WriteLine($"Unknown argument: {args[i]}"); return 1;
    }
}

byte[] data;
string source;
if (filePath is not null)
{
    if (!File.Exists(filePath)) { Console.Error.WriteLine($"File not found: {filePath}"); return 1; }
    data = File.ReadAllBytes(filePath);
    source = $"file {Path.GetFileName(filePath)}";
}
else
{
    long byteCount = (long)sizeMB * 1024 * 1024;
    if (byteCount > int.MaxValue) { Console.Error.WriteLine("Size too large for single array (> 2 GB)"); return 1; }
    data = new byte[byteCount];
    new Random(42).NextBytes(data);
    source = $"{sizeMB} MB random";
}

double mb = data.Length / (1024.0 * 1024.0);
bool isDebug;
#if DEBUG
isDebug = true;
#else
isDebug = false;
#endif

Console.WriteLine($"Data: {source} ({mb:F1} MB), passes: {iterations}");
Console.WriteLine($"Build: {(isDebug ? "DEBUG — results meaningless, use -c Release" : "Release")}");
Console.WriteLine();

// Reference: n16 core (production implementation).
uint reference = Crc32.Compute(data);
Console.WriteLine($"Reference CRC (n16): {reference:X8}");
Console.WriteLine();

// MustMatch=false for Z: standard zlib — intentionally different value.
var variants = new List<(string Name, Func<byte[], uint> Fn, bool MustMatch)>
{
    ("n16 single",      d => Crc32.Compute(d),                    true),
    ("n16 parallel",    d => Crc32.ComputeParallel(d),            true),
    ("v3 slice-by-8",   d => LegacyVariants.V3_SliceBy8(d),       true),
    ("v2 two-table",    d => LegacyVariants.V2_TwoTable(d),       true),
    ("slice-N=2",       d => LegacyVariants.SliceByN(d, 2),       true),
    ("slice-N=4",       d => LegacyVariants.SliceByN(d, 4),       true),
    ("slice-N=8",       d => LegacyVariants.SliceByN(d, 8),       true),
    ("slice-N=16",      d => LegacyVariants.SliceByN(d, 16),      true),
    ("slice-N=32",      d => LegacyVariants.SliceByN(d, 32),      true),
    ("v5 parallel(v3)", d => LegacyVariants.V5_ParallelV3(d),     true),
    ("z lib (zlib)*",   d => LegacyVariants.Z_LibCrc32(d),        false),
};
if (runBitwise)
    variants.Insert(0, ("v1 bitwise", d => LegacyVariants.V1_Bitwise(d), true));

// Correctness check.
var crcValues = new Dictionary<string, uint>();
bool anyMismatch = false;
foreach (var (name, fn, mustMatch) in variants)
{
    uint v = fn(data);
    crcValues[name] = v;
    if (mustMatch && v != reference)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"MISMATCH '{name}': {v:X8} != {reference:X8}");
        Console.ResetColor();
        anyMismatch = true;
    }
}
if (anyMismatch) return 1;
Console.WriteLine("Correctness OK — all variants match (z differs by design).");
Console.WriteLine();

// Benchmark.
Console.WriteLine($"{"variant",-20} {"CRC",10} {"best ms",10} {"avg ms",10} {"MB/s",10} {"speedup",10}");
Console.WriteLine(new string('-', 74));

double? baseline = null;
foreach (var (name, fn, _) in variants)
{
    fn(data); // warm-up / JIT

    double best = double.MaxValue, sum = 0;
    for (int it = 0; it < iterations; it++)
    {
        var sw = Stopwatch.StartNew();
        fn(data);
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;
        if (ms < best) best = ms;
        sum += ms;
    }
    double avg = sum / iterations;
    double mbps = mb / (best / 1000.0);
    baseline ??= best;
    string crc = crcValues[name].ToString("X8");
    Console.WriteLine($"{name,-20} {crc,10} {best,10:F1} {avg,10:F1} {mbps,10:F0} {baseline.Value / best,9:F1}x");
}
Console.WriteLine();
Console.WriteLine("* z — standard zlib CRC-32 (System.IO.Hashing); value differs — not compatible.");
return 0;
