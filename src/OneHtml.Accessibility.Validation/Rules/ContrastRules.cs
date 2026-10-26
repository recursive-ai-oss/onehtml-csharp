using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>What the contrast and presentation rules share: which text is visible, which text is exempt, and the facts it needs.</summary>
internal static class TextSupport
{
    internal const string Html = "http://www.w3.org/1999/xhtml";

    internal static readonly string[] BlendedFacts = ["blended background colors of the element", "paint order and stacking of the element"];

    internal static readonly string[] SampleFacts = ["background samples (median and 5th/95th percentile colours) behind the text"];

    /// <summary>True when the text has a character other than white space.</summary>
    internal static bool HasVisibleChar(ReadOnlySpan<char> text)
    {
        foreach (char ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the text has a letter or digit of any script.</summary>
    internal static bool HasLetterOrDigit(ReadOnlySpan<char> text)
    {
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True for text nodes children of the element that have a visible character. With facts, a text node counts only if it
    /// was laid out with a non-empty run that is not wholly above or left of the page; without them, only what the cascade can show.
    /// </summary>
    internal static List<Node> VisibleTextChildren(CheckContext context, Node element)
    {
        var list = new List<Node>();
        for (Node c = element.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != NodeKind.Text || !HasVisibleChar(c.Data))
            {
                continue;
            }

            if (context.Facts is { } facts)
            {
                if (!facts.TryGet(c, out NodeFacts nf) || !nf.HasLayout)
                {
                    continue;
                }

                bool any = false;
                foreach (TextBox box in nf.Layout.TextBoxes)
                {
                    any |= !box.Bounds.IsEmpty && box.Bounds.Right > 0 && box.Bounds.Bottom > 0;
                }

                if (!any)
                {
                    continue;
                }
            }

            list.Add(c);
        }

        return list;
    }

    /// <summary>True for an element the page draws: it has a tree node, no <c>display: none</c> or <c>visibility</c> hiding.</summary>
    internal static bool IsDrawn(CheckContext context, Node element, ComputedStyle style)
    {
        if (context.Find(element) is not { } ax || ax.Hidden.IsNotRendered || style.Visibility != VisibilityValue.Visible)
        {
            return false;
        }

        if (context.Facts is { } facts)
        {
            if (!facts.TryGet(element, out NodeFacts nf) || !nf.HasLayout || nf.Layout.BorderBox.Right <= 0 || nf.Layout.BorderBox.Bottom <= 0)
            {
                return false;
            }

            // Clipped away by an ancestor with hidden overflow (a closed drop-down panel): nothing of the element is on screen.
            LayoutRect b = nf.Layout.BorderBox;
            double x0 = b.X, y0 = b.Y, x1 = b.Right, y1 = b.Bottom;
            return TargetSizeRule.ClipByAncestors(context, facts, element, default, ref x0, ref y0, ref x1, ref y1);
        }

        return true;
    }

    /// <summary>True when the role is a widget (WAI-ARIA 1.2: <c>widget</c> or a role inheriting from it).</summary>
    internal static bool IsWidget(AriaRole? role)
    {
        if (role is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<AriaRole>();
        stack.Push(role);
        while (stack.Count > 0)
        {
            AriaRole r = stack.Pop();
            if (r.Name == "widget")
            {
                return true;
            }

            foreach (string s in r.Superclasses)
            {
                if (seen.Add(s) && AriaTaxonomy.FindRole(s) is { } sup)
                {
                    stack.Push(sup);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Elements whose text is incidental in WCAG 1.4.3 because it belongs to a disabled control: the elements that label a
    /// disabled widget, by <c>label</c> or by <c>aria-labelledby</c>.
    /// </summary>
    internal static HashSet<int> DisabledLabels(CheckContext context, CancellationTokenLike token)
    {
        var set = new HashSet<int>();
        Dictionary<int, List<Node>>? byControl = null;
        int counter = 0;
        foreach (AxNode n in context.Tree.Nodes)
        {
            if ((++counter & 1023) == 0)
            {
                token.Throw();
            }

            if (n.Kind != AxNodeKind.Element || !n.States.Disabled || !IsWidget(n.AriaRole))
            {
                continue;
            }

            byControl ??= FormFields.LabelsByControl(context.Document, token.Token);
            if (byControl.TryGetValue(n.DomNode.Index, out List<Node>? labels))
            {
                foreach (Node l in labels)
                {
                    set.Add(l.Index);
                }
            }

            if (n.GetRelation(AxRelationKind.LabelledBy) is { } rel)
            {
                foreach (Node e in rel.Elements)
                {
                    set.Add(e.Index);
                }
            }
        }

        return set;
    }

    /// <summary>Walks up the elements, memoising whether any is a disabled widget or a disabled group (ACT afw4f7 exceptions) or labels one.</summary>
    internal sealed class ExemptCache(CheckContext context, HashSet<int> labels)
    {
        private readonly Dictionary<int, bool> _memo = [];

        internal bool IsExempt(Node element)
        {
            var chain = new List<Node>();
            bool result = false;
            for (Node e = element; !e.IsNull; e = e.ParentElement)
            {
                if (_memo.TryGetValue(e.Index, out bool known))
                {
                    result = known;
                    break;
                }

                chain.Add(e);
            }

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Node e = chain[i];
                if (!result)
                {
                    result = labels.Contains(e.Index)
                        || (context.Find(e) is { } ax && ax.States.Disabled && (IsWidget(ax.AriaRole) || ax.AriaRole is { Name: "group" } || (e.NamespaceURI == Html && e.LocalName == "fieldset")));
                }

                _memo[e.Index] = result;
            }

            return result;
        }
    }
}

/// <summary>Carries the cancellation token without passing the reporter around.</summary>
internal readonly struct CancellationTokenLike(System.Threading.CancellationToken token)
{
    internal System.Threading.CancellationToken Token => token;

    internal void Throw() => token.ThrowIfCancellationRequested();
}

// axe color-contrast / color-contrast-enhanced, ACT afw4f7 and 09o5cg (WCAG 1.4.3 Contrast (Minimum), 1.4.6 Contrast (Enhanced)).
// The contrast ratio is (L1 + 0.05) / (L2 + 0.05) over the relative luminance of the text colour and the colour behind it.
// "Highest possible contrast" (ACT): where several colours can be behind the text (a gradient), the best of them counts.
// The background is never guessed: it comes from the cascade where the cascade paints a solid colour or a gradient with
// stops, from the browser's blended colour where something out of flow may overlap, from supplied pixel samples where an
// image is behind the text, and is otherwise reported as needing facts.
internal sealed class ColorContrastRule : AriaRule
{
    private readonly bool _enhanced;

    public ColorContrastRule(bool enhanced)
        : base(enhanced ? "color-contrast-enhanced" : "color-contrast",
            enhanced ? "Text must have a contrast ratio of at least 7:1 with its background (4.5:1 for large text)." : "Text must have a contrast ratio of at least 4.5:1 with its background (3:1 for large text).",
            enhanced ? "Change the text or background colour until the ratio reaches 7:1, or 4.5:1 for large text." : "Change the text or background colour until the ratio reaches 4.5:1, or 3:1 for large text.",
            null, "Colours are a design choice: many pairs would pass, and the right one depends on the brand and the page.",
            actIds: enhanced ? ["09o5cg"] : ["afw4f7"], needs: RuleNeeds.RenderedFacts)
    {
        _enhanced = enhanced;
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Ancestors | RuleScope.IdReferences;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var paint = new PaintCalculator(context);
        var backdrop = new BackdropIndex(context, paint);
        var exempt = new TextSupport.ExemptCache(context, TextSupport.DisabledLabels(context, new CancellationTokenLike(reporter.CancellationToken)));
        int counter = 0;
        foreach (Node element in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (element.NamespaceURI != TextSupport.Html || element.LocalName is "script" or "style" or "template" or "noscript" or "title")
            {
                continue;
            }

            ComputedStyle style = context.Styles.GetStyle(element);
            if (!TextSupport.IsDrawn(context, element, style))
            {
                continue;
            }

            bool hasText = false, language = false;
            for (Node c = element.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == NodeKind.Text && TextSupport.HasVisibleChar(c.Data))
                {
                    hasText = true;
                    language |= TextSupport.HasLetterOrDigit(c.Data);
                }
            }

            // The label of a push button made with <input> is drawn from its value (or the browser's default label), not from a text node.
            bool inputLabel = false;
            if (element.LocalName == "input" && element.GetAttribute("type") is { } inputType && (inputType.Equals("submit", StringComparison.OrdinalIgnoreCase) || inputType.Equals("button", StringComparison.OrdinalIgnoreCase) || inputType.Equals("reset", StringComparison.OrdinalIgnoreCase)))
            {
                string value = element.GetAttribute("value") ?? string.Empty;
                inputLabel = TextSupport.HasVisibleChar(value) || !inputType.Equals("button", StringComparison.OrdinalIgnoreCase);
                if (inputLabel)
                {
                    hasText = true;
                    language |= value.Length == 0 || TextSupport.HasLetterOrDigit(value);
                }
            }

            ComputedStyle? before = Generated(context, element, PseudoElementKind.Before);
            ComputedStyle? after = Generated(context, element, PseudoElementKind.After);
            if (!hasText && before is null && after is null)
            {
                continue;
            }

            if (exempt.IsExempt(element))
            {
                continue;
            }

            PaintState state = paint.Of(element);
            if (hasText && !state.Offscreen && style.GetPropertyValue("text-indent") is var indent && !(CssLength.TryPx(indent, style.FontSizePx, out double ind) && ind <= -1000)
                && (inputLabel || TextSupport.VisibleTextChildren(context, element).Count > 0))
            {
                if (language)
                {
                    Check(context, reporter, element, style, state.With(backdrop.Query(element, inputLabel)), pseudo: null);
                }
                else
                {
                    // ACT afw4f7 expectation: text that expresses nothing in human language (rows of symbols) is exempt.
                    reporter.Passed(element, "The text has no letters or digits, so it does not express anything in human language.");
                }
            }

            foreach ((ComputedStyle? ps, string name) in new[] { (before, "::before"), (after, "::after") })
            {
                if (ps is not null && !state.Offscreen && ps.Visibility == VisibilityValue.Visible && (context.Facts is not { } pf || (pf.TryGet(element, out NodeFacts pnf) && pnf.GeneratedBoxes.Count > 0)))
                {
                    // Generated text has no node of its own: the cascade decides, and with facts only a pseudo-element that was laid out counts.
                    // Like element text, content made only of symbols (a ">" breadcrumb separator) expresses nothing in human language and is exempt.
                    if (GeneratedHasLanguage(ps))
                    {
                        Check(context, reporter, element, ps, paint.OfPseudo(element, ps).With(backdrop.Query(element, true)), name);
                    }
                }
            }
        }
    }

    private static Backing[] WithShadows(ComputedStyle style, Backing[] backings, double opacity)
    {
        string shadows = style.GetPropertyValue("text-shadow").Trim();
        if (shadows.Length == 0 || shadows.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return backings;
        }

        var list = new List<Backing>(backings);
        foreach (string shadow in CssText.SplitTopLevel(shadows, ','))
        {
            List<string> words = CssText.SplitTopLevel(shadow.Trim(), ' ');
            CssColor color = CssColor.CurrentColor;
            foreach (string w in new[] { words[0], words[^1] })
            {
                if (CssColor.TryParse(w, out CssColor parsed))
                {
                    color = parsed;
                    break;
                }
            }

            (byte r, byte g, byte b, double a) = color.Resolve(style.Color).ToRgba8();
            if (a * opacity <= 0)
            {
                continue;
            }

            foreach (Backing under in backings)
            {
                var add = new Backing(ContrastMath.Over(new Rgb8(r, g, b), a * opacity, under.Color), false);
                if (!list.Contains(add))
                {
                    list.Add(add);
                }
            }
        }

        return [.. list];
    }

    // A generated box with string content that has a visible character (CSS Generated Content 3: only strings are text this rule reads).
    private static ComputedStyle? Generated(CheckContext context, Node element, PseudoElementKind kind)
    {
        if (context.Styles.GetPseudoStyle(element, kind) is not { } ps || ps.Content is not { IsNone: false, IsNormal: false } content)
        {
            return null;
        }

        foreach (ContentItem item in content.Items)
        {
            if (item.Kind == ContentItemKind.Text && item.Text is not null && TextSupport.HasVisibleChar(item.Text))
            {
                return ps.Display.IsRendered ? ps : null;
            }
        }

        return null;
    }

    private static bool GeneratedHasLanguage(ComputedStyle ps)
    {
        foreach (ContentItem item in ps.Content?.Items ?? [])
        {
            if (item.Kind == ContentItemKind.Text && item.Text is not null && TextSupport.HasLetterOrDigit(item.Text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFormControl(Node e) =>
        e.Namespace == ElementNamespace.Html && (e.LocalName is "button" or "select" or "textarea" or "input");

    // The colour is only inherited when no declaration (an explicit "color: inherit" included) won for this element.
    private static bool ColorIsInherited(ComputedStyle s) => s.Parent is not null && !s.IsDeclared("color");

    private void Check(CheckContext context, RuleReporter reporter, Node element, ComputedStyle style, PaintState state, string? pseudo)
    {
        (byte fr, byte fg, byte fb, double fa) = style.Color.ToRgba8();
        if (pseudo is null && IsFormControl(element) && ColorIsInherited(style))
        {
            // A browser's own sheet gives a form control its own text colour (ButtonText or FieldText, black with the default
            // colour scheme) instead of the parent's; the HTML Standard's rendering sheet does not say so.
            (fr, fg, fb, fa) = (0, 0, 0, 1.0);
        }

        double alpha = fa * state.Opacity;
        if (alpha <= 0)
        {
            return;
        }

        var used = new List<string>();
        Backing[] backings = state.Backings;
        if (state.Hazard is { } hazard)
        {
            Rgba blended = default;
            if (context.Facts is { } facts && facts.TryGet(element, out NodeFacts nf) && nf.HasLayout && nf.Layout.BlendedBackgroundColor is { } text && Rgba.TryParse(text, out blended))
            {
                var list = new List<Backing> { new(new Rgb8(blended.R, blended.G, blended.B), false) };
                foreach (Backing b in backings)
                {
                    if (b.Unknown)
                    {
                        list.Add(b);
                    }
                }

                if (state.Gradient)
                {
                    // The browser reports the first colour of a gradient, not the gradient.
                    list.Add(new Backing(default, true));
                }

                backings = [.. list];
                used.Add("blended background color");
            }
            else
            {
                reporter.NeedsRender(element, "The element is " + hazard + ", so other content may be behind its text and the cascade cannot say which colour.", TextSupport.BlendedFacts);
                return;
            }
        }

        bool sampled = false;
        double fontPx = style.FontSizePx;
        bool large = ContrastMath.IsLarge(fontPx, style.FontWeight);
        double needed = _enhanced ? (large ? 4.5 : 7) : (large ? 3 : 4.5);
        var fore = new Rgb8(fr, fg, fb);
        var ratios = new List<double>();
        Rgb8 best = default;
        double bestRatio = -1;
        bool anyUnknown = false;
        foreach (Backing b in backings)
        {
            anyUnknown |= b.Unknown;
        }

        if (anyUnknown)
        {
            BackgroundSample? sample = null;
            if (context.Facts is { } sf)
            {
                for (Node e = element; !e.IsNull && sample is null; e = e.ParentElement)
                {
                    if (sf.TryGet(e, out NodeFacts nf2))
                    {
                        sample = nf2.Background;
                    }
                }
            }

            if (sample is not { } s)
            {
                reporter.NeedsRender(element, "Something the cascade cannot colour (an image, a gradient it cannot read, or a positioned generated box) is behind the text; its pixels decide the contrast.", TextSupport.SampleFacts);
                return;
            }

            sampled = true;
            used.Add("background samples");
            backings = [new Backing(new Rgb8(s.Median.R, s.Median.G, s.Median.B), false), new Backing(new Rgb8(s.P5.R, s.P5.G, s.P5.B), false), new Backing(new Rgb8(s.P95.R, s.P95.G, s.P95.B), false)];
        }

        // ACT afw4f7 counts the colours of a text shadow among the colours behind the glyphs; WCAG says nothing either way.
        if (!sampled)
        {
            backings = WithShadows(style, backings, state.Opacity);
        }

        bool identical = true;
        foreach (Backing b in backings)
        {
            Rgb8 shown = ContrastMath.Over(fore, alpha, b.Color);
            identical &= shown == b.Color;
            double ratio = ContrastMath.Ratio(shown, b.Color);
            ratios.Add(ratio);
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                best = b.Color;
            }
        }

        if (identical)
        {
            // Text drawn in exactly its background colour is not a presentation of text (ACT afw4f7 assumptions); nothing to judge.
            return;
        }

        double min = double.MaxValue;
        foreach (double r in ratios)
        {
            min = Math.Min(min, r);
        }

        string sizeNote = (large ? "Large text " : "Normal text ") + fontPx.ToString("0.##", CultureInfo.InvariantCulture) + "px, weight " + style.FontWeight.ToString("0", CultureInfo.InvariantCulture);
        var findings = new List<Finding>
        {
            new("color", new Rgb8(fr, fg, fb).ToString() + (alpha < 1 ? " at " + alpha.ToString("0.##", CultureInfo.InvariantCulture) + " opacity" : string.Empty), "The text colour" + (pseudo is null ? "." : " of the " + pseudo + " content.")),
            new(null, best.ToString(), sampled ? "The sampled background with the highest contrast." : backings.Length > 1 ? "The background colour with the highest contrast." : "The background colour."),
            new(null, ContrastMath.Format(bestRatio) + ":1", sizeNote + "; " + ContrastMath.Format(needed) + ":1 is needed."),
        };
        string[] factsUsed = [.. used];
        if (sampled)
        {
            if (min >= needed)
            {
                reporter.Passed(element, "The text has enough contrast with every sampled background colour.", factsUsed);
            }
            else if (bestRatio < needed)
            {
                reporter.Failed(element, "The text does not have enough contrast with the sampled background (" + ContrastMath.Format(bestRatio) + ":1 at best, " + ContrastMath.Format(needed) + ":1 needed).", findings, null, factsUsed);
            }
            else
            {
                reporter.NeedsReview(element, "The text has enough contrast with some sampled background colours and not with others; whether it is legible over the image needs a person.", findings, factsUsed);
            }

            return;
        }

        if (bestRatio >= needed)
        {
            reporter.Passed(element, backings.Length > 1 ? "The text has enough contrast with the most contrasting colour of its gradient background." : null, factsUsed);
        }
        else
        {
            reporter.Failed(element, "The text has a contrast ratio of " + ContrastMath.Format(bestRatio) + ":1; " + ContrastMath.Format(needed) + ":1 is needed.", findings, null, factsUsed);
        }
    }
}

// axe link-in-text-block (WCAG 1.4.1 Use of Color, technique G183 and failure F73): a link inside a block of text must be
// told apart from the text around it by more than colour, or by a colour contrast of at least 3:1 with that text.
// Cues read from the cascade: a text decoration line, a border, an outline, a different font weight, style, size or family.
// The extra cue on hover and focus that G183 asks for with the colour route is script- and state-dependent and is not checked.
// A "block of text" is the nearest block container of the link when the text of its inline content outside links is longer
// than the text of its links (white space collapsed): a row of navigation links or a link standing alone is not a sentence,
// and the criterion is about links that are read among words. This is the reading axe-core's results show for 100+ probe
// links (docs/axe-differential.md); WCAG 1.4.1 itself only says "within a block of text".
internal sealed class LinkInTextBlockRule : AriaRule
{
    public LinkInTextBlockRule()
        : base("link-in-text-block", "A link inside a block of text must be distinguishable from the surrounding text by more than colour.",
            "Underline the link, or give it a colour with at least 3:1 contrast against the surrounding text.", null,
            "How to mark a link (underline, weight, colour) is a design choice.", needs: RuleNeeds.Static)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var paint = new PaintCalculator(context);
        var blocks = new Dictionary<int, BlockText>();
        int counter = 0;
        foreach (Node link in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (link.NamespaceURI != TextSupport.Html || link.LocalName != "a" || !link.HasAttribute("href"))
            {
                continue;
            }

            ComputedStyle ls = context.Styles.GetStyle(link);
            if (!TextSupport.IsDrawn(context, link, ls) || !IsInline(ls) || context.Find(link) is not { AriaRole.Name: "link" })
            {
                continue;
            }

            // The nearest ancestor that is not inline holds the text block.
            Node block = link.ParentElement;
            while (!block.IsNull && IsInline(context.Styles.GetStyle(block)))
            {
                block = block.ParentElement;
            }

            if (block.IsNull)
            {
                continue;
            }

            if (!blocks.TryGetValue(block.Index, out BlockText? text))
            {
                blocks[block.Index] = text = BlockText.Measure(context, block);
            }

            // Not a link among words: no words around it, or the links carry most of the text; the link has no text of its own.
            if (text.Reference.IsNull || text.OtherLength <= text.LinkLength || !HasText(link))
            {
                continue;
            }

            Node reference = text.Reference;

            ComputedStyle rs = context.Styles.GetStyle(reference);
            bool linkLine = HasLine(context, link, block, includeSelf: true);
            bool textLine = HasLine(context, reference, block, includeSelf: true);
            if (linkLine && !textLine)
            {
                reporter.Passed(link, "The link is underlined or struck through and the surrounding text is not.");
                continue;
            }

            if (HasBorder(ls) && !HasBorder(rs))
            {
                reporter.Passed(link, "The link has a border the surrounding text does not.");
                continue;
            }

            if (Math.Abs(ls.FontWeight - rs.FontWeight) >= 100 || ls.FontStyle != rs.FontStyle
                || Math.Abs(ls.FontSizePx - rs.FontSizePx) > 0.01 || !ls.GetPropertyValue("font-family").Equals(rs.GetPropertyValue("font-family"), StringComparison.Ordinal))
            {
                reporter.Passed(link, "The link differs from the surrounding text in font weight, style, size or family.");
                continue;
            }

            if (HasOutline(ls) && !HasOutline(rs))
            {
                reporter.Passed(link, "The link has an outline the surrounding text does not.");
                continue;
            }

            // A background image on the link only (an underline drawn as a gradient, an animated highlight) is a visual cue (measured with axe-core).
            if (HasBackgroundImage(ls) && !HasBackgroundImage(rs))
            {
                reporter.Passed(link, "The link has a background image the surrounding text does not.");
                continue;
            }

            PaintState back = paint.Of(link);
            Rgb8 under = back.Backings.Length > 0 && !back.Backings[0].Unknown ? back.Backings[0].Color : new Rgb8(255, 255, 255);
            (byte lr, byte lg, byte lb, double la) = ls.Color.ToRgba8();
            (byte tr, byte tg, byte tb, double ta) = rs.Color.ToRgba8();
            Rgb8 linkColor = ContrastMath.Over(new Rgb8(lr, lg, lb), la * back.Opacity, under);
            Rgb8 textColor = ContrastMath.Over(new Rgb8(tr, tg, tb), ta * paint.Of(reference).Opacity, under);
            double ratio = ContrastMath.Ratio(linkColor, textColor);
            var findings = new List<Finding>
            {
                new("color", linkColor.ToString(), "The link colour."),
                new(null, textColor.ToString(), "The colour of the surrounding text."),
                new(null, ContrastMath.Format(ratio) + ":1", "The contrast between them; 3:1 is needed when nothing else marks the link."),
            };
            if (ratio >= 3)
            {
                reporter.Passed(link, "The link colour has at least 3:1 contrast with the surrounding text. A cue on hover and focus is not checked.");
            }
            else
            {
                reporter.Failed(link, "Only colour sets the link apart from the surrounding text, and the contrast between them is " + ContrastMath.Format(ratio) + ":1.", findings);
            }
        }
    }

    /// <summary>True when the block has visible text of its own outside links and controls: the link sits in a line of words.</summary>
    internal static bool HasOtherText(CheckContext context, Node block, Dictionary<int, bool> cache)
    {
        if (!cache.TryGetValue(block.Index, out bool has))
        {
            cache[block.Index] = has = !BlockText.Measure(context, block).Reference.IsNull;
        }

        return has;
    }

    /// <summary>True for an element that sits in a line of text: its outer display type is inline (inline-block included).</summary>
    internal static bool IsInlineLevel(ComputedStyle s) => s.Display.Kind == DisplayKind.Box && s.Display.Outside == DisplayOutside.Inline;

    private static bool HasText(Node link)
    {
        for (Node n = link.NextInDocumentOrder(link); !n.IsNull; n = n.NextInDocumentOrder(link))
        {
            if (n.Kind == NodeKind.Text && TextSupport.HasVisibleChar(n.Data))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBackgroundImage(ComputedStyle s)
    {
        string image = s.GetPropertyValue("background-image").Trim();
        return image.Length > 0 && !image.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasOutline(ComputedStyle s)
    {
        string style = s.GetPropertyValue("outline-style");
        return style.Length > 0 && !style.Equals("none", StringComparison.OrdinalIgnoreCase) && !style.Equals("hidden", StringComparison.OrdinalIgnoreCase)
            && CssLength.TryPx(s.GetPropertyValue("outline-width"), s.FontSizePx, out double w) && w > 0;
    }

    /// <summary>The text of one block container: what is in its inline content outside links, and what is in links.</summary>
    private sealed class BlockText
    {
        internal Node Reference { get; private init; }

        internal int OtherLength { get; private init; }

        internal int LinkLength { get; private init; }

        /// <summary>One walk over the block's inline-level content (nested blocks, out-of-flow boxes and controls are not part of its line of text).</summary>
        internal static BlockText Measure(CheckContext context, Node block)
        {
            var other = new CollapsedLength();
            var links = new CollapsedLength();
            Node reference = default;
            var stack = new Stack<(Node Node, bool InLink)>();
            for (Node c = block.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push((c, false));
            }

            int budget = 200_000;
            while (stack.Count > 0 && budget-- > 0)
            {
                (Node n, bool inLink) = stack.Pop();
                if (n.Kind == NodeKind.Text)
                {
                    (inLink ? links : other).Add(n.Data);
                    if (!inLink && reference.IsNull && TextSupport.HasVisibleChar(n.Data))
                    {
                        reference = n.ParentElement;
                    }

                    continue;
                }

                if (!n.IsElement || n.NamespaceURI != TextSupport.Html || n.LocalName is "script" or "style" or "template" or "button" or "select" or "textarea" or "input" or "object" or "video" or "audio" or "canvas")
                {
                    continue;
                }

                ComputedStyle s = context.Styles.GetStyle(n);
                if (!s.Display.IsRendered || s.Display.Outside != DisplayOutside.Inline)
                {
                    continue;
                }

                bool nowInLink = inLink || (n.LocalName == "a" && n.HasAttribute("href"));
                for (Node c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
                {
                    stack.Push((c, nowInLink));
                }
            }

            return new BlockText { Reference = reference, OtherLength = other.Length, LinkLength = links.Length };
        }
    }

    /// <summary>Length of text with runs of white space counted as one character and the ends trimmed.</summary>
    private sealed class CollapsedLength
    {
        private int _length;
        private bool _pendingSpace;

        internal int Length => _length;

        internal void Add(ReadOnlySpan<char> text)
        {
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    _pendingSpace = _length > 0;
                    continue;
                }

                if (_pendingSpace)
                {
                    _length++;
                    _pendingSpace = false;
                }

                _length++;
            }
        }
    }

    internal static bool IsInline(ComputedStyle s) => s.Display is { Kind: DisplayKind.Box, Outside: DisplayOutside.Inline, Inside: DisplayInside.Flow };

    // The first run of text in the block that is not part of a link or of a nested block: the text around the links.
    internal static bool TryReferenceText(CheckContext context, Node block, out Node parentOfText)
    {
        parentOfText = default;
        var stack = new Stack<Node>();
        for (Node c = block.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            stack.Push(c);
        }

        int budget = 100_000;
        while (stack.Count > 0 && budget-- > 0)
        {
            Node n = stack.Pop();
            if (n.Kind == NodeKind.Text)
            {
                if (TextSupport.HasVisibleChar(n.Data))
                {
                    parentOfText = n.ParentElement;
                    return true;
                }

                continue;
            }

            if (!n.IsElement || n.NamespaceURI != TextSupport.Html || (n.LocalName == "a" && n.HasAttribute("href")) || n.LocalName is "script" or "style")
            {
                continue;
            }

            ComputedStyle s = context.Styles.GetStyle(n);
            if (!IsInline(s))
            {
                continue;
            }

            for (Node c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push(c);
            }
        }

        return false;
    }

    // Text decoration lines propagate to the inline descendants of the element that declares them (CSS Text Decoration 3 section 2).
    private static bool HasLine(CheckContext context, Node from, Node block, bool includeSelf)
    {
        for (Node e = includeSelf ? from : from.ParentElement; !e.IsNull && e != block; e = e.ParentElement)
        {
            string v = context.Styles.GetStyle(e).GetPropertyValue("text-decoration-line");
            if (v.Contains("underline", StringComparison.OrdinalIgnoreCase) || v.Contains("line-through", StringComparison.OrdinalIgnoreCase) || v.Contains("overline", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBorder(ComputedStyle s)
    {
        foreach (string side in new[] { "top", "right", "bottom", "left" })
        {
            string style = s.GetPropertyValue("border-" + side + "-style");
            if (style.Length > 0 && !style.Equals("none", StringComparison.OrdinalIgnoreCase) && !style.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                && CssLength.TryPx(s.GetPropertyValue("border-" + side + "-width"), s.FontSizePx, out double w) && w > 0)
            {
                return true;
            }
        }

        return false;
    }
}
