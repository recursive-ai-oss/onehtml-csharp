using System;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Color space conversion (CSS Color 4 §17 sample code, restated), gamut mapping (§13.2) and mixing (§12).</summary>
internal static class ColorMath
{
    private static readonly double[] LinSrgbToXyz = [0.41239079926595934, 0.357584339383878, 0.1804807884018343, 0.21263900587151027, 0.715168678767756, 0.07219231536073371, 0.01933081871559182, 0.11919477979462598, 0.9505321522496607];
    private static readonly double[] XyzToLinSrgb = [3.2409699419045226, -1.537383177570094, -0.4986107602930034, -0.9692436362808796, 1.8759675015077202, 0.04155505740717559, 0.05563007969699366, -0.20397695888897652, 1.0569715142428786];
    private static readonly double[] LinP3ToXyz = [0.4865709486482162, 0.26566769316909306, 0.1982172852343625, 0.2289745640697488, 0.6917385218365064, 0.079286914093745, 0.0, 0.04511338185890264, 1.043944368900976];
    private static readonly double[] XyzToLinP3 = [2.493496911941425, -0.9313836179191239, -0.40271078445071684, -0.8294889695615747, 1.7626640603183463, 0.023624685841943577, 0.03584583024378447, -0.07617238926804182, 0.9568845240076872];
    private static readonly double[] LinA98ToXyz = [0.5766690429101305, 0.1855582379065463, 0.1882286462349947, 0.29734497525053605, 0.6273635662554661, 0.0752914584939979, 0.02703136138641234, 0.07068885253582723, 0.9913375368376388];
    private static readonly double[] XyzToLinA98 = [2.0415879038107465, -0.5650069742788596, -0.34473135077832956, -0.9692436362808795, 1.8759675015077202, 0.04155505740717557, 0.013444280632031142, -0.11836239223101838, 1.0151749943912054];
    private static readonly double[] Lin2020ToXyz = [0.6369580483012914, 0.14461690358620832, 0.1688809751641721, 0.2627002120112671, 0.6779980715188708, 0.05930171646986196, 0.0, 0.028072693049087428, 1.060985057710791];
    private static readonly double[] XyzToLin2020 = [1.7166511879712674, -0.35567078377639233, -0.25336628137365974, -0.6666843518324892, 1.6164812366349395, 0.01576854581391113, 0.017639857445310783, -0.042770613257808524, 0.9421031212354738];
    private static readonly double[] LinProToXyz50 = [0.7977604896723027, 0.13518583717574031, 0.0313493495815248, 0.2880711282292934, 0.7118432178101014, 0.00008565396060525902, 0.0, 0.0, 0.8251046025104601];
    private static readonly double[] Xyz50ToLinPro = [1.3457989731028281, -0.25558010007997534, -0.05110628506753401, -0.5446224939028347, 1.5082327413132781, 0.02053603239147973, 0.0, 0.0, 1.2119675456389454];
    private static readonly double[] D65ToD50 = [1.0479297925449969, 0.022946870601609652, -0.05019226628920524, 0.02962780877005599, 0.9904344267538799, -0.017073799063418826, -0.009243040646204504, 0.015055191490298152, 0.7518742814281371];
    private static readonly double[] D50ToD65 = [0.955473421488075, -0.02309845494876471, 0.06325924320057072, -0.0283697093338637, 1.0099953980813041, 0.021041441191917323, 0.012314014864481998, -0.020507649298898964, 1.330365926242124];
    private static readonly double[] XyzToLms = [0.8190224379967030, 0.3619062600528904, -0.1288737815209879, 0.0329836539323885, 0.9292868615863434, 0.0361446663506424, 0.0481771893596242, 0.2642395317527308, 0.6335478284694309];
    private static readonly double[] LmsToXyz = [1.2268798758459243, -0.5578149944602171, 0.2813910456659647, -0.0405757452148008, 1.1122868032803170, -0.0717110580655164, -0.0763729366746601, -0.4214933324022432, 1.5869240198367816];
    private static readonly double[] LmsToOklab = [0.2104542683093140, 0.7936177747023054, -0.0040720430116193, 1.9779985324311684, -2.4285922420485799, 0.4505937096174110, 0.0259040424655478, 0.7827717124575296, -0.8086757549230774];
    private static readonly double[] OklabToLms = [1.0, 0.3963377773761749, 0.2158037573099136, 1.0, -0.1055613458156586, -0.0638541728258133, 1.0, -0.0894841775298119, -1.2914855480194092];

    private static readonly double[] WhiteD50 = [0.3457 / 0.3585, 1.0, (1.0 - 0.3457 - 0.3585) / 0.3585];

    public static string SpaceName(ColorSpace s) => s switch
    {
        ColorSpace.Srgb => "srgb",
        ColorSpace.SrgbLinear => "srgb-linear",
        ColorSpace.DisplayP3 => "display-p3",
        ColorSpace.A98Rgb => "a98-rgb",
        ColorSpace.ProphotoRgb => "prophoto-rgb",
        ColorSpace.Rec2020 => "rec2020",
        ColorSpace.XyzD50 => "xyz-d50",
        ColorSpace.XyzD65 => "xyz-d65",
        ColorSpace.Hsl => "hsl",
        ColorSpace.Hwb => "hwb",
        ColorSpace.Lab => "lab",
        ColorSpace.Lch => "lch",
        ColorSpace.Oklab => "oklab",
        _ => "oklch",
    };

    private static double[] Mul(double[] m, double a, double b, double c) =>
    [
        (m[0] * a) + (m[1] * b) + (m[2] * c),
        (m[3] * a) + (m[4] * b) + (m[5] * c),
        (m[6] * a) + (m[7] * b) + (m[8] * c),
    ];

    // Math.Sign throws for NaN, which a calc() in a color channel can produce; NaN gets sign 0 here.
    private static double Sgn(double v) => v < 0 ? -1 : v > 0 ? 1 : 0;

    private static double SrgbToLinear(double v)
    {
        double s = Sgn(v);
        double a = Math.Abs(v);
        return a <= 0.04045 ? v / 12.92 : s * Math.Pow((a + 0.055) / 1.055, 2.4);
    }

    private static double LinearToSrgb(double v)
    {
        double s = Sgn(v);
        double a = Math.Abs(v);
        return a > 0.0031308 ? s * ((1.055 * Math.Pow(a, 1 / 2.4)) - 0.055) : 12.92 * v;
    }

    private static double A98ToLinear(double v) => Sgn(v) * Math.Pow(Math.Abs(v), 563.0 / 256.0);

    private static double LinearToA98(double v) => Sgn(v) * Math.Pow(Math.Abs(v), 256.0 / 563.0);

    private static double ProToLinear(double v)
    {
        double a = Math.Abs(v);
        return a <= 16.0 / 512.0 ? v / 16.0 : Sgn(v) * Math.Pow(a, 1.8);
    }

    private static double LinearToPro(double v)
    {
        double a = Math.Abs(v);
        return a >= 1.0 / 512.0 ? Sgn(v) * Math.Pow(a, 1 / 1.8) : 16.0 * v;
    }

    private const double Alpha2020 = 1.09929682680944;
    private const double Beta2020 = 0.018053968510807;

    private static double R2020ToLinear(double v)
    {
        double a = Math.Abs(v);
        return a < Beta2020 * 4.5 ? v / 4.5 : Sgn(v) * Math.Pow((a + Alpha2020 - 1) / Alpha2020, 1 / 0.45);
    }

    private static double LinearToR2020(double v)
    {
        double a = Math.Abs(v);
        return a >= Beta2020 ? Sgn(v) * ((Alpha2020 * Math.Pow(a, 0.45)) - (Alpha2020 - 1)) : 4.5 * v;
    }

    internal static double[] HslToSrgb(double h, double s, double l)
    {
        h = (((h % 360) + 360) % 360);
        s /= 100;
        l /= 100;
        double F(double n)
        {
            double k = (n + (h / 30)) % 12;
            double a = s * Math.Min(l, 1 - l);
            return l - (a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        return [F(0), F(8), F(4)];
    }

    internal static double[] HwbToSrgb(double h, double w, double b)
    {
        w /= 100;
        b /= 100;
        if (w + b >= 1)
        {
            double gray = w / (w + b);
            return [gray, gray, gray];
        }

        double[] rgb = HslToSrgb(h, 100, 50);
        for (int i = 0; i < 3; i++)
        {
            rgb[i] = (rgb[i] * (1 - w - b)) + w;
        }

        return rgb;
    }

    private static double[] SrgbToHsl(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = double.NaN;
        double s = 0;
        double l = (min + max) / 2;
        double d = max - min;
        if (d != 0)
        {
            s = l == 0 || l == 1 ? 0 : (max - l) / Math.Min(l, 1 - l);
            if (max == r)
            {
                h = ((g - b) / d) + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = ((b - r) / d) + 2;
            }
            else
            {
                h = ((r - g) / d) + 4;
            }

            h *= 60;
        }

        if (s < 0)
        {
            h += 180;
            s = Math.Abs(s);
        }

        if (h >= 360)
        {
            h -= 360;
        }

        return [double.IsNaN(h) ? 0 : h, s * 100, l * 100];
    }

    private static double[] SrgbToHwb(double r, double g, double b)
    {
        double[] hsl = SrgbToHsl(r, g, b);
        double w = Math.Min(r, Math.Min(g, b));
        double bl = 1 - Math.Max(r, Math.Max(g, b));
        return [hsl[0], w * 100, bl * 100];
    }

    private static double[] LabToXyz50(double l, double a, double b)
    {
        const double e = 216.0 / 24389.0;
        const double k = 24389.0 / 27.0;
        double f1 = (l + 16) / 116;
        double f0 = (a / 500) + f1;
        double f2 = f1 - (b / 200);
        double x = Math.Pow(f0, 3) > e ? Math.Pow(f0, 3) : ((116 * f0) - 16) / k;
        double y = l > k * e ? Math.Pow((l + 16) / 116, 3) : l / k;
        double z = Math.Pow(f2, 3) > e ? Math.Pow(f2, 3) : ((116 * f2) - 16) / k;
        return [x * WhiteD50[0], y * WhiteD50[1], z * WhiteD50[2]];
    }

    private static double[] Xyz50ToLab(double x, double y, double z)
    {
        const double e = 216.0 / 24389.0;
        const double k = 24389.0 / 27.0;
        double[] xyz = [x / WhiteD50[0], y / WhiteD50[1], z / WhiteD50[2]];
        double[] f = new double[3];
        for (int i = 0; i < 3; i++)
        {
            f[i] = xyz[i] > e ? Math.Cbrt(xyz[i]) : ((k * xyz[i]) + 16) / 116;
        }

        return [(116 * f[1]) - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])];
    }

    private static double[] OklabToXyz65(double l, double a, double b)
    {
        double[] lms = Mul(OklabToLms, l, a, b);
        return Mul(LmsToXyz, Math.Pow(lms[0], 3), Math.Pow(lms[1], 3), Math.Pow(lms[2], 3));
    }

    private static double[] Xyz65ToOklab(double x, double y, double z)
    {
        double[] lms = Mul(XyzToLms, x, y, z);
        return Mul(LmsToOklab, Math.Cbrt(lms[0]), Math.Cbrt(lms[1]), Math.Cbrt(lms[2]));
    }

    private static double[] Polar(double[] lab)
    {
        double c = Math.Sqrt((lab[1] * lab[1]) + (lab[2] * lab[2]));
        double h = Math.Atan2(lab[2], lab[1]) * 180 / Math.PI;
        if (h < 0)
        {
            h += 360;
        }

        return [lab[0], c, c < 1e-9 ? 0 : h];
    }

    private static double[] Rect(double l, double c, double h)
    {
        double r = h * Math.PI / 180;
        return [l, c * Math.Cos(r), c * Math.Sin(r)];
    }

    /// <summary>Component values of <paramref name="c"/> in XYZ D65.</summary>
    public static double[] ToXyz65(in CssColor c)
    {
        double a = c.Comp(0), b = c.Comp(1), d = c.Comp(2);
        switch (c.Space)
        {
            case ColorSpace.XyzD65:
                return [a, b, d];
            case ColorSpace.XyzD50:
                return Mul(D50ToD65, a, b, d);
            case ColorSpace.Srgb:
                return Mul(LinSrgbToXyz, SrgbToLinear(a), SrgbToLinear(b), SrgbToLinear(d));
            case ColorSpace.SrgbLinear:
                return Mul(LinSrgbToXyz, a, b, d);
            case ColorSpace.Hsl:
            {
                double[] rgb = HslToSrgb(a, b, d);
                return Mul(LinSrgbToXyz, SrgbToLinear(rgb[0]), SrgbToLinear(rgb[1]), SrgbToLinear(rgb[2]));
            }

            case ColorSpace.Hwb:
            {
                double[] rgb = HwbToSrgb(a, b, d);
                return Mul(LinSrgbToXyz, SrgbToLinear(rgb[0]), SrgbToLinear(rgb[1]), SrgbToLinear(rgb[2]));
            }

            case ColorSpace.DisplayP3:
                return Mul(LinP3ToXyz, SrgbToLinear(a), SrgbToLinear(b), SrgbToLinear(d));
            case ColorSpace.A98Rgb:
                return Mul(LinA98ToXyz, A98ToLinear(a), A98ToLinear(b), A98ToLinear(d));
            case ColorSpace.ProphotoRgb:
                return Mul(D50ToD65, Mul(LinProToXyz50, ProToLinear(a), ProToLinear(b), ProToLinear(d)));
            case ColorSpace.Rec2020:
                return Mul(Lin2020ToXyz, R2020ToLinear(a), R2020ToLinear(b), R2020ToLinear(d));
            case ColorSpace.Lab:
                return Mul(D50ToD65, LabToXyz50(a, b, d));
            case ColorSpace.Lch:
            {
                double[] lab = Rect(a, b, d);
                return Mul(D50ToD65, LabToXyz50(lab[0], lab[1], lab[2]));
            }

            case ColorSpace.Oklab:
                return OklabToXyz65(a, b, d);
            default:
            {
                double[] lab = Rect(a, b, d);
                return OklabToXyz65(lab[0], lab[1], lab[2]);
            }
        }
    }

    private static double[] Mul(double[] m, double[] v) => Mul(m, v[0], v[1], v[2]);

    public static CssColor Convert(in CssColor c, ColorSpace target)
    {
        if (c.Space == target)
        {
            return c;
        }

        double[] xyz = ToXyz65(c);
        double[] o = FromXyz65(xyz, target);
        byte missing = (byte)(c.MissingMask & 8);
        if (IsHuePowerless(c, target, o))
        {
            // Color 4 §12.4: a hue that carries no information after a conversion is treated as missing, so that
            // interpolation and relative colors take it from the other color.
            missing |= (byte)(target is ColorSpace.Hsl or ColorSpace.Hwb ? 1 : 4);
        }

        return new CssColor(target, o[0], o[1], o[2], c.Alpha, missing, false);
    }

    // Thresholds follow the precision the spec gives for each space: chroma below 0.0015 in lab and lch, 0.000004 in
    // oklab and oklch, saturation below 0.001% in hsl, whiteness plus blackness within 0.002% of 100% in hwb.
    private static bool IsHuePowerless(in CssColor source, ColorSpace target, double[] o)
    {
        if (target is not (ColorSpace.Hsl or ColorSpace.Hwb or ColorSpace.Lch or ColorSpace.Oklch))
        {
            return false;
        }

        switch (source.Space)
        {
            case ColorSpace.Lab:
                return Math.Sqrt((source.Comp(1) * source.Comp(1)) + (source.Comp(2) * source.Comp(2))) <= 0.0015;
            case ColorSpace.Lch:
                return source.Comp(1) <= 0.0015;
            case ColorSpace.Oklab:
                return Math.Sqrt((source.Comp(1) * source.Comp(1)) + (source.Comp(2) * source.Comp(2))) <= 0.000004;
            case ColorSpace.Oklch:
                return source.Comp(1) <= 0.000004;
        }

        return target switch
        {
            ColorSpace.Hsl => o[1] < 0.001,
            ColorSpace.Hwb => o[1] + o[2] >= 99.998,
            ColorSpace.Lch => o[1] <= 0.0015,
            _ => o[1] <= 0.000004,
        };
    }

    public static double[] FromXyz65(double[] xyz, ColorSpace target)
    {
        double x = xyz[0], y = xyz[1], z = xyz[2];
        switch (target)
        {
            case ColorSpace.XyzD65:
                return [x, y, z];
            case ColorSpace.XyzD50:
                return Mul(D65ToD50, x, y, z);
            case ColorSpace.Srgb:
            {
                double[] l = Mul(XyzToLinSrgb, x, y, z);
                return [LinearToSrgb(l[0]), LinearToSrgb(l[1]), LinearToSrgb(l[2])];
            }

            case ColorSpace.SrgbLinear:
                return Mul(XyzToLinSrgb, x, y, z);
            case ColorSpace.Hsl:
            {
                double[] l = Mul(XyzToLinSrgb, x, y, z);
                return SrgbToHsl(LinearToSrgb(l[0]), LinearToSrgb(l[1]), LinearToSrgb(l[2]));
            }

            case ColorSpace.Hwb:
            {
                double[] l = Mul(XyzToLinSrgb, x, y, z);
                return SrgbToHwb(LinearToSrgb(l[0]), LinearToSrgb(l[1]), LinearToSrgb(l[2]));
            }

            case ColorSpace.DisplayP3:
            {
                double[] l = Mul(XyzToLinP3, x, y, z);
                return [LinearToSrgb(l[0]), LinearToSrgb(l[1]), LinearToSrgb(l[2])];
            }

            case ColorSpace.A98Rgb:
            {
                double[] l = Mul(XyzToLinA98, x, y, z);
                return [LinearToA98(l[0]), LinearToA98(l[1]), LinearToA98(l[2])];
            }

            case ColorSpace.ProphotoRgb:
            {
                double[] x50 = Mul(D65ToD50, x, y, z);
                double[] l = Mul(Xyz50ToLinPro, x50);
                return [LinearToPro(l[0]), LinearToPro(l[1]), LinearToPro(l[2])];
            }

            case ColorSpace.Rec2020:
            {
                double[] l = Mul(XyzToLin2020, x, y, z);
                return [LinearToR2020(l[0]), LinearToR2020(l[1]), LinearToR2020(l[2])];
            }

            case ColorSpace.Lab:
            {
                double[] x50 = Mul(D65ToD50, x, y, z);
                return Xyz50ToLab(x50[0], x50[1], x50[2]);
            }

            case ColorSpace.Lch:
            {
                double[] x50 = Mul(D65ToD50, x, y, z);
                return Polar(Xyz50ToLab(x50[0], x50[1], x50[2]));
            }

            case ColorSpace.Oklab:
                return Xyz65ToOklab(x, y, z);
            default:
                return Polar(Xyz65ToOklab(x, y, z));
        }
    }

    // CSS Color 4 §13.2.1: reduce Oklch chroma until the clipped color is within a just-noticeable difference.
    public static (double R, double G, double B) GamutMap(in CssColor c)
    {
        const double jnd = 0.02;
        const double eps = 0.0001;
        double[] xyz = ToXyz65(c);
        double[] oklch = Polar(Xyz65ToOklab(xyz[0], xyz[1], xyz[2]));
        double l = oklch[0];
        if (l >= 1)
        {
            return (1, 1, 1);
        }

        if (l <= 0)
        {
            return (0, 0, 0);
        }

        double h = oklch[2];
        (double, double, double) Clip(double[] lch)
        {
            double[] lab = Rect(lch[0], lch[1], lch[2]);
            double[] rgb = FromXyz65(OklabToXyz65(lab[0], lab[1], lab[2]), ColorSpace.Srgb);
            return (Math.Clamp(rgb[0], 0, 1), Math.Clamp(rgb[1], 0, 1), Math.Clamp(rgb[2], 0, 1));
        }

        bool InGamut(double chroma)
        {
            double[] lab = Rect(l, chroma, h);
            double[] rgb = FromXyz65(OklabToXyz65(lab[0], lab[1], lab[2]), ColorSpace.Srgb);
            return rgb[0] is >= -eps and <= 1 + eps && rgb[1] is >= -eps and <= 1 + eps && rgb[2] is >= -eps and <= 1 + eps;
        }

        double min = 0;
        double max = oklch[1];
        bool minInGamut = true;
        (double, double, double) current = Clip([l, max, h]);
        if (InGamut(max))
        {
            return current;
        }

        while (max - min > eps)
        {
            double chroma = (min + max) / 2;
            if (minInGamut && InGamut(chroma))
            {
                min = chroma;
                continue;
            }

            (double cr, double cg, double cb) = Clip([l, chroma, h]);
            double[] clippedXyz = Mul(LinSrgbToXyz, SrgbToLinear(cr), SrgbToLinear(cg), SrgbToLinear(cb));
            double[] clippedLab = Xyz65ToOklab(clippedXyz[0], clippedXyz[1], clippedXyz[2]);
            double dl = clippedLab[0] - Rect(l, chroma, h)[0];
            double da = clippedLab[1] - Rect(l, chroma, h)[1];
            double db = clippedLab[2] - Rect(l, chroma, h)[2];
            double e = Math.Sqrt((dl * dl) + (da * da) + (db * db));
            if (e < jnd)
            {
                if (jnd - e < eps)
                {
                    return (cr, cg, cb);
                }

                minInGamut = false;
                min = chroma;
            }
            else
            {
                max = chroma;
            }
        }

        return Clip([l, min, h]);
    }

    /// <summary>
    /// CSS Color 4 §12 interpolation of two colors in <paramref name="space"/>: missing components take the other
    /// color's value, hue follows the chosen method, other components are premultiplied by alpha.
    /// </summary>
    public static CssColor Mix(in CssColor a, in CssColor b, double t, ColorSpace space, int hueMethod)
    {
        CssColor ca = Convert(a, space);
        CssColor cb = Convert(b, space);
        if (a.Space == space)
        {
            ca = a;
        }

        if (b.Space == space)
        {
            cb = b;
        }

        bool aAlphaMissing = (a.MissingMask & 8) != 0;
        bool bAlphaMissing = (b.MissingMask & 8) != 0;
        double aa = aAlphaMissing ? (bAlphaMissing ? 0 : b.Alpha) : a.Alpha;
        double ba = bAlphaMissing ? (aAlphaMissing ? 0 : a.Alpha) : b.Alpha;
        double alpha = (aa * (1 - t)) + (ba * t);
        int hueIndex = space switch
        {
            ColorSpace.Hsl or ColorSpace.Hwb => 0,
            ColorSpace.Lch or ColorSpace.Oklch => 2,
            _ => -1,
        };

        double[] o = new double[3];
        byte missing = 0;
        for (int i = 0; i < 3; i++)
        {
            bool ma = (ca.MissingMask & (1 << i)) != 0;
            bool mb = (cb.MissingMask & (1 << i)) != 0;
            if (ma && mb)
            {
                missing |= (byte)(1 << i);
                continue;
            }

            double va = ma ? Component(cb, i) : Component(ca, i);
            double vb = mb ? Component(ca, i) : Component(cb, i);
            if (i == hueIndex)
            {
                o[i] = MixHue(va, vb, t, hueMethod);
            }
            else if (alpha != 0)
            {
                o[i] = ((va * aa * (1 - t)) + (vb * ba * t)) / alpha;
            }
            else
            {
                o[i] = (va * (1 - t)) + (vb * t);
            }
        }

        byte m = missing;
        if (aAlphaMissing && bAlphaMissing)
        {
            m |= 8;
        }

        return new CssColor(space, o[0], o[1], o[2], alpha, m, false);
    }

    private static double Component(in CssColor c, int i) => i switch { 0 => c.C1, 1 => c.C2, _ => c.C3 };

    private static double MixHue(double a, double b, double t, int method)
    {
        a = ((a % 360) + 360) % 360;
        b = ((b % 360) + 360) % 360;
        double d = b - a;
        switch (method)
        {
            case 1: // longer
                if (d is > 0 and < 180)
                {
                    a += 360;
                }
                else if (d is > -180 and <= 0)
                {
                    b += 360;
                }

                break;
            case 2: // increasing
                if (b < a)
                {
                    b += 360;
                }

                break;
            case 3: // decreasing
                if (a < b)
                {
                    a += 360;
                }

                break;
            default: // shorter
                if (d > 180)
                {
                    a += 360;
                }
                else if (d < -180)
                {
                    b += 360;
                }

                break;
        }

        return (((a * (1 - t)) + (b * t)) % 360 + 360) % 360;
    }
}
