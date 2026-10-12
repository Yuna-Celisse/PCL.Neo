using System.Security.Cryptography;
using System.Text;
using PCL.Neo.Core.IO.Downloads;

namespace PCL.Neo.Tests;

/// <summary>
/// 下载引擎的集成测试，跑在本项目的内置 HTTP 服务器上：
/// 镜像回落、校验失败换源、Range 续传、重试这些行为只有真实网络栈才能验准。
/// </summary>
public class FileDownloaderTests
{
    private static string Sha1(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    private static DownloadOptions Options(TestHttpServer server, params MirrorRule[] mirrors) => new()
    {
        Mirrors = mirrors,
        RetryBaseDelay = TimeSpan.FromMilliseconds(5),
        MaxParallelism = 4
    };

    [Fact]
    public async Task Downloads_And_Verifies_Sha1()
    {
        await using var server = new TestHttpServer();
        var payload = Encoding.UTF8.GetBytes("gson-2.10.1 内容");
        server.AddFile("/libs/gson.jar", payload);

        using var temp = TempDirectory.Create();
        var destination = temp.PathOf("libs", "gson.jar");
        var downloader = new FileDownloader(new HttpClient(), Options(server));

        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/libs/gson.jar", destination, Sha1(payload), payload.Length));

        Assert.Equal(DownloadOutcome.Downloaded, result.Outcome);
        Assert.Equal(payload, File.ReadAllBytes(destination));
        Assert.Equal(1, server.RequestsFor("/libs/gson.jar"));
    }

    [Fact]
    public async Task Corrupted_Content_Is_Rejected_And_Leaves_No_File()
    {
        await using var server = new TestHttpServer();
        server.AddFile("/bad.jar", Encoding.UTF8.GetBytes("被篡改的内容"));

        using var temp = TempDirectory.Create();
        var destination = temp.PathOf("bad.jar");
        var downloader = new FileDownloader(new HttpClient(), Options(server));

        var ex = await Assert.ThrowsAsync<DownloadIntegrityException>(() =>
            downloader.DownloadAsync(new DownloadItem(
                server.BaseUrl + "/bad.jar", destination, Sha1("原始内容"u8.ToArray()), null)));

        Assert.False(File.Exists(destination), "校验失败的文件绝不能落地——半真半假的 jar 会让下次启动直接跳过下载");
        Assert.False(File.Exists(destination + ".partial"));
        Assert.NotEqual(ex.Expected, ex.Actual);
    }

    [Fact]
    public async Task Falls_Back_To_Mirror_When_Origin_404s()
    {
        await using var server = new TestHttpServer();
        var payload = Encoding.UTF8.GetBytes("mirror-only");
        server.AddFile("/mirror/lwjgl.jar", payload);

        using var temp = TempDirectory.Create();
        var mirror = new MirrorRule(server.BaseUrl + "/origin/", server.BaseUrl + "/mirror/");
        var downloader = new FileDownloader(new HttpClient(), Options(server, mirror));

        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/origin/lwjgl.jar", temp.PathOf("lwjgl.jar"), Sha1(payload), payload.Length));

        Assert.Equal(DownloadOutcome.DownloadedFromMirror, result.Outcome);
        // 404 属于确定性失败，不该在原地址上重试
        Assert.Equal(1, server.RequestsFor("/origin/lwjgl.jar"));
        Assert.Equal(1, server.RequestsFor("/mirror/lwjgl.jar"));
    }

    [Fact]
    public async Task Switches_Source_On_Integrity_Failure()
    {
        await using var server = new TestHttpServer();
        var good = Encoding.UTF8.GetBytes("正确的库");
        server.AddFile("/origin/asset.bin", Encoding.UTF8.GetBytes("坏镜像的内容"));
        server.AddFile("/mirror/asset.bin", good);

        using var temp = TempDirectory.Create();
        var mirror = new MirrorRule(server.BaseUrl + "/origin/", server.BaseUrl + "/mirror/");
        var downloader = new FileDownloader(new HttpClient(), Options(server, mirror));

        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/origin/asset.bin", temp.PathOf("asset.bin"), Sha1(good), good.Length));

        Assert.Equal(DownloadOutcome.DownloadedFromMirror, result.Outcome);
        Assert.Equal(good, File.ReadAllBytes(temp.PathOf("asset.bin")));
    }

    [Fact]
    public async Task Retries_Transient_Server_Errors()
    {
        await using var server = new TestHttpServer();
        var payload = Encoding.UTF8.GetBytes("after-retry");
        server.AddFile("/flaky.jar", payload);
        server.FailFirstRequests("/flaky.jar", 503, 500);

        using var temp = TempDirectory.Create();
        var downloader = new FileDownloader(new HttpClient(), Options(server));

        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/flaky.jar", temp.PathOf("flaky.jar"), Sha1(payload), payload.Length));

        Assert.Equal(DownloadOutcome.Downloaded, result.Outcome);
        Assert.Equal(3, server.RequestsFor("/flaky.jar"));
    }

    [Fact]
    public async Task Resumes_From_Partial_File()
    {
        await using var server = new TestHttpServer();
        var payload = Encoding.UTF8.GetBytes(new string('x', 5000) + "tail");
        server.AddFile("/big.jar", payload);

        using var temp = TempDirectory.Create();
        var destination = temp.PathOf("big.jar");
        var half = payload[..2500];
        File.WriteAllBytes(destination + ".partial", half);

        var downloader = new FileDownloader(new HttpClient(), Options(server));
        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/big.jar", destination, Sha1(payload), payload.Length));

        Assert.Equal(DownloadOutcome.Resumed, result.Outcome);
        Assert.Equal(payload, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Restarts_From_Scratch_When_Source_Ignores_Range()
    {
        await using var server = new TestHttpServer { SupportRange = false };
        var payload = Encoding.UTF8.GetBytes(new string('y', 3000));
        server.AddFile("/no-range.jar", payload);

        using var temp = TempDirectory.Create();
        var destination = temp.PathOf("no-range.jar");
        File.WriteAllBytes(destination + ".partial", payload[..10]); // 陈旧半截文件

        var downloader = new FileDownloader(new HttpClient(), Options(server));
        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/no-range.jar", destination, Sha1(payload), payload.Length));

        Assert.Equal(DownloadOutcome.Downloaded, result.Outcome);
        // 源不支持 Range 时必须整体覆盖，不能把陈旧半截文件拼上去
        Assert.Equal(payload, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Cache_Hit_Makes_No_Request()
    {
        await using var server = new TestHttpServer();
        var payload = Encoding.UTF8.GetBytes("already-here");
        server.AddFile("/cached.jar", payload);

        using var temp = TempDirectory.Create();
        var destination = temp.PathOf("cached.jar");
        File.WriteAllBytes(destination, payload);

        var downloader = new FileDownloader(new HttpClient(), Options(server));
        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/cached.jar", destination, Sha1(payload), payload.Length));

        Assert.Equal(DownloadOutcome.AlreadyCached, result.Outcome);
        Assert.Equal(0, server.RequestsFor("/cached.jar"));
    }

    [Fact]
    public async Task Stale_Cache_With_Wrong_Hash_Is_Redownloaded()
    {
        await using var server = new TestHttpServer();
        var payload = Encoding.UTF8.GetBytes("fresh");
        server.AddFile("/stale.jar", payload);

        using var temp = TempDirectory.Create();
        var destination = temp.PathOf("stale.jar");
        File.WriteAllBytes(destination, Encoding.UTF8.GetBytes("旧的、大小相同但内容不同"));

        var downloader = new FileDownloader(new HttpClient(), Options(server));
        var result = await downloader.DownloadAsync(new DownloadItem(
            server.BaseUrl + "/stale.jar", destination, Sha1(payload), null));

        Assert.Equal(DownloadOutcome.Downloaded, result.Outcome);
        Assert.Equal(payload, File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task Batch_Download_Handles_Many_Files_Concurrently()
    {
        await using var server = new TestHttpServer();
        using var temp = TempDirectory.Create();

        var items = new List<DownloadItem>();
        for (var i = 0; i < 40; i++)
        {
            var path = $"/libs/lib{i}.jar";
            var payload = Encoding.UTF8.GetBytes($"内容-{i}");
            server.AddFile(path, payload);
            items.Add(new DownloadItem(server.BaseUrl + path, temp.PathOf("libs", $"lib{i}.jar"), Sha1(payload), payload.Length));
        }

        var downloader = new FileDownloader(new HttpClient(), Options(server));
        var results = await downloader.DownloadAllAsync(items);

        Assert.Equal(40, results.Count);
        for (var i = 0; i < 40; i++)
            Assert.Equal($"内容-{i}", File.ReadAllText(temp.PathOf("libs", $"lib{i}.jar")));
    }

    [Fact]
    public async Task Batch_Propagates_Failure_Instead_Of_Silently_Skipping()
    {
        await using var server = new TestHttpServer();
        using var temp = TempDirectory.Create();
        var good = Encoding.UTF8.GetBytes("ok");
        server.AddFile("/ok.jar", good);

        var downloader = new FileDownloader(new HttpClient(), Options(server));
        var items = new[]
        {
            new DownloadItem(server.BaseUrl + "/ok.jar", temp.PathOf("ok.jar"), Sha1(good), good.Length),
            new DownloadItem(server.BaseUrl + "/missing.jar", temp.PathOf("missing.jar"), "deadbeef", null)
        };

        await Assert.ThrowsAnyAsync<IOException>(() => downloader.DownloadAllAsync(items));
    }
}
