using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using PCL.Neo;
using PCL.Neo.UI;
using PCL.Neo.ViewModels;
using PCL.Neo.Views;

namespace PCL.Neo.Tests;

[Collection(AvaloniaAppFixture.CollectionName)]
public class UiSmokeTests(AvaloniaAppFixture fx)
{
    [Fact]
    public void App_Initializes()
    {
        Assert.IsType<App>(fx.Application);
    }

    [Fact]
    public void NeoTheme_Is_Active_In_Main_Application()
    {
        Assert.NotNull(NeoTheme.Current);
    }

    [Fact]
    public void Palette_Exposes_All_Twelve_Accent_Tokens()
    {
        var p = fx.Theme.ColorPalette;
        var tokens = new[]
        {
            p.Accent, p.AccentDark1, p.AccentDark2, p.AccentDark3,
            p.AccentLight1, p.AccentLight2, p.AccentLight3,
            p.AccentLight4, p.AccentLight5, p.AccentLight6,
            p.AccentStroke, p.AccentPressed
        };

        // 出现 default(Color) 说明资源管线或生成逻辑断了
        Assert.All(tokens, t => Assert.NotEqual(default, t));
    }

    [Fact]
    public void ViewLocator_Resolves_Home_And_About()
    {
        // ViewLocator 找不到视图时会退化成 "Not Found: ..." 的 TextBlock
        Assert.IsType<HomeView>(fx.HomeView);
        Assert.IsType<AboutView>(fx.AboutView);
    }

    [Fact]
    public void MainWindow_Completes_Layout()
    {
        Assert.True(fx.WindowVisible);
        Assert.True(fx.DesiredSize.Width > 0, $"宽度为 {fx.DesiredSize.Width}");
        Assert.True(fx.DesiredSize.Height > 0, $"高度为 {fx.DesiredSize.Height}");
    }

    [Fact]
    public void Switching_To_Dark_Regenerates_The_Palette()
    {
        Assert.Equal(ThemeVariant.Dark, fx.ActualVariantInDark);
        Assert.NotEqual(fx.LightAccentLight1, fx.DarkAccentLight1);
    }
}
