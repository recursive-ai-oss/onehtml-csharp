using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

// axe avoid-inline-spacing, ACT 24afc2 (letter-spacing), 9e45ec (word-spacing), 78fd32 (line-height); WCAG 1.4.12 Text Spacing.
// A person who needs wider spacing overrides it with their own style sheet, which cannot beat a style attribute that says
// !important. The text spacing the criterion asks to survive: line height 1.5, letter spacing 0.12 and word spacing 0.16 times
// the font size. An important inline declaration passes when its value is already at least that wide, when it only passes
// the parent's value on (inherit, unset), or when the cascade took the value from somewhere else.
internal sealed class AvoidInlineSpacingRule : AriaRule
{
    private readonly record struct Spacing(string Property, double Factor, string ActId);

    private static readonly Spacing[] s_properties =
    [
        new("letter-spacing", 0.12, "24afc2"),
        new("line-height", 1.5, "78fd32"),
        new("word-spacing", 0.16, "9e45ec"),
    ];

    internal static readonly string[] WrapFacts = ["text boxes of the element (to see whether its text wraps onto more than one line)"];

    public AvoidInlineSpacingRule()
        : base("avoid-inline-spacing", "Line height, letter spacing and word spacing set with !important in a style attribute must already be wide enough for WCAG's text spacing.",
            "Drop !important from the declaration in the style attribute, or make the value at least 1.5 times the font size for line height, 0.12 for letter spacing and 0.16 for word spacing.",
            "Declare the property again in the style attribute without !important. The page keeps its look, and a reader's own style sheet can then adjust the spacing.",
            null, actIds: ["24afc2", "78fd32", "9e45ec"], needs: RuleNeeds.RenderedFacts)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var paint = new PaintCalculator(context);
        int counter = 0;
        foreach (Node element in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (element.NamespaceURI != TextSupport.Html || element.GetAttribute("style") is not { Length: > 0 } attr)
            {
                continue;
            }

            CssTree tree = CssTree.ParseDeclarationList(attr);
            var declared = new Dictionary<string, List<(bool Important, string Raw)>>(StringComparer.Ordinal);
            foreach (CssNode d in tree.Root.Declarations)
            {
                foreach (Spacing sp in s_properties)
                {
                    if (d.NameEqualsIgnoreCase(sp.Property))
                    {
                        if (!declared.TryGetValue(sp.Property, out List<(bool Important, string Raw)>? list))
                        {
                            declared[sp.Property] = list = [];
                        }

                        list.Add((d.IsImportant, d.ValueText.ToString().Trim()));
                    }
                }
            }

            if (declared.Count == 0)
            {
                continue;
            }

            ComputedStyle style = context.Styles.GetStyle(element);
            if (!TextSupport.IsDrawn(context, element, style) || paint.Of(element).Offscreen)
            {
                continue;
            }

            var findings = new List<Finding>();
            var suggestions = new List<SuggestedEdit>();
            var used = new List<string>();
            bool failed = false, wrapUnknown = false, review = false;
            foreach (Spacing sp in s_properties)
            {
                if (!declared.TryGetValue(sp.Property, out List<(bool Important, string Raw)>? decls))
                {
                    continue;
                }

                // ACT 24afc2 "cascade": another rule's important declaration won, so the style attribute does not set the value.
                // (A CSS-wide keyword has no winning declaration recorded; the attribute's own last important one is the candidate then.)
                if (style.GetSource(sp.Property) is { Kind: not DeclarationKind.InlineStyle })
                {
                    continue;
                }

                // ACT 24afc2 "not important": an important declaration in the attribute beats a later normal one.
                (bool important, string raw) = (false, string.Empty);
                foreach ((bool imp, string r) in decls)
                {
                    if (imp || !important)
                    {
                        (important, raw) = (imp, r);
                    }
                }

                if (!important)
                {
                    continue;
                }

                if (raw.Equals("inherit", StringComparison.OrdinalIgnoreCase) || raw.Equals("unset", StringComparison.OrdinalIgnoreCase))
                {
                    // The inherited value is not fixed by this element, so a reader's style sheet can still set it on the parent.
                    continue;
                }

                string value = style.GetPropertyValue(sp.Property).Trim();
                double font = style.FontSizePx;
                if (!TryUsedPx(sp.Property, value, font, out double px))
                {
                    if (!value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    {
                        review = true;
                        findings.Add(new Finding("style", sp.Property + ": " + value, "The value cannot be resolved without layout; check that it is at least " + sp.Factor.ToString(CultureInfo.InvariantCulture) + " times the font size."));
                        continue;
                    }

                    px = 0;
                }

                if (px + 1e-9 >= sp.Factor * font)
                {
                    continue;
                }

                if (sp.Property == "line-height")
                {
                    // ACT 78fd32 applies only to text that wraps; only the browser's text boxes show that.
                    if (context.Facts is { } facts)
                    {
                        used.Add("text boxes");
                        if (!Wraps(facts, element))
                        {
                            continue;
                        }
                    }
                    else if (style.GetPropertyValue("white-space").StartsWith("nowrap", StringComparison.OrdinalIgnoreCase) || style.GetPropertyValue("white-space").StartsWith("pre", StringComparison.OrdinalIgnoreCase) && !style.GetPropertyValue("white-space").Contains("wrap", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    else
                    {
                        // Cannot say whether the text wraps: leave this property out of the verdict unless nothing else is wrong.
                        wrapUnknown = true;
                        continue;
                    }
                }

                failed = true;
                findings.Add(new Finding("style", sp.Property + ": " + raw + " !important", "Important in the style attribute and narrower than " + sp.Factor.ToString(CultureInfo.InvariantCulture) + " times the font size (" + font.ToString("0.##", CultureInfo.InvariantCulture) + "px)."));
                if (decls.Count == 1)
                {
                    suggestions.Add(reporter.Suggest(Edit.SetStyle(element, sp.Property, raw, important: false), "Declare " + sp.Property + " in the style attribute without !important."));
                }
            }

            if (!failed && wrapUnknown)
            {
                reporter.NeedsRender(element, "Line height is important in the style attribute and narrower than 1.5 times the font size; whether it matters depends on whether the text wraps.", WrapFacts);
            }
            else if (failed)
            {
                reporter.Failed(element, "Spacing set with !important in the style attribute stops a reader's style sheet from widening it.", findings, suggestions, [.. used]);
            }
            else if (review)
            {
                reporter.NeedsReview(element, "An important spacing value in the style attribute could not be compared with the font size.", findings, [.. used]);
            }
            else
            {
                reporter.Passed(element, factsUsed: [.. used]);
            }
        }
    }

    // The used value in px of letter-spacing, word-spacing or line-height; line-height also takes a number or a percentage.
    private static bool TryUsedPx(string property, string value, double font, out double px)
    {
        px = 0;
        if (value.Length == 0 || value.Contains("calc(", StringComparison.OrdinalIgnoreCase) || value.Contains("var(", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CssLength.TryPx(value, font, out px) && (property != "line-height" || !IsPlainNumber(value) || value.Trim() == "0"))
        {
            return true;
        }

        if (property == "line-height")
        {
            if (value.EndsWith('%') && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                px = pct / 100 * font;
                return true;
            }

            if (IsPlainNumber(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            {
                px = n * font;
                return true;
            }
        }

        return false;
    }

    private static bool IsPlainNumber(string v) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    // True when any text node in the element's subtree was laid out on more than one line.
    internal static bool Wraps(RenderedFacts facts, Node element)
    {
        int budget = 1_000_000;
        for (Node n = element.NextInDocumentOrder(element); !n.IsNull && budget-- > 0; n = n.NextInDocumentOrder(element))
        {
            if (n.Kind == NodeKind.Text && facts.TryGet(n, out NodeFacts nf) && nf.HasLayout)
            {
                double? first = null;
                foreach (TextBox box in nf.Layout.TextBoxes)
                {
                    if (box.Bounds.IsEmpty)
                    {
                        continue;
                    }

                    if (first is null)
                    {
                        first = box.Bounds.Y;
                    }
                    else if (Math.Abs(box.Bounds.Y - first.Value) > 1)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}

// axe css-orientation-lock, ACT b33eff (WCAG 1.3.4 Orientation): content must not be turned a quarter turn in one orientation
// and not the other, which would lock the page to the orientation where it reads upright. The check styles the page twice,
// in a landscape and a portrait viewport, and compares the rotation about the z axis of every element that declares a
// rotation, so it follows the cascade, media queries and shorthand forms rather than looking at one stylesheet's text.
internal sealed class CssOrientationLockRule : AriaRule
{
    public CssOrientationLockRule()
        : base("css-orientation-lock", "Content must not be rotated by a quarter turn in only one display orientation.",
            "Remove the rotation inside the orientation media query, or apply the same rotation in both orientations.", null,
            "Whether to drop the rotation or rework the layout for the other orientation is a design decision.", actIds: ["b33eff"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        if (!MentionsOrientation(context))
        {
            return;
        }

        MediaFeatures media = context.StyleContext.Media;
        DocumentStyles landscape = context.Styles.WithContext(context.StyleContext.WithMedia(media with { Width = 1280, Height = 800 }), reporter.CancellationToken);
        DocumentStyles portrait = context.Styles.WithContext(context.StyleContext.WithMedia(media with { Width = 800, Height = 1280 }), reporter.CancellationToken);
        int counter = 0;
        foreach (Node element in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (element.NamespaceURI != TextSupport.Html)
            {
                continue;
            }

            ComputedStyle l = landscape.GetStyle(element), p = portrait.GetStyle(element);
            string lt = l.GetPropertyValue("transform") + "|" + l.GetPropertyValue("rotate");
            string pt = p.GetPropertyValue("transform") + "|" + p.GetPropertyValue("rotate");
            if (lt == pt)
            {
                continue;
            }

            if (!Rotates(l) && !Rotates(p))
            {
                continue;
            }

            if (!TextSupport.IsDrawn(context, element, context.Styles.GetStyle(element)))
            {
                continue;
            }

            double al = AngleDegrees(l), ap = AngleDegrees(p);
            double diff = Math.Abs(al - ap) % 180;
            var findings = new List<Finding>
            {
                new("transform", l.GetPropertyValue("transform"), "In a landscape viewport (rotation " + al.ToString("0.##", CultureInfo.InvariantCulture) + " degrees)."),
                new("transform", p.GetPropertyValue("transform"), "In a portrait viewport (rotation " + ap.ToString("0.##", CultureInfo.InvariantCulture) + " degrees)."),
            };
            if (Math.Abs(diff - 90) <= 1)
            {
                reporter.Failed(element, "The element is turned by a quarter turn in one orientation and not in the other.", findings);
            }
            else
            {
                reporter.Passed(element, "The element's rotation changes with the orientation, but not by a quarter turn.");
            }
        }
    }

    private static bool MentionsOrientation(CheckContext context)
    {
        foreach (StyleSheetInfo info in context.Styles.Sheets)
        {
            if (info.Origin == StyleOrigin.Author && info.Sheet.Text.Contains("orientation", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Rotates(ComputedStyle s)
    {
        string t = s.GetPropertyValue("transform");
        foreach (string fn in new[] { "rotate(", "rotate3d(", "rotatez(", "matrix(", "matrix3d(" })
        {
            if (t.Contains(fn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string r = s.GetPropertyValue("rotate").Trim();
        return r.Length > 0 && !r.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    // The rotation about the z axis in degrees, summed over the transform list. The individual rotate property turns first.
    internal static double AngleDegrees(ComputedStyle s)
    {
        double total = 0;
        string individual = s.GetPropertyValue("rotate").Trim();
        if (individual.Length > 0 && !individual.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            List<string> words = CssText.SplitTopLevel(individual, ' ');
            // "<angle>", "z <angle>", or "<x> <y> <z> <angle>" about the z axis only.
            if (words.Count == 1 || (words.Count == 2 && words[0].Equals("z", StringComparison.OrdinalIgnoreCase)))
            {
                total += ParseAngle(words[^1]);
            }
            else if (words.Count == 4 && IsZero(words[0]) && IsZero(words[1]))
            {
                total += Math.Sign(Number(words[2])) * ParseAngle(words[3]);
            }
        }

        string t = s.GetPropertyValue("transform");
        int i = 0;
        while (i < t.Length)
        {
            int open = t.IndexOf('(', i);
            if (open < 0)
            {
                break;
            }

            int depth = 1, j = open + 1;
            while (j < t.Length && depth > 0)
            {
                depth += t[j] == '(' ? 1 : t[j] == ')' ? -1 : 0;
                j++;
            }

            string name = t[i..open].Trim().ToLowerInvariantAscii();
            List<string> args = CssText.SplitTopLevel(t[(open + 1)..Math.Max(open + 1, j - 1)], ',');
            switch (name)
            {
                case "rotate" or "rotatez" when args.Count == 1:
                    total += ParseAngle(args[0]);
                    break;
                case "rotate3d" when args.Count == 4 && IsZero(args[0]) && IsZero(args[1]):
                    total += Math.Sign(Number(args[2])) * ParseAngle(args[3]);
                    break;
                case "matrix" when args.Count == 6:
                    total += Math.Atan2(Number(args[1]), Number(args[0])) * 180 / Math.PI;
                    break;
                case "matrix3d" when args.Count == 16:
                    total += Math.Atan2(Number(args[1]), Number(args[0])) * 180 / Math.PI;
                    break;
            }

            i = j;
        }

        return total;
    }

    private static double Number(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static bool IsZero(string s) => Number(s) == 0;

    // CSS Values 4 angle units: deg, grad (400 per turn), rad, turn. A bare zero is allowed.
    private static double ParseAngle(string s)
    {
        string t = s.Trim().ToLowerInvariantAscii();
        foreach ((string unit, double factor) in new[] { ("deg", 1.0), ("grad", 0.9), ("rad", 180 / Math.PI), ("turn", 360.0) })
        {
            if (t.EndsWith(unit, StringComparison.Ordinal))
            {
                return Number(t[..^unit.Length]) * factor;
            }
        }

        return Number(t);
    }
}

// axe target-size (WCAG 2.2 2.5.8 Target Size (Minimum)): a pointer target is at least 24 by 24 CSS pixels, or a circle of
// 24 pixels centred on it touches no other target (nor the circle of another undersized target). Sentences of text, controls
// the browser sizes and links inside a sentence are exempt. Sizes come from the browser's layout, so without facts every
// target is reported as needing them. The exceptions a person has to judge (an equivalent control elsewhere, a size that is
// essential) are not evaluated.
internal sealed class TargetSizeRule : AriaRule
{
    private const double Minimum = 24;

    internal static readonly string[] MissingFacts = ["border box of the target", "border boxes of the targets near it"];

    private static readonly string[] s_authorSized =
    [
        "width", "height", "min-width", "min-height", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "font-size", "line-height", "appearance", "zoom",
    ];

    public TargetSizeRule()
        : base("target-size", "A pointer target should be at least 24 by 24 CSS pixels, or have enough space around it.",
            "Make the target at least 24 by 24 CSS pixels with padding or a minimum size, or move it at least 24 pixels from the other targets.", null,
            "Enlarging a target or spacing it out changes the layout, which is the author's decision.", needs: RuleNeeds.RenderedFacts)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var targets = new List<AxNode>();
        int counter = 0;
        foreach (AxNode n in context.Tree.Nodes)
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (n.Kind == AxNodeKind.Element && n.Focus.IsFocusable && !n.States.Disabled && context.IsExposed(n.DomNode) && n.DomNode.NamespaceURI == TextSupport.Html
                && n.AriaRole is not { Name: "none" or "presentation" } && IsDrawnNode(context, n.DomNode))
            {
                targets.Add(n);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        if (context.Facts is not { } facts)
        {
            foreach (AxNode n in targets)
            {
                reporter.NeedsRender(n.DomNode, "The size of a pointer target is known only after layout.", MissingFacts);
            }

            return;
        }

        var boxes = new List<(AxNode Node, LayoutRect Box)>();
        foreach (AxNode n in targets)
        {
            if (facts.TryGet(n.DomNode, out NodeFacts nf) && nf.HasLayout && !nf.Layout.BorderBox.IsEmpty)
            {
                boxes.Add((n, nf.Layout.BorderBox));
            }
        }

        var grid = new TargetGrid(boxes);
        string[] used = ["border boxes of the targets"];
        for (int i = 0; i < boxes.Count; i++)
        {
            if ((i & 255) == 255)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            (AxNode node, LayoutRect box) = boxes[i];
            if (box.Width >= Minimum - 1e-6 && box.Height >= Minimum - 1e-6)
            {
                reporter.Passed(node.DomNode, "The target is " + Dim(box) + " CSS pixels.", used);
                continue;
            }

            if (IsInSentence(context, node.DomNode))
            {
                reporter.Passed(node.DomNode, "The target is a link inside a sentence, which the criterion exempts.", used);
                continue;
            }

            if (IsUserAgentControl(context, node.DomNode))
            {
                reporter.Passed(node.DomNode, "The target is a control whose size the browser sets and the page leaves alone, which the criterion exempts.", used);
                continue;
            }

            double cx = box.X + (box.Width / 2), cy = box.Y + (box.Height / 2);
            int blocker = grid.FirstNear(i, cx, cy, Minimum / 2);
            if (blocker < 0)
            {
                reporter.Passed(node.DomNode, "The target is " + Dim(box) + " CSS pixels, and a 24-pixel circle around it touches no other target.", used);
            }
            else
            {
                LayoutRect other = boxes[blocker].Box;
                reporter.Failed(node.DomNode, "The target is " + Dim(box) + " CSS pixels, under 24 by 24, and another target is too close.",
                    [new Finding(null, Dim(box), "Width and height of the target."), new Finding(null, Dim(other), "Size of the target that is too close. Exceptions that need a person (an equivalent control, an essential size) are not evaluated.")], null, used);
            }
        }
    }

    private static string Dim(LayoutRect r) => r.Width.ToString("0.##", CultureInfo.InvariantCulture) + " by " + r.Height.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool IsDrawnNode(CheckContext context, Node element) => context.Styles.GetStyle(element).Visibility == VisibilityValue.Visible;

    // WCAG 2.5.8 "Inline": the target is inside a sentence or its size is otherwise constrained by the line of text around it.
    private static bool IsInSentence(CheckContext context, Node element)
    {
        ComputedStyle s = context.Styles.GetStyle(element);
        if (!LinkInTextBlockRule.IsInline(s))
        {
            return false;
        }

        Node block = element.ParentElement;
        while (!block.IsNull && LinkInTextBlockRule.IsInline(context.Styles.GetStyle(block)))
        {
            block = block.ParentElement;
        }

        return !block.IsNull && LinkInTextBlockRule.TryReferenceText(context, block, out _);
    }

    // WCAG 2.5.8 "User agent control": a control whose size the page leaves to the browser.
    private static bool IsUserAgentControl(CheckContext context, Node element)
    {
        if (element.NamespaceURI != TextSupport.Html || element.LocalName is not ("input" or "select" or "textarea" or "button"))
        {
            return false;
        }

        ComputedStyle s = context.Styles.GetStyle(element);
        foreach (string property in s_authorSized)
        {
            if (s.GetSource(property) is { Origin: not StyleOrigin.UserAgent })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A uniform grid over the targets' boxes so that the spacing test looks only at neighbours.</summary>
    private sealed class TargetGrid
    {
        private const double Cell = 64;
        private const int MaxCellsPerBox = 256;
        private readonly List<(AxNode Node, LayoutRect Box)> _boxes;
        private readonly Dictionary<(int, int), List<int>> _cells = [];
        private readonly List<int> _large = [];

        internal TargetGrid(List<(AxNode Node, LayoutRect Box)> boxes)
        {
            _boxes = boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                LayoutRect b = boxes[i].Box;
                (int x0, int x1) = Span(b.X, b.Right);
                (int y0, int y1) = Span(b.Y, b.Bottom);
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > MaxCellsPerBox)
                {
                    _large.Add(i);
                    continue;
                }

                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!_cells.TryGetValue((x, y), out List<int>? list))
                        {
                            _cells[(x, y)] = list = [];
                        }

                        list.Add(i);
                    }
                }
            }
        }

        private static (int, int) Span(double a, double b) => ((int)Math.Floor(Math.Clamp(a, -1e9, 1e9) / Cell), (int)Math.Floor(Math.Clamp(b, -1e9, 1e9) / Cell));

        /// <summary>The index of the first other target whose box touches the circle, or whose own circle (if undersized) overlaps it; -1 for none.</summary>
        internal int FirstNear(int self, double cx, double cy, double radius)
        {
            (int x0, int x1) = Span(cx - (2 * radius), cx + (2 * radius));
            (int y0, int y1) = Span(cy - (2 * radius), cy + (2 * radius));
            int best = -1;
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (_cells.TryGetValue((x, y), out List<int>? list))
                    {
                        foreach (int j in list)
                        {
                            best = Closer(best, Test(self, j, cx, cy, radius));
                        }
                    }
                }
            }

            foreach (int j in _large)
            {
                best = Closer(best, Test(self, j, cx, cy, radius));
            }

            return best;
        }

        private static int Closer(int a, int b) => a < 0 ? b : b < 0 ? a : Math.Min(a, b);

        private int Test(int self, int j, double cx, double cy, double r)
        {
            if (j == self)
            {
                return -1;
            }

            LayoutRect o = _boxes[j].Box;
            // Circle against rectangle: the nearest point of the rectangle to the centre.
            double nx = Math.Clamp(cx, o.X, o.Right), ny = Math.Clamp(cy, o.Y, o.Bottom);
            if (((nx - cx) * (nx - cx)) + ((ny - cy) * (ny - cy)) < r * r)
            {
                return j;
            }

            // Two undersized targets whose circles overlap: their centres are closer than a diameter.
            if (o.Width < Minimum - 1e-6 || o.Height < Minimum - 1e-6)
            {
                double ox = o.X + (o.Width / 2), oy = o.Y + (o.Height / 2);
                if (((ox - cx) * (ox - cx)) + ((oy - cy) * (oy - cy)) < 4 * r * r)
                {
                    return j;
                }
            }

            return -1;
        }
    }
}

/// <summary>The contrast and visual-presentation rules.</summary>
internal static class VisualRuleSet
{
    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new ColorContrastRule(enhanced: false);
        yield return new ColorContrastRule(enhanced: true);
        yield return new LinkInTextBlockRule();
        yield return new AvoidInlineSpacingRule();
        yield return new CssOrientationLockRule();
        yield return new TargetSizeRule();
    }
}
