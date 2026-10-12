using PCL.Neo.Core.Platform;

namespace PCL.Neo.Tests;

public class PlatformTests
{
    [Fact]
    public void Describe_Follows_PlatformDashArchitecture_Format()
    {
        var text = PlatformInfo.Describe();

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains('-', text);
        Assert.StartsWith($"{PlatformInfo.Platform}-", text);
    }

    [Fact]
    public void Platform_Is_Detected_On_Supported_Systems()
    {
        Assert.True(PlatformInfo.IsWindows || PlatformInfo.IsMacOS || PlatformInfo.IsLinux,
            $"未识别的宿主平台：{PlatformInfo.OsDescription}");
    }

    [Fact]
    public void Architecture_Is_Not_Unknown_On_Supported_Isas()
    {
        // README 承诺 x64 / x86 / ARM64 / LoongArch64 / RiscV64；探测失败会退回 Unknown
        Assert.NotEqual(ArchitectureKind.Unknown, PlatformInfo.Architecture);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigDirectory_Is_Absolute_And_Namespaced(bool ensure)
    {
        var dir = AppPaths.ConfigDirectory;

        Assert.True(Path.IsPathRooted(dir), $"配置目录不是绝对路径：{dir}");
        Assert.EndsWith(AppPaths.AppFolderName, dir);
        Assert.DoesNotContain("  ", dir);

        if (ensure)
        {
            // 属性求值本身不应产生副作用：EnsureDirectories 才是唯一建目录的入口
            AppPaths.EnsureDirectories();
            Assert.True(Directory.Exists(dir));
            Assert.True(Directory.Exists(AppPaths.LogDirectory));
        }
    }

    [Fact]
    public void Config_Cache_And_Minecraft_Roots_Are_Distinct()
    {
        Assert.NotEqual(AppPaths.ConfigDirectory, AppPaths.CacheDirectory);
        Assert.NotEqual(AppPaths.ConfigDirectory, AppPaths.DefaultMinecraftRoot);
        Assert.EndsWith(".minecraft", AppPaths.DefaultMinecraftRoot);
    }
}
