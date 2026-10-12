namespace PCL.Neo.Core.Platform;

/// <summary>
/// 跨平台路径解析。Windows 走 %APPDATA%，Linux 遵循 XDG Base Directory，macOS 走 ~/Library。
/// 所有属性只做字符串拼接，不会创建目录；需要落盘时显式调用 <see cref="EnsureDirectories"/>。
/// </summary>
public static class AppPaths
{
    /// <summary>配置目录下的应用子目录名。</summary>
    public const string AppFolderName = "PCL.Neo";

    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>用户配置目录（设置、实例列表等持久化数据）。</summary>
    public static string ConfigDirectory { get; } = ResolveConfig();

    /// <summary>缓存目录（下载临时文件、版本清单缓存），可安全清空。</summary>
    public static string CacheDirectory { get; } = ResolveCache();

    /// <summary>日志目录。</summary>
    public static string LogDirectory => Path.Combine(ConfigDirectory, "logs");

    /// <summary>游戏实例的默认根目录（<c>~/.minecraft</c>）。</summary>
    public static string DefaultMinecraftRoot => Path.Combine(UserProfile, ".minecraft");

    /// <summary>创建配置与日志目录，幂等。</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string ResolveConfig()
    {
        if (PlatformInfo.IsWindows)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppFolderName);
        }

        if (PlatformInfo.IsMacOS)
        {
            return Path.Combine(UserProfile, "Library", "Application Support", AppFolderName);
        }

        // Linux 及其它类 Unix 系统：$XDG_CONFIG_HOME 优先，回退 ~/.config
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg) || !Path.IsPathRooted(xdg)
            ? Path.Combine(UserProfile, ".config")
            : xdg;
        return Path.Combine(root, AppFolderName);
    }

    private static string ResolveCache()
    {
        if (PlatformInfo.IsWindows)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, AppFolderName, "cache");
        }

        if (PlatformInfo.IsMacOS)
        {
            return Path.Combine(UserProfile, "Library", "Caches", AppFolderName);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var root = string.IsNullOrWhiteSpace(xdg) || !Path.IsPathRooted(xdg)
            ? Path.Combine(UserProfile, ".cache")
            : xdg;
        return Path.Combine(root, AppFolderName);
    }
}
