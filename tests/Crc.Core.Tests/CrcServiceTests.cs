using System.Text;
using Xunit;

namespace Crc.Tests;

public class CrcServiceTests : IDisposable
{
    private readonly string _root;

    public CrcServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"crc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // -------------------------------------------------------------------------
    //  Single file
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleFile_CrcMatchesDirectCompute()
    {
        byte[] content = Encoding.UTF8.GetBytes("hello world");
        string path = WriteFile("a.txt", content);

        var results = CrcService.Process(path);

        Assert.Single(results);
        Assert.Equal(Crc32.Compute(content).ToString("X8"), results[0].Crc);
    }

    [Fact]
    public void SingleFile_RelativePath_IsJustFileName()
    {
        string path = WriteFile("test.bin", [1, 2, 3]);
        var results = CrcService.Process(path);
        Assert.Equal("test.bin", results[0].RelativePath);
    }

    // -------------------------------------------------------------------------
    //  Directory
    // -------------------------------------------------------------------------

    [Fact]
    public void Directory_AllFilesIncluded()
    {
        WriteFile("b.txt", [10]);
        WriteFile("a.txt", [20]);
        WriteFile("c.txt", [30]);

        var results = CrcService.Process(_root);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Directory_DeterministicOrder()
    {
        WriteFile("z.txt", [1]);
        WriteFile("a.txt", [2]);
        WriteFile("m.txt", [3]);

        var r1 = CrcService.Process(_root).Select(f => f.RelativePath).ToList();
        var r2 = CrcService.Process(_root).Select(f => f.RelativePath).ToList();
        Assert.Equal(r1, r2);
        Assert.Equal(["a.txt", "m.txt", "z.txt"], r1);
    }

    [Fact]
    public void Directory_Recursive_SubdirSortedFirst()
    {
        string sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "nested.bin"), [7]);
        WriteFile("root.txt", [8]);

        var results = CrcService.Process(_root);
        Assert.Equal(2, results.Count);
        // Sub-directory files come before root files (recursive traversal: dirs first).
        Assert.Contains("nested.bin", results[0].RelativePath);
        Assert.Contains("root.txt",   results[1].RelativePath);
    }

    [Fact]
    public void Directory_Empty_ReturnsEmpty()
    {
        var results = CrcService.Process(_root);
        Assert.Empty(results);
    }

    [Fact]
    public void Directory_CrcValues_MatchDirectCompute()
    {
        byte[] content = [0xDE, 0xAD, 0xBE, 0xEF];
        WriteFile("file.bin", content);

        var results = CrcService.Process(_root);
        string expected = Crc32.Compute(content).ToString("X8");
        Assert.Equal(expected, results[0].Crc);
    }

    // -------------------------------------------------------------------------
    //  CrcProtocol
    // -------------------------------------------------------------------------

    [Fact]
    public void Protocol_WritesCorrectFormat()
    {
        WriteFile("x.bin", [1, 2]);
        WriteFile("y.bin", [3, 4]);

        var results = CrcService.Process(_root);
        string outFile = Path.Combine(_root, "out.txt");
        CrcProtocol.Write(results, outFile);

        var lines = File.ReadAllLines(outFile, Encoding.UTF8);
        Assert.Equal(2, lines.Length);
        foreach (string line in lines)
            Assert.Matches(@"^.+=[\dA-F]{8}$", line);
    }

    [Fact]
    public void Protocol_Utf8_NoByteOrderMark()
    {
        WriteFile("f.bin", [0]);
        var results = CrcService.Process(_root);
        string outFile = Path.Combine(_root, "out.txt");
        CrcProtocol.Write(results, outFile);

        byte[] raw = File.ReadAllBytes(outFile);
        // UTF-8 BOM is EF BB BF — must not be present.
        Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF);
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private string WriteFile(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
