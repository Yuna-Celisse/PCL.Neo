using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PCL.Neo.Tests;

/// <summary>
/// 测试用的最小 HTTP/1.1 服务器。
/// <para>
/// 不用 <c>HttpListener</c>：它在 Windows 上以非管理员运行时绑定 localhost 会抛
/// AccessDenied，而本项目的 CI 覆盖 win-x64。自己用 <see cref="TcpListener"/> 实现
/// 才能在三个平台上行为一致。
/// </para>
/// 支持：Range 续传、按路径统计请求次数、前 N 次返回指定状态码（测重试）。
/// </summary>
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;

    public TestHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>路径 → 内容。请求未命中的路径返回 404。</summary>
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    /// <summary>路径 → 已处理请求数。</summary>
    public ConcurrentDictionary<string, int> RequestCounts { get; } = new();

    /// <summary>路径 → 前 N 次要返回的状态码（用于测重试）。</summary>
    public ConcurrentDictionary<string, Queue<int>> ForcedFailures { get; } = new();

    /// <summary>是否支持 Range。关掉可验证下载器在源不支持续传时的回退。</summary>
    public bool SupportRange { get; set; } = true;

    public void AddFile(string path, byte[] content) => Files[path] = content;

    public void AddFile(string path, string content) => AddFile(path, Encoding.UTF8.GetBytes(content));

    public int RequestsFor(string path) => RequestCounts.GetValueOrDefault(path, 0);

    public void FailFirstRequests(string path, params int[] statusCodes)
        => ForcedFailures[path] = new Queue<int>(statusCodes);

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { return; }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var requestLine = await ReadLineAsync(stream);
            if (requestLine is null) return;

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return;
            var path = parts[1];

            // 读头部（我们只需要 Range）
            string? rangeHeader = null;
            string? line;
            while ((line = await ReadLineAsync(stream)) is { Length: > 0 })
            {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    rangeHeader = line["Range:".Length..].Trim();
            }

            RequestCounts.AddOrUpdate(path, 1, (_, c) => c + 1);

            if (ForcedFailures.TryGetValue(path, out var failures))
            {
                int status;
                lock (failures) { status = failures.Count > 0 ? failures.Dequeue() : -1; }
                if (status > 0)
                {
                    await WriteAsync(stream, status, "Service Unavailable", Array.Empty<byte>(), null);
                    return;
                }
            }

            if (!Files.TryGetValue(path, out var content))
            {
                await WriteAsync(stream, 404, "Not Found", Array.Empty<byte>(), null);
                return;
            }

            if (SupportRange && rangeHeader is not null
                && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
                && rangeHeader["bytes=".Length..] is { } spec
                && spec.Split('-') is [var startText, ..]
                && int.TryParse(startText, out var start)
                && start < content.Length)
            {
                var slice = content[start..];
                await WriteAsync(stream, 206, "Partial Content", slice,
                    $"bytes {start}-{content.Length - 1}/{content.Length}");
                return;
            }

            await WriteAsync(stream, 200, "OK", content, null);
        }
    }

    private static async Task WriteAsync(
        NetworkStream stream, int status, string reason, byte[] body, string? contentRange)
    {
        var header = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append("Content-Type: application/octet-stream\r\n");

        if (contentRange is not null) header.Append($"Content-Range: {contentRange}\r\n");
        header.Append("Connection: close\r\n\r\n");

        var head = Encoding.ASCII.GetBytes(header.ToString());
        await stream.WriteAsync(head);
        if (body.Length > 0) await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 8192; i++)
        {
            var b = stream.ReadByte();
            if (b == -1) return sb.Length == 0 ? null : sb.ToString();
            if (b == '\n') break;
            if (b != '\r') sb.Append((char)b);
        }

        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _acceptLoop; } catch { /* 关闭期竞态，忽略 */ }
        _stop.Dispose();
    }
}
