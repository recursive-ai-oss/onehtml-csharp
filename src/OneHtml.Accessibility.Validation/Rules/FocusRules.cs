using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

internal static class FocusRuleSet
{
    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new NestedInteractiveRule();
        yield return new TabIndexRule();
        yield return new FocusOrderSemanticsRule();
        yield return new ScrollableRegionFocusableRule();
        yield return new FrameFocusableContentRule();
        yield return new AccessKeysRule();
    }

    /// <summary>
    /// True for elements a browser puts in sequential focus navigation without a tabindex (HTML 6.6.3 "focusable area"):
    /// links with href, form controls that are not hidden inputs, summary of a details, iframes, media with controls,
    /// editing hosts.
    /// </summary>
    internal static bool IsNativelyFocusable(Node e)
    {
        if (e.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        switch (e.LocalName)
        {
            case "a":
            case "area":
                return e.HasAttribute("href");
            case "button":
            case "select":
            case "textarea":
                return true;
            case "input":
                return !Ascii.EqualsIgnoreCase(e.GetAttribute("type") ?? string.Empty, "hidden");
            case "iframe":
            case "embed":
            case "object":
                return true;
            case "summary":
                return true;
            case "audio":
            case "video":
                return e.HasAttribute("controls");
            default:
                return e.GetAttribute("contenteditable") is { } v && !Ascii.EqualsIgnoreCase(v, "false");
        }
    }

    /// <summary>The integer value of a tabindex attribute by the HTML rules for parsing integers, or null when absent or invalid.</summary>
    internal static long? TabIndex(Node e)
    {
        if (e.GetAttribute("tabindex") is not { } v)
        {
            return null;
        }

        // HTML 2.3.4.3 rules for parsing integers: leading whitespace, optional sign, digits; the rest is ignored.
        int i = 0;
        while (i < v.Length && v[i] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            i++;
        }

        bool negative = false;
        if (i < v.Length && v[i] is '-' or '+')
        {
            negative = v[i] == '-';
            i++;
        }

        if (i >= v.Length || v[i] is < '0' or > '9')
        {
            return null;
        }

        long n = 0;
        while (i < v.Length && v[i] is >= '0' and <= '9')
        {
            n = n < 100_000_000_000 ? (n * 10) + (v[i] - '0') : n;
            i++;
        }

        return negative ? -n : n;
    }
}

// ACT 307n5z, axe nested-interactive, WCAG 4.1.2: an element whose role makes its children presentational (button, tab,
// img, ...) must not contain anything reachable with the Tab key, because assistive technology flattens the children away.
internal sealed class NestedInteractiveRule : StructureRule
{
    public NestedInteractiveRule()
        : base("nested-interactive", "An element whose role makes its children presentational (a button, a tab, an image) must not contain anything reachable with the Tab key.",
            "Move the focusable element out, or change the outer role to one that exposes its children.", null,
            "Removing the focusable child, moving it or changing the outer role each change the page; the author must choose.", actIds: ["307n5z"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        bool[]? tabbable = null;
        foreach (Node el in Elements(context))
        {
            if (!IsHtmlOrSvg(el) || context.Tree.Roles[el].Role is not { ChildrenPresentational: true } role)
            {
                continue;
            }

            tabbable ??= SubtreeHas(context, n => context.Find(n) is { } ax && ax.Focus.IsTabbable, includeSelf: false);
            if (tabbable[el.Index])
            {
                reporter.Failed(el, "The role " + role.Name + " makes its children presentational, yet it contains something reachable with the Tab key.");
            }
            else
            {
                reporter.Passed(el);
            }
        }
    }
}

// axe tabindex. Best practice: a positive tabindex moves an element ahead of the whole page's natural focus order, which
// is hard to keep right (HTML 6.6.3: positive values sort first). The suggested edit sets the value to 0, and is offered
// only when it provably leaves the focus order unchanged: when the page's current sequential order already follows the
// document order. Then every positive value can become 0 and every element keeps its place. The edits are one set; apply
// them together. Any other order means the author chose it, so there is no suggestion.
internal sealed class TabIndexRule : StructureRule
{
    public TabIndexRule()
        : base("tabindex", "A tabindex above 0 should not be used.", "Reorder the elements in the document and use tabindex 0 or -1.",
            "Set tabindex to 0, when the page's focus order already follows the document order.",
            null)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        bool? orderIsDocumentOrder = null;
        foreach (Node e in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (e.Namespace != ElementNamespace.Html || FocusRuleSet.TabIndex(e) is not { } value || value <= 0 || !IsRendered(context, e))
            {
                continue;
            }

            orderIsDocumentOrder ??= FocusFollowsDocumentOrder(context);
            IReadOnlyList<SuggestedEdit>? fix = orderIsDocumentOrder == true
                ? [reporter.Suggest(Editing.Edit.SetAttribute(e, "tabindex", "0"), "Set tabindex to 0; the focus order does not change.")]
                : null;
            reporter.Failed(e, "The tabindex is " + value + ", above 0.", [new Finding("tabindex", value.ToString(System.Globalization.CultureInfo.InvariantCulture), "Positive values reorder the page's focus order.")], fix);
        }

        // An element with tabindex 0 or less is fine; the rule reports no pass for it.
    }

    private static bool FocusFollowsDocumentOrder(CheckContext c)
    {
        var position = new Dictionary<int, int>();
        int i = 0;
        foreach (Node e in Elements(c))
        {
            position[e.Index] = i++;
        }

        int last = -1;
        foreach (AxNode n in c.Tree.FocusOrder)
        {
            if (!position.TryGetValue(n.DomNode.Index, out int p) || p <= last)
            {
                return false;
            }

            last = p;
        }

        return true;
    }
}

// axe focus-order-semantics (experimental). Best practice: an element a script made focusable with tabindex, in the tab
// order, should have a role that says what it does (a widget, a landmark or a dialog), or screen reader users land on
// something with no meaning. A scrollable container made focusable so it can be scrolled is the usual exception: its
// computed overflow is auto or scroll, which stands in for "valid scrollable semantics".
internal sealed class FocusOrderSemanticsRule : StructureRule
{
    public FocusOrderSemanticsRule()
        : base("focus-order-semantics", "An element made focusable with tabindex should have a role that says what it is.", "Give the element a widget, landmark or dialog role, or take it out of the tab order.", null,
            "Which role fits the element depends on what it does, which only the author knows.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (AxNode ax in context.Tree.FocusOrder)
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            Node e = ax.DomNode;
            if (ax.Kind != AxNodeKind.Element || e.Namespace != ElementNamespace.Html || FocusRuleSet.TabIndex(e) is null || FocusRuleSet.IsNativelyFocusable(e) || !context.IsExposed(e))
            {
                continue;
            }

            AriaRole? role = ax.AriaRole;
            bool meaningful = role is not null && (IsWidgetRole(role) || IsLandmark(ax) || role.Name is "dialog" or "alertdialog" or "application" or "tabpanel");
            ComputedStyle st = context.Styles.GetStyle(e);
            bool scroller = st.OverflowX is OverflowValue.Auto or OverflowValue.Scroll || st.OverflowY is OverflowValue.Auto or OverflowValue.Scroll;
            if (meaningful || scroller)
            {
                reporter.Passed(e);
            }
            else
            {
                reporter.Failed(e, "The element is in the tab order but its role (" + (role?.Name ?? "generic") + ") says nothing about what it does.", [new Finding("role", role?.Name, "Not a widget, landmark or dialog role.")]);
            }
        }
    }
}

// ACT 0ssw9k, axe scrollable-region-focusable, WCAG 2.1.1 (technique G202): an element with something to scroll must be
// reachable by Tab, or have a focusable descendant, so a keyboard user can scroll it. Whether an element has something
// to scroll is layout: without facts, an element that is already reachable passes (it does whether or not it scrolls),
// and one that is not is needs-render. With facts the scroll and client sizes decide, and an element that does not
// scroll is not applicable.
internal sealed class ScrollableRegionFocusableRule : StructureRule
{
    public static readonly string[] Facts = ["clientRects", "scrollRects"];

    public ScrollableRegionFocusableRule()
        : base("scrollable-region-focusable", "A scrollable region must be reachable with the keyboard.", "Give the region tabindex 0, or put a focusable element inside it.", null,
            "Whether to make the region focusable or to supply another way to scroll it is the author's decision.",
            actIds: ["0ssw9k"], needs: RuleNeeds.RenderedFacts)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        bool[] content = SubtreeHas(context, e => HasContent(context, e), includeSelf: true);
        bool[]? tabbableBelow = null;
        foreach (Node e in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (e.Namespace != ElementNamespace.Html || e.LocalName is "html" or "body" || !IsRendered(context, e) || !context.IsExposed(e) || !content[e.Index])
            {
                continue;
            }

            ComputedStyle st = context.Styles.GetStyle(e);
            bool x = st.OverflowX is OverflowValue.Auto or OverflowValue.Scroll;
            bool y = st.OverflowY is OverflowValue.Auto or OverflowValue.Scroll;
            if (!x && !y)
            {
                continue;
            }

            tabbableBelow ??= SubtreeHas(context, n => context.Find(n) is { } ax && ax.Focus.IsTabbable, includeSelf: true);
            bool reachable = tabbableBelow[e.Index];
            string[]? used = null;
            if (context.Facts is { } facts)
            {
                if (!facts.TryGet(e, out NodeFacts nf) || !nf.HasLayout || nf.Layout.ScrollRect is not { } scroll || nf.Layout.ClientRect is not { } client)
                {
                    if (!reachable)
                    {
                        reporter.NeedsRender(e, "The facts have no scroll and client size for this element.", Facts);
                    }

                    continue;
                }

                bool scrolls = (x && scroll.Width - client.Width > 1) || (y && scroll.Height - client.Height > 1);
                if (!scrolls)
                {
                    continue;
                }

                used = Facts;
            }

            if (reachable)
            {
                reporter.Passed(e, used is null ? "Reachable with the keyboard, whether or not it scrolls." : null, used);
            }
            else if (used is null)
            {
                reporter.NeedsRender(e, "Whether this element has content to scroll depends on layout.", Facts);
            }
            else
            {
                reporter.Failed(e, "The element scrolls but neither it nor anything in it can be reached with the keyboard.", factsUsed: used);
            }
        }
    }

    private static bool HasContent(CheckContext c, Node e)
    {
        if (!IsRendered(c, e))
        {
            return false;
        }

        if (e.LocalName is "img" or "svg" or "video" or "audio" or "canvas" or "iframe" or "object" or "embed" or "input" or "button" or "select" or "textarea")
        {
            return true;
        }

        for (Node ch = e.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind == NodeKind.Text && !IsBlank(ch.Data))
            {
                return true;
            }
        }

        return false;
    }
}

// ACT akn7bn, axe frame-focusable-content, WCAG 2.1.1: an iframe taken out of the tab order with a negative tabindex
// must not hold anything focusable, since the keyboard can no longer reach it. Only inline content (srcdoc) is available;
// a frame loaded from a URL is a review item. A frame that is not displayed holds nothing a user could reach, and so
// does one the facts show as no larger than one pixel.
internal sealed class FrameFocusableContentRule : StructureRule
{
    public FrameFocusableContentRule()
        : base("frame-focusable-content", "A frame removed from the tab order must not contain focusable content.", "Remove the negative tabindex from the frame, or remove the focusable content.", null,
            "Whether the frame or its content should change is the author's decision.", actIds: ["akn7bn"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node f in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!(IsHtml(f, "iframe") || IsHtml(f, "frame")) || FocusRuleSet.TabIndex(f) is not < 0)
            {
                continue;
            }

            if (!IsRendered(context, f))
            {
                reporter.Passed(f, "The frame is not displayed.");
                continue;
            }

            if (context.Facts is { } facts && facts.TryGet(f, out NodeFacts nf) && nf.HasLayout && nf.Layout.BorderBox is { Width: <= 1, Height: <= 1 })
            {
                reporter.Passed(f, "The frame is no larger than one pixel.", ["bounds"]);
                continue;
            }

            string? srcdoc = IsHtml(f, "iframe") ? f.GetAttribute("srcdoc") : null;
            if (srcdoc is null)
            {
                reporter.NeedsReview(f, "The frame's content is loaded from a URL, which the checker does not have.");
                continue;
            }

            int focusable;
            try
            {
                Document inner = Document.Parse(srcdoc);
                AccessibilityTree tree = AccessibilityTree.Build(inner, DocumentStyles.Create(inner, null, null, reporter.CancellationToken), null, null, reporter.CancellationToken);
                focusable = tree.FocusOrder.Count;
            }
            catch (HtmlLimitExceededException)
            {
                reporter.NeedsReview(f, "The frame's inline content is too large to check.");
                continue;
            }

            if (focusable > 0)
            {
                reporter.Failed(f, "The frame has a negative tabindex but contains " + focusable + " element" + (focusable == 1 ? string.Empty : "s") + " reachable with the Tab key.");
            }
            else
            {
                reporter.Passed(f);
            }
        }
    }
}

// axe accesskeys. Best practice: an access key must be one element's only (HTML 6.8, the accesskey attribute): two
// elements claiming the same key leave the browser to pick one. A key is each space-separated token, compared without
// regard to ASCII case, as browsers' shortcuts are.
internal sealed class AccessKeysRule : StructureRule
{
    public AccessKeysRule()
        : base("accesskeys", "Each accesskey value must be unique.", "Give every element that has an accesskey its own key.", null,
            "Which element keeps the key, and what the others use, is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        var withKeys = new List<(Node Element, List<string> Keys)>();
        foreach (Node e in Elements(context))
        {
            if (!e.IsElement || e.GetAttribute("accesskey") is not { } v || !IsRendered(context, e))
            {
                continue;
            }

            var keys = new List<string>();
            foreach (string t in Tokens(v))
            {
                string k = Normalize(t);
                if (!keys.Contains(k))
                {
                    keys.Add(k);
                    owners.TryGetValue(k, out int n);
                    owners[k] = n + 1;
                }
            }

            if (keys.Count > 0)
            {
                withKeys.Add((e, keys));
            }
        }

        foreach ((Node e, List<string> keys) in withKeys)
        {
            string? shared = keys.Find(k => owners[k] > 1);
            if (shared is null)
            {
                reporter.Passed(e);
            }
            else
            {
                reporter.Failed(e, "Another element has the access key " + shared + ".", [new Finding("accesskey", shared, owners[shared] + " elements use this key.")]);
            }
        }
    }
}
