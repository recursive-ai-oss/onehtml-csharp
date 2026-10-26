using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>Small helpers the names-and-labels rules share.</summary>
internal static class NameSupport
{
    internal const string Html = "http://www.w3.org/1999/xhtml";
    internal const string Svg = "http://www.w3.org/2000/svg";

    internal static bool Is(Node e, string local) => e.NamespaceURI == Html && e.LocalName == local;

    /// <summary>True when the text has no character other than white space (any Unicode white space).</summary>
    internal static bool IsEmptyText(string s)
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

    /// <summary>The input's type as the HTML Standard resolves it: an ASCII case-insensitive match of a keyword, else "text".</summary>
    internal static string InputType(Node input)
    {
        string? t = input.GetAttribute("type");
        if (t is null)
        {
            return "text";
        }

        foreach (string k in s_inputTypes)
        {
            if (Ascii.EqualsIgnoreCase(t, k))
            {
                return k;
            }
        }

        return "text";
    }

    private static readonly string[] s_inputTypes =
    [
        "hidden", "text", "search", "tel", "url", "email", "password", "date", "month", "week", "time", "datetime-local",
        "number", "range", "color", "checkbox", "radio", "file", "submit", "image", "reset", "button",
    ];

    /// <summary>ASCII-lowercases and collapses ASCII white space, the comparison the identical-name rules use.</summary>
    internal static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s)
        {
            if (ch is ' ' or '\t' or '\n' or '\f' or '\r' || char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch);
        }

        return sb.ToString();
    }

    private const string PlaceholderBase = "https://document.invalid/a/b/c/d/e/";

    /// <summary>
    /// Makes link targets comparable. The document's own address is not known to the library, so relative targets are
    /// resolved against a fixed placeholder (or the document's <c>base</c> element); two targets that resolve to the same
    /// resource in the real document resolve to the same string here. HTML Standard section 2.5.3. The <c>base</c> element
    /// is looked up once per resolver.
    /// </summary>
    internal sealed class TargetResolver
    {
        private readonly Uri _base;

        internal TargetResolver(Document doc, string? documentUrl = null)
        {
            _base = documentUrl is { Length: > 0 } && Uri.TryCreate(documentUrl, UriKind.Absolute, out Uri? docUri) ? docUri : new Uri(PlaceholderBase);
            foreach (Node e in doc.Root.DescendantElements)
            {
                if (Is(e, "base") && e.GetAttribute("href") is { } bh)
                {
                    if (Uri.TryCreate(_base, bh.Trim(), out Uri? b))
                    {
                        _base = b;
                    }

                    break;
                }
            }
        }

        internal string Resolve(string? href)
        {
            string raw = (href ?? string.Empty).Trim();
            return Uri.TryCreate(_base, raw, out Uri? u) ? u.AbsoluteUri : raw;
        }
    }

    /// <summary>Most tree nodes the text-gathering rules visit in one run; a hostile page with deeply nested controls stops being read after this.</summary>
    internal const long VisitBudget = 20_000_000;

    /// <summary>
    /// The text nodes below a tree node that are exposed, in tree order (explicit stack, no recursion). Every node visited
    /// is taken from <paramref name="budget"/>; when it runs out the list so far is returned.
    /// </summary>
    internal static List<AxNode> TextNodes(AxNode root, ref long budget)
    {
        var result = new List<AxNode>();
        var stack = new Stack<AxNode>();
        for (int i = root.Children.Count - 1; i >= 0; i--)
        {
            stack.Push(root.Children[i]);
        }

        while (stack.Count > 0 && --budget >= 0)
        {
            AxNode n = stack.Pop();
            if (n.Hidden.IsHidden)
            {
                continue;
            }

            if (n.Kind == AxNodeKind.Text)
            {
                result.Add(n);
                continue;
            }

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(n.Children[i]);
            }
        }

        return result;
    }
}

/// <summary>
/// A rule of the shape "every exposed element of this kind must have an accessible name". The tree computes the name
/// (AccName 1.2), so the rule only picks the subjects and reads the result.
/// </summary>
internal sealed class SubjectNameRule : AriaRule
{
    private readonly Func<CheckContext, AxNode, bool> _applies;
    private readonly string _subject;
    private readonly Func<AxNode, bool>? _named;

    public SubjectNameRule(string id, string description, string help, string subject, Func<CheckContext, AxNode, bool> applies, IReadOnlyList<string>? actIds = null, Func<AxNode, bool>? named = null, string noFixReason = "The name is content: only the author knows what the element is called or shows.", Impact? impact = null)
        : base(id, description, help, null, noFixReason, actIds: actIds, impact: impact)
    {
        _subject = subject;
        _applies = applies;
        _named = named;
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Ancestors;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (AxNode node in context.TreeNodes())
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (node.Kind != AxNodeKind.Element || !context.IsExposed(node.DomNode) || !_applies(context, node))
            {
                continue;
            }

            if (_named is null ? NameSupport.IsEmptyText(node.Name.Value) : !_named(node))
            {
                reporter.Failed(node.DomNode, "The " + _subject + " has no accessible name.", [new Finding(null, null, "No accessible name could be computed.")]);
            }
            else
            {
                reporter.Passed(node.DomNode);
            }
        }
    }
}

/// <summary>Judgement-only rules: every applicable element is reported for review, never decided.</summary>
internal sealed class ReviewOnlyRule : AriaRule
{
    private readonly Func<CheckContext, AxNode, bool> _applies;
    private readonly string _message;

    public ReviewOnlyRule(string id, string description, string help, string message, Func<CheckContext, AxNode, bool> applies, IReadOnlyList<string> actIds)
        : base(id, description, help, null, "Whether the text is descriptive is a judgement about content; there is no single correct replacement.", axe: false, actIds: actIds, impact: Impact.Moderate, extraTags: ["needs-review-only"])
    {
        _message = message;
        _applies = applies;
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Ancestors;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (AxNode node in context.TreeNodes())
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (node.Kind == AxNodeKind.Element && context.IsExposed(node.DomNode) && !NameSupport.IsEmptyText(node.Name.Value) && _applies(context, node))
            {
                reporter.NeedsReview(node.DomNode, _message, [new Finding(null, node.Name.Value, "The accessible name.")]);
            }
        }
    }
}

// ACT 23a2a8 / axe image-alt: an img is either named or marked decorative. HTML-AAM maps alt="" (and, trimmed, alt=" ")
// to the role none; ACT 23a2a8 treats only an empty alt, or a presentational role, as the decorative mark, so an alt of
// white space alone with no other name fails. WCAG 1.1.1, technique H67 for the decorative case.
internal sealed class ImageAltRule : AriaRule
{
    public ImageAltRule()
        : base("image-alt", "An img element must have alternative text, or be marked as decorative with an empty alt.",
            "Add an alt attribute that says what the image shows, or alt=\"\" if it is decorative.", null,
            "Whether the image is informative or decorative, and what it shows, is content only the author knows.", actIds: ["23a2a8"])
    {
    }

    public override RuleScope Scope => RuleScope.Node;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (AxNode node in context.TreeNodes())
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (node.Kind != AxNodeKind.Element || !NameSupport.Is(node.DomNode, "img") || !context.IsExposed(node.DomNode))
            {
                continue;
            }

            string? alt = node.DomNode.GetAttribute("alt");
            if (node.AriaRole is { Name: "none" or "presentation" })
            {
                if (alt is { Length: > 0 } && NameSupport.IsEmptyText(alt) && NameSupport.IsEmptyText(node.Name.Value) && node.DomNode.GetAttribute("role") is null)
                {
                    reporter.Failed(node.DomNode, "The image's alt attribute holds only white space.", [new Finding("alt", alt, "White space is not a text alternative, and it does not mark the image as decorative.")]);
                }
                else
                {
                    reporter.Passed(node.DomNode, "The image is marked as decorative.");
                }
            }
            else if (node.AriaRole is { Name: "img" })
            {
                if (NameSupport.IsEmptyText(node.Name.Value))
                {
                    reporter.Failed(node.DomNode, "The image has no accessible name.", [new Finding("alt", alt, "No text alternative could be computed.")]);
                }
                else
                {
                    reporter.Passed(node.DomNode);
                }
            }
        }
    }
}

// ACT 8fc3b6 / axe object-alt: an object that renders non-text content needs a name. Whether the resource is text is
// known from the type attribute or the extension of data; anything else is taken as non-text content, the cautious reading
// (the page may not render text, and the rule would otherwise miss real failures).
internal sealed class ObjectAltRule : AriaRule
{
    private static readonly string[] s_textExtensions = [".html", ".htm", ".xhtml", ".xht", ".txt", ".xml", ".css", ".js", ".json", ".md", ".csv"];

    public ObjectAltRule()
        : base("object-alt", "An object element that shows non-text content must have an accessible name.", "Name the object with aria-label, aria-labelledby or a title attribute.", null,
            "The name is content: only the author knows what the embedded resource is.", actIds: ["8fc3b6"])
    {
    }

    internal static bool RendersText(Node obj)
    {
        string? type = obj.GetAttribute("type");
        if (type is not null)
        {
            string t = NameSupport.Fold(type);
            int semi = t.IndexOf(';', StringComparison.Ordinal);
            if (semi >= 0)
            {
                t = t[..semi].TrimEnd();
            }

            return t.StartsWith("text/", StringComparison.Ordinal) || t is "application/xhtml+xml" or "application/xml" or "application/json";
        }

        if (obj.GetAttribute("data") is { } data)
        {
            string d = NameSupport.Fold(data);
            int cut = d.IndexOfAny(['?', '#']);
            if (cut >= 0)
            {
                d = d[..cut];
            }

            foreach (string ext in s_textExtensions)
            {
                if (d.EndsWith(ext, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Descendants;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (AxNode node in context.TreeNodes())
        {
            if (node.Kind != AxNodeKind.Element || !NameSupport.Is(node.DomNode, "object") || !context.IsExposed(node.DomNode)
                || node.AriaRole is { Name: "none" or "presentation" } || RendersText(node.DomNode))
            {
                continue;
            }

            if (NameSupport.IsEmptyText(node.Name.Value))
            {
                reporter.Failed(node.DomNode, "The object has no accessible name.", [new Finding(null, null, "No accessible name could be computed.")]);
            }
            else
            {
                reporter.Passed(node.DomNode);
            }
        }
    }
}

// ACT 2779a5 / axe document-title: the document's title element (the first title element in tree order, HTML namespace
// only, HTML Standard 3.2.1) must have text. WCAG 2.4.2.
internal sealed class DocumentTitleRule : AriaRule
{
    public DocumentTitleRule()
        : base("document-title", "The document must have a title element with text in it.", "Add a title element to the head of the document and say in it what the page is.", null,
            "The title is content: only the author knows what the page is called.", actIds: ["2779a5"])
    {
    }

    public override RuleScope Scope => RuleScope.Page;

    public override IReadOnlyCollection<string>? RelevantElements => ["html", "title"];

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node html = default;
        Node title = default;
        foreach (Node e in context.Document.Root.DescendantElements)
        {
            if (html.IsNull && NameSupport.Is(e, "html"))
            {
                html = e;
            }

            if (NameSupport.Is(e, "title"))
            {
                title = e;
                break;
            }
        }

        if (html.IsNull)
        {
            return;
        }

        if (!title.IsNull && !NameSupport.IsEmptyText(title.TextContent ?? string.Empty))
        {
            reporter.Passed(html);
        }
        else
        {
            reporter.Failed(html, title.IsNull ? "The document has no title element." : "The document's title element is empty.");
        }
    }
}

// Shared by label, label-title-only and form-field-multiple-labels. A "form field" is the native control a person types
// into or chooses with; buttons and image buttons have their own rules. Disabled fields count too (ACT e086e5 passes and fails them like any other).
internal static class FormFields
{
    internal static bool IsField(CheckContext c, AxNode n)
    {
        Node e = n.DomNode;
        if (e.NamespaceURI != NameSupport.Html || n.AriaRole is { Name: "none" or "presentation" })
        {
            return false;
        }

        switch (e.LocalName)
        {
            case "select":
            case "textarea":
                return true;
            case "input":
                return NameSupport.InputType(e) is not ("hidden" or "image" or "submit" or "reset" or "button");
            default:
                return false;
        }
    }

    internal static bool IsLabelable(Node e)
    {
        if (e.NamespaceURI != NameSupport.Html)
        {
            return false;
        }

        return e.LocalName switch
        {
            "button" or "meter" or "output" or "progress" or "select" or "textarea" => true,
            "input" => NameSupport.InputType(e) != "hidden",
            _ => false,
        };
    }

    /// <summary>For every labelable control, the label elements that label it (HTML Standard 4.10.4: for= or the first labelable descendant).</summary>
    internal static Dictionary<int, List<Node>> LabelsByControl(Document doc, System.Threading.CancellationToken ct)
    {
        var map = new Dictionary<int, List<Node>>();
        long budget = 2_000_000;
        int visited = 0;
        foreach (Node label in doc.Root.DescendantElements)
        {
            if ((++visited & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (!NameSupport.Is(label, "label"))
            {
                continue;
            }

            Node target = default;
            if (label.GetAttribute("for") is { } f)
            {
                Node t = f.Length == 0 ? default : doc.GetElementById(f);
                if (!t.IsNull && IsLabelable(t))
                {
                    target = t;
                }
            }
            else
            {
                // Labels nested in labels could make this quadratic; the scan has a shared budget like the tree's own.
                for (Node d = label.NextInDocumentOrder(label); !d.IsNull && budget-- > 0; d = d.NextInDocumentOrder(label))
                {
                    if (IsLabelable(d))
                    {
                        target = d;
                        break;
                    }
                }
            }

            if (!target.IsNull)
            {
                if (!map.TryGetValue(target.Index, out List<Node>? list))
                {
                    map[target.Index] = list = [];
                }

                list.Add(label);
            }
        }

        return map;
    }
}

// axe label-title-only (WCAG 3.3.2, best practice): a field whose only name is its title attribute, or that is described
// but not named, has no visible label. A field with a name from a label, aria-label or aria-labelledby passes.
internal sealed class LabelTitleOnlyRule : AriaRule
{
    public LabelTitleOnlyRule()
        : base("label-title-only", "A form field should have a visible label, not only a title attribute or a description.",
            "Add a visible label element for the field, or name it with aria-label or aria-labelledby.", null,
            "The label is content: its wording and place on the page are the author's.")
    {
    }

    public override RuleScope Scope => RuleScope.Node;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (AxNode node in context.TreeNodes())
        {
            if (node.Kind != AxNodeKind.Element || !context.IsExposed(node.DomNode) || !FormFields.IsField(context, node))
            {
                continue;
            }

            if (node.Name.Source == AxTextSource.Title)
            {
                reporter.Failed(node.DomNode, "The field is named only by its title attribute.", [new Finding("title", node.DomNode.GetAttribute("title"), "The title is the only name.")]);
            }
            else if (node.Name.IsEmpty && node.DomNode.GetAttribute("aria-describedby") is { } d && !NameSupport.IsEmptyText(d))
            {
                reporter.Failed(node.DomNode, "The field is described by aria-describedby but has no name.", [new Finding("aria-describedby", d, "A description does not name the field.")]);
            }
            else if (!node.Name.IsEmpty)
            {
                reporter.Passed(node.DomNode);
            }
        }
    }
}

// axe form-field-multiple-labels (WCAG 3.3.2): several label elements for one field are joined into its name in an order
// assistive technology may read differently; whether the joined name is right is for a person to say.
internal sealed class FormFieldMultipleLabelsRule : AriaRule
{
    public FormFieldMultipleLabelsRule()
        : base("form-field-multiple-labels", "A form field should not have more than one label element.", "Use one label element for the field and put the extra text inside it.", null,
            "Which label to keep, or how to merge them, is a content decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Dictionary<int, List<Node>> labels = FormFields.LabelsByControl(context.Document, reporter.CancellationToken);
        foreach (AxNode node in context.Tree.Nodes)
        {
            if (node.Kind != AxNodeKind.Element || !context.IsExposed(node.DomNode) || !FormFields.IsField(context, node))
            {
                continue;
            }

            if (!labels.TryGetValue(node.DomNode.Index, out List<Node>? list))
            {
                continue;
            }

            if (list.Count > 1)
            {
                reporter.NeedsReview(node.DomNode, "The field has " + list.Count + " label elements; check that the name they make together reads well.");
            }
            else
            {
                reporter.Passed(node.DomNode);
            }
        }
    }
}

// ACT 4b1c6c / axe frame-title-unique: frames that share a name should serve the same purpose. Showing the same resource
// is the one case the library can tell; otherwise a person has to look.
internal sealed class FrameTitleUniqueRule : AriaRule
{
    public FrameTitleUniqueRule()
        : base("frame-title-unique", "Frames with the same accessible name must have the same purpose.", "Give each frame a name that says what it holds, or show the same resource in frames that share a name.", null,
            "Naming a frame is a content decision.", actIds: ["4b1c6c"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var targets = new NameSupport.TargetResolver(context.Document, context.Tree.Options.BaseUrl);
        var groups = new Dictionary<string, List<AxNode>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (AxNode node in context.Tree.Nodes)
        {
            if (node.Kind != AxNodeKind.Element || node.DomNode.NamespaceURI != NameSupport.Html || node.DomNode.LocalName is not ("iframe" or "frame")
                || !context.IsExposed(node.DomNode) || node.AriaRole is { Name: "none" or "presentation" } || NameSupport.IsEmptyText(node.Name.Value))
            {
                continue;
            }

            string key = NameSupport.Fold(node.Name.Value);
            if (!groups.TryGetValue(key, out List<AxNode>? list))
            {
                groups[key] = list = [];
                order.Add(key);
            }

            list.Add(node);
        }

        foreach (string key in order)
        {
            List<AxNode> list = groups[key];
            if (list.Count < 2)
            {
                continue;
            }

            string first = Purpose(targets, list[0].DomNode);
            bool same = true;
            foreach (AxNode n in list)
            {
                same &= string.Equals(Purpose(targets, n.DomNode), first, StringComparison.Ordinal);
            }

            foreach (AxNode n in list)
            {
                if (same)
                {
                    reporter.Passed(n.DomNode, "Frames with this name show the same resource.");
                }
                else
                {
                    reporter.NeedsReview(n.DomNode, "Frames named \"" + list[0].Name.Value + "\" show different resources; check that they serve the same purpose.", [new Finding("src", n.DomNode.GetAttribute("src"), "Where the frame points.")]);
                }
            }
        }
    }

    private static string Purpose(NameSupport.TargetResolver targets, Node frame) =>
        frame.GetAttribute("srcdoc") is { } doc ? "doc:" + doc : "src:" + targets.Resolve(frame.GetAttribute("src"));
}

// ACT b20e66 / axe identical-links-same-purpose (WCAG 2.4.9): links with the same name go to the same resource, or a
// person must say whether they serve the same purpose. Links that exist alone are not applicable.
internal sealed class IdenticalLinksRule : AriaRule
{
    public IdenticalLinksRule()
        : base("identical-links-same-purpose", "Links with the same accessible name should serve the same purpose.", "Give links that go to different places different names, or send the identical names to the same resource.", null,
            "Naming a link is a content decision.", actIds: ["b20e66"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var targets = new NameSupport.TargetResolver(context.Document, context.Tree.Options.BaseUrl);
        var groups = new Dictionary<string, List<AxNode>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (AxNode node in context.Tree.Nodes)
        {
            if (node.Kind != AxNodeKind.Element || node.AriaRole is not { Name: "link" } || !context.IsExposed(node.DomNode) || NameSupport.IsEmptyText(node.Name.Value))
            {
                continue;
            }

            string key = NameSupport.Fold(node.Name.Value);
            if (!groups.TryGetValue(key, out List<AxNode>? list))
            {
                groups[key] = list = [];
                order.Add(key);
            }

            list.Add(node);
        }

        foreach (string key in order)
        {
            List<AxNode> list = groups[key];
            if (list.Count < 2)
            {
                continue;
            }

            // A link with no href (a script-driven role=link) has no target the library can compare.
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
                    reporter.Passed(n.DomNode, "Links with this name go to the same resource.");
                }
                else
                {
                    reporter.NeedsReview(n.DomNode, "Links named \"" + list[0].Name.Value + "\" go to different resources; check that they serve the same purpose.", [new Finding("href", n.DomNode.GetAttribute("href"), "Where the link points.")]);
                }
            }
        }
    }

    private static string? Target(NameSupport.TargetResolver targets, Node link) =>
        (link.GetAttribute("href") ?? link.GetAttribute("xlink:href")) is { } href ? targets.Resolve(href) : null;
}

// axe image-redundant-alt (best practice): the alternative text of an image inside a link, button or text block that
// already says the same thing is read twice.
internal sealed class ImageRedundantAltRule : AriaRule
{
    public ImageRedundantAltRule()
        : base("image-redundant-alt", "The alternative text of an image should not repeat the text next to it in a link or button.",
            "Give the image an empty alt when the surrounding text already says it, or word the alt differently.", null,
            "Whether to empty the alt or reword it depends on what the image shows.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var texts = new Dictionary<int, string>();
        long budget = NameSupport.VisitBudget;
        foreach (AxNode img in context.Tree.Nodes)
        {
            if (img.Kind != AxNodeKind.Element || !NameSupport.Is(img.DomNode, "img") || !context.IsExposed(img.DomNode) || img.DomNode.GetAttribute("alt") is not { } alt)
            {
                continue;
            }

            string a = NameSupport.Fold(alt);
            AxNode? container = img.Parent;
            while (container is not null && !(container.Kind == AxNodeKind.Element && IsContainer(container)))
            {
                container = container.Parent;
            }

            if (a.Length == 0 || container is null || budget < 0)
            {
                continue;
            }

            if (!texts.TryGetValue(container.Id, out string? text))
            {
                var sb = new StringBuilder();
                foreach (AxNode t in NameSupport.TextNodes(container, ref budget))
                {
                    sb.Append(t.Name.Value).Append(' ');
                }

                texts[container.Id] = text = NameSupport.Fold(sb.ToString());
            }

            if (text == a)
            {
                reporter.Failed(img.DomNode, "The image's alt text repeats text beside it.", [new Finding("alt", alt, "The same words are in the " + container.DomNode.LocalName + ".")]);
            }
            else
            {
                reporter.Passed(img.DomNode);
            }
        }
    }

    private static bool IsContainer(AxNode n) =>
        n.AriaRole is { Name: "button" or "link" } || NameSupport.Is(n.DomNode, "p") || NameSupport.Is(n.DomNode, "li") || NameSupport.Is(n.DomNode, "td") || NameSupport.Is(n.DomNode, "th");
}

// ACT 2ee8b8 / axe label-content-name-mismatch (WCAG 2.5.3): when a control's name does not come from its content, the
// name must contain the text people see on it. What people see is a layout question: with rendered facts the rule reads
// the text that was laid out; without them it decides only when the cascade shows nothing that can hide, move or restyle
// the text, and otherwise says needs-render.
internal sealed class LabelContentNameRule : AriaRule
{
    private static readonly HashSet<string> s_roles = new(StringComparer.Ordinal)
    {
        "button", "checkbox", "gridcell", "link", "menuitem", "menuitemcheckbox", "menuitemradio", "option", "radio", "switch", "tab", "treeitem",
    };

    internal static readonly string[] MissingFacts = ["layout text of the visible label", "text boxes of the visible label"];

    public LabelContentNameRule()
        : base("label-content-name-mismatch", "The accessible name of a control must contain the text visible on it.",
            "Start or include the visible text in the aria-label or aria-labelledby that names the control.", null,
            "The name is content: the fix is to reword it around the visible text, which is the author's call.", actIds: ["2ee8b8"], extraTags: ["experimental"], needs: RuleNeeds.RenderedFacts)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        long budget = NameSupport.VisitBudget;
        foreach (AxNode node in context.Tree.Nodes)
        {
            if (budget < 0)
            {
                break;
            }

            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (node.Kind != AxNodeKind.Element || node.AriaRole is not { } role || !s_roles.Contains(role.Name) || !context.IsExposed(node.DomNode)
                || node.Name.Source is not (AxTextSource.AriaLabel or AxTextSource.LabelledBy) || NameSupport.IsEmptyText(node.Name.Value))
            {
                continue;
            }

            string? label;
            string[] used = [];
            if (context.Facts is { } facts)
            {
                label = LaidOutText(facts, node, ref budget);
                used = ["layout text", "text boxes"];
            }
            else if (HasLayoutHazard(context, node, ref budget))
            {
                reporter.NeedsRender(node.DomNode, "Text inside the control may be hidden, moved or clipped by style, so what is visible depends on layout.", MissingFacts);
                continue;
            }
            else
            {
                var sb = new StringBuilder();
                foreach (AxNode t in NameSupport.TextNodes(node, ref budget))
                {
                    sb.Append(t.Name.Value).Append(' ');
                }

                label = sb.ToString();
            }

            string visible = Normalize(label);
            if (visible.Length == 0)
            {
                continue;
            }

            // A single character is read as a symbol (an X for close) and may be named by what it stands for; ACT 2ee8b8 passes it.
            if (visible.Length == 1 || Normalize(node.Name.Value).Contains(visible, StringComparison.Ordinal))
            {
                reporter.Passed(node.DomNode, factsUsed: used);
            }
            else
            {
                reporter.Failed(node.DomNode, "The accessible name does not contain the visible text.",
                    [new Finding(node.Name.Attribute, node.Name.Value, "The name."), new Finding(null, label.Trim(), "The visible text.")], null, used);
            }
        }
    }

    private static string LaidOutText(Rendered.RenderedFacts facts, AxNode node, ref long budget)
    {
        var sb = new StringBuilder();
        foreach (AxNode t in NameSupport.TextNodes(node, ref budget))
        {
            if (facts.TryGet(t.DomNode, out Rendered.NodeFacts nf) && nf.HasLayout)
            {
                bool any = false;
                foreach (Rendered.TextBox box in nf.Layout.TextBoxes)
                {
                    any |= !box.Bounds.IsEmpty;
                }

                if (any)
                {
                    sb.Append(nf.Layout.LayoutText ?? t.Name.Value).Append(' ');
                }
            }
        }

        return sb.ToString();
    }

    // Lower-cases letters and digits, drops everything else (punctuation, symbols, spacing) and joins words with one space.
    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool gap = false;
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (gap && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                gap = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                gap = true;
            }
        }

        return sb.ToString();
    }

    // Styles that can hide, move, clip or re-draw text without removing it from the tree, on the control and below it.
    private static bool HasLayoutHazard(CheckContext c, AxNode control, ref long budget)
    {
        var stack = new Stack<AxNode>();
        stack.Push(control);
        while (stack.Count > 0 && --budget >= 0)
        {
            AxNode n = stack.Pop();
            if (n.Kind == AxNodeKind.Element && n.DomNode.NamespaceURI == NameSupport.Html)
            {
                ComputedStyle s = c.Styles.GetStyle(n.DomNode);
                if (s.GetPropertyValue("opacity") == "0" || s.GetPropertyValue("font-size") == "0px"
                    || s.GetPropertyValue("clip-path") is not ("none" or "")
                    || s.GetPropertyValue("clip") is not ("auto" or "")
                    || s.GetPropertyValue("text-indent") is not ("0px" or "0" or "")
                    || (s.GetPropertyValue("position") is "absolute" or "fixed" && (s.GetPropertyValue("left").StartsWith('-') || s.GetPropertyValue("top").StartsWith('-')))
                    || s.GetPropertyValue("overflow-x") is not ("visible" or "")
                    || s.GetPropertyValue("overflow-y") is not ("visible" or "")
                    || s.GetPropertyValue("transform") is not ("none" or "")
                    || s.GetPropertyValue("font-family").Contains("icon", StringComparison.OrdinalIgnoreCase)
                    || s.GetPropertyValue("font-family").Contains("symbol", StringComparison.OrdinalIgnoreCase)
                    )
                {
                    return true;
                }
            }

            foreach (AxNode ch in n.Children)
            {
                stack.Push(ch);
            }
        }

        return false;
    }
}
