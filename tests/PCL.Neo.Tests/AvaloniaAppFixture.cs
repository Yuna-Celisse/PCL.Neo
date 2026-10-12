using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using PCL.Neo.UI;

namespace PCL.Neo.Tests;

/// <summary>
/// 无头 Avalonia 宿主。
/// <para>
/// Avalonia 会把“初始化 Application 的那条线程”当作 UI 线程，之后的控件创建与布局
/// 都必须回到该线程。xUnit 不保证同一集合内的测试跑在同一线程上，因此这里在构造函数里
/// 一次性完成全部 UI 操作，测试方法只负责断言已捕获的结果。
/// </para>
/// <para>
/// 这一层的意义：XAML 里的模板、绑定、资源缺失过去只能靠人肉开窗口发现，
/// 现在在没有显示器的 CI runner 上也会失败。
/// </para>
/// </summary>
public sealed class AvaloniaAppFixture : IDisposable
{
    public const string CollectionName = "Avalonia";

    [CollectionDefinition(CollectionName)]
    public sealed class AvaloniaCollection : ICollectionFixture<AvaloniaAppFixture>;

    public Application Application { get; }
    public NeoTheme Theme { get; }
    public Color LightAccentLight1 { get; }
    public Color DarkAccentLight1 { get; }
    public ThemeVariant ActualVariantInDark { get; }
    public object? HomeView { get; }
    public object? AboutView { get; }
    public object? DownloadView { get; }
    public object? InstancesView { get; }
    public Size DesiredSize { get; }
    public bool WindowVisible { get; }

    private readonly Window? _window;

    public AvaloniaAppFixture()
    {
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        Application = Application.Current ?? throw new InvalidOperationException("Application.Current 为空");
        Theme = NeoTheme.Current ?? throw new InvalidOperationException("NeoTheme 未被加载");
        LightAccentLight1 = Theme.ColorPalette.AccentLight1;

        var locator = new ViewLocator();
        HomeView = locator.Build(new ViewModels.HomeViewModel());
        AboutView = locator.Build(new ViewModels.AboutViewModel());
        DownloadView = locator.Build(new ViewModels.DownloadViewModel());
        InstancesView = locator.Build(new ViewModels.InstancesViewModel());

        _window = new Views.MainWindow { DataContext = new ViewModels.MainWindowViewModel() };
        _window.Show();
        _window.Measure(new Size(900, 580));
        _window.Arrange(new Rect(0, 0, 900, 580));
        DesiredSize = _window.DesiredSize;
        WindowVisible = _window.IsVisible;

        // 深色模式：切换主题变体后整套调色板必须重算（对应 #175）
        Application.RequestedThemeVariant = ThemeVariant.Dark;
        ActualVariantInDark = Application.ActualThemeVariant;
        DarkAccentLight1 = Theme.ColorPalette.AccentLight1;
        Application.RequestedThemeVariant = ThemeVariant.Light;
    }

    public void Dispose() => _window?.Close();
}
