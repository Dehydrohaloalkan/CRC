using System.Text;
using Xunit;

namespace Crc.Tests;

public class Crc32Tests
{
    // -------------------------------------------------------------------------
    //  Known-answer tests (KAT)
    // -------------------------------------------------------------------------

    [Fact]
    public void KAT_Reference_Vector()
    {
        // "123456789" must produce 0x22896B0A for this custom convention.
        byte[] input = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0x22896B0Au, Crc32.Compute(input));
    }

    [Fact]
    public void KAT_Empty_Input()
    {
        // Empty input: all-zero flush + inversion.
        uint expected = BitwiseReference.Compute([]);
        Assert.Equal(expected, Crc32.Compute([]));
    }

    [Fact]
    public void KAT_Single_Byte()
    {
        for (int b = 0; b < 256; b++)
        {
            byte[] input = [(byte)b];
            Assert.Equal(BitwiseReference.Compute(input), Crc32.Compute(input));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(100)]
    [InlineData(1023)]
    [InlineData(1024)]
    public void KAT_MatchesBitwiseReference(int length)
    {
        byte[] data = MakeData(length, seed: 7);
        Assert.Equal(BitwiseReference.Compute(data), Crc32.Compute(data));
    }

    // -------------------------------------------------------------------------
    //  Stream == Span
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(1 << 20)]   // 1 MB — crosses buffer boundary
    public void Stream_EqualsSpan(int length)
    {
        byte[] data = MakeData(length, seed: 13);
        uint fromSpan   = Crc32.Compute(data.AsSpan());
        uint fromStream = Crc32.Compute(new MemoryStream(data));
        Assert.Equal(fromSpan, fromStream);
    }

    // -------------------------------------------------------------------------
    //  Parallel == Sequential
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1 << 16)]           // exactly MinPerThread
    [InlineData((1 << 16) - 1)]     // just under threshold
    [InlineData((1 << 16) + 1)]     // just over threshold
    [InlineData(1 << 20)]           // 1 MB
    [InlineData(4 * (1 << 20))]     // 4 MB, multi-chunk
    public void ParallelArray_EqualsSequential(int length)
    {
        byte[] data = MakeData(length, seed: 99);
        uint seq = Crc32.Compute(data.AsSpan());
        Assert.Equal(seq, Crc32.ComputeParallel(data, degreeOfParallelism: 1));
        Assert.Equal(seq, Crc32.ComputeParallel(data, degreeOfParallelism: 2));
        Assert.Equal(seq, Crc32.ComputeParallel(data, degreeOfParallelism: 4));
        Assert.Equal(seq, Crc32.ComputeParallel(data, degreeOfParallelism: 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1 << 20)]
    [InlineData(3 * (1 << 20))]
    public void ParallelStream_EqualsSequential(int length)
    {
        byte[] data = MakeData(length, seed: 55);
        uint seq = Crc32.Compute(data.AsSpan());
        Assert.Equal(seq, Crc32.ComputeParallel(new MemoryStream(data), degreeOfParallelism: 2));
        Assert.Equal(seq, Crc32.ComputeParallel(new MemoryStream(data), degreeOfParallelism: 4));
    }

    // -------------------------------------------------------------------------
    //  Chunked streaming (simulates file read in pieces)
    // -------------------------------------------------------------------------

    [Fact]
    public void Chunked_EqualsOnce()
    {
        byte[] data = MakeData(1 << 18, seed: 3);
        // Simulate reading 64 KB at a time.
        uint chunked = Crc32.Compute(new MemoryStream(data));
        Assert.Equal(Crc32.Compute(data.AsSpan()), chunked);
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private static byte[] MakeData(int length, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }
}
