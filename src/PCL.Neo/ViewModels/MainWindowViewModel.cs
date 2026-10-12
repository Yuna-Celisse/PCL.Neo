namespace PCL.Neo.ViewModels;

/// <summary>
/// 主窗口的导航宿主。M0 阶段只提供“主页 / 关于”两个页面，
/// 后续里程碑按 <c>docs/ROADMAP</c> 追加下载、设置、实例列表等页面。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public HomeViewModel Home { get; } = new();

    public AboutViewModel About { get; } = new();
}
