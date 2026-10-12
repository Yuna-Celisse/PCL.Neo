namespace PCL.Neo.Core.Minecraft.Manifest;

public sealed class LibraryDownloads
{
    public DownloadSpec? Artifact { get; set; }

    /// <summary>按 classifier 索引的附加产物（原生库、sources、maps）。</summary>
    public Dictionary<string, DownloadSpec>? Classifiers { get; set; }
}

public sealed class ExtractSpec
{
    /// <summary>解压原生库时要跳过的路径前缀，通常是 META-INF/（签名冲突会导致游戏起不来）。</summary>
    public List<string> Exclude { get; set; } = [];
}

/// <summary>解析出的一个具体下载项。</summary>
/// <param name="Classifier">原生 classifier；主产物为 null。</param>
/// <param name="RelativePath">相对 Maven 仓库的路径。</param>
/// <param name="AbsoluteUrl">清单直接给出的绝对地址（新版清单都有）。</param>
/// <param name="Sha1">期望哈希；旧格式可能缺失，此时调用方必须降级为“不校验”并记录。</param>
/// <param name="Size">期望字节数，用于进度与快速失败。</param>
public sealed record LibraryArtifact(
    string? Classifier,
    string RelativePath,
    string? AbsoluteUrl,
    string? Sha1,
    long? Size);

/// <summary>
/// 一条库依赖。新旧两种写法都要支持：
/// 新版给 <c>downloads.artifact</c> / <c>downloads.classifiers</c>；
/// 旧版只给 <c>natives</c> 映射（值里的 <c>${arch}</c> 需替换）与可选的 <c>url</c> 基址。
/// </summary>
public sealed class LibraryInfo
{
    /// <summary>Maven 坐标 <c>group:artifact:version[:classifier]</c>。</summary>
    public string Name { get; set; } = "";

    public List<Rule>? Rules { get; set; }
    public LibraryDownloads? Downloads { get; set; }

    /// <summary>旧格式：os 名 → classifier 模板。</summary>
    public Dictionary<string, string>? Natives { get; set; }

    public ExtractSpec? Extract { get; set; }

    /// <summary>旧格式的仓库基址（如 <c>https://libraries.minecraft.net/</c>）。</summary>
    public string? Url { get; set; }

    public MavenCoordinate Coordinate => MavenCoordinate.Parse(Name);

    public bool IsAllowed(RuntimeValue runtime) => RuleEvaluator.IsAllowed(Rules, runtime);

    /// <summary>当前平台对应的原生 classifier；非原生库返回 null。</summary>
    public string? ResolveClassifier(RuntimeValue runtime)
    {
        if (Natives is null) return null;
        if (!Natives.TryGetValue(runtime.OsName, out var template)) return null;
        return template.Replace("${arch}", runtime.ArchBits, StringComparison.Ordinal);
    }

    /// <summary>
    /// 当前平台实际需要下载的产物集合。调用前应先用 <see cref="IsAllowed"/> 过滤。
    /// </summary>
    public IEnumerable<LibraryArtifact> ResolveArtifacts(RuntimeValue runtime)
    {
        var coordinate = Coordinate;
        var classifier = ResolveClassifier(runtime);

        // 1) 主产物。原生库条目通常没有 artifact，只有 classifiers。
        if (Downloads?.Artifact is { } artifact)
        {
            yield return new(null, artifact.Url ?? coordinate.Path,
                Absolute(artifact.Url), artifact.Sha1, artifact.Size);
        }
        else if (classifier is null)
        {
            // 既无 downloads 也非原生库：按坐标推导，无哈希可用
            yield return new(null, coordinate.Path, Absolute(coordinate.Path), null, null);
        }

        if (classifier is null) yield break;

        // 2) 当前平台的原生产物
        if (Downloads?.Classifiers is { } classifiers
            && classifiers.TryGetValue(classifier, out var spec) && spec is not null)
        {
            yield return new(classifier, spec.Url ?? coordinate.Path.WithClassifier(classifier),
                Absolute(spec.Url), spec.Sha1, spec.Size);
            yield break;
        }

        // 3) 旧格式兜底：由坐标推导 classifier 路径
        var legacy = coordinate.Path.WithClassifier(classifier);
        yield return new(classifier, legacy, Absolute(legacy), null, null);
    }

    /// <summary>把相对路径拼到该库自带的基址上；没有基址时返回 null 让调用方用默认仓库。</summary>
    private string? Absolute(string? relativePath)
    {
        if (relativePath is null) return null;
        if (relativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return relativePath;
        if (string.IsNullOrEmpty(Url)) return null;
        return Url!.TrimEnd('/') + "/" + relativePath.TrimStart('/');
    }
}

internal static class MavenPathExtensions
{
    /// <summary>在路径尾部插入 classifier：<c>a-v.jar</c> → <c>a-v-natives-linux.jar</c>。</summary>
    public static string WithClassifier(this string path, string classifier)
    {
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        return dot <= slash ? $"{path}-{classifier}"
            : string.Concat(path[..dot], "-", classifier, path[dot..]);
    }
}
