using System.Text.Json;
using PCL.Neo.Core.IO.Downloads;
using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Core.Minecraft.Installer;

/// <summary>安装进度与阶段汇报。</summary>
public enum InstallationStage
{
    ResolvingManifest,
    DownloadingAssetIndex,
    PlanningDownloads,
    DownloadingFiles,
    ExtractingNatives,
    Finished
}

public sealed record InstallationProgress(
    InstallationStage Stage,
    string Description,
    int CompletedItems = 0,
    int TotalItems = 0,
    long DownloadedBytes = 0,
    long? TotalBytes = null);

/// <summary>
/// 游戏下载与安装协调器。
/// 将清单拉取、资源索引下载、批量下载与原生库解压无缝串联。
/// </summary>
public sealed class GameInstaller(
    MojangVersionClient versionClient,
    IFileDownloader downloader,
    MinecraftLayout layout)
{
    private readonly MojangVersionClient _versionClient = versionClient ?? throw new ArgumentNullException(nameof(versionClient));
    private readonly IFileDownloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private readonly MinecraftLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    public async Task<InstallationPlan> InstallAsync(
        string versionId,
        VersionManifestDocument? manifest,
        RuntimeValue runtime,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(versionId);
        ArgumentNullException.ThrowIfNull(runtime);

        _layout.EnsureDirectories();

        // 1. 解析版本链
        progress?.Report(new(InstallationStage.ResolvingManifest, $"正在解析版本清单：{versionId}"));
        var resolvedVersion = await _versionClient.ResolveChainAsync(
            versionId,
            manifest,
            localLookup: id =>
            {
                var localJson = _layout.VersionJsonPath(id);
                return File.Exists(localJson) ? VersionInfo.Parse(File.ReadAllText(localJson)) : null;
            },
            cancellationToken).ConfigureAwait(false);

        // 保存合并后的版本 JSON 到本地
        var versionDir = _layout.VersionDirectory(versionId);
        Directory.CreateDirectory(versionDir);
        var targetJsonPath = _layout.VersionJsonPath(versionId);
        await File.WriteAllTextAsync(targetJsonPath, JsonSerializer.Serialize(resolvedVersion, VersionInfo.JsonOptions), cancellationToken)
            .ConfigureAwait(false);

        // 2. 检查并拉取 AssetIndex (如果存在)
        AssetIndex? assetIndex = null;
        if (resolvedVersion.Downloads?.AssetIndex is { } indexSpec && !string.IsNullOrEmpty(indexSpec.Url) && resolvedVersion.Assets is { } assetsId)
        {
            progress?.Report(new(InstallationStage.DownloadingAssetIndex, $"正在同步资源索引：{assetsId}"));
            var localIndexPath = _layout.AssetIndexPath(assetsId);
            if (!File.Exists(localIndexPath) || (indexSpec.Sha1 is not null && !FileMatchesSha1(localIndexPath, indexSpec.Sha1)))
            {
                await _downloader.DownloadAsync(new DownloadItem(indexSpec.Url, localIndexPath, indexSpec.Sha1, indexSpec.Size), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (File.Exists(localIndexPath))
            {
                var indexJson = await File.ReadAllTextAsync(localIndexPath, cancellationToken).ConfigureAwait(false);
                assetIndex = AssetIndex.Parse(indexJson);
            }
        }

        // 3. 构建安装规划
        progress?.Report(new(InstallationStage.PlanningDownloads, "正在生成下载清单..."));
        var plan = InstallationPlanner.Build(resolvedVersion, _layout, runtime, assetIndex);

        // 4. 并发批量下载
        var totalItems = plan.Downloads.Count;
        var completedItems = 0;
        long totalBytesDownloaded = 0;
        var totalBytes = plan.TotalBytes;

        var downloadProgress = new Progress<DownloadResult>(res =>
        {
            Interlocked.Increment(ref completedItems);
            Interlocked.Add(ref totalBytesDownloaded, res.BytesWritten);
            progress?.Report(new(
                InstallationStage.DownloadingFiles,
                $"正在下载文件 ({completedItems}/{totalItems})",
                completedItems,
                totalItems,
                totalBytesDownloaded,
                totalBytes));
        });

        await _downloader.DownloadAllAsync(plan.Downloads, downloadProgress, cancellationToken)
            .ConfigureAwait(false);

        // 5. 解压当前系统专属 Natives 原生库
        progress?.Report(new(InstallationStage.ExtractingNatives, "正在配置原生动态链接库 (Natives)..."));
        var nativesDir = plan.NativesDirectory;
        Directory.CreateDirectory(nativesDir);

        foreach (var lib in resolvedVersion.Libraries)
        {
            if (!lib.IsAllowed(runtime)) continue;
            var classifier = lib.ResolveClassifier(runtime);
            if (classifier is null) continue;

            // 定位原生库本地 jar 文件
            foreach (var artifact in lib.ResolveArtifacts(runtime))
            {
                if (artifact.Classifier == classifier)
                {
                    var nativeJarPath = _layout.LibraryPath(artifact.RelativePath);
                    if (File.Exists(nativeJarPath))
                    {
                        IO.SafeArchive.ExtractZip(nativeJarPath, nativesDir, overwrite: true);
                    }
                }
            }
        }

        progress?.Report(new(InstallationStage.Finished, "安装完成", totalItems, totalItems, totalBytesDownloaded, totalBytes));
        return plan;
    }

    private static bool FileMatchesSha1(string path, string expectedSha1)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(fs));
            return string.Equals(expectedSha1, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
