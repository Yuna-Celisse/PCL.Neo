using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Neo.Core.Minecraft.Manifest;

/// <summary>version manifest 里的一条版本记录。</summary>
public sealed record VersionListEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("releaseTime")] string? ReleaseTime);

public sealed class VersionManifestDocument
{
    public LatestPointers? Latest { get; set; }
    public List<VersionListEntry> Versions { get; set; } = [];

    public static VersionManifestDocument Parse(string json)
        => JsonSerializer.Deserialize<VersionManifestDocument>(json, VersionInfo.JsonOptions)
           ?? throw new JsonException("版本列表为空");

    /// <summary>按 id 精确查找。</summary>
    public VersionListEntry? Find(string id)
        => Versions.Find(v => string.Equals(v.Id, id, StringComparison.Ordinal));

    /// <summary>只保留正式版与快照，过滤掉旧版 alpha/beta/第三方 mod loader 条目。</summary>
    public IEnumerable<VersionListEntry> FilterOfficial(bool includeSnapshots = true)
        => Versions.Where(v => v.Type == "release" || (includeSnapshots && v.Type == "snapshot"));
}

public sealed class LatestPointers
{
    public string? Release { get; set; }
    public string? Snapshot { get; set; }
}

/// <summary>asset index（<c>assets/indexes/&lt;id&gt;.json</c>）。</summary>
public sealed class AssetIndex
{
    public Dictionary<string, AssetObject> Objects { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 是否要把资源映射回 resources 目录（legacy 与 pre-1.6 的玩法）。
    /// 现代版本一律 false；true 时对象不能直接按哈希取用。
    /// </summary>
    public bool MapToResources { get; set; }

    public static AssetIndex Parse(string json)
        => JsonSerializer.Deserialize<AssetIndex>(json, VersionInfo.JsonOptions)
           ?? throw new JsonException("资源索引为空");
}

public sealed class AssetObject
{
    /// <summary>SHA1，同时也是文件名；前两位作子目录以控制单目录文件数。</summary>
    public string Hash { get; set; } = "";
    public long Size { get; set; }

    /// <summary>相对 <c>assets/objects/</c> 的路径，例如 <c>03/03c27cd7...b7</c>。</summary>
    [JsonIgnore]
    public string ObjectPath => Hash.Length < 3
        ? throw new FormatException($"非法的资源哈希：{Hash}")
        : $"{Hash[..2]}/{Hash}";
}
