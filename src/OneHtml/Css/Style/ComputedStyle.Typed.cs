using System;
using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

public sealed partial class ComputedStyle
{
    private delegate bool TryParser<T>(ComponentValueList value, out T result);

    private const int KDisplay = 0, KVisibility = 1, KOpacity = 2, KBackgroundColor = 3, KOutlineColor = 4, KFontStyle = 5, KFontFamily = 6,
        KPosition = 7, KTop = 8, KRight = 9, KBottom = 10, KLeft = 11, KOverflowX = 12, KOverflowY = 13, KPointerEvents = 14, KSpeak = 15,
        KContent = 16, KClip = 17, KOutlineStyle = 18, KOutlineWidth = 19, KOutlineOffset = 20, KClipPath = 21, KBackgroundImage = 22,
        KSpeakAs = 23, KCount = 24;

    internal const int DeclColor = 1, DeclFontSize = 2, DeclFontWeight = 4;

    private object?[]? _typed;
    private object? _color;
    private double _fontSize = double.NaN;
    private double _fontWeight = double.NaN;

    /// <summary>The style of the root element this style belongs to the tree of.</summary>
    internal ComputedStyle Root => Parent is null ? this : Parent.Root;

    private object?[] Typed => Volatile.Read(ref _typed) ?? (Interlocked.CompareExchange(ref _typed, new object?[KCount], null) ?? _typed!);

    private T Parse<T>(int propertyId, int key, TryParser<T> parser, T fallback, string initialText)
    {
        object?[] cache = Typed;
        object? o = Volatile.Read(ref cache[key]);
        if (o is T t)
        {
            return t;
        }

        StyleValue v = Get(propertyId);
        T result;
        if (!parser(v.Tokens, out result!))
        {
            // The initial text is the table's own; an unparsable value cannot come from a validated declaration.
            result = initialText.Length > 0 && parser(CssTree.ParseComponentValueList(initialText).Values.Trimmed(), out T init) ? init : fallback;
        }

        cache[key] = result;
        return result;
    }

    private ColorParseOptions ColorOptions => Engine.ColorOptions;

    /// <summary>The element's <c>display</c>. Defaults to <c>inline</c> if the stored value does not parse.</summary>
    public DisplayValue Display => Parse<DisplayValue>(PropertyIds.Display, KDisplay, DisplayValue.TryParse, default, "inline");

    /// <summary>The element's <c>visibility</c>.</summary>
    public VisibilityValue Visibility => Parse<VisibilityValue>(PropertyIds.Visibility, KVisibility, TypedProperties.TryParseVisibility, default, "visible");

    /// <summary>The element's <c>opacity</c> in 0 to 1 (clamped); 1 by default.</summary>
    public double Opacity => Parse<double>(PropertyIds.Opacity, KOpacity, TypedProperties.TryParseOpacity, 1, "1");

    /// <summary>The element's <c>position</c>.</summary>
    public PositionValue Position => Parse<PositionValue>(PropertyIds.Get("position"), KPosition, TypedProperties.TryParsePosition, default, "static");

    /// <summary>The <c>top</c> offset as specified (percentages and <c>auto</c> are not resolved).</summary>
    public LengthPercentageOrAuto Top => Parse<LengthPercentageOrAuto>(PropertyIds.Get("top"), KTop, TypedProperties.TryParseOffset, default, "auto");

    /// <summary>The <c>right</c> offset as specified.</summary>
    public LengthPercentageOrAuto Right => Parse<LengthPercentageOrAuto>(PropertyIds.Get("right"), KRight, TypedProperties.TryParseOffset, default, "auto");

    /// <summary>The <c>bottom</c> offset as specified.</summary>
    public LengthPercentageOrAuto Bottom => Parse<LengthPercentageOrAuto>(PropertyIds.Get("bottom"), KBottom, TypedProperties.TryParseOffset, default, "auto");

    /// <summary>The <c>left</c> offset as specified.</summary>
    public LengthPercentageOrAuto Left => Parse<LengthPercentageOrAuto>(PropertyIds.Get("left"), KLeft, TypedProperties.TryParseOffset, default, "auto");

    /// <summary>The <c>overflow-x</c> keyword.</summary>
    public OverflowValue OverflowX => Parse<OverflowValue>(PropertyIds.Get("overflow-x"), KOverflowX, TypedProperties.TryParseOverflow, default, "visible");

    /// <summary>The <c>overflow-y</c> keyword.</summary>
    public OverflowValue OverflowY => Parse<OverflowValue>(PropertyIds.Get("overflow-y"), KOverflowY, TypedProperties.TryParseOverflow, default, "visible");

    /// <summary>The <c>pointer-events</c> keyword.</summary>
    public PointerEventsValue PointerEvents => Parse<PointerEventsValue>(PropertyIds.Get("pointer-events"), KPointerEvents, TypedProperties.TryParsePointerEvents, default, "auto");

    /// <summary>The <c>speak</c> keyword (<c>auto</c>, <c>never</c> or <c>always</c>).</summary>
    public string Speak => Parse<string>(PropertyIds.Get("speak"), KSpeak, TypedProperties.TryParseSpeak, "auto", "auto");

    /// <summary>The <c>content</c> value, or null when the stored value does not parse.</summary>
    public ContentValue? Content => Parse<ContentValue?>(PropertyIds.Get("content"), KContent, ContentValue.TryParse, null, "normal");

    /// <summary>The <c>clip</c> value.</summary>
    public ClipValue Clip => Parse<ClipValue>(PropertyIds.Get("clip"), KClip, TypedProperties.TryParseClip, default, "auto");

    /// <summary>The kind of shape the <c>clip-path</c> names.</summary>
    public ClipPathKind ClipPath => Parse<ClipPathKind>(PropertyIds.Get("clip-path"), KClipPath, TypedProperties.TryParseClipPath, default, "none");

    /// <summary>The kinds of the <c>background-image</c> layers.</summary>
    public IReadOnlyList<ImageKind> BackgroundImage => Parse<ImageKind[]>(PropertyIds.Get("background-image"), KBackgroundImage, TypedProperties.TryParseBackgroundImage, [], "none");

    /// <summary>The <c>speak-as</c> value.</summary>
    public SpeakAsValue SpeakAs => Parse<SpeakAsValue>(PropertyIds.Get("speak-as"), KSpeakAs, TypedProperties.TryParseSpeakAs, default, "normal");

    /// <summary>The <c>font-style</c> value.</summary>
    public FontStyleValue FontStyle => Parse<FontStyleValue>(PropertyIds.Get("font-style"), KFontStyle, TypedProperties.TryParseFontStyle, default, "normal");

    /// <summary>The <c>font-family</c> list, or null when the stored value does not parse.</summary>
    public FontFamilyValue? FontFamily => Parse<FontFamilyValue?>(PropertyIds.Get("font-family"), KFontFamily, TypedProperties.TryParseFontFamily, null, string.Empty);

    /// <summary>The <c>outline-style</c> keyword.</summary>
    public string OutlineStyle => Parse<string>(PropertyIds.Get("outline-style"), KOutlineStyle, TypedProperties.TryParseOutlineStyle, "none", "none");

    /// <summary>The used <c>outline-width</c> in px (a style of <c>none</c> or <c>hidden</c> makes it 0, CSS UI 4 §5.1).</summary>
    public double OutlineWidthPx
    {
        get
        {
            string style = OutlineStyle;
            if (style is "none" or "hidden")
            {
                return 0;
            }

            LineWidthValue? w = Parse<LineWidthValue?>(PropertyIds.Get("outline-width"), KOutlineWidth, TypedProperties.TryParseLineWidth, null, "medium");
            return w?.ResolvePx(LengthContextFor(FontSizePx)) ?? 3;
        }
    }

    /// <summary>The <c>outline-offset</c> in px.</summary>
    public double OutlineOffsetPx
    {
        get
        {
            CssQuantity q = Parse<CssQuantity>(PropertyIds.Get("outline-offset"), KOutlineOffset, TypedProperties.TryParseOutlineOffset, default, "0");
            return q.Resolve(LengthContextFor(FontSizePx));
        }
    }

    internal LengthContext LengthContextFor(double fontSizePx)
    {
        ComputedStyle root = Root;
        return Engine.BaseLength with
        {
            FontSize = fontSizePx,
            RootFontSize = ReferenceEquals(root, this) ? 16 : root.FontSizePx,
            VerticalWritingMode = (_mode >> 1) != 0,
        };
    }

    /// <summary>The computed <c>font-size</c> in px: keywords, <c>em</c>, <c>rem</c>, percentages and <c>calc()</c> resolved along the parent chain, from a 16px medium.</summary>
    public double FontSizePx
    {
        get
        {
            double d = Volatile.Read(ref _fontSize);
            if (!double.IsNaN(d))
            {
                return d;
            }

            var pending = new List<ComputedStyle>();
            ComputedStyle? s = this;
            while (s is not null && double.IsNaN(Volatile.Read(ref s._fontSize)))
            {
                pending.Add(s);
                s = s.Parent;
            }

            double parentPx = s is null ? 16 : s._fontSize;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                ComputedStyle st = pending[i];
                StyleValue v = st.Get(PropertyIds.FontSize);
                double px;
                if (st.Parent is not null && (st._declared & DeclFontSize) == 0)
                {
                    px = parentPx;
                }
                else
                {
                    FontSizeValue? fs;
                    if (TypedProperties.TryParseFontSize(v.Tokens, out fs) && fs is not null)
                    {
                        LengthContext ctx = Engine.BaseLength with
                        {
                            FontSize = parentPx,
                            RootFontSize = st.Parent is null ? 16 : st.Root._fontSize is var r && !double.IsNaN(r) ? r : 16,
                            VerticalWritingMode = (st._mode >> 1) != 0,
                        };
                        px = fs.ResolvePx(in ctx);
                    }
                    else
                    {
                        px = parentPx;
                    }
                }

                Volatile.Write(ref st._fontSize, px);
                parentPx = px;
            }

            return _fontSize;
        }
    }

    /// <summary>The computed <c>font-weight</c> as a number (1 to 1000); <c>bolder</c> and <c>lighter</c> resolved against the parent (CSS Fonts 4 §2.2).</summary>
    public double FontWeight
    {
        get
        {
            double d = Volatile.Read(ref _fontWeight);
            if (!double.IsNaN(d))
            {
                return d;
            }

            var pending = new List<ComputedStyle>();
            ComputedStyle? s = this;
            while (s is not null && double.IsNaN(Volatile.Read(ref s._fontWeight)))
            {
                pending.Add(s);
                s = s.Parent;
            }

            double parentWeight = s is null ? 400 : s._fontWeight;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                ComputedStyle st = pending[i];
                StyleValue v = st.Get(PropertyIds.FontWeight);
                double w;
                if (st.Parent is not null && (st._declared & DeclFontWeight) == 0)
                {
                    w = parentWeight;
                }
                else if (TypedProperties.TryParseFontWeight(v.Tokens, out FontWeightValue? fw) && fw is not null)
                {
                    w = fw.Resolve(parentWeight);
                }
                else
                {
                    w = parentWeight;
                }

                Volatile.Write(ref st._fontWeight, w);
                parentWeight = w;
            }

            return _fontWeight;
        }
    }

    /// <summary>
    /// The computed <c>color</c>. <c>currentcolor</c> on <c>color</c> itself means the inherited color; the root's inherited
    /// color is the initial <c>CanvasText</c> of the context's color scheme.
    /// </summary>
    public CssColor Color
    {
        get
        {
            if (Volatile.Read(ref _color) is CssColor c)
            {
                return c;
            }

            var pending = new List<ComputedStyle>();
            ComputedStyle? s = this;
            while (s is not null && Volatile.Read(ref s._color) is null)
            {
                pending.Add(s);
                s = s.Parent;
            }

            CssColor parentColor = s is null ? ParseColorOr(InitialOf(PropertyIds.Color).Tokens, CssColor.Create(ColorSpace.Srgb, 0, 0, 0)) : (CssColor)s._color!;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                ComputedStyle st = pending[i];
                StyleValue v = st.Get(PropertyIds.Color);
                CssColor result;
                if (st.Parent is not null && (st._declared & DeclColor) == 0)
                {
                    result = parentColor;
                }
                else
                {
                    CssColor parsed = ParseColorOr(v.Tokens, parentColor);
                    result = parsed.IsCurrentColor ? parentColor : parsed;
                }

                Volatile.Write(ref st._color, result);
                parentColor = result;
            }

            return (CssColor)_color!;
        }
    }

    private CssColor ParseColorOr(ComponentValueList tokens, CssColor fallback) =>
        CssColor.TryParse(tokens, out CssColor c, ColorOptions) ? c : fallback;

    /// <summary>The computed <c>background-color</c> with <c>currentcolor</c> replaced by <see cref="Color"/>. Transparent by default.</summary>
    public CssColor BackgroundColor => ResolvedColor(PropertyIds.BackgroundColor, KBackgroundColor, CssColor.Transparent);

    /// <summary>The computed <c>outline-color</c> with <c>currentcolor</c> replaced by <see cref="Color"/>.</summary>
    public CssColor OutlineColor => ResolvedColor(PropertyIds.Get("outline-color"), KOutlineColor, CssColor.CurrentColor);

    private CssColor ResolvedColor(int propertyId, int key, CssColor fallback)
    {
        object?[] cache = Typed;
        if (Volatile.Read(ref cache[key]) is CssColor cached)
        {
            return cached;
        }

        CssColor c = ParseColorOr(Get(propertyId).Tokens, fallback);
        if (c.IsCurrentColor)
        {
            c = Color;
        }

        cache[key] = c;
        return c;
    }

    /// <summary>
    /// The computed value of a property as <c>getComputedStyle</c> would serialize it, for the typed set (colors as
    /// resolved colors, lengths of font size, outline and offsets in px with relative units resolved, font weight as a
    /// number, keywords lower case). Other properties return their cascaded value text (CSS Cascade 5 §4.4: they are
    /// kept as token lists), and custom properties their substituted text. Layout-dependent values stay as specified.
    /// </summary>
    /// <param name="property">A property name.</param>
    /// <returns>The text, or the empty string for an unknown property.</returns>
    public string GetComputedValue(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
        {
            return TryGetCustomProperty(property, out string cv) ? cv : string.Empty;
        }

        int id = PropertyIds.Get(property);
        if (id < 0)
        {
            return string.Empty;
        }

        id = LogicalMap.Physical(id, Mode) is var p and >= 0 ? p : id;
        string name = PropertyIds.Info[id].Name;
        switch (name)
        {
            case "color":
                return Color.ToCss();
            case "background-color":
                return BackgroundColor.ToCss();
            case "outline-color":
                return OutlineColor.ToCss();
            case "font-size":
                return CssUnits.FormatNumber(Math.Max(0, FontSizePx)) + "px";
            case "font-weight":
                return CssUnits.FormatNumber(FontWeight);
            case "font-style":
                return FontStyle.ToCss();
            case "font-family" when FontFamily is { } ff:
                return ff.ToCss();
            case "display":
                return Display.ToCss();
            case "visibility":
                return Visibility.ToString().AsciiLower();
            case "opacity":
                return CssUnits.FormatNumber(Opacity);
            case "position":
                return Position == PositionValue.Sticky ? "sticky" : Position.ToString().AsciiLower();
            case "top":
                return OffsetText(Top);
            case "right":
                return OffsetText(Right);
            case "bottom":
                return OffsetText(Bottom);
            case "left":
                return OffsetText(Left);
            case "overflow-x":
                return OverflowName(ComputedOverflowX);
            case "overflow-y":
                return OverflowName(ComputedOverflowY);
            case "overflow":
                return ComputedOverflowX == ComputedOverflowY ? OverflowName(ComputedOverflowX) : OverflowName(ComputedOverflowX) + " " + OverflowName(ComputedOverflowY);
            case "outline-style":
                return OutlineStyle;
            case "outline-width":
            {
                // CSS Values 4 §7.4 / CSS UI 4 §4.1: a line width is never negative and snaps down to whole device pixels (at least 1).
                double w = OutlineWidthPx;
                return CssUnits.FormatNumber(w <= 0 ? 0 : Math.Max(1, Math.Floor(w))) + "px";
            }

            case "outline-offset" when Get(id).Text.Trim().Equals("inset", StringComparison.OrdinalIgnoreCase):
                return "inset";
            case "outline-offset":
                return CssUnits.FormatNumber(OutlineOffsetPx) + "px";
            case "speak":
                return Speak;
            default:
                return Get(id).Text;
        }
    }

    private static string OverflowName(OverflowValue v) => v.ToString().AsciiLower();

    // CSS Overflow 3 §3.1: visible computes to auto when the other axis scrolls (hidden, scroll or auto).
    private OverflowValue ComputedOverflowX => AdjustOverflow(OverflowX, OverflowY);

    private OverflowValue ComputedOverflowY => AdjustOverflow(OverflowY, OverflowX);

    private static OverflowValue AdjustOverflow(OverflowValue self, OverflowValue other) =>
        self == OverflowValue.Visible && other is OverflowValue.Hidden or OverflowValue.Scroll or OverflowValue.Auto ? OverflowValue.Auto : self;

    private string OffsetText(LengthPercentageOrAuto v)
    {
        if (v.IsAuto || v.Value.IsPercentage)
        {
            return v.ToCss();
        }

        double px = v.Value.Resolve(LengthContextFor(FontSizePx));
        return CssUnits.FormatNumber(px) + "px";
    }
}
