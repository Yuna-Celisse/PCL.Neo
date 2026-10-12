using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Core.Minecraft;

/// <summary>
/// 拉取 Mojang 元数据并解析版本继承链。基址可注入，便于测试与镜像替换。
/// </summary>
public sealed class MojangVersionClient(
    HttpClient httpClient,
    string metaBaseUrl = "https://piston-meta.mojang.com")
{
    public const string DefaultMetaBaseUrl = "https://piston-meta.mojang.com";

    /// <summary>官方 Maven 仓库；清单没给绝对地址的库走这里。</summary>
    public const string DefaultLibraryRepository = "https://libraries.minecraft.net/";

    /// <summary>资源对象仓库，按哈希前两位分目录。</summary>
    public const string DefaultAssetsBaseUrl = "https://resources.download.minecraft.net/";

    public const int MaxChainDepth = 8;

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string MetaBaseUrl { get; } = metaBaseUrl.TrimEnd('/');

    public async Task<VersionManifestDocument> GetVersionListAsync(CancellationToken cancellationToken = default)
    {
        var json = await _httpClient
            .GetStringAsync($"{MetaBaseUrl}/mc/game/version_manifest_v2.json", cancellationToken)
            .ConfigureAwait(false);
        return VersionManifestDocument.Parse(json);
    }

    public async Task<VersionInfo> GetVersionAsync(string url, CancellationToken cancellationToken = default)
    {
        var json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return VersionInfo.Parse(json);
    }

    public async Task<AssetIndex> GetAssetIndexAsync(string url, CancellationToken cancellationToken = default)
    {
        var json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return AssetIndex.Parse(json);
    }

    /// <summary>
    /// 沿 <c>inheritsFrom</c> 链解析出可直接使用的完整清单。
    /// <para>
    /// Forge / Fabric / NeoForge 的清单只写增量，必须与原版合并才能启动；
    /// 而且它们通常<b>不在</b> Mojang 的版本列表里（是本机安装出来的 json），
    /// 所以要通过 <paramref name="localLookup"/> 提供本地读取能力。
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">链断裂、成环或超过深度上限。</exception>
    public async Task<VersionInfo> ResolveChainAsync(
        string versionId,
        VersionManifestDocument? manifest,
        Func<string, VersionInfo?>? localLookup = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(versionId);

        var chain = new List<VersionInfo>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = versionId;

        // 逐级向上收集，直到某个版本没有 InheritsFrom。
        // 注意：没有父版本是常态，不是错误——早期实现会在这里误抛异常。
        while (!string.IsNullOrEmpty(current))
        {
            if (!visited.Add(current))
                throw new InvalidOperationException($"版本清单存在循环继承：{current}");

            if (chain.Count >= MaxChainDepth)
                throw new InvalidOperationException($"版本继承层级超过 {MaxChainDepth}，拒绝继续解析");

            // 本地已安装的版本优先：mod loader 的清单不在官方版本列表里
            var info = localLookup?.Invoke(current);
            if (info is null)
            {
                var entry = manifest?.Find(current)
                            ?? throw new InvalidOperationException($"版本列表中找不到 {current}");
                info = await GetVersionAsync(entry.Url, cancellationToken).ConfigureAwait(false);
            }

            chain.Add(info);
            current = info.InheritsFrom;
        }

        // chain = [子, 父, 祖父]。合并必须<b>从子版本出发</b>向上逐级补齐：
        // 反过来写会让父版本去继承子版本，导致 mainClass、参数顺序、库顺序全部颠倒。
        var result = chain[0];
        for (var i = 1; i < chain.Count; i++)
            result.InheritFrom(chain[i]);

        // InheritFrom 只搬运字段，不影响 Id；这里显式回到被请求的子版本
        result.Id = versionId;
        return result;
    }
}
