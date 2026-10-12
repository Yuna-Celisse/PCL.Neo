namespace PCL.Neo.UI.Styles;

public class RefreshedEventArgs(NeoColorPalette palette) : EventArgs
{
    public NeoColorPalette Palette { get; } = palette;
}
