using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Neo.Core.Minecraft.Java;
using PCL.Neo.Core.Platform;

namespace PCL.Neo.ViewModels;

/// <summary>主页：展示运行环境、检测到的 Java 环境、以及启动器核心状态。</summary>
public partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isBusy;

    public string PlatformDescription { get; } = PlatformInfo.Describe();
    public string OsDescription { get; } = PlatformInfo.OsDescription;
    public string ConfigDirectory { get; } = AppPaths.ConfigDirectory;

    public ObservableCollection<string> DetectedJavaList { get; } = [];

    public HomeViewModel()
    {
        ScanJavaCommand.Execute(null);
    }

    [RelayCommand]
    private void ScanJava()
    {
        DetectedJavaList.Clear();
        var candidates = JavaLocator.EnumerateCandidates();
        if (candidates.Count == 0)
        {
            DetectedJavaList.Add("未在标准路径发现已安装的 Java 环境");
        }
        else
        {
            foreach (var c in candidates)
            {
                DetectedJavaList.Add($"{c.Executable} ({c.Source})");
            }
        }
    }
}
