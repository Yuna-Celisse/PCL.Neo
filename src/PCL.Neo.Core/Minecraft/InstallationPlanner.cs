using PCL.Neo.Core.IO.Downloads;
using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Core.Minecraft;

/// <summary>一次安装所需的全部下载项与启动信息。</summary>
public sealed record InstallationPlan(
    string VersionId,
    string MainClass,
    int RequiredJavaMajor,
    IReadOnlyList<DownloadItem> Downloads,
    IReadOnlyList<string> Classpath,
    string? ClientJarPath,
    string NativesDirectory)
{
    /// <summary>按目标路径去重后的字节数上限之和，用于进度与预估。</summary>
    public long? TotalBytes
    {
        get
        {
            long? sum = null;
            foreach (var item in Downloads)
            {
                if (item.ExpectedSize is null) continue;
                sum = (sum ?? 0) + item.ExpectedSize;
            }

            return sum;
        }
    }
}

/// <summary>
/// 把一份已合并的清单翻译成“要下哪些文件、放到哪、按什么顺序拼 classpath”。
/// 纯函数，不碰网络也不碰磁盘，因此可以在离线状态下被完整测试。
/// </summary>
public static class InstallationPlanner
{
    public static InstallationPlan Build(
        VersionInfo version,
        MinecraftLayout layout,
        RuntimeValue runtime,
        AssetIndex? assetIndex = null,
        string libraryRepository = MojangVersionClient.DefaultLibraryRepository,
        string assetsBaseUrl = MojangVersionClient.DefaultAssetsBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(runtime);

        if (string.IsNullOrEmpty(version.MainClass))
            throw new InvalidOperationException($"版本 {version.Id} 缺少 mainClass，无法生成启动计划");

        var downloads = new List<DownloadItem>();
        var classpath = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 1) 主 jar
        string? clientJar = null;
        if (version.Downloads?.Client is { } client && !string.IsNullOrEmpty(client.Url))
        {
            clientJar = layout.VersionJarPath(version.ResolveJarName());
            Add(new DownloadItem(client.Url!, clientJar, client.Sha1, client.Size));
        }

        // 2) 库：规则过滤后按清单顺序加入 classpath
        foreach (var library in version.Libraries)
        {
            if (!library.IsAllowed(runtime)) continue;

            foreach (var artifact in library.ResolveArtifacts(runtime))
            {
                var destination = layout.LibraryPath(artifact.RelativePath);
                var url = artifact.AbsoluteUrl ?? libraryRepository.TrimEnd('/') + "/" + artifact.RelativePath;

                // 同一坐标可能同时出现在原版与 mod loader 清单里，classpath 不能重复
                if (seen.Add(destination)) classpath.Add(destination);
                Add(new DownloadItem(url, destination, artifact.Sha1, artifact.Size));
            }
        }

        // 3) 资源索引与资源对象
        if (version.Downloads?.AssetIndex is { } index && !string.IsNullOrEmpty(index.Url) && version.Assets is { } indexId)
        {
            Add(new DownloadItem(index.Url!, layout.AssetIndexPath(indexId), index.Sha1, index.Size));
        }

        if (assetIndex is not null && version.Assets is { Length: > 0 } assetsId)
        {
            if (assetIndex.MapToResources)
            {
                // legacy 版本要把资源映射回 resources 目录，对象哈希不能直接当路径用。
                // 现代版本不会走到这里；真要做 1.5- 兼容时再补映射表。
                throw new NotSupportedException($"版本 {version.Id} 的资源索引要求 map_to_resources，尚未支持");
            }

            foreach (var asset in assetIndex.Objects.Values)
            {
                if (string.IsNullOrEmpty(asset.Hash)) continue;
                Add(new DownloadItem(
                    assetsBaseUrl.TrimEnd('/') + "/" + asset.ObjectPath,
                    layout.AssetObjectPath(asset.Hash),
                    asset.Hash,          // 资源对象以自身哈希为校验值
                    asset.Size));
            }
        }

        return new InstallationPlan(
            version.Id,
            version.MainClass!,
            version.RequiredJavaMajor,
            downloads,
            classpath,
            clientJar,
            layout.NativesDirectory(version.Id));

        void Add(DownloadItem item)
        {
            // 多个库共用同一份文件（如重复的原生库）时只下一次
            if (seen.Add("DL:" + item.Destination)) downloads.Add(item);
        }
    }
}
