using Avalonia;
using Avalonia.Controls;

namespace PCL.Neo.UI.Controls;

/// <summary>
/// 1:1 复刻 PCL 原版 MyCard 的卡片容器：
/// 自带标题、圆角、背景平滑过渡与 PCL 标志性的悬浮投影微抬升动效。
/// </summary>
public class NeoCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<NeoCard, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
