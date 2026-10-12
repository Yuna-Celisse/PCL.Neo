namespace PCL.Neo.ViewModels;

/// <summary>
/// 主窗口的导航宿主。
/// 包含：主页、版本下载、实例管理、关于。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public HomeViewModel Home { get; } = new();

    public DownloadViewModel Download { get; } = new();

    public InstancesViewModel Instances { get; } = new();

    public AboutViewModel About { get; } = new();
}
