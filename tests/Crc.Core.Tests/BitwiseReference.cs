namespace Crc.Tests;

/// <summary>
/// Standalone bitwise CRC-32 reference implementation used only in tests.
/// Computes the same value as Crc32.Compute but via the original bit-by-bit LFSR loop —
/// no tables, no optimisation. Used to verify that Crc32.Compute and ComputeParallel
/// are bit-for-bit identical to the primitive definition.
/// </summary>
internal static class BitwiseReference
{
    private const uint Poly   = 0xEDB88320;
    private const uint InitVal = 0xFFFFFFFF;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint fcs = InitVal;
        foreach (byte b in data)
            fcs = Step8(fcs, b);
        return Finish(fcs);
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
}
