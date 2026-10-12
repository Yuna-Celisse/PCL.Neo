using System.IO.Compression;
using System.Text;
using PCL.Neo.Core.IO;

namespace PCL.Neo.Tests;

/// <summary>
/// 启动器会解压来源不可信的档案（Mojang 库、模组、整合包）。
/// PCL-CE 目前挂着 path traversal 与 library path 两个安全分支，
/// 这些用例就是本项目对应的防线。
/// </summary>
public class SafeArchiveTests
{
    private static string MakeZip(TempDirectory temp, params (string Name, string Content)[] entries)
    {
        var path = temp.PathOf("archive.zip");
        using (var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return path;
    }

    [Theory]
    [InlineData("a/b/c.txt", true)]
    [InlineData("mod.jar", true)]
    [InlineData("./relative.txt", true)]
    [InlineData("a/../b.txt", true)]                 // 归一后仍在根内
    [InlineData("dir\\sub\\file.txt", true)]         // Windows 工具制作的反斜杠
    [InlineData("../escape.txt", false)]             // 目录穿越
    [InlineData("a/../../escape.txt", false)]
    [InlineData("../../escape.txt", false)]
    [InlineData("/etc/passwd", false)]               // 绝对路径
    [InlineData("C:/Windows/system32/evil.dll", false)]
    [InlineData("", false)]
    [InlineData("/", false)]
    public void ResolveSecurePath_Only_Allows_In_Root_Targets(string entryName, bool allowed)
    {
        using var temp = TempDirectory.Create();

        if (allowed)
        {
            var resolved = SafeArchive.ResolveSecurePath(temp.Root, entryName);
            Assert.StartsWith(Path.TrimEndingDirectorySeparator(temp.Root), resolved, StringComparison.Ordinal);
        }
        else
        {
            Assert.ThrowsAny<IOException>(() => SafeArchive.ResolveSecurePath(temp.Root, entryName));
        }
    }

    [Fact]
    public void ExtractZip_Writes_Files_And_Returns_Byte_Count()
    {
        using var temp = TempDirectory.Create();
        var zip = MakeZip(temp, ("mods/a.json", "{\"id\":1}"), ("assets/b.txt", "hello"));
        var target = temp.PathOf("out");

        var total = SafeArchive.ExtractZip(zip, target);

        Assert.Equal(13, total); // {"id":1} = 8B, hello = 5B
        Assert.Equal("{\"id\":1}", File.ReadAllText(Path.Combine(target, "mods", "a.json")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "assets", "b.txt")));
    }

    [Fact]
    public void ExtractZip_Rejects_Traversal_Entry_Without_Escaping_Root()
    {
        using var temp = TempDirectory.Create();
        var zip = MakeZip(temp, ("../escaped.txt", "pwned"));
        var target = temp.PathOf("out");

        Assert.ThrowsAny<IOException>(() => SafeArchive.ExtractZip(zip, target));
        Assert.False(File.Exists(temp.PathOf("escaped.txt")), "文件被写到了根目录之外");
    }

    [Fact]
    public void ExtractZip_Stops_When_Total_Size_Exceeds_Cap()
    {
        using var temp = TempDirectory.Create();
        var zip = MakeZip(temp, ("big.txt", new string('x', 4096)));

        Assert.ThrowsAny<IOException>(
            () => SafeArchive.ExtractZip(zip, temp.PathOf("out"), maxTotalBytes: 64));
    }

    [Fact]
    public void ExtractZip_Rejects_Oversized_Entry()
    {
        using var temp = TempDirectory.Create();
        var zip = MakeZip(temp, ("big.txt", new string('x', 4096)));

        Assert.ThrowsAny<IOException>(
            () => SafeArchive.ExtractZip(zip, temp.PathOf("out"), maxEntryBytes: 128));
    }

    [Fact]
    public void ExtractZip_Without_Overwrite_Keeps_Existing_File()
    {
        using var temp = TempDirectory.Create();
        var zip = MakeZip(temp, ("keep.txt", "new"));
        var target = temp.PathOf("out");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "old");

        Assert.ThrowsAny<IOException>(() => SafeArchive.ExtractZip(zip, target, overwrite: false));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "keep.txt")));

        SafeArchive.ExtractZip(zip, target, overwrite: true);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void CreateZip_Then_Extract_RoundTrips_Chinese_And_Nested_Paths()
    {
        using var temp = TempDirectory.Create();
        var source = temp.PathOf("src");
        Directory.CreateDirectory(Path.Combine(source, "子目录"));
        File.WriteAllText(Path.Combine(source, "子目录", "说明.txt"), "整合包内容");

        var zip = temp.PathOf("pack.zip");
        SafeArchive.CreateZip(source, zip);
        SafeArchive.ExtractZip(zip, temp.PathOf("out"));

        Assert.Equal("整合包内容", File.ReadAllText(Path.Combine(temp.Root, "out", "子目录", "说明.txt")));
    }
}
