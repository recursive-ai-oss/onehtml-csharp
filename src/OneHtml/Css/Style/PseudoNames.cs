using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Selectors;

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
        PseudoElementKind.SliderThumb => "slider-thumb",
        PseudoElementKind.SliderTrack => "slider-track",
        PseudoElementKind.SliderFill => "slider-fill",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The name a style is indexed under for a pseudo-element written in a selector (lowercased), or null when no style is served for it.
    /// Chrome's and Firefox's names for the placeholder and the range slider parts are aliases of the standard ones. Chrome rejects the standard
    /// <c>::slider-*</c> names and the <c>-moz-</c> ones; they are accepted here so that one stylesheet written for any browser styles the parts.
    /// </summary>
    public static string? Canonical(string? selectorName) => selectorName switch
    {
        "before" or "after" or "marker" or "selection" or "placeholder" or "slider-thumb" or "slider-track" or "slider-fill" => selectorName,
        "-webkit-input-placeholder" => "placeholder",
        "-webkit-slider-thumb" or "-moz-range-thumb" => "slider-thumb",
        "-webkit-slider-runnable-track" or "-moz-range-track" => "slider-track",
        "-moz-range-progress" => "slider-fill",
        _ => null,
    };

    /// <summary>True when the element cannot have a box for the pseudo-element whatever the rules say: <c>::placeholder</c> needs a text control with a placeholder, the slider parts a range input.</summary>
    public static bool IsMissingBox(Node element, PseudoElementKind kind) => kind switch
    {
        PseudoElementKind.Placeholder => !ElementState.HasPlaceholderBox(element),
        PseudoElementKind.SliderThumb or PseudoElementKind.SliderTrack or PseudoElementKind.SliderFill => !ElementState.HasRangeBox(element),
        _ => false,
    };

    public static long Key(int elementIndex, PseudoElementKind kind) => ((long)elementIndex << 4) | (long)kind;
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
