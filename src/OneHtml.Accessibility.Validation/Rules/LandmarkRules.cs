using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

internal static class LandmarkRuleSet
{
    private const string NoFix = "Moving a landmark, removing one or renaming one changes how the page is organised; the author has to decide which.";

    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new LandmarkTopLevelRule("landmark-banner-is-top-level", "banner", "A banner landmark must not sit inside another landmark.", "Move the banner out of the other landmark, or give the inner element a role that is not banner.");
        yield return new LandmarkTopLevelRule("landmark-complementary-is-top-level", "complementary", "A complementary landmark (an aside) should not sit inside another landmark.", "Move the aside out of the other landmark, or give it a role that is not complementary.");
        yield return new LandmarkTopLevelRule("landmark-contentinfo-is-top-level", "contentinfo", "A contentinfo landmark (a page footer) must not sit inside another landmark.", "Move the footer out of the other landmark, or give the inner element a role that is not contentinfo.");
        yield return new LandmarkTopLevelRule("landmark-main-is-top-level", "main", "A main landmark must not sit inside another landmark.", "Move the main element out of the other landmark.");
        yield return new LandmarkDuplicateRule("landmark-no-duplicate-banner", "banner", "A page should have at most one banner landmark.", "Keep one banner; give the other elements a role that is not banner, or use header inside sectioning content.");
        yield return new LandmarkDuplicateRule("landmark-no-duplicate-contentinfo", "contentinfo", "A page should have at most one contentinfo landmark.", "Keep one page footer; give the other elements a role that is not contentinfo.");
        yield return new LandmarkDuplicateRule("landmark-no-duplicate-main", "main", "A page should have at most one main landmark.", "Keep one main element; remove the role or element from the others.");
        yield return new LandmarkOneMainRule();
        yield return new LandmarkUniqueRule();
        yield return new RegionRule();
        yield return new BypassRule();
        yield return new SkipLinkRule();
    }

    internal static string NoFixReason => NoFix;
}

// axe landmark-*-is-top-level. Best practice (no WCAG criterion): landmark navigation lists a nested landmark under its
// parent, and some screen readers only list the top level.
internal sealed class LandmarkTopLevelRule : StructureRule
{
    private readonly string _role;

    public LandmarkTopLevelRule(string id, string role, string description, string help)
        : base(id, description, help, null, LandmarkRuleSet.NoFixReason)
    {
        _role = role;
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (AxNode n in Landmarks(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (n.AriaRole!.Name != _role)
            {
                continue;
            }

            AxNode? outer = null;
            for (AxNode? p = n.Parent; p is not null; p = p.Parent)
            {
                if (IsLandmark(p))
                {
                    outer = p;
                    break;
                }
            }

            // A complementary landmark may sit directly in the main landmark: the ARIA Authoring Practices place sidebars there.
            if (outer is null || (_role == "complementary" && outer.AriaRole!.Name == "main"))
            {
                reporter.Passed(n.DomNode);
            }
            else
            {
                reporter.Failed(n.DomNode, "The " + _role + " landmark is inside a " + outer.AriaRole!.Name + " landmark.",
                    [new Finding("role", _role, "Contained in a " + outer.AriaRole.Name + " landmark.")]);
            }
        }
    }
}

// axe landmark-no-duplicate-*. Best practice: one banner, one contentinfo and one main per page, as the landmark roles
// describe them (WAI-ARIA 1.2 5.3.4, 5.3.5 and 5.3.9 say a page has one of each).
internal sealed class LandmarkDuplicateRule : StructureRule
{
    private readonly string _role;

    public LandmarkDuplicateRule(string id, string role, string description, string help)
        : base(id, description, help, null, LandmarkRuleSet.NoFixReason)
    {
        _role = role;
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        List<AxNode> found = Landmarks(context).FindAll(n => n.AriaRole!.Name == _role);
        foreach (AxNode n in found)
        {
            if (found.Count == 1)
            {
                reporter.Passed(n.DomNode);
            }
            else
            {
                reporter.Failed(n.DomNode, "The page has " + found.Count + " " + _role + " landmarks; it should have one.",
                    [new Finding("role", _role, found.Count + " landmarks with this role.")]);
            }
        }
    }
}

// axe landmark-one-main. Best practice: a page needs a main landmark so landmark navigation can reach the content (WAI-ARIA 1.2 5.3.9).
internal sealed class LandmarkOneMainRule : StructureRule
{
    public LandmarkOneMainRule()
        : base("landmark-one-main", "A page should have a main landmark.", "Wrap the page's primary content in a main element.", null,
            "Which part of the page is its main content is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node page = PageNode(context);
        if (page.IsNull)
        {
            return;
        }

        if (Landmarks(context).Exists(n => n.AriaRole!.Name == "main"))
        {
            reporter.Passed(page, "The page has a main landmark.");
        }
        else
        {
            reporter.Failed(page, "The page has no main landmark.");
        }
    }
}

// axe landmark-unique. Best practice: landmarks of one role are told apart by their names, so two navs both named "Menu"
// (or both unnamed) are indistinguishable in a landmark list (WAI-ARIA 1.2 5.3.4 and Landmark Regions technique ARIA11).
internal sealed class LandmarkUniqueRule : StructureRule
{
    public LandmarkUniqueRule()
        : base("landmark-unique", "Landmarks that share a role must have different accessible names.", "Give each landmark of the same role a distinct aria-label or aria-labelledby.", null,
            "The names are content: only the author knows what to call each landmark.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        List<AxNode> all = Landmarks(context);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (AxNode n in all)
        {
            counts.TryGetValue(Key(n), out int k);
            counts[Key(n)] = k + 1;
        }

        foreach (AxNode n in all)
        {
            if (counts[Key(n)] == 1)
            {
                reporter.Passed(n.DomNode);
            }
            else
            {
                reporter.Failed(n.DomNode, "Another " + n.AriaRole!.Name + " landmark has the same accessible name.",
                    [new Finding("role", n.AriaRole.Name, Normalize(n.Name.Value).Length == 0 ? "No accessible name." : "Name: " + n.Name.Value)]);
            }
        }
    }

    private static string Key(AxNode n) => n.AriaRole!.Name + "\0" + Normalize(n.Name.Value);
}

// axe region. Best practice: content outside every landmark is skipped by landmark navigation. Exempt, as users of
// landmarks expect: skip links, dialogs and live regions (they announce themselves). Reported on the topmost element
// that holds uncovered content, or on the element that holds stray text next to landmarks.
internal sealed class RegionRule : StructureRule
{
    private static readonly HashSet<string> s_contentTags = new(StringComparer.Ordinal)
    {
        "img", "video", "audio", "canvas", "object", "embed", "input", "select", "textarea", "meter", "progress", "math",
    };

    private static readonly HashSet<string> s_inert = new(StringComparer.Ordinal)
    {
        "script", "style", "template", "noscript", "head", "title", "meta", "link", "base",
    };

    public RegionRule()
        : base("region", "All page content should sit inside a landmark.", "Put the content inside a header, nav, main, aside, footer or a named section or region.", null,
            "Choosing the landmark that fits each block of content is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node page = PageNode(context);
        Node body = default;
        foreach (Node ch in page.Children)
        {
            if (IsHtml(ch, "body"))
            {
                body = ch;
                break;
            }
        }

        if (body.IsNull)
        {
            return;
        }

        bool[] landmarkBelow = SubtreeHas(context, e => IsCovering(context, e), includeSelf: false);
        bool[] contentIn = SubtreeHas(context, e => HasOwnContent(context, e), includeSelf: true);
        int violations = 0;
        bool anyContent = contentIn[body.Index];
        var stack = new Stack<Node>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            Node e = stack.Pop();
            if (HasStrayText(context, e))
            {
                violations++;
                reporter.Failed(e, "This element holds text that is outside every landmark.");
            }

            var kids = new List<Node>();
            foreach (Node k in e.Children)
            {
                if (s_inert.Contains(k.LocalName) || !IsRendered(context, k) || !context.IsExposed(k) || IsCovering(context, k) || IsExempt(context, k))
                {
                    continue;
                }

                if (landmarkBelow[k.Index])
                {
                    kids.Add(k);
                }
                else if (contentIn[k.Index])
                {
                    violations++;
                    reporter.Failed(k, "This content is outside every landmark.");
                }
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        if (violations == 0 && anyContent)
        {
            reporter.Passed(page, "All content is inside a landmark.");
        }
    }

    private static bool IsCovering(CheckContext c, Node e)
    {
        if (c.Find(e) is not { } ax)
        {
            return false;
        }

        // An element given role=region is taken as a region even without a name, as axe-core does for this rule.
        return IsLandmark(ax) || EqualsAscii(e.GetAttribute("role")?.Trim(), "region") || IsRole(ax, "dialog") || IsRole(ax, "alertdialog") || (ax.States.Live is { } l && l != "off");
    }

    private static bool IsExempt(CheckContext c, Node e) => !SkipTarget(c, e).IsNull;

    private static bool HasStrayText(CheckContext c, Node e)
    {
        if (IsHtml(e, "html") || (!IsHtml(e, "body") && (IsCovering(c, e) || !IsRendered(c, e))))
        {
            return false;
        }

        for (Node ch = e.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind == NodeKind.Text && !IsBlank(ch.Data) && !IsExempt(c, e))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOwnContent(CheckContext c, Node e)
    {
        // axe-core does not count a button, a frame or an inline graphic (or the text inside a button) as page content for this rule.
        if (s_inert.Contains(e.LocalName) || IsHtml(e, "button") || IsExempt(c, e) || !IsRendered(c, e) || !c.IsExposed(e))
        {
            return false;
        }

        if (s_contentTags.Contains(e.LocalName) && !(IsHtml(e, "input") && Ascii.EqualsIgnoreCase(e.GetAttribute("type") ?? string.Empty, "hidden")))
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

// ACT cf77f2 (composite of 047fe0, b40fd1, 3e12e1, ye5d6e), axe bypass, WCAG 2.4.1. What can be decided from one page: a
// heading, a landmark for the main content, or a skip link whose target exists. Whether the content a mechanism skips is
// really repeated across pages needs more than one page (see docs/rules/structure.md).
internal sealed class BypassRule : StructureRule
{
    public BypassRule()
        : base("bypass", "A page must give a way to skip blocks of content that repeat across pages: a heading, a main landmark or a skip link.",
            "Add a skip link to the main content, mark it up with a main element, or start it with a heading.", null,
            "Choosing where the main content starts, and what the skip link says, is the author's decision.", actIds: ["cf77f2"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node page = PageNode(context);
        if (page.IsNull)
        {
            return;
        }

        if (Headings(context).Count > 0)
        {
            reporter.Passed(page, "The page has a heading.");
            return;
        }

        if (Landmarks(context).Exists(n => n.AriaRole!.Name == "main"))
        {
            reporter.Passed(page, "The page has a main landmark.");
            return;
        }

        foreach (Node a in Elements(context))
        {
            if (!SkipTarget(context, a).IsNull && IsRendered(context, a))
            {
                reporter.Passed(page, "The page has a skip link.");
                return;
            }
        }

        reporter.Failed(page, "The page has no heading, no main landmark and no skip link.");
    }
}

// axe skip-link. Best practice: an in-page link at the top of a page is a skip link, and it must lead somewhere. The
// target has to exist and be on the page for a user to land on it (HTML 7.4.6.3).
internal sealed class SkipLinkRule : StructureRule
{
    public SkipLinkRule()
        : base("skip-link", "A link to a place on the same page must have a target that exists and is not hidden.", "Point the link at the id of an element that is on the page, or remove the link.", null,
            "Which element the link should reach is the author's decision.")
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.IdReferences;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node a in Elements(context))
        {
            if (!IsHtml(a, "a") || a.GetAttribute("href") is not { Length: > 1 } href || href[0] != '#' || !IsRendered(context, a))
            {
                continue;
            }

            Node t = SkipTarget(context, a);
            if (t.IsNull && Ascii.EqualsIgnoreCase(href, "#top"))
            {
                // HTML 7.4.6.3: the fragment "top" scrolls to the top of the document when no element has that id.
                reporter.Passed(a);
            }
            else if (t.IsNull)
            {
                reporter.Failed(a, "No element has the id " + href[1..] + ".", [new Finding("href", href, "The fragment names no element.")]);
            }
            else if (!IsRendered(context, t))
            {
                reporter.Failed(a, "The element with the id " + href[1..] + " is hidden.", [new Finding("href", href, "The target is not rendered.")]);
            }
            else
            {
                reporter.Passed(a);
            }
        }
    }
}
