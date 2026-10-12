using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Neo.Core.Minecraft.Instances;
using PCL.Neo.Core.Minecraft.Java;
using PCL.Neo.Core.Minecraft.Launch;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;
using PCL.Neo.Services;

namespace PCL.Neo.ViewModels;

public sealed record LocalInstanceItemViewModel(
    string Id,
    string Details,
    string DirectoryPath,
    LocalInstance Model);

public partial class InstancesViewModel : ViewModelBase
{
    private readonly LauncherService _service = LauncherService.Instance;

    [ObservableProperty]
    private LocalInstanceItemViewModel? _selectedInstance;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _playerName = "Player";

    [ObservableProperty]
    private int _maxMemoryMb = 2048;

    [ObservableProperty]
    private string? _selectedJavaPath;

    public ObservableCollection<LocalInstanceItemViewModel> Instances { get; } = [];
    public ObservableCollection<string> JavaPaths { get; } = [];

    public InstancesViewModel()
    {
        RefreshInstances();
        RefreshJavaList();
    }

    [RelayCommand]
    public void RefreshInstances()
    {
        Instances.Clear();
        var list = _service.Instances.ScanInstances();
        foreach (var inst in list)
        {
            var reqJava = inst.Version?.RequiredJavaMajor ?? 8;
            var details = $"类型: {inst.Version?.Type ?? "未知"} | 需要 Java {reqJava} | 路径: {inst.DirectoryPath}";
            Instances.Add(new LocalInstanceItemViewModel(inst.Id, details, inst.DirectoryPath, inst));
        }

        if (Instances.Count > 0 && SelectedInstance is null)
        {
            SelectedInstance = Instances[0];
        }

        StatusMessage = $"找到 {Instances.Count} 个本地实例";
    }

    [RelayCommand]
    public void RefreshJavaList()
    {
        JavaPaths.Clear();
        var candidates = JavaLocator.EnumerateCandidates();
        foreach (var c in candidates)
        {
            JavaPaths.Add(c.Executable);
        }

        if (JavaPaths.Count > 0 && SelectedJavaPath is null)
        {
            SelectedJavaPath = JavaPaths[0];
        }
    }

    [RelayCommand]
    private async Task LaunchSelectedInstanceAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个要启动的实例";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedJavaPath))
        {
            StatusMessage = "请指定可用的 Java 路径";
            return;
        }

        var instance = SelectedInstance.Model;
        if (instance.Version is null)
        {
            StatusMessage = "该实例缺少有效的版本清单 (json 损坏)";
            return;
        }

        StatusMessage = $"正在启动 {instance.Id}...";

        try
        {
            var options = new LaunchOptions(
                JavaExecutable: SelectedJavaPath,
                PlayerName: string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName,
                MaxMemoryMb: MaxMemoryMb);

            var runtime = RuntimeValue.ForCurrentPlatform(PlatformInfo.Platform, PlatformInfo.Architecture);

            var process = await _service.Launcher.LaunchAsync(instance.Version, options, runtime);
            StatusMessage = $"游戏进程已启动 (PID: {process.Pid})！正在运行...";

            // 监听退出
            _ = Task.Run(async () =>
            {
                var code = await process.WaitForExitAsync();
                StatusMessage = $"游戏进程已退出 (代码: {code})";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"启动失败: {ex.Message}";
        }
    }
}
