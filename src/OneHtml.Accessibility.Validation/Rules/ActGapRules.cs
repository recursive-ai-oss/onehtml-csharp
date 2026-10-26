using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// ACT rules the other areas left open. Most of them ask whether text or a picture is "descriptive", "decorative" or
/// "an image of text", which only a person can judge; the library finds exactly the elements the ACT rule applies to
/// and reports needs-review for each. The exception is the visible focus indicator, which the cascade can decide.
/// </summary>
internal static class ActGapRuleSet
{
    private static bool IsRole(AxNode n, string name) => n.AriaRole is { } r && r.Name == name;

    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new ImageOfTextRule();
        yield return new DocumentTitleDescriptiveRule();
        yield return new FocusVisibleIndicatorRule();
        yield return new ReviewOnlyRule("link-descriptive", "A link's name should describe where it goes.",
            "Reword the link so that its own text says what it leads to.", "Check that the link's name alone describes its purpose.",
            (c, n) => IsRole(n, "link"), ["aizyf1"]);
        yield return new HeadingDescriptiveRule();
        yield return new LabelDescriptiveRule();
        yield return new DecorativeImageRule();
        yield return new IdenticalLinksSameContextRule();
    }
}

// ACT 0va7u6 / WCAG 1.4.5 and 1.4.9: a rendered image should not contain text unless its look is essential or the text is
// incidental. What the pixels show is not in the document, so every rendered image is a review item. The rendered images are
// img with a source, image buttons, SVG image elements, object elements with data, and any element with a background image.
internal sealed class ImageOfTextRule : AriaRule
{
    public ImageOfTextRule()
        : base("image-of-text", "An image should not contain text, unless the look of the text is essential.",
            "Put the text in the page as real text styled with CSS, or leave it in the image only when its exact look is essential (such as a logo).", null,
            "Whether an image holds text, and whether its look is essential, depends on what the picture shows.", axe: false, actIds: ["0va7u6"], impact: Impact.Moderate)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (Node e in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (!IsImageElement(e) && !HasBackgroundImage(context, e))
            {
                continue;
            }

            if (TextSupport.IsDrawn(context, e, context.Styles.GetStyle(e)))
            {
                reporter.NeedsReview(e, "Check that the image holds no text, or only text whose look is essential, or text that is a small part of the picture.");
            }
        }
    }

    private static bool IsImageElement(Node e)
    {
        if (e.NamespaceURI == NameSupport.Html)
        {
            return e.LocalName switch
            {
                "img" => !string.IsNullOrWhiteSpace(e.GetAttribute("src")) || !string.IsNullOrWhiteSpace(e.GetAttribute("srcset")),
                "input" => NameSupport.InputType(e) == "image" && !string.IsNullOrWhiteSpace(e.GetAttribute("src")),
                "object" => !string.IsNullOrWhiteSpace(e.GetAttribute("data")),
                _ => false,
            };
        }

        return e.NamespaceURI == NameSupport.Svg && e.LocalName == "image";
    }

    private static bool HasBackgroundImage(CheckContext context, Node e)
    {
        string v = context.Styles.GetStyle(e).GetPropertyValue("background-image");
        return v.Contains("url(", StringComparison.OrdinalIgnoreCase) || v.Contains("image-set(", StringComparison.OrdinalIgnoreCase);
    }
}

// ACT c4a8a4 / WCAG 2.4.2: the first title element of the document, when it has text, should say what the page is about.
internal sealed class DocumentTitleDescriptiveRule : AriaRule
{
    public DocumentTitleDescriptiveRule()
        : base("document-title-descriptive", "The title of the page should describe its topic or purpose.",
            "Reword the title element so that it says what the page is for.", null,
            "Whether a title describes the page is a judgement about content.", axe: false, actIds: ["c4a8a4"], impact: Impact.Moderate)
    {
    }

    public override RuleScope Scope => RuleScope.Page;

    public override IReadOnlyCollection<string>? RelevantElements => ["html", "title"];

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node html = default;
        foreach (Node e in Elements(context))
        {
            if (html.IsNull && NameSupport.Is(e, "html"))
            {
                html = e;
            }

            if (!html.IsNull && NameSupport.Is(e, "title"))
            {
                // The rule applies to the first title that is a descendant of html, and only when a child text node has more than white space.
                bool text = false;
                foreach (Node child in e.ChildNodes)
                {
                    if (child.Kind == NodeKind.Text && !NameSupport.IsEmptyText(child.TextContent ?? string.Empty))
                    {
                        text = true;
                    }
                }

                if (text)
                {
                    reporter.NeedsReview(e, "Check that the title describes the topic or purpose of the page.", [new Finding(null, e.TextContent, "The title text.")]);
                }

                return;
            }
        }
    }
}

// ACT oj04fd / WCAG 2.4.7: every element in sequential focus navigation shows when it has focus. The check puts the focus on each
// element in turn (keyboard focus, so :focus-visible matches as in a browser) and compares the styles of every element
// with the unfocused styles; a change to any property that paints is a visible indicator. Nothing else about focus is judged:
// whether the change is easy to see is for 2.4.11 and 2.4.12. A script that runs on focus can add an indicator the
// cascade does not see, so an element with a focus handler attribute is needs-review instead of failed.
internal sealed class FocusVisibleIndicatorRule : AriaRule
{
    // Elements whose focus state is compared for every other element of the page; past it only the element itself is compared.
    private const long WorkBudget = 20_000_000;

    private const int MaxFocusables = 2000;

    private static readonly string[] s_paintProperties =
    [
        "outline-style", "outline-width", "outline-color", "outline-offset", "box-shadow",
        "border-top-style", "border-top-width", "border-top-color", "border-right-style", "border-right-width", "border-right-color",
        "border-bottom-style", "border-bottom-width", "border-bottom-color", "border-left-style", "border-left-width", "border-left-color",
        "background-color", "background-image", "background-position", "background-size", "color", "opacity", "filter", "transform",
        "text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness", "text-shadow",
        "fill", "stroke", "stroke-width", "visibility", "display", "content", "font-weight", "font-style", "font-size",
        "clip-path", "mask-image", "width", "height", "margin-top", "margin-left", "top", "left",
    ];

    private static readonly string[] s_handlers = ["onfocus", "onfocusin", "onblur", "onfocusout"];

    public FocusVisibleIndicatorRule()
        : base("focus-visible-indicator", "An element that can be reached with the keyboard must show that it has focus.",
            "Do not remove the browser's focus outline without replacing it; give :focus-visible a visible outline, background or border change.", null,
            "How the focus should look is a design decision.", axe: false, actIds: ["oj04fd"], impact: Impact.Serious)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        IReadOnlyList<AxNode> order = context.Tree.FocusOrder;
        if (order.Count < 2)
        {
            // ACT oj04fd: a page with a single focusable element satisfies 2.4.7 by itself.
            return;
        }

        StyleContext plain = context.StyleContext.WithoutInteraction();
        DocumentStyles baseline = context.Styles.WithContext(plain, reporter.CancellationToken);
        var elements = new List<Node>(Elements(context));
        long budget = WorkBudget;
        int done = 0;
        foreach (AxNode ax in order)
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            Node e = ax.DomNode;
            if (done++ >= MaxFocusables)
            {
                reporter.NeedsReview(e, "The page has more focusable elements than the check follows; check that this one shows focus.");
                continue;
            }

            DocumentStyles focused = context.Styles.WithContext(plain.WithFocus(e, FocusSource.Keyboard), reporter.CancellationToken);
            bool changed = Differs(baseline.GetStyle(e), focused.GetStyle(e));
            if (!changed)
            {
                if (budget > 0)
                {
                    budget -= elements.Count;
                    foreach (Node other in elements)
                    {
                        if (Differs(baseline.GetStyle(other), focused.GetStyle(other)))
                        {
                            changed = true;
                            break;
                        }
                    }
                }
            }

            if (changed)
            {
                reporter.Passed(e, "The element's painted style changes when it has keyboard focus.");
            }
            else if (HasFocusHandler(e))
            {
                reporter.NeedsReview(e, "No style changes on focus, but the element has a focus handler that may show an indicator by script; check it in a browser.");
            }
            else
            {
                reporter.Failed(e, "Nothing about the page's painted style changes when this element has keyboard focus.");
            }
        }
    }

    private static bool HasFocusHandler(Node e)
    {
        foreach (string a in s_handlers)
        {
            if (e.HasAttribute(a))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Differs(ComputedStyle a, ComputedStyle b)
    {
        if (ReferenceEquals(a, b))
        {
            return false;
        }

        foreach (string p in s_paintProperties)
        {
            if (!string.Equals(a.GetPropertyValue(p), b.GetPropertyValue(p), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

// ACT cc0f0a / WCAG 2.4.6: the visible label of a text field, check box, radio button, slider and the like says what the control is for.
// The labels are the label elements that label the control and the elements its aria-labelledby points at. A label is
// reported once, even when it names several controls.
internal sealed class LabelDescriptiveRule : AriaRule
{
    private static readonly HashSet<string> s_roles = new(StringComparer.Ordinal)
    {
        "checkbox", "combobox", "listbox", "menuitemcheckbox", "menuitemradio", "radio", "searchbox", "slider", "spinbutton", "switch", "textbox",
    };

    public LabelDescriptiveRule()
        : base("label-descriptive", "The visible label of a form field should describe its purpose.",
            "Reword the label so that it says what to enter or choose.", null,
            "Whether a label describes its field is a judgement about content.", axe: false, actIds: ["cc0f0a"], impact: Impact.Moderate)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Dictionary<int, List<Node>>? byControl = null;
        var seen = new HashSet<int>();
        int counter = 0;
        foreach (AxNode n in context.Tree.Nodes)
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (n.Kind != AxNodeKind.Element || n.AriaRole is not { } role || !s_roles.Contains(role.Name) || !TextSupport.IsDrawn(context, n.DomNode, context.Styles.GetStyle(n.DomNode)))
            {
                continue;
            }

            var labels = new List<Node>();
            if (FormFields.IsLabelable(n.DomNode))
            {
                byControl ??= FormFields.LabelsByControl(context.Document, reporter.CancellationToken);
                if (byControl.TryGetValue(n.DomNode.Index, out List<Node>? l))
                {
                    labels.AddRange(l);
                }
            }

            if (n.GetRelation(AxRelationKind.LabelledBy) is { } rel)
            {
                labels.AddRange(rel.Elements);
            }

            foreach (Node label in labels)
            {
                // The label must be visible (not display: none or visibility: hidden); aria-hidden does not hide it from sighted people.
                if (seen.Add(label.Index) && context.Find(label) is { } lax && !lax.Hidden.IsNotRendered && (lax.Hidden.Reasons & HiddenReasons.VisibilityHidden) == 0 && !NameSupport.IsEmptyText(label.TextContent ?? string.Empty))
                {
                    reporter.NeedsReview(label, "Check that the label, with its visual context, describes the purpose of the field.", [new Finding(null, label.TextContent, "The label text.")]);
                }
            }
        }
    }
}

// ACT e88epe / WCAG 1.1.1: a picture the accessibility tree ignores (alt="", a presentational role, aria-hidden, a
// graphic or canvas with no name) is claimed to be decoration, and a person has to confirm it carries no information.
// A picture inside a control that an author-given name already covers is not applicable.
internal sealed class DecorativeImageRule : AriaRule
{
    public DecorativeImageRule()
        : base("image-decorative", "A picture that is left out of the accessibility tree must be purely decorative.",
            "Give the picture alternative text if it carries information, or keep it hidden if it is only decoration.", null,
            "Whether a picture is decoration depends on what it shows and what the page says next to it.", axe: false, actIds: ["e88epe"], impact: Impact.Moderate)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (AxNode n in context.Tree.Nodes)
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (n.Kind != AxNodeKind.Element || !Candidate(context, n) || NamedAncestor(context, n.DomNode)
                || !TextSupport.IsDrawn(context, n.DomNode, context.Styles.GetStyle(n.DomNode)))
            {
                continue;
            }

            reporter.NeedsReview(n.DomNode, "Check that the picture is purely decorative: it adds no information that the page does not give in text.");
        }
    }

    private static bool Candidate(CheckContext context, AxNode n)
    {
        Node e = n.DomNode;
        bool empty = NameSupport.IsEmptyText(n.Name.Value);
        if (NameSupport.Is(e, "img"))
        {
            return (!string.IsNullOrWhiteSpace(e.GetAttribute("src")) || !string.IsNullOrWhiteSpace(e.GetAttribute("srcset")))
                && (!context.IsExposed(e) || n.AriaRole is { Name: "none" or "presentation" });
        }

        if (NameSupport.Is(e, "canvas"))
        {
            return !context.IsExposed(e) || n.AriaRole is { Name: "none" or "presentation" } || (empty && n.RoleOrigin != RoleOrigin.Explicit);
        }

        if (e.NamespaceURI == NameSupport.Svg && e.LocalName == "svg")
        {
            return !context.IsExposed(e) || n.AriaRole is { Name: "none" or "presentation" } || (empty && n.AriaRole is { Name: "graphics-document" });
        }

        return false;
    }

    // ACT e88epe: an ancestor named from the author's aria-label or aria-labelledby already speaks for the picture inside it.
    private static bool NamedAncestor(CheckContext context, Node e)
    {
        for (Node p = e.ParentElement; !p.IsNull; p = p.ParentElement)
        {
            if (context.Find(p) is { } a && a.Name.Source is AxTextSource.AriaLabel or AxTextSource.LabelledBy && !NameSupport.IsEmptyText(a.Name.Value))
            {
                return true;
            }
        }

        return false;
    }
}

// ACT fd3a94 / WCAG 2.4.4, 2.4.9: links with the same name and the same context (the nearest block of text around them) should
// go to the same place. Context is the closest ancestor with a block-level display, which covers the sentence, paragraph, list
// item and table cell of the ACT definition; header cells of a table cell are not followed.
internal sealed class IdenticalLinksSameContextRule : AriaRule
{
    public IdenticalLinksSameContextRule()
        : base("identical-links-same-context", "Links with the same name in the same context should serve the same purpose.",
            "Give links that go to different places different names, or send the identical names in one context to the same resource.", null,
            "Naming a link is a content decision.", axe: false, actIds: ["fd3a94"], impact: Impact.Moderate)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var targets = new NameSupport.TargetResolver(context.Document);
        var groups = new Dictionary<(string, int), List<AxNode>>();
        var order = new List<(string, int)>();
        foreach (AxNode node in context.Tree.Nodes)
        {
            if (node.Kind != AxNodeKind.Element || node.AriaRole is not { Name: "link" } || !context.IsExposed(node.DomNode) || NameSupport.IsEmptyText(node.Name.Value))
            {
                continue;
            }

            (string, int) key = (NameSupport.Fold(node.Name.Value), ContextOf(context, node.DomNode));
            if (!groups.TryGetValue(key, out List<AxNode>? list))
            {
                groups[key] = list = [];
                order.Add(key);
            }

            list.Add(node);
        }

        foreach ((string, int) key in order)
        {
            List<AxNode> list = groups[key];
            if (list.Count < 2)
            {
                continue;
            }

            string? first = Target(targets, list[0].DomNode);
            bool same = first is not null;
            foreach (AxNode n in list)
            {
                same &= string.Equals(Target(targets, n.DomNode), first, StringComparison.Ordinal);
            }

            foreach (AxNode n in list)
            {
                if (same)
                {
                    reporter.Passed(n.DomNode, "Links with this name in this context go to the same resource.");
                }
                else
                {
                    reporter.NeedsReview(n.DomNode, "Links named \"" + list[0].Name.Value + "\" in the same context go to different resources; check that they serve the same purpose.", [new Finding("href", n.DomNode.GetAttribute("href"), "Where the link points.")]);
                }
            }
        }
    }

    private static int ContextOf(CheckContext context, Node link)
    {
        for (Node p = link.ParentElement; !p.IsNull; p = p.ParentElement)
        {
            string display = context.Styles.GetStyle(p).GetPropertyValue("display");
            if (display is "block" or "list-item" or "table-cell" or "flow-root" || display.StartsWith("table", StringComparison.Ordinal) || display.StartsWith("flex", StringComparison.Ordinal) || display.StartsWith("grid", StringComparison.Ordinal))
            {
                return p.Index;
            }
        }

        return -1;
    }

    private static string? Target(NameSupport.TargetResolver targets, Node link) =>
        (link.GetAttribute("href") ?? link.GetAttribute("xlink:href")) is { } href ? targets.Resolve(href) : null;
}

// ACT b49b2e / WCAG 2.4.6: a visible heading says what its section is about. A heading hidden from assistive technology with
// aria-hidden is still on screen, so it is reviewed too; a heading with no text is empty-heading's concern.
internal sealed class HeadingDescriptiveRule : AriaRule
{
    public HeadingDescriptiveRule()
        : base("heading-descriptive", "A heading should describe the topic or purpose of the section it introduces.",
            "Reword the heading so that it says what the section is about.", null,
            "Whether a heading describes its section is a judgement about content.", axe: false, actIds: ["b49b2e"], impact: Impact.Moderate)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (AxNode n in context.Tree.Nodes)
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (n.Kind == AxNodeKind.Element && n.AriaRole is { Name: "heading" } && TextSupport.IsDrawn(context, n.DomNode, context.Styles.GetStyle(n.DomNode))
                && !(NameSupport.IsEmptyText(n.Name.Value) && NameSupport.IsEmptyText(n.DomNode.TextContent ?? string.Empty)))
            {
                string text = NameSupport.IsEmptyText(n.Name.Value) ? n.DomNode.TextContent ?? string.Empty : n.Name.Value;
                reporter.NeedsReview(n.DomNode, "Check that the heading describes the topic or purpose of its section.", [new Finding(null, text, "The heading text.")]);
            }
        }
    }
}
