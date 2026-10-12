using System.Diagnostics;

namespace PCL.Neo.Core.Platform;

/// <summary>
/// 与桌面环境交互的操作。这里刻意只开放“用系统浏览器打开 http(s) 链接”，
/// 并且强制协议白名单——绝不可把来自远端内容（新闻页、模组描述、崩溃日志）的字符串
/// 直接交给 shell 执行。
/// </summary>
public static class ShellOps
{
    /// <summary>仅允许 http / https，用于校验来自外部内容的链接是否可安全打开。</summary>
    public static bool IsSafeWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// 用系统默认浏览器打开链接。
    /// </summary>
    /// <exception cref="ArgumentException">链接不是 http/https 绝对地址。</exception>
    public static void OpenWebLink(string url)
    {
        if (!IsSafeWebUrl(url))
            throw new ArgumentException($"拒绝打开非 http(s) 链接: {url}", nameof(url));

        using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
