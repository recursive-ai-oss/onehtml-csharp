using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

internal static class StructureRuleSet
{
    /// <summary>Every rule of the page-structure area.</summary>
    internal static IEnumerable<IAccessibilityRule> Create()
    {
        foreach (IAccessibilityRule r in LandmarkRuleSet.Create())
        {
            yield return r;
        }

        yield return new HeadingOrderRule();
        yield return new PageHasHeadingOneRule();
        yield return new ParagraphAsHeadingRule();
        foreach (IAccessibilityRule r in ListRuleSet.Create())
        {
            yield return r;
        }

        foreach (IAccessibilityRule r in IdRuleSet.Create())
        {
            yield return r;
        }

        foreach (IAccessibilityRule r in FocusRuleSet.Create())
        {
            yield return r;
        }

        foreach (IAccessibilityRule r in MiscRuleSet.Create())
        {
            yield return r;
        }
    }
}

internal static class MiscRuleSet
{
    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new HiddenContentRule();
        yield return new ObsoleteElementRule("blink", "blink", "The blink element makes text flash, which WCAG forbids for more than five seconds.", "Remove the blink element and mark emphasis another way.",
            "Removing the element drops the author's intended effect, and CSS the library cannot see may style the element, so the author decides what replaces it.");
        yield return new ObsoleteElementRule("marquee", "marquee", "The marquee element scrolls text on its own, which a user must be able to stop.", "Replace the marquee with static content, or with an animation a user can pause.",
            "Static text, a slower effect or a control to pause it are all valid replacements, so the author decides.");
        yield return new ServerSideImageMapRule();
        yield return new MetaRefreshRule("meta-refresh", false, "A page must not refresh or redirect on a timer, unless the delay is zero or longer than twenty hours.", "Remove the timed refresh, redirect on the server, or add a way to turn it off.", ["bc659a"]);
        yield return new MetaRefreshRule("meta-refresh-no-exceptions", true, "A page must not refresh or redirect on a timer at all, other than immediately.", "Remove the timed refresh and redirect on the server instead.", ["bisz58"]);
        yield return new MetaViewportRule("meta-viewport", 2, "The viewport meta element must not stop a user from zooming in, or limit zoom below two times.", "Remove user-scalable=no and any maximum-scale below 2 from the viewport content.", ["b4f0c3"]);
        yield return new MetaViewportRule("meta-viewport-large", 5, "The viewport meta element should let a user zoom to at least five times.", "Remove user-scalable=no and any maximum-scale below 5 from the viewport content.", null);
    }
}

// axe hidden-content (experimental review item): content that CSS hides is not seen by sighted users or, for display
// and visibility hiding, by assistive technology either, so a person should confirm it is meant to be hidden. Reported on
// the topmost hidden element that holds text. Needs no layout: the cascade decides.
internal sealed class HiddenContentRule : StructureRule
{
    private static readonly HashSet<string> s_skip = new(StringComparer.Ordinal)
    {
        "script", "style", "template", "noscript", "head", "title", "meta", "link", "base", "datalist", "option", "optgroup", "source", "track", "param", "area",
    };

    public HiddenContentRule()
        : base("hidden-content", "Content hidden with CSS should be checked: confirm that it is meant to be hidden.", "Confirm the content is not needed by anyone, or show it.", null,
            "Whether hidden content should be shown or removed is the author's decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node e in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!e.IsElement || e.Namespace != ElementNamespace.Html || s_skip.Contains(e.LocalName) || InSkipped(e))
            {
                continue;
            }

            HiddenInfo h = context.Tree.GetHidden(e);
            const HiddenReasons css = HiddenReasons.DisplayNone | HiddenReasons.VisibilityHidden;
            if ((h.Reasons & css) == 0 || h.IsInherited || (h.Reasons & HiddenReasons.Template) != 0 || e.Parent.IsNull || (e.TextContent is not { } text || IsBlank(text)))
            {
                continue;
            }

            reporter.NeedsReview(e, "This content is hidden with CSS.", [new Finding(null, null, (h.Reasons & HiddenReasons.DisplayNone) != 0 ? "display: none" : "visibility: hidden")]);
        }
    }

    private static bool InSkipped(Node e)
    {
        for (Node p = e.ParentElement; !p.IsNull; p = p.ParentElement)
        {
            if (s_skip.Contains(p.LocalName))
            {
                return true;
            }
        }

        return false;
    }
}

// axe blink and marquee, WCAG 2.2.2 (technique F47 for blink): obsolete elements whose whole point is content that
// moves or flashes without a way to stop it. Not suggested for removal: see the descriptor.
internal sealed class ObsoleteElementRule : StructureRule
{
    private readonly string _element;

    public ObsoleteElementRule(string id, string element, string description, string help, string noFix)
        : base(id, description, help, null, noFix)
    {
        _element = element;
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node e in Elements(context))
        {
            if (IsHtml(e, _element))
            {
                reporter.Failed(e, "The " + _element + " element moves or flashes content and cannot be paused.", [new Finding(null, _element, "Obsolete element.")]);
            }
        }
    }
}

// axe server-side-image-map, WCAG 2.1.1 (HTML 4.8.3, the ismap attribute): an image with ismap inside a link sends the click's
// coordinates to the server, which a keyboard cannot give. A person has to confirm the same destinations exist as plain
// links. An ismap image outside a link does nothing (HTML 4.8.3 ismap).
internal sealed class ServerSideImageMapRule : StructureRule
{
    public ServerSideImageMapRule()
        : base("server-side-image-map", "A server-side image map cannot be used with a keyboard.", "Replace the server-side image map with a client-side map or plain links.", null,
            "How the map's regions become links is design, so the author decides.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node img in Elements(context))
        {
            if (!IsHtml(img, "img") || !img.HasAttribute("ismap") || !IsRendered(context, img))
            {
                continue;
            }

            bool inLink = false;
            for (Node p = img.ParentElement; !p.IsNull; p = p.ParentElement)
            {
                if (IsHtml(p, "a") && p.HasAttribute("href"))
                {
                    inLink = true;
                    break;
                }
            }

            if (inLink)
            {
                reporter.NeedsReview(img, "A server-side image map: confirm each region is also reachable as a link.", [new Finding("ismap", null, "The image sends click coordinates to the server.")]);
            }
        }
    }
}

// ACT bc659a and bisz58, axe meta-refresh and meta-refresh-no-exceptions, WCAG 2.2.1 and 2.2.4 (3.2.5, technique F40
// and F41). The rule applies to the first meta element whose http-equiv is refresh and whose content is valid by the
// HTML "shared declarative refresh steps" (4.2.5.3 and 7.5): in meta-refresh the delay must be 0 or over 72000 seconds;
// with no exceptions it must be 0. A URL that cannot be parsed makes the content invalid there; the checker has no base
// URL to resolve against and treats any URL text as valid.
internal sealed class MetaRefreshRule : StructureRule
{
    private readonly bool _zeroOnly;

    public MetaRefreshRule(string id, bool zeroOnly, string description, string help, IReadOnlyList<string> act)
        : base(id, description, help, null, "Whether to redirect on the server, remove the refresh or give users a control is the author's decision.", actIds: act)
    {
        _zeroOnly = zeroOnly;
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node m in Elements(context))
        {
            if (!IsHtml(m, "meta") || !EqualsAscii(m.GetAttribute("http-equiv"), "refresh") || m.GetAttribute("content") is not { } content || !TryParseDelay(content, out long delay))
            {
                continue;
            }

            bool ok = delay == 0 || (!_zeroOnly && delay > 72000);
            if (ok)
            {
                reporter.Passed(m);
            }
            else
            {
                reporter.Failed(m, "The page refreshes or redirects after " + delay.ToString(CultureInfo.InvariantCulture) + " seconds.", [new Finding("content", content, "A timed refresh.")]);
            }

            // The ACT rules look at the first valid refresh only.
            return;
        }
    }

    /// <summary>HTML 4.2.5.3 "shared declarative refresh steps", up to the delay: time, then end, ";", "," or white space.</summary>
    internal static bool TryParseDelay(string input, out long time)
    {
        time = 0;
        int i = 0;
        while (i < input.Length && IsSpace(input[i]))
        {
            i++;
        }

        int start = i;
        while (i < input.Length && input[i] is >= '0' and <= '9')
        {
            time = time < 1_000_000_000_000 ? (time * 10) + (input[i] - '0') : time;
            i++;
        }

        if (i == start && !(i < input.Length && input[i] == '.'))
        {
            return false;
        }

        while (i < input.Length && (input[i] is >= '0' and <= '9' || input[i] == '.'))
        {
            i++;
        }

        return i >= input.Length || input[i] is ';' or ',' || IsSpace(input[i]);
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';
}

// ACT b4f0c3, axe meta-viewport (zoom limit 2) and meta-viewport-large (5), WCAG 1.4.4 (and 1.4.10): a viewport meta element
// that switches off pinch zoom or caps it too low. The content is a list of name=value properties separated by comma,
// semicolon or white space (CSS Device Adaptation, "meta viewport" interpretation). yes is 1 and no is 0; a number's
// leading numeric part counts; a negative or unparseable value is ignored, as the ACT cases expect. user-scalable is
// off for no or any number whose magnitude is under 1. A later property of the same name wins. With a limit of 5 a
// property of user-scalable=no also fails, since zoom is then not possible at all.
internal sealed class MetaViewportRule : StructureRule
{
    private readonly double _limit;

    public MetaViewportRule(string id, double limit, string description, string help, IReadOnlyList<string>? act)
        : base(id, description, help, null,
            "Removing the restriction and raising the limit both make zoom possible, so there is more than one correct edit.", actIds: act)
    {
        _limit = limit;
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node m in Elements(context))
        {
            if (!IsHtml(m, "meta") || !EqualsAscii(m.GetAttribute("name"), "viewport") || m.GetAttribute("content") is not { } content)
            {
                continue;
            }

            Dictionary<string, string> props = Parse(content);
            bool hasScalable = props.TryGetValue("user-scalable", out string? us);
            bool hasMax = props.TryGetValue("maximum-scale", out string? ms);
            if (!hasScalable && !hasMax)
            {
                continue;
            }

            var findings = new List<Finding>();
            if (hasScalable && Number(us!) is { } u && Math.Abs(u) < 1)
            {
                findings.Add(new Finding("user-scalable", us, "Zooming is switched off."));
            }

            if (hasMax && Number(ms!) is { } mx && mx >= 0 && mx < _limit)
            {
                findings.Add(new Finding("maximum-scale", ms, "Zooming stops below " + _limit.ToString(CultureInfo.InvariantCulture) + " times."));
            }

            if (findings.Count == 0)
            {
                reporter.Passed(m);
            }
            else
            {
                reporter.Failed(m, "The viewport restricts zooming.", findings);
            }
        }
    }

    private static Dictionary<string, string> Parse(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        int i = 0;
        while (i < content.Length)
        {
            while (i < content.Length && (IsSep(content[i])))
            {
                i++;
            }

            int ks = i;
            while (i < content.Length && !IsSep(content[i]) && content[i] != '=')
            {
                i++;
            }

            string key = Normalize(content[ks..i]);
            while (i < content.Length && content[i] is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                i++;
            }

            if (i < content.Length && content[i] == '=')
            {
                i++;
                while (i < content.Length && content[i] is ' ' or '\t' or '\n' or '\f' or '\r')
                {
                    i++;
                }

                int vs = i;
                while (i < content.Length && !IsSep(content[i]))
                {
                    i++;
                }

                if (key.Length > 0)
                {
                    map[key] = Normalize(content[vs..i]);
                }
            }
        }

        return map;
    }

    private static bool IsSep(char c) => c is ',' or ';' or ' ' or '\t' or '\n' or '\f' or '\r';

    private static double? Number(string v)
    {
        switch (v)
        {
            case "yes":
                return 1;
            case "no":
                return 0;
            case "device-width":
            case "device-height":
                return 10;
        }

        int i = 0;
        if (i < v.Length && v[i] is '-' or '+')
        {
            i++;
        }

        int digits = i;
        while (i < v.Length && (v[i] is >= '0' and <= '9' || v[i] == '.'))
        {
            i++;
        }

        return i > digits && double.TryParse(v.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }
}
