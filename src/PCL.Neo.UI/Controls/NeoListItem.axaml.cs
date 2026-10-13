using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PCL.Neo.UI.Controls;

/// <summary>
/// 1:1 复刻 PCL 原版 MyListItem：
/// 作为全启动器侧栏、设置项、下载分类的最核心原子单元。
/// 支持图标、标题、副标题、单选/选中态指示条。
/// </summary>
public class NeoListItem : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<NeoListItem, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<NeoListItem, string?>(nameof(Subtitle));

    public static readonly StyledProperty<string?> IconTextProperty =
        AvaloniaProperty.Register<NeoListItem, string?>(nameof(IconText));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<NeoListItem, bool>(nameof(IsChecked));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? IconText
    {
        get => GetValue(IconTextProperty);
        set => SetValue(IconTextProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }
}
