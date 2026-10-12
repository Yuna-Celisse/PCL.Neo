using System;
using System.Net.Http;
using System.Threading.Tasks;
using PCL.Neo.Core.IO.Downloads;
using PCL.Neo.Core.Minecraft;
using PCL.Neo.Core.Minecraft.Installer;
using PCL.Neo.Core.Minecraft.Instances;
using PCL.Neo.Core.Minecraft.Java;
using PCL.Neo.Core.Minecraft.Launch;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;
using PCL.Neo.Core.Processes;

namespace PCL.Neo.Services;

/// <summary>
/// 供 UI 使用的启动器服务单例容器。
/// 在没有引入大型 IoC 框架之前，统一负责底层 Core 服务的初始化与协同。
/// </summary>
public sealed class LauncherService
{
    public static LauncherService Instance { get; } = new();

    public HttpClient HttpClient { get; }
    public MinecraftLayout Layout { get; }
    public IProcessRunner ProcessRunner { get; }
    public IFileDownloader Downloader { get; }
    public MojangVersionClient VersionClient { get; }
    public GameInstaller Installer { get; }
    public GameLauncher Launcher { get; }
    public InstanceManager Instances { get; }

    private LauncherService()
    {
        HttpClient = new HttpClient();
        Layout = new MinecraftLayout(AppPaths.DefaultMinecraftRoot);
        ProcessRunner = new ProcessRunner();
        Downloader = new FileDownloader(HttpClient, new DownloadOptions
        {
            MaxParallelism = 12
        });
        VersionClient = new MojangVersionClient(HttpClient);
        Installer = new GameInstaller(VersionClient, Downloader, Layout);
        Launcher = new GameLauncher(ProcessRunner, Layout);
        Instances = new InstanceManager(Layout);
    }
}
