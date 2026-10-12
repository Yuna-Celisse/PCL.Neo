using PCL.Neo.Core.Platform;

namespace PCL.Neo.ViewModels;

/// <summary>主页：展示运行环境与主题管线是否打通。</summary>
public partial class HomeViewModel : ViewModelBase
{
    /// <summary>形如 <c>Linux-X64</c>，用于确认平台探测在真机上生效。</summary>
    public string PlatformDescription { get; } = PlatformInfo.Describe();

    public string OsDescription { get; } = PlatformInfo.OsDescription;

    /// <summary>配置目录，M2 的配置系统会落在这里。</summary>
    public string ConfigDirectory { get; } = AppPaths.ConfigDirectory;
}
