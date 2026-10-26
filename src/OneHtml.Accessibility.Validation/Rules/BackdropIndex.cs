using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>What else may paint behind a piece of text besides the backgrounds of its own ancestors.</summary>
/// <param name="Image">An image-like painter (a replaced element or a background image) that is not an ancestor may be behind the text: only pixels can say what shows.</param>
/// <param name="Solid">A positioned or transformed box with a background colour, not an ancestor, may be behind the text; null when none.</param>
internal readonly record struct Overlap(bool Image, string? Solid)
{
    internal bool Any => Image || Solid is not null;
}

/// <summary>
/// Finds the boxes that can paint behind text without being in its ancestor chain, which is all the cascade's background walk sees
/// (CSS 2 appendix E, painting order: a positioned or replaced box, a negative-z box or an overlapping sibling can sit under text of
/// another subtree). With rendered facts the answer is geometric: a candidate counts when its (clipped) box meets the text's boxes and
/// it paints earlier than the text. Without facts there is no geometry, so every candidate that may reach the text counts: an
/// out-of-flow box reaches everything inside its containing block, a box pulled by a negative margin, a relative offset or a transform
/// reaches its parent's subtree. Contrast never guesses a background, so either answer sends the text to a person or a render.
/// </summary>
internal sealed class BackdropIndex
{
    private const double Tolerance = 0.5;
    private const double BandHeight = 256;
    private const int TallBand = -1;
    private readonly CheckContext _context;
    private readonly PaintCalculator _paint;
    private bool _built;

    // Facts mode: candidates by rectangle, bucketed in horizontal bands.
    private readonly List<FactCandidate> _fact = [];
    private readonly Dictionary<int, List<int>> _bands = [];

    // Static mode: candidates by the node whose subtree they can reach (-1 is the whole page).
    private readonly Dictionary<int, List<(int Node, bool Image, bool Solid)>> _scoped = [];

    internal BackdropIndex(CheckContext context, PaintCalculator paint)
    {
        _context = context;
        _paint = paint;
    }

    private readonly record struct FactCandidate(int Node, LayoutRect Box, int PaintOrder, bool Image);

    /// <summary>What may be behind the text of <paramref name="element"/>. <paramref name="useElementBox"/> measures the element's own box (input labels, generated content) rather than its text runs.</summary>
    internal Overlap Query(Node element, bool useElementBox)
    {
        Build();
        return _context.Facts is { } facts ? QueryFacts(facts, element, useElementBox) : QueryStatic(element);
    }

    private void Build()
    {
        if (_built)
        {
            return;
        }

        _built = true;
        foreach (Node x in _context.Elements())
        {
            if (x.NamespaceURI != TextSupport.Html && !(x.NamespaceURI == "http://www.w3.org/2000/svg" && x.LocalName == "svg"))
            {
                continue;
            }

            ComputedStyle s = _context.Styles.GetStyle(x);
            bool image = IsImageLike(x, s);
            bool solid = !image && s.BackgroundColor.ToRgba8().A > 0 && (!IsStatic(s) || NotNone(s, "transform"));
            if (!image && !solid)
            {
                continue;
            }

            if (_context.Facts is { } facts)
            {
                AddFact(facts, x, s, image);
            }
            else
            {
                AddStatic(x, s, image, solid);
            }
        }
    }

    private void AddFact(RenderedFacts facts, Node x, ComputedStyle s, bool image)
    {
        if (!TextSupport.IsDrawn(_context, x, s) || !facts.TryGet(x, out NodeFacts nf) || !nf.HasLayout || _paint.Of(x).Opacity <= 0)
        {
            return;
        }

        LayoutRect b = nf.Layout.BorderBox;
        if (b.Width < 3 || b.Height < 3)
        {
            // Spacer pixels and hairlines: too small to be what text is read over.
            return;
        }

        double x0 = b.X, y0 = b.Y, x1 = b.Right, y1 = b.Bottom;
        if (!TargetSizeRule.ClipByAncestors(_context, facts, x, default, ref x0, ref y0, ref x1, ref y1) || x1 <= x0 || y1 <= y0)
        {
            return;
        }

        int index = _fact.Count;
        _fact.Add(new FactCandidate(x.Index, new LayoutRect(x0, y0, x1 - x0, y1 - y0), nf.Layout.PaintOrder ?? -1, image));
        int first = (int)Math.Floor(Math.Max(0, y0) / BandHeight), last = (int)Math.Floor(Math.Max(0, y1) / BandHeight);
        // A very tall box (a page-high background) goes in one shared band instead of thousands.
        bool tall = last - first > 64;
        for (int band = tall ? TallBand : first; band <= (tall ? TallBand : last); band++)
        {
            if (!_bands.TryGetValue(band, out List<int>? list))
            {
                _bands[band] = list = [];
            }

            list.Add(index);
        }
    }

    private void AddStatic(Node x, ComputedStyle s, bool image, bool solid)
    {
        if (!TextSupport.IsDrawn(_context, x, s) || _paint.Of(x).Opacity <= 0)
        {
            return;
        }

        string pos = s.GetPropertyValue("position").Trim();
        int scope;
        if (pos.Equals("fixed", StringComparison.OrdinalIgnoreCase))
        {
            scope = -1;
        }
        else if (pos.Equals("absolute", StringComparison.OrdinalIgnoreCase))
        {
            scope = -1;
            for (Node a = x.ParentElement; !a.IsNull; a = a.ParentElement)
            {
                ComputedStyle p = _context.Styles.GetStyle(a);
                if (!IsStatic(p) || NotNone(p, "transform") || NotNone(p, "filter") || NotNone(p, "perspective"))
                {
                    scope = a.Index;
                    break;
                }
            }
        }
        else if (NotNone(s, "transform") || (image && (HasNegativeMargin(s) || HasRelativeOffset(s, pos))))
        {
            scope = x.ParentElement.IsNull ? -1 : x.ParentElement.Index;
        }
        else
        {
            return;
        }

        if (!_scoped.TryGetValue(scope, out List<(int, bool, bool)>? list))
        {
            _scoped[scope] = list = [];
        }

        list.Add((x.Index, image, solid));
    }

    private Overlap QueryFacts(RenderedFacts facts, Node element, bool useElementBox)
    {
        if (_fact.Count == 0)
        {
            return default;
        }

        var rects = new List<LayoutRect>();
        int order = -1;
        bool unknownOrder = false;
        if (!useElementBox)
        {
            for (Node c = element.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == NodeKind.Text && facts.TryGet(c, out NodeFacts tf) && tf.HasLayout)
                {
                    foreach (TextBox box in tf.Layout.TextBoxes)
                    {
                        if (!box.Bounds.IsEmpty)
                        {
                            rects.Add(box.Bounds);
                        }
                    }

                    if (tf.Layout.PaintOrder is { } po)
                    {
                        order = Math.Max(order, po);
                    }
                    else
                    {
                        unknownOrder = true;
                    }
                }
            }
        }

        if (rects.Count == 0 && facts.TryGet(element, out NodeFacts ef) && ef.HasLayout)
        {
            rects.Add(ef.Layout.BorderBox);
            if (ef.Layout.PaintOrder is { } eo)
            {
                order = Math.Max(order, eo);
            }
            else
            {
                unknownOrder = true;
            }
        }

        if (rects.Count == 0)
        {
            return default;
        }

        var chain = new HashSet<int>();
        for (Node a = element; !a.IsNull; a = a.ParentElement)
        {
            chain.Add(a.Index);
        }

        bool image = false;
        string? solid = null;
        foreach (LayoutRect r in rects)
        {
            int first = (int)Math.Floor(Math.Max(0, r.Y) / BandHeight), last = (int)Math.Floor(Math.Max(0, r.Bottom) / BandHeight);
            var seen = new HashSet<int>();
            IEnumerable<int> bands = last - first > 64 ? _bands.Keys : Range(first, last);
            foreach (int band in bands)
            {
                if (!_bands.TryGetValue(band, out List<int>? list))
                {
                    continue;
                }

                foreach (int ci in list)
                {
                    if (!seen.Add(ci))
                    {
                        continue;
                    }

                    FactCandidate c = _fact[ci];
                    if (chain.Contains(c.Node) || !Meets(c.Box, r) || (!unknownOrder && c.PaintOrder >= 0 && c.PaintOrder >= order))
                    {
                        continue;
                    }

                    if (c.Image)
                    {
                        image = true;
                    }
                    else
                    {
                        solid ??= "overlapped by a positioned box with its own background";
                    }
                }
            }

            if (image)
            {
                break;
            }
        }

        return new Overlap(image, solid);
    }

    private static IEnumerable<int> Range(int first, int last)
    {
        for (int i = first; i <= last; i++)
        {
            yield return i;
        }

        yield return TallBand;
    }

    private static bool Meets(LayoutRect a, LayoutRect b) =>
        a.X < b.Right - Tolerance && b.X < a.Right - Tolerance && a.Y < b.Bottom - Tolerance && b.Y < a.Bottom - Tolerance;

    private Overlap QueryStatic(Node element)
    {
        if (_scoped.Count == 0)
        {
            return default;
        }

        var chain = new HashSet<int>();
        for (Node a = element; !a.IsNull; a = a.ParentElement)
        {
            chain.Add(a.Index);
        }

        bool image = false, solid = false;
        void Scan(int scope)
        {
            if (!_scoped.TryGetValue(scope, out List<(int Node, bool Image, bool Solid)>? list))
            {
                return;
            }

            foreach ((int node, bool img, bool sol) in list)
            {
                if (!chain.Contains(node))
                {
                    image |= img;
                    solid |= sol;
                }
            }
        }

        Scan(-1);
        foreach (int a in chain)
        {
            if (!image)
            {
                Scan(a);
            }
        }

        return new Overlap(image, solid ? "overlapped by a positioned box with its own background" : null);
    }

    private static bool IsImageLike(Node x, ComputedStyle s)
    {
        if (x.NamespaceURI != TextSupport.Html)
        {
            return true;
        }

        if (x.LocalName is "img" or "video" or "canvas" or "iframe" or "object" or "embed")
        {
            return true;
        }

        if (x.LocalName == "input" && string.Equals(x.GetAttribute("type"), "image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return NotNone(s, "background-image") && !s.GetPropertyValue("background-clip").Contains("text", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStatic(ComputedStyle s) => s.GetPropertyValue("position").Trim().Equals("static", StringComparison.OrdinalIgnoreCase);

    private static bool NotNone(ComputedStyle s, string property)
    {
        string v = s.GetPropertyValue(property).Trim();
        return v.Length > 0 && !v.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNegativeMargin(ComputedStyle s)
    {
        foreach (string side in new[] { "top", "right", "bottom", "left" })
        {
            if (CssLength.TryPx(s.GetPropertyValue("margin-" + side), s.FontSizePx, out double px) && px < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRelativeOffset(ComputedStyle s, string pos)
    {
        if (!pos.Equals("relative", StringComparison.OrdinalIgnoreCase) && !pos.Equals("sticky", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string side in new[] { "top", "right", "bottom", "left" })
        {
            string v = s.GetPropertyValue(side).Trim();
            if (v.Length > 0 && !v.Equals("auto", StringComparison.OrdinalIgnoreCase) && !(CssLength.TryPx(v, s.FontSizePx, out double px) && px == 0))
            {
                return true;
            }
        }

        return false;
    }
}
