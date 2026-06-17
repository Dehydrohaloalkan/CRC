using System.Numerics;

namespace Crc;

/// <summary>
/// GF(2) matrix operations for merging independently computed CRC-32 chunks.
///
/// The CRC update over L bytes from state s can be decomposed as:
///   F(s, data) = M^L(s) XOR F(0, data)
///
/// where M is the linear state-propagation operator (one byte, no input data).
/// This means partial results computed with starting state 0 can be combined
/// sequentially without reprocessing the data.
///
/// M is represented as a 32×32 binary matrix over GF(2), one uint per column.
/// </summary>
internal static class Crc32Combine
{
    private const uint Poly = 0xEDB88320;

    // Lookup table: Ts[b] = effect of low byte b on state after one byte step (no input).
    // Built on first use by Crc32 static constructor — shared via internal access.
    internal static readonly uint[] Ts = new uint[256];

    // ByteMat[i] = M applied to (1 << i): the i-th column of matrix M.
    internal static readonly uint[] ByteMat = new uint[32];

    internal static void Init()
    {
        // Step8: one byte of LFSR division, exact copy of the reference bitwise loop.
        // We build Ts from it so all tables are derived from the same primitive.
        for (int i = 0; i < 256; i++)
            Ts[i] = Step8((uint)i, 0);

        for (int i = 0; i < 32; i++)
            ByteMat[i] = PropagateState(1u << i);
    }

    /// <summary>
    /// Propagates state s through one byte step with zero input data.
    /// M(x) = (x >> 8) XOR Ts[x & 0xFF]
    /// </summary>
    internal static uint PropagateState(uint x) => (x >> 8) ^ Ts[x & 0xFF];

    /// <summary>Apply GF(2) matrix operator to state v.</summary>
    internal static uint ApplyOp(uint[] mat, uint v)
    {
        uint r = 0;
        while (v != 0)
        {
            r ^= mat[BitOperations.TrailingZeroCount(v)];
            v &= v - 1;
        }
        return r;
    }

    /// <summary>Compose two operators: result = a after b.</summary>
    internal static uint[] MatMul(uint[] a, uint[] b)
    {
        var c = new uint[32];
        for (int i = 0; i < 32; i++) c[i] = ApplyOp(a, b[i]);
        return c;
    }

    /// <summary>Raise M to the power of <paramref name="bytes"/> using binary exponentiation — O(32 log bytes).</summary>
    internal static uint[] MatPow(uint[] mat, long bytes)
    {
        // Start with identity matrix.
        var result = new uint[32];
        for (int i = 0; i < 32; i++) result[i] = 1u << i;

        var basePow = (uint[])mat.Clone();
        for (long e = bytes; e > 0; e >>= 1)
        {
            if ((e & 1) != 0) result = MatMul(basePow, result);
            basePow = MatMul(basePow, basePow);
        }
        return result;
    }

    /// <summary>
    /// Sequentially merge partial CRC values (each computed with starting state 0).
    /// s = INIT; for each chunk i: s = M^{len_i}(s) XOR partial_i; return Finish(s).
    /// </summary>
    internal static uint CombineParts(uint[] partials, int[] lengths, int count, uint init, Func<uint, uint> finish)
    {
        if (count == 0) return finish(init);

        uint[] opBase = MatPow(ByteMat, lengths[0]);
        int baseLen = lengths[0];
        uint s = init;
        for (int i = 0; i < count; i++)
        {
            uint[] op = (lengths[i] == baseLen) ? opBase : MatPow(ByteMat, lengths[i]);
            s = ApplyOp(op, s) ^ partials[i];
        }
        return finish(s);
    }

    /// <summary>
    /// Process 8 bits of LFSR division: feedback from bit 0, data bit into bit 31.
    /// This is the reference primitive from which all tables are derived.
    /// </summary>
    internal static uint Step8(uint fcs, int data)
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
}
