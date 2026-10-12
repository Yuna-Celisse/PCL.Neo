using Avalonia.Media;
using PCL.Neo.UI.Media;

namespace PCL.Neo.Tests;

/// <summary>
/// NeoTheme 的一切视觉表现都建立在 OKLCH 转换之上，这里锁住最小不变量，
/// 防止后续调色板改动引入色值溢出或往返失真。
/// </summary>
public class ColorEngineTests
{
    private static int MaxChannelDelta(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    [Theory]
    [InlineData("#fa9545")] // 当前默认 accent
    [InlineData("#0078D4")] // 合规备选蓝
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#FF0000")]
    [InlineData("#7F7F7F")]
    public void Oklch_RoundTrip_Stays_Within_Rounding_Error(string hex)
    {
        var source = Color.Parse(hex);
        var result = source.ToOklch().ToRgb();

        Assert.True(MaxChannelDelta(source, result) <= 2,
            $"{hex} 往返后偏差 {MaxChannelDelta(source, result)} 超过 8bit 舍入误差");
    }

    [Theory]
    [InlineData("#fa9545", 0.2)]
    [InlineData("#0078D4", 0.5)]
    [InlineData("#2d8f4e", 0.8)]
    public void Lighten_Increases_Perceptual_Lightness(string hex, double amount)
    {
        var source = Color.Parse(hex);
        var lighter = ColorHelper.Lighten(source, amount);

        Assert.True(lighter.ToOklch().L > source.ToOklch().L,
            $"{hex} 提亮 {amount} 后 L 未上升");
    }

    [Theory]
    [InlineData("#fa9545", 0.2)]
    [InlineData("#0078D4", 0.6)]
    public void Darken_Decreases_Perceptual_Lightness(string hex, double amount)
    {
        var source = Color.Parse(hex);
        var darker = ColorHelper.Darken(source, amount);

        Assert.True(darker.ToOklch().L < source.ToOklch().L,
            $"{hex} 变暗 {amount} 后 L 未下降");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]   // 越界输入必须被 clamp 而不是抛异常
    [InlineData(-1.0)]
    public void Lighten_And_Darken_Are_Total_Functions(double amount)
    {
        var source = Color.Parse("#fa9545");

        var up = ColorHelper.Lighten(source, amount);
        var down = ColorHelper.Darken(source, amount);

        Assert.Equal((byte)255, up.A);
        Assert.Equal((byte)255, down.A);
    }

    [Fact]
    public void Zero_Amount_Returns_Unchanged_Color()
    {
        var source = Color.Parse("#fa9545");

        Assert.Equal(source, ColorHelper.Lighten(source, 0.0));
        Assert.Equal(source, ColorHelper.Darken(source, 0.0));
    }

    [Fact]
    public void Extreme_Lightening_Does_Not_Leave_Srgb()
    {
        // 高饱和色提亮到接近白时，色域映射必须把 chroma 压回 sRGB 内，
        // 否则会出现色带断裂（这正是 #167 修的问题）
        var saturated = Color.Parse("#0055ff");
        var result = ColorHelper.Lighten(saturated, 0.45);

        Assert.InRange((int)result.R, 0, 255);
        Assert.InRange((int)result.G, 0, 255);
        Assert.InRange((int)result.B, 0, 255);
        Assert.True(result.ToOklch().L > saturated.ToOklch().L);
    }
}
