using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Neo.Core.Minecraft.Installer;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;
using PCL.Neo.Services;

namespace PCL.Neo.ViewModels;

public sealed record VersionItemViewModel(string Id, string Type, string ReleaseTime, string Url);

public partial class DownloadViewModel : ViewModelBase
{
    private readonly LauncherService _service = LauncherService.Instance;
    private VersionManifestDocument? _manifest;

    [ObservableProperty]
    private bool _isLoadingManifest;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _statusMessage = "点击上方按钮拉取官方版本列表";

    [ObservableProperty]
    private double _downloadProgressValue;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private VersionItemViewModel? _selectedVersion;

    [ObservableProperty]
    private bool _includeSnapshots;

    public ObservableCollection<VersionItemViewModel> Versions { get; } = [];

    public DownloadViewModel()
    {
    }

    [RelayCommand]
    private async Task RefreshManifestAsync()
    {
        if (IsLoadingManifest || IsDownloading) return;

        IsLoadingManifest = true;
        StatusMessage = "正在拉取 Mojang 官方版本清单...";
        IsProgressIndeterminate = true;

        try
        {
            _manifest = await _service.VersionClient.GetVersionListAsync();
            ApplyFilter();
            StatusMessage = $"获取成功，共找到 {Versions.Count} 个可用版本";
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取版本清单失败: {ex.Message}";
        }
        finally
        {
            IsLoadingManifest = false;
            IsProgressIndeterminate = false;
        }
    }

    partial void OnIncludeSnapshotsChanged(bool value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_manifest is null) return;

        Versions.Clear();
        foreach (var v in _manifest.FilterOfficial(IncludeSnapshots))
        {
            Versions.Add(new VersionItemViewModel(v.Id, v.Type, v.ReleaseTime ?? "", v.Url));
        }

        if (Versions.Count > 0 && SelectedVersion is null)
        {
            SelectedVersion = Versions[0];
        }
    }

    [RelayCommand]
    private async Task InstallSelectedVersionAsync()
    {
        if (SelectedVersion is null || IsDownloading) return;

        IsDownloading = true;
        DownloadProgressValue = 0;
        IsProgressIndeterminate = true;

        var targetId = SelectedVersion.Id;
        StatusMessage = $"准备安装 {targetId}...";

        var progress = new Progress<InstallationProgress>(p =>
        {
            if (p.TotalItems > 0)
            {
                IsProgressIndeterminate = false;
                DownloadProgressValue = (double)p.CompletedItems / p.TotalItems * 100.0;
            }
            StatusMessage = $"[{p.Stage}] {p.Description}";
        });

        try
        {
            var runtime = RuntimeValue.ForCurrentPlatform(PlatformInfo.Platform, PlatformInfo.Architecture);
            await _service.Installer.InstallAsync(targetId, _manifest, runtime, progress);
            StatusMessage = $"版本 {targetId} 安装成功！可在实例管理页面启动。";
            DownloadProgressValue = 100;
        }
        catch (Exception ex)
        {
            StatusMessage = $"安装失败: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            IsProgressIndeterminate = false;
        }
    }
}
