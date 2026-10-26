using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

// axe heading-order. Best practice: a heading level may go up by at most one step from the previous heading, so the
// outline has no holes (WCAG 1.3.1 technique H42 and G141 describe headings as the page's structure). The first heading
// of a page is never flagged: page-has-heading-one speaks about where a page starts.
internal sealed class HeadingOrderRule : StructureRule
{
    public HeadingOrderRule()
        : base("heading-order", "Heading levels should only increase by one.", "Change the heading's level so it is at most one deeper than the heading before it.", null,
            "Whether the heading or its predecessor has the wrong level is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int previous = -1;
        foreach ((AxNode node, int level) in Headings(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (previous >= 0 && level > previous + 1)
            {
                reporter.Failed(node.DomNode, "A level " + level + " heading follows a level " + previous + " heading.",
                    [new Finding("aria-level", level.ToString(System.Globalization.CultureInfo.InvariantCulture), "Skips from level " + previous + ".")]);
            }
            else
            {
                reporter.Passed(node.DomNode);
            }

            previous = level;
        }
    }
}

// axe page-has-heading-one. Best practice: a page has a level-one heading naming what it is about.
internal sealed class PageHasHeadingOneRule : StructureRule
{
    public PageHasHeadingOneRule()
        : base("page-has-heading-one", "A page should contain a level-one heading.", "Add an h1 that says what the page is about.", null,
            "The heading's text is content, and which existing heading should be level one is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node page = PageNode(context);
        if (page.IsNull)
        {
            return;
        }

        if (Headings(context).Exists(h => h.Level == 1))
        {
            reporter.Passed(page, "The page has a level-one heading.");
        }
        else
        {
            reporter.Failed(page, "The page has no level-one heading.");
        }
    }
}

// axe p-as-heading (experimental). WCAG 1.3.1, technique F2: a short paragraph that looks like a heading (bold, italic or
// bigger than the paragraph after it) but is marked up as a paragraph. Font weight, style and size are computed style, so
// the cascade decides and no layout is needed. Two signs together are a failure; a much larger size alone is a review item;
// bold or italic alone is ordinary emphasis and passes (axe agrees on black-box probes).
internal sealed class ParagraphAsHeadingRule : StructureRule
{
    private const int MaxLength = 140;

    public ParagraphAsHeadingRule()
        : base("p-as-heading", "A paragraph styled to look like a heading should be a real heading.", "Use a heading element (h1 to h6) for the text and style it with CSS.", null,
            "The right heading level depends on the page's outline, which is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node p in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!IsHtml(p, "p") || !IsRendered(context, p) || !context.IsExposed(p))
            {
                continue;
            }

            Node next = NextParagraph(p);
            if (next.IsNull)
            {
                continue;
            }

            string text = p.TextContent ?? string.Empty;
            if (Normalize(text).Length is 0 or > MaxLength)
            {
                continue;
            }

            Emphasis mine = Measure(context, p);
            Emphasis theirs = Measure(context, next);
            if (!mine.AnyText || !theirs.AnyText)
            {
                continue;
            }

            // Bold type, a size clearly above the next paragraph's and italic each count as a sign of a heading. Two signs together fail; a much larger size alone is left to review; one bold or italic paragraph alone passes.
            bool bold = mine.AllBold;
            bool bigger = mine.MinSize >= theirs.MaxSize * 1.4;
            bool muchBigger = bigger;
            bool italic = mine.AllItalic && !theirs.AllItalic;
            if ((bold && bigger) || (bold && italic) || (italic && bigger))
            {
                string how = bold && bigger ? "bold and larger" : bold ? "bold and italic" : "italic and larger";
                reporter.Failed(p, "This paragraph looks like a heading (" + how + ") but is not one.",
                    [new Finding(null, null, "Its text is " + how + ".")]);
            }
            else if (muchBigger)
            {
                reporter.NeedsReview(p, "This paragraph is much larger than the next one and may be a heading.", [new Finding(null, null, "Its text is at least 1.4 times the size of the next paragraph's.")]);
            }
            else
            {
                reporter.Passed(p);
            }
        }
    }

    // The next sibling element, when it is a paragraph; a heading-like paragraph has to introduce something.
    private static Node NextParagraph(Node p)
    {
        for (Node s = p.NextSibling; !s.IsNull; s = s.NextSibling)
        {
            if (s.IsElement)
            {
                return IsHtml(s, "p") ? s : default;
            }

            if (s.Kind == NodeKind.Text && !IsBlank(s.Data))
            {
                return default;
            }
        }

        return default;
    }

    private readonly record struct Emphasis(bool AnyText, bool AllBold, bool AllItalic, double MinSize, double MaxSize);

    // Style of every non-blank text node in the paragraph, taken from the element that holds it. Walks the subtree with a stack.
    private static Emphasis Measure(CheckContext c, Node p)
    {
        bool any = false, bold = true, italic = true;
        double min = double.MaxValue, max = 0;
        var stack = new Stack<Node>();
        stack.Push(p);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            for (Node ch = n.FirstChild; !ch.IsNull; ch = ch.NextSibling)
            {
                if (ch.IsElement)
                {
                    stack.Push(ch);
                }
                else if (ch.Kind == NodeKind.Text && !IsBlank(ch.Data))
                {
                    ComputedStyle st = c.Styles.GetStyle(n);
                    any = true;
                    bold &= st.FontWeight >= 700;
                    italic &= st.FontStyle.Keyword is "italic" or "oblique";
                    min = Math.Min(min, st.FontSizePx);
                    max = Math.Max(max, st.FontSizePx);
                }
            }
        }

        return new Emphasis(any, any && bold, any && italic, any ? min : 0, max);
    }
}
