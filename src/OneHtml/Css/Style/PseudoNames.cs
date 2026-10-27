using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Names and cache keys of the pseudo-elements that <see cref="DocumentStyles.GetPseudoStyle"/> serves.</summary>
internal static class PseudoNames
{
    public static string Of(PseudoElementKind kind) => kind switch
    {
        PseudoElementKind.Before => "before",
        PseudoElementKind.After => "after",
        PseudoElementKind.Marker => "marker",
        PseudoElementKind.Selection => "selection",
        PseudoElementKind.Placeholder => "placeholder",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static long Key(int elementIndex, PseudoElementKind kind) => ((long)elementIndex << 3) | (long)kind;
}

/// <summary>
/// The properties that apply to highlight pseudo-elements such as <c>::selection</c> (CSS Pseudo-Elements 4 section 3.1, "Highlight Pseudo-elements"):
/// the colours and decorations of the highlighted text and the paint properties of SVG text, plus custom properties. The list is what Chrome 156
/// applied when each property was set on <c>::selection</c> in turn; <c>caret-color</c>, the <c>-webkit-text-*</c> paints, <c>text-emphasis</c> and
/// every layout, font and box property had no effect there.
/// </summary>
internal static class HighlightProperties
{
    private static readonly string[] Names =
    [
        "color", "background-color", "text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness",
        "text-underline-offset", "text-shadow", "fill", "stroke", "stroke-width",
    ];

    /// <summary>The ids of the properties that apply, ascending.</summary>
    public static readonly int[] Ids = BuildIds();

    private static readonly bool[] Allowed = BuildFlags();

    /// <summary>Whether a declared value is for a property that applies to highlights (custom properties always do).</summary>
    public static bool Applies(StyleValue v) => v.CustomName is not null || ((uint)v.PropertyId < (uint)Allowed.Length && Allowed[v.PropertyId]);

    private static int[] BuildIds()
    {
        var ids = new List<int>();
        foreach (string n in Names)
        {
            int id = PropertyIds.Get(n);
            if (id >= 0)
            {
                ids.Add(id);
            }
        }

        ids.Sort();
        return [.. ids];
    }

    private static bool[] BuildFlags()
    {
        var flags = new bool[PropertyIds.Count];
        foreach (int id in Ids)
        {
            flags[id] = true;
        }

        return flags;
    }
}
