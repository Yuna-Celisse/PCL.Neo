using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Neo.Core.Minecraft.Manifest;

/// <summary>
/// 清单里的一个下载描述。注意 <c>path</c> 与 <c>url</c> 是两个字段：
/// path 是相对仓库的路径（用于本地落盘与镜像改写），url 是绝对地址。
/// 只存 url 会让本地路径退化成坐标推导，与清单声明不一致。
/// </summary>
public sealed record DownloadSpec(string? Url, string? Sha1, long? Size, string? Path = null);

public sealed class AssetIndexSpec
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("totalSize")] public long? TotalSize { get; set; }
}

public sealed class VersionDownloads
{
    public DownloadSpec? Client { get; set; }
    public DownloadSpec? Server { get; set; }
    public AssetIndexSpec? AssetIndex { get; set; }
}

public sealed class JavaVersionSpec
{
    /// <summary>所需主版本；早期清单缺省表示 Java 8。</summary>
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; } = 8;
}

/// <summary>
/// 一个 Minecraft 版本清单（<c>*.json</c>）。
/// 支持 <c>inheritsFrom</c>：整合包与 Forge/Fabric 版本都靠它复用原版清单，
/// 未声明的字段要从被继承版本取。
/// </summary>
public sealed class VersionInfo
{
    public string Id { get; set; } = "";
    public string? Type { get; set; }
    public string? MainClass { get; set; }
    public string? Assets { get; set; }
    public string? Jar { get; set; }
    public string? InheritsFrom { get; set; }
    public string? ReleaseTime { get; set; }
    public VersionDownloads? Downloads { get; set; }
    public JavaVersionSpec? JavaVersion { get; set; }
    public List<LibraryInfo> Libraries { get; set; } = [];
    public ArgumentsSpec? Arguments { get; set; }

    /// <summary>1.12 及更早的字符串式参数。</summary>
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    /// <summary>
    /// 把 mod 加载器等附加库合并进来（Forge/Fabric 的清单只有增量部分）。
    /// </summary>
    public void InheritFrom(VersionInfo baseVersion)
    {
        ArgumentNullException.ThrowIfNull(baseVersion);

        MainClass ??= baseVersion.MainClass;
        Assets ??= baseVersion.Assets;
        Jar ??= baseVersion.Jar;
        Type ??= baseVersion.Type;
        JavaVersion ??= baseVersion.JavaVersion;
        MinecraftArguments ??= baseVersion.MinecraftArguments;

        if (Downloads is null) Downloads = baseVersion.Downloads;
        else Downloads.AssetIndex ??= baseVersion.Downloads?.AssetIndex;

        // 参数：mod loader 的 game 参数要排在原版之前，jvm 参数同理。
        if (Arguments is null) Arguments = baseVersion.Arguments;
        else if (baseVersion.Arguments is not null) Arguments.Prepend(baseVersion.Arguments);

        Libraries = [.. baseVersion.Libraries, .. Libraries];
    }

    /// <summary>实际运行的 jar 名：继承时默认落在被继承版本的 jar 上。</summary>
    public string ResolveJarName() => Jar ?? InheritsFrom ?? Id;

    /// <summary>所需 Java 主版本。</summary>
    public int RequiredJavaMajor => JavaVersion?.MajorVersion ?? 8;

    public static VersionInfo Parse(string json)
        => JsonSerializer.Deserialize<VersionInfo>(json, JsonOptions)
           ?? throw new JsonException("版本清单为空");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ArgumentConverter() }
    };
}

public sealed class ArgumentsSpec
{
    public List<Argument> Game { get; set; } = [];
    public List<Argument> Jvm { get; set; } = [];

    /// <summary>被继承版本的参数排在前面，保持与原版启动器一致的顺序。</summary>
    public void Prepend(ArgumentsSpec other)
    {
        Game = [.. other.Game, .. Game];
        Jvm = [.. other.Jvm, .. Jvm];
    }
}

/// <summary>
/// 一条参数：可能是裸字符串，也可能是 <c>{"rules": [...], "value": "..."}</c>，
/// 后者的 value 还能是数组（整组共享同一套规则）。
/// </summary>
public sealed class Argument
{
    public string? Text { get; set; }
    public List<Argument>? Group { get; set; }
    public List<Rule>? Rules { get; set; }
}

public sealed class ArgumentConverter : JsonConverter<Argument>
{
    public override Argument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new Argument { Text = reader.GetString() };

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("参数既不是字符串也不是对象");

        var argument = new Argument();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return argument;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            switch (reader.GetString())
            {
                case "rules":
                    reader.Read();
                    argument.Rules = JsonSerializer.Deserialize<List<Rule>>(ref reader, options);
                    break;
                case "value":
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        argument.Text = reader.GetString();
                    }
                    else
                    {
                        argument.Group = JsonSerializer.Deserialize<List<Argument>>(ref reader, options);
                    }

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("参数对象未正常结束");
    }

    public override void Write(Utf8JsonWriter writer, Argument value, JsonSerializerOptions options)
        => throw new NotSupportedException("清单只读不写");
}
