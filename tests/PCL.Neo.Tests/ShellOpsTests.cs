using PCL.Neo.Core.Platform;

namespace PCL.Neo.Tests;

/// <summary>
/// 启动器会渲染来自远端的内容（新闻、模组描述、崩溃日志）。
/// 这些字符串绝不能变成执行入口，因此链接白名单必须有回归测试。
/// </summary>
public class ShellOpsTests
{
    [Theory]
    [InlineData("https://meloong.com/afd/a/LTCat", true)]
    [InlineData("http://example.com", true)]
    [InlineData("HTTPS://EXAMPLE.COM", true)]
    [InlineData("file:///C:/Windows/system32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("cmd:// /c calc", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("relative/path.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Http_And_Https_Are_Allowed(string? url, bool expected)
    {
        Assert.Equal(expected, ShellOps.IsSafeWebUrl(url));
    }

    [Fact]
    public void OpenWebLink_Rejects_Unsafe_Url_Without_Launching()
    {
        Assert.Throws<ArgumentException>(() => ShellOps.OpenWebLink("file:///etc/passwd"));
    }
}
