using System.Security.Cryptography;
using System.Text;

namespace PCL.Neo.Core.IO.Downloads;

public sealed class DownloadOptions
{
    /// <summary>单个地址的最大尝试次数（不含换源）。</summary>
    public int MaxAttemptsPerUrl { get; init; } = 3;

    /// <summary>并发上限。库文件数量大，串行会慢到不可用。</summary>
    public int MaxParallelism { get; init; } = 8;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>镜像改写规则，按顺序作为备选源。</summary>
    public IReadOnlyList<MirrorRule> Mirrors { get; init; } = [];

    /// <summary>是否用 Range 续传半截文件。</summary>
    public bool EnableResume { get; init; } = true;
}

public interface IFileDownloader
{
    Task<DownloadResult> DownloadAsync(DownloadItem item, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
        IEnumerable<DownloadItem> items,
        IProgress<DownloadResult>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 带完整性校验的下载器。
/// <para>
/// 与“能下下来就行”的下载器相比，这里坚持三条：
/// </para>
/// <list type="bullet">
/// <item><b>哈希不符要换源</b>而不是直接失败——单个镜像坏掉是常态；
/// 同时校验失败也是投毒/中间人唯一可检测的信号，因此绝不静默接受。</item>
/// <item><b>先写 .partial 再原子改名</b>：崩溃或断网不会留下半个 jar，
/// 否则下次启动会因为“文件已存在”跳过下载，然后被损坏的 jar 卡死。</item>
/// <item>已存在且哈希一致的文件直接命中缓存，一个请求都不发。</item>
/// </list>
/// </summary>
public sealed class FileDownloader(HttpClient httpClient, DownloadOptions? options = null) : IFileDownloader
{
    private const string PartialSuffix = ".partial";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly DownloadOptions _options = options ?? new DownloadOptions();

    public Task<DownloadResult> DownloadAsync(DownloadItem item, CancellationToken cancellationToken = default)
        => DownloadCoreAsync(item, cancellationToken);

    public async Task<IReadOnlyList<DownloadResult>> DownloadAllAsync(
        IEnumerable<DownloadItem> items,
        IProgress<DownloadResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var queue = new System.Collections.Concurrent.ConcurrentQueue<DownloadItem>(items);
        var results = new System.Collections.Concurrent.ConcurrentBag<DownloadResult>();
        var workers = new List<Task>();

        // 固定 worker 数 + 共享队列：比给每个文件开一个 Task 更可控，
        // 数千个库文件时不会把连接池和线程池打满。
        var workerCount = Math.Max(1, _options.MaxParallelism);
        for (var i = 0; i < workerCount; i++)
        {
            workers.Add(Task.Run(async () =>
            {
                while (queue.TryDequeue(out var item))
                {
                    var result = await DownloadCoreAsync(item, cancellationToken).ConfigureAwait(false);
                    results.Add(result);
                    progress?.Report(result);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        return results.ToList();
    }

    private async Task<DownloadResult> DownloadCoreAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Url)) throw new ArgumentException("下载地址为空", nameof(item));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(item.Destination))!);

        if (IsSatisfied(item, out var cachedBytes))
            return new DownloadResult(item.Url, item.Destination, cachedBytes, DownloadOutcome.AlreadyCached);

        IOException? lastFailure = null;

        foreach (var (url, isMirror) in BuildCandidates(item.Url))
        {
            for (var attempt = 1; attempt <= _options.MaxAttemptsPerUrl; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await TryDownloadAsync(item, url, isMirror, cancellationToken).ConfigureAwait(false);
                }
                catch (DownloadIntegrityException ex)
                {
                    lastFailure = ex;
                    break; // 换源，不再重试同一地址
                }
                catch (DownloadFailedException ex)
                {
                    lastFailure = ex;
                    if (!ex.Retryable || attempt == _options.MaxAttemptsPerUrl) break;
                    await Task.Delay(_options.RetryBaseDelay * attempt, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw lastFailure ?? new DownloadFailedException(item.Url, "所有可用地址均已尝试");
    }

    private async Task<DownloadResult> TryDownloadAsync(
        DownloadItem item, string url, bool isMirror, CancellationToken cancellationToken)
    {
        var partial = item.Destination + PartialSuffix;
        var offset = _options.EnableResume && File.Exists(partial) ? new FileInfo(partial).Length : 0;

        if (item.ExpectedSize is { } ceiling && offset > ceiling) offset = 0; // 半截文件比目标大，内容不对，丢掉

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));
        if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);

        using var response = await SendAsync(request, url, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw new DownloadFailedException(url, "远端不存在该文件 (404)", retryable: false);
        }

        if (response.StatusCode != System.Net.HttpStatusCode.OK
            && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            throw new DownloadFailedException(url, $"HTTP {(int)response.StatusCode}");
        }

        var appending = response.StatusCode == System.Net.HttpStatusCode.PartialContent && offset > 0;
        if (offset > 0 && !appending) offset = 0; // 源不支持 Range，从头覆盖

        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            // 顺序很重要：必须在以独占方式打开 .partial 之前先把已有内容算进哈希，
            // 否则就是自己锁自己（续传路径会 100% 抛 IOException）。
            var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            if (appending) HashPrefix(partial, hasher);

            long written;
            string actual;

            // 句柄必须在这个作用域结束前彻底关闭：Linux 的 rename()/unlink() 允许
            // 文件仍处于打开状态，Windows 则会直接抛“正由另一进程使用”。
            // 也就是说，如果把下面的 Move/Delete 留在 using 内部，
            // Windows 上每一次成功下载都会失败 —— 只有 win-x64 的 CI 能发现这件事。
            {
                using var file = new FileStream(partial, appending ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                int read;
                written = 0;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, read);
                    written += read;
                }

                file.Flush(true);
                actual = ToHex(hasher.GetHashAndReset());
            }

            var total = offset + written;

            if (item.ExpectedSize is { } size && total != size)
                throw new DownloadFailedException(url, $"大小不符：期望 {size}，实际 {total}", retryable: false);

            if (item.Sha1 is { Length: > 0 } expectedSha1
                && !string.Equals(expectedSha1, actual, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partial);
                throw new DownloadIntegrityException(url, expectedSha1, actual);
            }

            File.Move(partial, item.Destination, overwrite: true);
            return new DownloadResult(url, item.Destination, total,
                appending ? DownloadOutcome.Resumed
                : isMirror ? DownloadOutcome.DownloadedFromMirror
                : DownloadOutcome.Downloaded);
        }
    }

    /// <summary>清理残留半截文件。删不掉也不能盖掉原始异常，否则真正的故障会被掩盖。</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new DownloadFailedException(url, ex.Message, retryable: true, inner: ex);
        }
    }

    /// <summary>规范地址在前，镜像按声明顺序追加。</summary>
    private IEnumerable<(string Url, bool IsMirror)> BuildCandidates(string url)
    {
        yield return (url, false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { url };
        foreach (var mirror in _options.Mirrors)
        {
            var candidate = mirror.Apply(url);
            if (candidate is not null && seen.Add(candidate))
                yield return (candidate, true);
        }
    }

    private bool IsSatisfied(DownloadItem item, out long bytes)
    {
        bytes = 0;
        if (!File.Exists(item.Destination)) return false;
        bytes = new FileInfo(item.Destination).Length;

        if (item.ExpectedSize is { } expected && bytes != expected) return false;
        if (item.Sha1 is not { Length: > 0 }) return item.ExpectedSize is not null;

        using var stream = File.OpenRead(item.Destination);
        return string.Equals(item.Sha1, ToHex(SHA1.HashData(stream)), StringComparison.OrdinalIgnoreCase);
    }

    private static void HashPrefix(string path, IncrementalHash hasher)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hasher.AppendData(buffer, 0, read);
    }

    private static string ToHex(ReadOnlySpan<byte> hash)
    {
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
