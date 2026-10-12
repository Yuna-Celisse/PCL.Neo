namespace PCL.Neo.UI.Media.Primitives;

internal static class ColorUtils
{
    internal static (double l, double c, double h) OklabToOklch(double l, double a, double b)
    {
        var c = Math.Sqrt(a * a + b * b);
        var h = Math.Atan2(b, a) * 180 / Math.PI;
        return (l, c, h);
    }

    internal static (double l, double a, double b) OklchToOklab(double l, double c, double h)
    {
        var hRad = h * Math.PI / 180;
        var a = c * Math.Cos(hRad);
        var b = c * Math.Sin(hRad);
        return (l, a, b);
    }

    internal static (double l, double a, double b) LinearRgbToOklab(double linearR, double linearG, double linearB)
    {
        var l = 0.4122214708 * linearR + 0.5363325363 * linearG + 0.0514459929 * linearB;
        var m = 0.2119034982 * linearR + 0.6806995451 * linearG + 0.1073969566 * linearB;
        var s = 0.0883024619 * linearR + 0.2817188376 * linearG + 0.6299787005 * linearB;

        var cbrtL = Math.Cbrt(l);
        var cbrtM = Math.Cbrt(m);
        var cbrtS = Math.Cbrt(s);

        return (
            0.2104542553 * cbrtL + 0.7936177850 * cbrtM - 0.0040720468 * cbrtS,
            1.9779984951 * cbrtL - 2.4285922050 * cbrtM + 0.4505937099 * cbrtS,
            0.0259040371 * cbrtL + 0.7827717662 * cbrtM - 0.8086757660 * cbrtS
        );
    }

    internal static (double linearR, double linearG, double linearB) OklabToLinearRgb(double l, double a, double b)
    {
        var cbrtL = l + 0.3963377774 * a + 0.2158037573 * b;
        var cbrtM = l - 0.1055613458 * a - 0.0638541728 * b;
        var cbrtS = l - 0.0894841775 * a - 1.2914855480 * b;

        var cubedL = cbrtL * cbrtL * cbrtL;
        var cubedM = cbrtM * cbrtM * cbrtM;
        var cubedS = cbrtS * cbrtS * cbrtS;

        return (
            4.0767416621 * cubedL - 3.3077115913 * cubedM + 0.2309699292 * cubedS,
            -1.2684380046 * cubedL + 2.6097574011 * cubedM - 0.3413193965 * cubedS,
            -0.0041960863 * cubedL - 0.7034186147 * cubedM + 1.7076147010 * cubedS
        );
    }

    internal static (double r, double g, double b) RgbToLinearRgb(byte r, byte g, byte b)
    {
        return (
            FInv(r / 255.0),
            FInv(g / 255.0),
            FInv(b / 255.0)
        );

        double FInv(double x)
        {
            if (x >= 0.04045)
            {
                return Math.Pow((x + 0.055) / 1.055, 2.4);
            }

            return x / 12.92;
        }
    }

    internal static (byte r, byte g, byte b) LinearRgbToRgb(double linearR, double linearG, double linearB)
    {
        return (
            (byte)Math.Round(F(linearR) * 255),
            (byte)Math.Round(F(linearG) * 255),
            (byte)Math.Round(F(linearB) * 255)
        );

        double F(double x)
        {
            if (x >= 0.0031308)
            {
                return 1.055 * Math.Pow(x, 1.0 / 2.4) - 0.055;
            }

            return x * 12.92;
        }
    }
}
