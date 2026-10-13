using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Neo.Core.Minecraft.Instances;
using PCL.Neo.Core.Minecraft.Java;
using PCL.Neo.Core.Minecraft.Launch;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;
using PCL.Neo.Services;

namespace PCL.Neo.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly LauncherService _service = LauncherService.Instance;

    [ObservableProperty]
    private string _playerName = "Player";

    [ObservableProperty]
    private string _accountType = "离线";

    [ObservableProperty]
    private string? _deviceLoginCode;

    [ObservableProperty]
    private string? _deviceLoginUrl;

    [ObservableProperty]
    private bool _isDeviceCodePending;

    [RelayCommand]
    private async Task StartMicrosoftLoginAsync()
    {
        if (IsDeviceCodePending) return;

        IsDeviceCodePending = true;
        LaunchStatusMessage = "正在请求微软设备代码...";

        try
        {
            var res = await _service.MicrosoftAuth.RequestDeviceCodeAsync();
            DeviceLoginCode = res.UserCode;
            DeviceLoginUrl = res.VerificationUri;
            LaunchStatusMessage = $"请在浏览器打开 {res.VerificationUri} 并输入配对码: {res.UserCode}";

            // 尝试直接调系统浏览器打开
            try { PCL.Neo.Core.Platform.ShellOps.OpenWebLink(res.VerificationUri); } catch { }

            var account = await _service.MicrosoftAuth.PollForAccountAsync(
                res.DeviceCode,
                res.Interval,
                statusProgress: new Progress<string>(msg => LaunchStatusMessage = msg));

            PlayerName = account.Username;
            AccountType = "微软正版";
            LaunchStatusMessage = $"登录成功：欢迎 {account.Username}！";
        }
        catch (Exception ex)
        {
            LaunchStatusMessage = $"登录失败: {ex.Message}";
        }
        finally
        {
            IsDeviceCodePending = false;
            DeviceLoginCode = null;
        }
    }

    [ObservableProperty]
    private LocalInstance? _selectedInstance;

    [ObservableProperty]
    private string? _selectedJavaPath;

    [ObservableProperty]
    private string _launchStatusMessage = "就绪";

    [ObservableProperty]
    private bool _isLaunching;

    public ObservableCollection<LocalInstance> Instances { get; } = [];
    public ObservableCollection<string> JavaList { get; } = [];

    public HomeViewModel()
    {
        RefreshData();
    }

    [RelayCommand]
    public void RefreshData()
    {
        // 1. 刷新版本列表
        Instances.Clear();
        var list = _service.Instances.ScanInstances();
        foreach (var inst in list) Instances.Add(inst);
        if (Instances.Count > 0 && SelectedInstance is null)
        {
            SelectedInstance = Instances[0];
        }

        // 2. 刷新 Java
        JavaList.Clear();
        var javas = JavaLocator.EnumerateCandidates();
        foreach (var j in javas) JavaList.Add(j.Executable);
        if (JavaList.Count > 0 && SelectedJavaPath is null)
        {
            SelectedJavaPath = JavaList[0];
        }
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (IsLaunching) return;

        if (SelectedInstance is null)
        {
            LaunchStatusMessage = "请先选择或安装一个游戏版本！";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedJavaPath) || !File.Exists(SelectedJavaPath))
        {
            LaunchStatusMessage = "未选择有效的 Java 路径，请在下拉框中选择！";
            return;
        }

        if (SelectedInstance.Version is null)
        {
            LaunchStatusMessage = "该实例的清单文件损坏";
            return;
        }

        IsLaunching = true;
        LaunchStatusMessage = $"正在准备启动 {SelectedInstance.Id}...";

        try
        {
            var options = new LaunchOptions(
                JavaExecutable: SelectedJavaPath,
                PlayerName: string.IsNullOrWhiteSpace(PlayerName) ? "Player" : PlayerName,
                MaxMemoryMb: 2048);

            var runtime = RuntimeValue.ForCurrentPlatform(PlatformInfo.Platform, PlatformInfo.Architecture);

            var process = await _service.Launcher.LaunchAsync(SelectedInstance.Version, options, runtime);
            LaunchStatusMessage = $"游戏已启动 (PID: {process.Pid})！正在运行...";

            _ = Task.Run(async () =>
            {
                var code = await process.WaitForExitAsync();
                LaunchStatusMessage = $"游戏已退出 (代码: {code})";
                IsLaunching = false;
            });
        }
        catch (Exception ex)
        {
            LaunchStatusMessage = $"启动失败: {ex.Message}";
            IsLaunching = false;
        }
    }
}
