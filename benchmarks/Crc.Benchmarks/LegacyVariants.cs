using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;

namespace Crc.Benchmarks;

/// <summary>
/// Alternative CRC-32 implementations kept for comparison benchmarks.
/// All variants compute the same value as <see cref="Crc32.Compute"/> —
/// they differ only in speed and memory access pattern.
///
/// These are NOT part of the production library. Use <see cref="Crc32"/> from Crc.Core.
/// </summary>
internal static class LegacyVariants
{
    private const uint Poly = 0xEDB88320;
    private const uint InitVal = 0xFFFFFFFF;

    // V1/V2 tables
    private static readonly uint[] Ts = new uint[256]; // state contribution per byte step
    private static readonly uint[] Tb = new uint[256]; // input byte contribution per step

    // V3 (slice-by-8) tables
    private static readonly uint[][] Slice8 = new uint[8][];
    private static readonly uint[][] StateMix8 = new uint[4][];

    // Generic slice-by-N cache
    private static readonly Dictionary<int, (uint[] StateMix, uint[] Slice)> SliceCache = new();

    // GF(2) column matrix for V5 parallel combine
    private static readonly uint[] ByteMat = new uint[32];

    static LegacyVariants()
    {
        for (int i = 0; i < 256; i++)
        {
            Ts[i] = Step8((uint)i, 0);
            Tb[i] = Step8(0, i);
        }
        for (int j = 0; j < 8; j++)
        {
            Slice8[j] = new uint[256];
            for (int b = 0; b < 256; b++)
                Slice8[j][b] = Mk(Tb[b], 7 - j);
        }
        for (int k = 0; k < 4; k++)
        {
            StateMix8[k] = new uint[256];
            for (int sb = 0; sb < 256; sb++)
                StateMix8[k][sb] = Mk((uint)sb << (8 * k), 8);
        }
        for (int i = 0; i < 32; i++)
            ByteMat[i] = M(1u << i);
    }

    // -------------------------------------------------------------------------
    //  V1 — Bitwise (reference, slowest)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pure bitwise LFSR division. Exact reference implementation.
    /// ~8 branches + shifts per byte — very slow, used as correctness reference.
    /// </summary>
    public static uint V1_Bitwise(ReadOnlySpan<byte> data)
    {
        uint fcs = InitVal;
        foreach (byte b in data)
            fcs = Step8(fcs, b);
        return Finish(fcs);
    }

    // -------------------------------------------------------------------------
    //  V2 — Two-table (one byte per iteration)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two-table byte-at-a-time: fcs = (fcs >> 8) XOR Ts[fcs &amp; 0xFF] XOR Tb[byte].
    /// ~27× faster than V1 on typical hardware.
    /// </summary>
    public static uint V2_TwoTable(ReadOnlySpan<byte> data)
    {
        uint fcs = InitVal;
        foreach (byte b in data)
            fcs = (fcs >> 8) ^ Ts[fcs & 0xFF] ^ Tb[b];
        return Finish(fcs);
    }

    // -------------------------------------------------------------------------
    //  V3 — Slice-by-8 (hand-unrolled, 8 bytes per iteration)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Slice-by-8: 12 independent table lookups per 8-byte block, hiding memory latency.
    /// ~60× faster than V1.
    /// </summary>
    public static uint V3_SliceBy8(ReadOnlySpan<byte> data) => Finish(V3Core(InitVal, data));

    // -------------------------------------------------------------------------
    //  V5 — Parallel (array overload using V3 core)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Multi-threaded variant using V3 as the per-chunk core, with GF(2) matrix
    /// stitching. Kept here as a baseline to compare against the new Crc.Core
    /// parallel implementation (which uses the n16 core instead).
    /// </summary>
    public static uint V5_ParallelV3(byte[] data, int dop = 0)
    {
        if (dop <= 0) dop = Environment.ProcessorCount;
        const int MinPerThread = 1 << 16;
        int n = data.Length;
        int parts = Math.Clamp(n / MinPerThread, 1, dop);
        if (parts <= 1) return Finish(V3Core(InitVal, data));

        int baseLen = n / parts;
        var partials = new uint[parts];
        var lens = new int[parts];
        Parallel.For(0, parts, p =>
        {
            int start = p * baseLen;
            int len = (p == parts - 1) ? n - start : baseLen;
            lens[p] = len;
            partials[p] = V3Core(0u, data.AsSpan(start, len));
        });
        return CombineParts(partials, lens, parts);
    }

    // -------------------------------------------------------------------------
    //  Slice-by-N (generic, parametric width)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generic slice-by-N: shows speed as a function of block width W.
    /// Uses a loop (not hand-unrolled), so absolute speed is lower than
    /// the hand-rolled V3 or Crc32 (n16) at the same width.
    /// </summary>
    public static uint SliceByN(ReadOnlySpan<byte> data, int width)
    {
        var (stateMix, slice) = GetSliceTables(width);
        uint s = InitVal;
        int i = 0, lim = data.Length - data.Length % width;
        for (; i < lim; i += width)
        {
            uint acc = stateMix[s & 0xFF] ^ stateMix[256 + ((s >> 8) & 0xFF)]
                     ^ stateMix[512 + ((s >> 16) & 0xFF)] ^ stateMix[768 + ((s >> 24) & 0xFF)];
            for (int j = 0; j < width; j++)
                acc ^= slice[(j << 8) + data[i + j]];
            s = acc;
        }
        for (; i < data.Length; i++)
            s = (s >> 8) ^ Ts[s & 0xFF] ^ Tb[data[i]];
        return Finish(s);
    }

    // -------------------------------------------------------------------------
    //  Z — Standard zlib CRC-32 (System.IO.Hashing) — DIFFERENT value
    // -------------------------------------------------------------------------

    /// <summary>
    /// Standard zlib/ISO-HDLC CRC-32 from System.IO.Hashing.
    /// Same polynomial, different convention: bytes XOR into low bits, no flush.
    /// "123456789" → 0xCBF43926 (NOT 0x22896B0A) — incompatible with Crc32.Compute.
    /// Included only as an upper-bound speed reference.
    /// </summary>
    public static uint Z_LibCrc32(ReadOnlySpan<byte> data)
        => System.IO.Hashing.Crc32.HashToUInt32(data);

    // -------------------------------------------------------------------------
    //  Private helpers
    // -------------------------------------------------------------------------

    private static uint V3Core(uint s, ReadOnlySpan<byte> d)
    {
        int i = 0, lim = d.Length - (d.Length & 7);
        for (; i < lim; i += 8)
        {
            s = StateMix8[0][s & 0xFF] ^ StateMix8[1][(s >> 8) & 0xFF]
              ^ StateMix8[2][(s >> 16) & 0xFF] ^ StateMix8[3][(s >> 24) & 0xFF]
              ^ Slice8[0][d[i]]     ^ Slice8[1][d[i + 1]] ^ Slice8[2][d[i + 2]] ^ Slice8[3][d[i + 3]]
              ^ Slice8[4][d[i + 4]] ^ Slice8[5][d[i + 5]] ^ Slice8[6][d[i + 6]] ^ Slice8[7][d[i + 7]];
        }
        for (; i < d.Length; i++)
            s = (s >> 8) ^ Ts[s & 0xFF] ^ Tb[d[i]];
        return s;
    }

    private static (uint[] StateMix, uint[] Slice) GetSliceTables(int w)
    {
        lock (SliceCache)
        {
            if (SliceCache.TryGetValue(w, out var t)) return t;
            var sm = new uint[4 * 256];
            for (int k = 0; k < 4; k++)
                for (int sb = 0; sb < 256; sb++)
                    sm[k * 256 + sb] = Mk((uint)sb << (8 * k), w);
            var sl = new uint[w * 256];
            for (int j = 0; j < w; j++)
                for (int b = 0; b < 256; b++)
                    sl[j * 256 + b] = Mk(Tb[b], w - 1 - j);
            t = (sm, sl);
            SliceCache[w] = t;
            return t;
        }
    }

    private static uint CombineParts(uint[] partial, int[] lens, int count)
    {
        if (count == 0) return Finish(InitVal);
        uint[] opBase = MatPow(ByteMat, lens[0]);
        int baseLen = lens[0];
        uint s = InitVal;
        for (int i = 0; i < count; i++)
        {
            uint[] op = (lens[i] == baseLen) ? opBase : MatPow(ByteMat, lens[i]);
            s = ApplyOp(op, s) ^ partial[i];
        }
        return Finish(s);
    }

    private static uint Finish(uint fcs)
    {
        for (int k = 0; k < 32; k++)
        {
            bool bit = (fcs & 1) != 0;
            fcs >>= 1;
            fcs &= 0x7FFFFFFFu;
            if (bit) fcs ^= Poly;
        }
        return fcs ^ 0xFFFFFFFF;
    }

    private static uint Step8(uint fcs, int data)
    {
        for (int k = 0; k < 8; k++)
        {
            bool bit = (fcs & 1) != 0;
            fcs >>= 1;
            if ((data & 1) != 0) fcs |= 0x80000000u; else fcs &= 0x7FFFFFFFu;
            if (bit) fcs ^= Poly;
            data >>= 1;
        }
        return fcs;
    }

    private static uint M(uint x) => (x >> 8) ^ Ts[x & 0xFF];
    private static uint Mk(uint x, int k) { for (int i = 0; i < k; i++) x = M(x); return x; }

    private static uint ApplyOp(uint[] mat, uint v)
    {
        uint r = 0;
        while (v != 0) { r ^= mat[BitOperations.TrailingZeroCount(v)]; v &= v - 1; }
        return r;
    }

    private static uint[] MatMul(uint[] a, uint[] b)
    {
        var c = new uint[32];
        for (int i = 0; i < 32; i++) c[i] = ApplyOp(a, b[i]);
        return c;
    }

    private static uint[] MatPow(uint[] mat, long bytes)
    {
        var result = new uint[32];
        for (int i = 0; i < 32; i++) result[i] = 1u << i;
        var bp = (uint[])mat.Clone();
        for (long e = bytes; e > 0; e >>= 1)
        {
            if ((e & 1) != 0) result = MatMul(bp, result);
            bp = MatMul(bp, bp);
        }
        return result;
    }
}
