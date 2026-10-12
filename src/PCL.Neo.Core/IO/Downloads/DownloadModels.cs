namespace PCL.Neo.Core.IO.Downloads;

/// <summary>一条镜像改写规则：把规范地址的前缀换成镜像前缀。</summary>
/// <param name="MatchPrefix">规范仓库前缀，例如 <c>https://libraries.minecraft.net/</c>。</param>
/// <param name="ReplacePrefix">镜像前缀，必须以 <c>/</c> 结尾。</param>
public sealed record MirrorRule(string MatchPrefix, string ReplacePrefix)
{
    public string? Apply(string url)
        => url.StartsWith(MatchPrefix, StringComparison.OrdinalIgnoreCase)
            ? ReplacePrefix + url[MatchPrefix.Length..]
            : null;
}

/// <summary>一个待下载文件。</summary>
/// <param name="Url">规范地址。</param>
/// <param name="Destination">落地绝对路径。</param>
/// <param name="Sha1">期望哈希；为空表示无法校验（旧清单缺哈希）。</param>
/// <param name="ExpectedSize">期望大小，用于快速失败与进度。</param>
public sealed record DownloadItem(string Url, string Destination, string? Sha1 = null, long? ExpectedSize = null);

/// <summary>下载结果。</summary>
public enum DownloadOutcome
{
    /// <summary>目标已存在且哈希一致，未发起请求。</summary>
    AlreadyCached,
    /// <summary>从规范地址下载成功。</summary>
    Downloaded,
    /// <summary>回落到镜像后成功。</summary>
    DownloadedFromMirror,
    /// <summary>续传成功。</summary>
    Resumed
}

public sealed record DownloadResult(string Url, string Destination, long BytesWritten, DownloadOutcome Outcome);

/// <summary>下载失败（网络、HTTP 状态、重试耗尽）。<paramref name="retryable"/> 决定是否值得再试。</summary>
public sealed class DownloadFailedException(string url, string message, bool retryable = true, Exception? inner = null)
    : IOException($"下载失败 {url}：{message}", inner)
{
    public bool Retryable { get; } = retryable;
}

/// <summary>
/// 完整性校验失败。与网络错误分开，因为它的处置方式完全不同：
/// 网络错误该重试，哈希不符说明源被污染或中间人，必须换源并明确报警。
/// </summary>
public sealed class DownloadIntegrityException(string url, string expected, string actual)
    : IOException($"校验失败 {url}：期望 SHA1 {expected}，实际 {actual}")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}
