namespace PCL.Neo.Tests;

/// <summary>每个测试独享的临时目录，Dispose 时尽力清理。</summary>
internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string root) => Root = root;

    public string Root { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pclneo-tests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public string PathOf(params string[] parts)
        => System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* 清理失败不影响测试结论 */ }
        catch (UnauthorizedAccessException) { }
    }
}
