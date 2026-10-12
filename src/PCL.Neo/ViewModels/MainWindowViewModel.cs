using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Neo.Core.Platform;

namespace PCL.Neo.ViewModels;

/// <summary>
/// 主窗口视图模型：负责顶层四大 Tab 的切换和状态分发。
/// 对应 PCL 的「启动」、「下载」、「设置」与「关于」。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public HomeViewModel LaunchPage { get; } = new();
    public DownloadViewModel DownloadPage { get; } = new();
    public InstancesViewModel SettingsPage { get; } = new();
    public AboutViewModel AboutPage { get; } = new();

    public string CurrentPlatformShort { get; } = PlatformInfo.Describe();

    [ObservableProperty]
    private ViewModelBase _currentPageView;

    [ObservableProperty]
    private bool _isLaunchTabSelected = true;

    [ObservableProperty]
    private bool _isDownloadTabSelected;

    [ObservableProperty]
    private bool _isSettingsTabSelected;

    [ObservableProperty]
    private bool _isAboutTabSelected;

    public MainWindowViewModel()
    {
        _currentPageView = LaunchPage;
    }

    partial void OnIsLaunchTabSelectedChanged(bool value)
    {
        if (value) CurrentPageView = LaunchPage;
    }

    partial void OnIsDownloadTabSelectedChanged(bool value)
    {
        if (value) CurrentPageView = DownloadPage;
    }

    partial void OnIsSettingsTabSelectedChanged(bool value)
    {
        if (value) CurrentPageView = SettingsPage;
    }

    partial void OnIsAboutTabSelectedChanged(bool value)
    {
        if (value) CurrentPageView = AboutPage;
    }
}
