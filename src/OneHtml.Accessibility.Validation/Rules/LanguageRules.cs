using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Tree;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>Shared language helpers: the page root, attribute reads, and which elements own rendered text.</summary>
internal static class LanguageSupport
{
    /// <summary>The <c>html</c> element of an HTML document, or a null node when the root is something else (an SVG document, a fragment).</summary>
    public static Node HtmlElement(CheckContext c)
    {
        Node root = c.Document.DocumentElement;
        return !c.Document.IsFragment && !root.IsNull && root.Namespace == ElementNamespace.Html && root.LocalName == "html" ? root : default;
    }

    public static bool IsBlank(string? s)
    {
        if (s is null)
        {
            return true;
        }

        foreach (char ch in s)
        {
            if (!(ch is ' ' or '\t' or '\n' or '\f' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The elements that carry a <c>lang</c> attribute and own at least one piece of rendered text or a non-empty image
    /// alternative, text being "inheriting its programmatic language from the element" in the ACT language rules: the
    /// nearest ancestor-or-self element with a <c>lang</c> attribute (even an empty one) owns a text node. Text that is
    /// <c>aria-hidden</c> or off-screen still counts; text of an element that is not rendered (<c>display: none</c>,
    /// <c>hidden</c>) does not. Only the subtree of <c>body</c> is visited. One pass, explicit stack.
    /// </summary>
    public static HashSet<int> LangOwnersWithText(CheckContext c, System.Threading.CancellationToken ct)
    {
        var owners = new HashSet<int>();
        Node html = HtmlElement(c);
        if (html.IsNull)
        {
            return owners;
        }

        Node body = default;
        for (Node ch = html.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.IsElement && ch.Namespace == ElementNamespace.Html && ch.LocalName == "body")
            {
                body = ch;
                break;
            }
        }

        if (body.IsNull)
        {
            return owners;
        }

        var stack = new Stack<(Node Node, int Owner)>();
        stack.Push((body, body.HasAttribute("lang") ? body.Index : -1));
        int counter = 0;
        while (stack.Count > 0)
        {
            (Node el, int owner) = stack.Pop();
            if (c.Find(el) is not { } ax || ax.Hidden.IsNotRendered)
            {
                continue;
            }

            if ((++counter & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (owner >= 0 && el.Namespace == ElementNamespace.Html && el.LocalName == "img" && !IsBlankUnicode(el.GetAttribute("alt")))
            {
                owners.Add(owner);
            }

            for (Node ch = el.FirstChild; !ch.IsNull; ch = ch.NextSibling)
            {
                if (ch.Kind == NodeKind.Text)
                {
                    if (owner >= 0 && !IsBlankUnicode(ch.Data))
                    {
                        owners.Add(owner);
                    }
                }
                else if (ch.IsElement)
                {
                    stack.Push((ch, ch.HasAttribute("lang") ? ch.Index : owner));
                }
            }
        }

        return owners;
    }

    private static bool IsBlankUnicode(string? s) => s is null || IsBlankUnicode(s.AsSpan());

    private static bool IsBlankUnicode(ReadOnlySpan<char> s)
    {
        foreach (char ch in s)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>axe <c>html-has-lang</c>, ACT b5c3f8.</summary>
internal sealed class HtmlHasLangRule : AriaRule
{
    public HtmlHasLangRule()
        : base("html-has-lang",
            "The html element must have a lang attribute that is not empty.",
            "Add a lang attribute to the html element that names the main language of the page, such as lang=\"en\".",
            null,
            "The language of the page is content that the checker cannot know.",
            actIds: ["b5c3f8"])
    {
    }

    public override RuleScope Scope => RuleScope.Page;

    public override IReadOnlyCollection<string>? RelevantElements => ["html"];

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT b5c3f8: applies to the html element of a text/html document; fails when lang is absent, empty or only whitespace.
        Node html = LanguageSupport.HtmlElement(context);
        if (html.IsNull)
        {
            return;
        }

        if (LanguageSupport.IsBlank(html.GetAttribute("lang")))
        {
            reporter.Failed(html, "The html element has no lang attribute with a value.", [new Finding("lang", html.GetAttribute("lang") ?? string.Empty, "missing or blank")]);
        }
        else
        {
            reporter.Passed(html);
        }
    }
}

/// <summary>axe <c>html-lang-valid</c>, ACT bf051a.</summary>
internal sealed class HtmlLangValidRule : AriaRule
{
    public HtmlLangValidRule()
        : base("html-lang-valid",
            "The lang attribute of the html element must start with a registered language code.",
            "Change the lang attribute of the html element to a language code from the IANA registry, such as en or fr-CA.",
            null,
            "Which language the page is in is content; a corrected value cannot be derived from the markup.",
            actIds: ["bf051a"])
    {
    }

    public override RuleScope Scope => RuleScope.Page;

    public override IReadOnlyCollection<string>? RelevantElements => ["html"];

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT bf051a: applies to html with a lang that is not blank; expects a known primary language tag (RFC 5646 / IANA registry).
        Node html = LanguageSupport.HtmlElement(context);
        if (html.IsNull || html.GetAttribute("lang") is not { } lang || LanguageSupport.IsBlank(lang))
        {
            return;
        }

        if (LanguageTag.IsKnownPrimary(lang))
        {
            reporter.Passed(html);
        }
        else
        {
            reporter.Failed(html, "The primary language subtag \"" + LanguageTag.Primary(lang) + "\" is not in the IANA language subtag registry.", [new Finding("lang", lang, "unknown primary language")]);
        }
    }
}

/// <summary>axe <c>html-xml-lang-mismatch</c>, ACT 5b7ae0.</summary>
internal sealed class HtmlXmlLangMismatchRule : AriaRule
{
    public HtmlXmlLangMismatchRule()
        : base("html-xml-lang-mismatch",
            "When the html element has both lang and xml:lang, their primary languages must be the same.",
            "Make the xml:lang attribute of the html element name the same language as lang, or remove xml:lang.",
            null,
            "Which of the two values is right is content; removing xml:lang would also discard it for XML consumers.",
            actIds: ["5b7ae0"])
    {
    }

    public override RuleScope Scope => RuleScope.Page;

    public override IReadOnlyCollection<string>? RelevantElements => ["html"];

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT 5b7ae0: applies to html with a lang that has a known primary language and a non-empty xml:lang;
        // compares the primary language subtags, ASCII case-insensitively.
        Node html = LanguageSupport.HtmlElement(context);
        if (html.IsNull || html.GetAttribute("lang") is not { } lang || !LanguageTag.IsKnownPrimary(lang)
            || html.GetAttribute("xml:lang") is not { } xml || xml.Length == 0)
        {
            return;
        }

        string a = LanguageTag.Primary(lang);
        string b = LanguageTag.Primary(xml);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            reporter.Passed(html);
        }
        else
        {
            reporter.Failed(html, "lang names \"" + a + "\" but xml:lang names \"" + b + "\".", [new Finding("lang", lang, "xml:lang is " + xml)]);
        }
    }
}

/// <summary>axe <c>valid-lang</c>, ACT de46e4.</summary>
internal sealed class ValidLangRule : AriaRule
{
    public ValidLangRule()
        : base("valid-lang",
            "A lang attribute on content inside the body must start with a registered language code.",
            "Change the lang attribute to a language code from the IANA registry, such as es or zh-Hans.",
            null,
            "Which language a passage is in is content; a corrected value cannot be derived from the markup.",
            actIds: ["de46e4"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT de46e4: applies to each element in body whose lang is not the empty string and which owns rendered text.
        // The tag only needs a known primary language subtag; en-US-GB passes the ACT rule.
        foreach (int index in OrderedOwners(context, reporter.CancellationToken))
        {
            Node el = context.Document.GetNode(index);
            string lang = el.GetAttribute("lang")!;
            if (lang.Length == 0)
            {
                continue;
            }

            if (LanguageTag.IsKnownPrimary(lang))
            {
                reporter.Passed(el);
            }
            else
            {
                string why = LanguageTag.IsWellFormed(lang) ? "unknown primary language" : "not a well-formed language tag";
                reporter.Failed(el, "\"" + lang + "\" does not start with a registered language code (" + why + ").", [new Finding("lang", lang, why)]);
            }
        }
    }

    internal static List<int> OrderedOwners(CheckContext context, System.Threading.CancellationToken ct)
    {
        var list = new List<int>(LanguageSupport.LangOwnersWithText(context, ct));
        list.Sort();
        return list;
    }
}

/// <summary>ACT ucwvc8: the page language matches the language of the content; a person judges.</summary>
internal sealed class HtmlLangMatchesContentRule : AriaRule
{
    public HtmlLangMatchesContentRule()
        : base("html-lang-matches-content",
            "The language named by the lang attribute of the html element should be the main language of the page's text.",
            "Read the page and make the lang attribute of the html element name the language most of the text is written in.",
            null,
            "Deciding which language the text is in needs a reader.",
            axe: false, actIds: ["ucwvc8"], impact: Impact.Serious)
    {
    }

    public override RuleScope Scope => RuleScope.Page;

    public override IReadOnlyCollection<string>? RelevantElements => ["html"];

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT ucwvc8: applies to html with a lang that has a known primary language. The library has no language detector,
        // so every applicable page is left for a person.
        Node html = LanguageSupport.HtmlElement(context);
        if (html.IsNull || html.GetAttribute("lang") is not { } lang || !LanguageTag.IsKnownPrimary(lang))
        {
            return;
        }

        reporter.NeedsReview(html, "Check that most of the page is written in \"" + LanguageTag.Primary(lang) + "\".", [new Finding("lang", lang, "page language")]);
    }
}

/// <summary>ACT off6ek: the language of a passage matches its lang attribute; a person judges.</summary>
internal sealed class ElementLangMatchesContentRule : AriaRule
{
    public ElementLangMatchesContentRule()
        : base("element-lang-matches-content",
            "The language named by a lang attribute should be the language of the text it covers.",
            "Read the passage and make its lang attribute name the language the text is written in.",
            null,
            "Deciding which language a passage is in needs a reader.",
            axe: false, actIds: ["off6ek"], impact: Impact.Serious)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT off6ek: applies to elements in body with a known primary language in lang and some rendered text of their own.
        foreach (int index in ValidLangRule.OrderedOwners(context, reporter.CancellationToken))
        {
            Node el = context.Document.GetNode(index);
            string lang = el.GetAttribute("lang")!;
            if (lang.Length == 0 || !LanguageTag.IsKnownPrimary(lang))
            {
                continue;
            }

            reporter.NeedsReview(el, "Check that the text is written in \"" + LanguageTag.Primary(lang) + "\".", [new Finding("lang", lang, "passage language")]);
        }
    }
}
