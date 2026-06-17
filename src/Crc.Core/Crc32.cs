using System.Buffers;
using System.Collections.Concurrent;

namespace Crc;

/// <summary>
/// Non-standard CRC-32 with custom convention.
///
/// Uses the standard reflected polynomial 0xEDB88320, but with a different bit-flow:
///   • data bits shift into bit 31 (MSB side), feedback comes from bit 0 (LSB side);
///   • finalization: 32-bit flush (32 zero bits pushed through) then XOR with 0xFFFFFFFF.
///
/// This produces results that differ from the standard zlib/ISO-HDLC CRC-32:
///   "123456789" → 0x22896B0A  (this implementation)
///   "123456789" → 0xCBF43926  (zlib / System.IO.Hashing.Crc32 — NOT compatible)
///
/// The inner loop uses Slice-by-16 (n16): processes 16 bytes per iteration via
/// 20 independent table lookups, maximising instruction-level parallelism.
/// For large inputs <see cref="ComputeParallel(byte[],int)"/> and
/// <see cref="ComputeParallel(Stream,int)"/> split work across CPU cores and
/// stitch partial results with GF(2) matrix arithmetic (O(32 log L) per chunk).
/// </summary>
public static class Crc32
{
    private const uint Init = 0xFFFFFFFF;
    private const uint Poly = 0xEDB88320;

    // Tb[b] = effect of input byte b on state starting from 0 (one byte step).
    private static readonly uint[] Tb = new uint[256];

    // Slice-by-16 tables:
    //   Slice16[j][b]    = M^(15-j)( Tb[b] )  — contribution of byte at position j in a 16-byte block.
    //   StateMix16[k][b] = M^16( b << 8k )     — contribution of byte k of state after 16 steps.
    private static readonly uint[][] Slice16 = new uint[16][];
    private static readonly uint[][] StateMix16 = new uint[4][];

    static Crc32()
    {
        Crc32Combine.Init();

        for (int i = 0; i < 256; i++)
            Tb[i] = Crc32Combine.Step8(0, i);

        for (int j = 0; j < 16; j++)
        {
            Slice16[j] = new uint[256];
            for (int b = 0; b < 256; b++)
                Slice16[j][b] = Mk(Tb[b], 15 - j);
        }
        for (int k = 0; k < 4; k++)
        {
            StateMix16[k] = new uint[256];
            for (int sb = 0; sb < 256; sb++)
                StateMix16[k][sb] = Mk((uint)sb << (8 * k), 16);
        }
    }

    // -------------------------------------------------------------------------
    //  Public API
    // -------------------------------------------------------------------------

    /// <summary>Compute CRC-32 of a byte span (single-threaded, n16 core).</summary>
    public static uint Compute(ReadOnlySpan<byte> data) => Finish(N16Core(Init, data));

    /// <summary>Compute CRC-32 of an array (single-threaded, n16 core).</summary>
    public static uint Compute(byte[] data) => Compute(data.AsSpan());

    /// <summary>
    /// Compute CRC-32 of a stream (single-threaded, streaming, any size).
    /// Uses a pooled 1 MB buffer; the stream is read to completion from its current position.
    /// </summary>
    public static uint Compute(Stream stream)
    {
        uint s = Init;
        byte[] buf = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int read;
            while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                s = N16Core(s, buf.AsSpan(0, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        return Finish(s);
    }

    /// <summary>
    /// Compute CRC-32 of a large in-memory array using multiple CPU cores.
    /// Falls back to single-threaded for small inputs (under 64 KB per thread).
    /// </summary>
    /// <param name="data">Input data.</param>
    /// <param name="degreeOfParallelism">Number of threads (0 = CPU count).</param>
    public static uint ComputeParallel(byte[] data, int degreeOfParallelism = 0)
    {
        if (degreeOfParallelism <= 0) degreeOfParallelism = Environment.ProcessorCount;
        const int MinPerThread = 1 << 16;

        int n = data.Length;
        int parts = Math.Clamp(n / MinPerThread, 1, degreeOfParallelism);
        if (parts <= 1) return Finish(N16Core(Init, data));

        int baseLen = n / parts;
        var partials = new uint[parts];
        var lengths = new int[parts];

        Parallel.For(0, parts, p =>
        {
            int start = p * baseLen;
            int len = (p == parts - 1) ? n - start : baseLen;
            lengths[p] = len;
            partials[p] = N16Core(0u, data.AsSpan(start, len));
        });

        return Crc32Combine.CombineParts(partials, lengths, parts, Init, Finish);
    }

    /// <summary>
    /// Compute CRC-32 of a stream using multiple CPU cores.
    /// A producer thread reads 1 MB chunks into a bounded queue; worker threads
    /// compute partial CRC values; results are stitched in order with GF(2) arithmetic.
    /// Memory usage is bounded to approximately <paramref name="degreeOfParallelism"/> × 2 MB.
    /// </summary>
    /// <param name="stream">Input stream (read from current position).</param>
    /// <param name="degreeOfParallelism">Number of worker threads (0 = CPU count).</param>
    public static uint ComputeParallel(Stream stream, int degreeOfParallelism = 0)
    {
        if (degreeOfParallelism <= 0) degreeOfParallelism = Environment.ProcessorCount;
        const int ChunkSize = 1 << 20; // 1 MB per chunk

        var queue = new BlockingCollection<(int Idx, byte[] Buf, int Len)>(degreeOfParallelism * 2);
        var results = new ConcurrentDictionary<int, (uint Partial, int Len)>();

        var workers = new Task[degreeOfParallelism];
        for (int w = 0; w < degreeOfParallelism; w++)
        {
            workers[w] = Task.Run(() =>
            {
                foreach (var item in queue.GetConsumingEnumerable())
                {
                    uint p = N16Core(0u, item.Buf.AsSpan(0, item.Len));
                    results[item.Idx] = (p, item.Len);
                    ArrayPool<byte>.Shared.Return(item.Buf);
                }
            });
        }

        int idx = 0;
        while (true)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(ChunkSize);
            int total = ReadFull(stream, buf, ChunkSize);
            if (total == 0) { ArrayPool<byte>.Shared.Return(buf); break; }
            queue.Add((idx++, buf, total));
            if (total < ChunkSize) break;
        }
        queue.CompleteAdding();
        Task.WaitAll(workers);

        int count = idx;
        var partials = new uint[count];
        var lengths = new int[count];
        for (int i = 0; i < count; i++)
            (partials[i], lengths[i]) = results[i];

        return Crc32Combine.CombineParts(partials, lengths, count, Init, Finish);
    }

    // -------------------------------------------------------------------------
    //  Internal core (used by CrcService)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Expose N16Core for use by CrcService when stitching multiple buffers.
    /// Starting state must be Init (0xFFFFFFFF) for a fresh computation,
    /// or 0u when computing a partial for later combination.
    /// </summary>
    internal static uint N16Core(uint s, ReadOnlySpan<byte> d)
    {
        int i = 0, lim = d.Length - (d.Length & 15);
        for (; i < lim; i += 16)
        {
            s = StateMix16[0][s & 0xFF]         ^ StateMix16[1][(s >> 8) & 0xFF]
              ^ StateMix16[2][(s >> 16) & 0xFF] ^ StateMix16[3][(s >> 24) & 0xFF]
              ^ Slice16[0][d[i]]      ^ Slice16[1][d[i + 1]]  ^ Slice16[2][d[i + 2]]  ^ Slice16[3][d[i + 3]]
              ^ Slice16[4][d[i + 4]]  ^ Slice16[5][d[i + 5]]  ^ Slice16[6][d[i + 6]]  ^ Slice16[7][d[i + 7]]
              ^ Slice16[8][d[i + 8]]  ^ Slice16[9][d[i + 9]]  ^ Slice16[10][d[i + 10]] ^ Slice16[11][d[i + 11]]
              ^ Slice16[12][d[i + 12]] ^ Slice16[13][d[i + 13]] ^ Slice16[14][d[i + 14]] ^ Slice16[15][d[i + 15]];
        }
        // Tail bytes (< 16) processed one at a time using the V2 formula.
        for (; i < d.Length; i++)
            s = (s >> 8) ^ Crc32Combine.Ts[s & 0xFF] ^ Tb[d[i]];
        return s;
    }

    internal static uint Finish(uint s)
    {
        // 32-bit flush: push 32 zero bits through the LFSR.
        for (int k = 0; k < 32; k++)
        {
            bool bit = (s & 1) != 0;
            s >>= 1;
            s &= 0x7FFFFFFFu;
            if (bit) s ^= Poly;
        }
        return s ^ 0xFFFFFFFF;
    }

    internal static uint InitValue => Init;

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static uint Mk(uint x, int k)
    {
        for (int i = 0; i < k; i++) x = Crc32Combine.PropagateState(x);
        return x;
    }

    private static int ReadFull(Stream s, byte[] buf, int want)
    {
        int total = 0, read;
        while (total < want && (read = s.Read(buf, total, want - total)) > 0)
            total += read;
        return total;
    }
}
