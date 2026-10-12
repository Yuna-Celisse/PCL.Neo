using CommunityToolkit.Mvvm.Input;
using PCL.Neo.Core.Platform;

namespace PCL.Neo.ViewModels;

/// <summary>
/// 关于页。按《PCL 存储库合理使用指南》对“重度使用”的要求：
/// 首位署名原作者龙腾猫跃并给出赞助链接，同时声明本项目的第三方二次创作身份。
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    public const string AuthorSponsorUrl = "https://meloong.com/afd/a/LTCat";
    public const string UpstreamPclUrl = "https://github.com/Meloong-Git/PCL";
    public const string UpstreamCeUrl = "https://github.com/PCL-Community/PCL-CE";
    public const string ThisRepoUrl = "https://github.com/PCL-Community/PCL.Neo";
    public const string LicenseGuideUrl = "https://github.com/Meloong-Git/PCL/blob/main/LICENCE";

    public string PlatformDescription { get; } = PlatformInfo.Describe();

    [RelayCommand]
    private static void OpenLink(string? url)
    {
        // 只放行 http(s)，防止将来新闻页/模组描述里的外部内容变成执行入口
        if (ShellOps.IsSafeWebUrl(url))
            ShellOps.OpenWebLink(url!);
    }
}
