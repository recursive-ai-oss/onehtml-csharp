using System;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>Per-name facts the tree construction rules ask about, looked up by name id.</summary>
internal static class ElementInfo
{
    public const byte Special = 1;
    public const byte Formatting = 2;
    public const byte ImpliedEnd = 4;
    public const byte ImpliedEndThorough = 8;

    /// <summary>An HTML element that ends the "in scope" search (the default list).</summary>
    public const byte ScopeBoundary = 16;

    private static readonly byte[] s_cat = Build();
    private static readonly int[] s_svgTagFix = BuildFix(N.SvgTagFixes);
    private static readonly int[] s_svgAttrFix = BuildFix(N.SvgAttributeFixes);

    /// <summary>The category bits of an HTML element name.</summary>
    public static byte Cat(int name) => (uint)name < (uint)s_cat.Length ? s_cat[name] : (byte)0;

    public static bool IsSpecialHtml(int name) => (Cat(name) & Special) != 0;

    /// <summary>Whether an element with this namespace and name is in the special category.</summary>
    public static bool IsSpecial(byte ns, int name) => ns switch
    {
        (byte)ElementNamespace.Html => (Cat(name) & Special) != 0,
        (byte)ElementNamespace.MathMl => name is N.Mi or N.Mo or N.Mn or N.Ms or N.Mtext or N.AnnotationXml,
        _ => name is N.CcForeignObject or N.Desc or N.Title,
    };

    /// <summary>The repaired spelling of an SVG element name given the lowercase id, or the id itself.</summary>
    public static int FixSvgTag(int lower) => (uint)lower < (uint)s_svgTagFix.Length && s_svgTagFix[lower] >= 0 ? s_svgTagFix[lower] : lower;

    /// <summary>The repaired spelling of an SVG attribute name given the lowercase id, or the id itself.</summary>
    public static int FixSvgAttribute(int lower) => (uint)lower < (uint)s_svgAttrFix.Length && s_svgAttrFix[lower] >= 0 ? s_svgAttrFix[lower] : lower;

    private static int[] BuildFix(int[] pairs)
    {
        int[] map = new int[N.Count];
        Array.Fill(map, -1);
        for (int i = 0; i < pairs.Length; i += 2)
        {
            map[pairs[i]] = pairs[i + 1];
        }

        return map;
    }

    private static byte[] Build()
    {
        byte[] cat = new byte[N.Count];

        // HTML 13.2.4.2: the special category.
        foreach (int n in new[]
        {
            N.Address, N.Applet, N.Area, N.Article, N.Aside, N.Base, N.Basefont, N.Bgsound, N.Blockquote, N.Body, N.Br,
            N.Button, N.Caption, N.Center, N.Col, N.Colgroup, N.Dd, N.Details, N.Dir, N.Div, N.Dl, N.Dt, N.Embed,
            N.Fieldset, N.Figcaption, N.Figure, N.Footer, N.Form, N.Frame, N.Frameset, N.H1, N.H2, N.H3, N.H4, N.H5,
            N.H6, N.Head, N.Header, N.Hgroup, N.Hr, N.Html, N.Iframe, N.Img, N.Input, N.Keygen, N.Li, N.Link,
            N.Listing, N.Main, N.Marquee, N.Menu, N.Meta, N.Nav, N.Noembed, N.Noframes, N.Noscript, N.Object, N.Ol,
            N.P, N.Param, N.Plaintext, N.Pre, N.Script, N.Search, N.Section, N.Select, N.Source, N.Style, N.Summary,
            N.Table, N.Tbody, N.Td, N.Template, N.Textarea, N.Tfoot, N.Th, N.Thead, N.Title, N.Tr, N.Track, N.Ul,
            N.Wbr, N.Xmp,
        })
        {
            cat[n] |= Special;
        }

        // The formatting category.
        foreach (int n in new[] { N.A, N.B, N.Big, N.Code, N.Em, N.Font, N.I, N.Nobr, N.S, N.Small, N.Strike, N.Strong, N.Tt, N.U })
        {
            cat[n] |= Formatting;
        }

        // HTML 13.2.6.3: generate implied end tags.
        foreach (int n in new[] { N.Dd, N.Dt, N.Li, N.Optgroup, N.Option, N.P, N.Rb, N.Rp, N.Rt, N.Rtc })
        {
            cat[n] |= ImpliedEnd | ImpliedEndThorough;
        }

        foreach (int n in new[] { N.Caption, N.Colgroup, N.Tbody, N.Td, N.Tfoot, N.Th, N.Thead, N.Tr })
        {
            cat[n] |= ImpliedEndThorough;
        }

        // HTML 13.2.4.2: the elements that end the default "in scope" search.
        foreach (int n in new[] { N.Applet, N.Caption, N.Html, N.Table, N.Td, N.Th, N.Marquee, N.Object, N.Select, N.Template })
        {
            cat[n] |= ScopeBoundary;
        }

        return cat;
    }
}
