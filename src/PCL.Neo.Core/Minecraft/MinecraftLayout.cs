namespace PCL.Neo.Core.Minecraft;

/// <summary>
/// .minecraft 目录布局。
/// <para>
/// 全部路径由这一处推导，避免散落在业务代码里的字符串拼接——
/// 大小写敏感、跨平台分隔符、实例隔离这三个坑都源自路径拼法不一致。
/// </para>
/// </summary>
public sealed class MinecraftLayout
{
    public MinecraftLayout(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string VersionsDirectory => Path.Combine(Root, "versions");
    public string LibrariesDirectory => Path.Combine(Root, "libraries");
    public string AssetsDirectory => Path.Combine(Root, "assets");
    public string LogsDirectory => Path.Combine(Root, "logs");

    public string VersionDirectory(string versionId) => Path.Combine(VersionsDirectory, versionId);
    public string VersionJsonPath(string versionId) => Path.Combine(VersionDirectory(versionId), versionId + ".json");
    public string VersionJarPath(string versionId) => Path.Combine(VersionDirectory(versionId), versionId + ".jar");

    public string AssetObjectPath(string hash)
        => Path.Combine(AssetsDirectory, "objects", hash[..2], hash);

    public string AssetIndexPath(string indexId)
        => Path.Combine(AssetsDirectory, "indexes", indexId + ".json");

    /// <summary>
    /// 原生库解压目录。按“根目录 + 版本”分开，避免不同版本共用同名 .so/.dll
    /// 时互相覆盖（这是升级后游戏崩溃的经典原因）。
    /// </summary>
    public string NativesDirectory(string versionId)
        => Path.Combine(VersionsDirectory, versionId, "natives");

    /// <summary>把库的相对路径映射成本地绝对路径。</summary>
    public string LibraryPath(string relativePath)
        => Path.Combine(LibrariesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>创建启动所需的目录骨架。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(VersionsDirectory);
        Directory.CreateDirectory(LibrariesDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(Path.Combine(AssetsDirectory, "objects"));
        Directory.CreateDirectory(Path.Combine(AssetsDirectory, "indexes"));
    }
}
