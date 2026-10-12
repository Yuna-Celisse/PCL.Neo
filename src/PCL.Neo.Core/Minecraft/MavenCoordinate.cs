namespace PCL.Neo.Core.Minecraft;

/// <summary>
/// Maven 坐标 <c>group:artifact:version[:classifier][.ext]</c>。
/// 库文件名与远端路径全部由它推导，写错一个分隔符就是 404。
/// </summary>
public sealed record MavenCoordinate(
    string Group,
    string Artifact,
    string Version,
    string? Classifier = null,
    string Extension = "jar")
{
    /// <summary>文件名：<c>artifact-version[-classifier].ext</c>。</summary>
    public string FileName
    {
        get
        {
            var suffix = string.IsNullOrEmpty(Classifier) ? "" : "-" + Classifier;
            return $"{Artifact}-{Version}{suffix}.{Extension}";
        }
    }

    /// <summary>相对仓库根的路径，使用正斜杠（远端 URL 一律正斜杠）。</summary>
    public string Path => $"{Group.Replace('.', '/')}/{Artifact}/{Version}/{FileName}";

    public static MavenCoordinate Parse(string coordinate)
    {
        ArgumentException.ThrowIfNullOrEmpty(coordinate);

        var extension = "jar";
        var at = coordinate.IndexOf('@');
        if (at >= 0)
        {
            extension = coordinate[(at + 1)..];
            coordinate = coordinate[..at];
        }

        var parts = coordinate.Split(':');
        if (parts.Length is < 3 or > 4)
            throw new FormatException($"非法的 Maven 坐标：{coordinate}");

        return new MavenCoordinate(parts[0], parts[1], parts[2], parts.Length == 4 ? parts[3] : null, extension);
    }

    public override string ToString() => Path;
}
