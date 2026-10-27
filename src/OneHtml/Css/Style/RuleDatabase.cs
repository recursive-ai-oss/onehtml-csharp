using System;
using RecursiveAI.OneHtml.Selectors;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>A complex selector of a rule, as stored in the rule index.</summary>
internal readonly record struct RuleSel(StyleRule Rule, int Sel, Specificity Spec);

/// <summary>
/// A compound of a selector that reads keyboard focus, and whether what follows it in the selector reaches below or beside the element it matches.
/// <paramref name="MaxDepth"/> bounds how many levels below the element (or below its later siblings) the rest of the selector can reach:
/// the number of child combinators when no descendant combinator follows, otherwise unbounded.
/// </summary>
internal readonly record struct FocusAnchor(ComplexSelector Selector, Compound Compound, bool ReachesBelow, bool ReachesBeside, bool Within, bool ViaHas = false, bool HasSideways = false, int MaxDepth = int.MaxValue);

/// <summary>Rules bucketed by the id, class or type of their rightmost compound (the usual right-to-left cascade index).</summary>
internal sealed class RuleIndex
{
    private readonly Dictionary<string, List<RuleSel>> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RuleSel>> _byClass = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RuleSel>> _byTag = new(StringComparer.Ordinal);
    private readonly List<RuleSel> _generic = [];
    private readonly bool _quirks;

    public RuleIndex(bool quirks) => _quirks = quirks;

    public int Count { get; private set; }

    public void Add(RuleSel rs, SelectorKey key)
    {
        Count++;
        switch (key.Kind)
        {
            case SelectorKeyKind.Id:
                Bucket(_byId, _quirks ? Lower(key.Name) : key.Name).Add(rs);
                break;
            case SelectorKeyKind.Class:
                Bucket(_byClass, _quirks ? Lower(key.Name) : key.Name).Add(rs);
                break;
            case SelectorKeyKind.Tag:
                Bucket(_byTag, key.Name).Add(rs);
                break;
            default:
                _generic.Add(rs);
                break;
        }
    }

    private static string Lower(string s) => s.AsciiLower();

    private static List<RuleSel> Bucket(Dictionary<string, List<RuleSel>> d, string k)
    {
        if (!d.TryGetValue(k, out List<RuleSel>? l))
        {
            d[k] = l = [];
        }

        return l;
    }

    /// <summary>Adds the candidate selectors for an element: those whose key it could satisfy. The caller still matches each.</summary>
    public void Collect(Node element, List<RuleSel> into)
    {
        if (_byId.Count > 0)
        {
            string? id = element.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && _byId.TryGetValue(_quirks ? Lower(id) : id, out List<RuleSel>? l))
            {
                into.AddRange(l);
            }
        }

        if (_byClass.Count > 0)
        {
            string? cls = element.GetAttribute("class");
            if (!string.IsNullOrEmpty(cls))
            {
                int i = 0;
                while (i < cls.Length)
                {
                    while (i < cls.Length && IsSpace(cls[i]))
                    {
                        i++;
                    }

                    int s = i;
                    while (i < cls.Length && !IsSpace(cls[i]))
                    {
                        i++;
                    }

                    if (i > s)
                    {
                        string token = cls.Substring(s, i - s);
                        if (_byClass.TryGetValue(_quirks ? Lower(token) : token, out List<RuleSel>? l2))
                        {
                            into.AddRange(l2);
                        }
                    }
                }
            }
        }

        if (_byTag.Count > 0 && _byTag.TryGetValue(element.LocalName.AsciiLower(), out List<RuleSel>? l3))
        {
            into.AddRange(l3);
        }

        into.AddRange(_generic);
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';
}

/// <summary>
/// The style rules of a document under one <see cref="StyleContext"/> media state: the user-agent, user and author sheets
/// (with imports, media, supports, layers and nesting resolved) flattened in cascade order and indexed for matching.
/// </summary>
internal sealed class RuleDatabase
{
    public readonly List<StyleRule> Rules = [];
    public readonly List<StyleSheetInfo> Sheets = [];
    public readonly List<StyleDiagnostic> Diagnostics = [];
    public readonly LayerTree Layers = new();
    public RuleIndex Index = null!;
    public RuleIndex DynamicIndex = null!;

    /// <summary>
    /// The compounds of dynamic selectors that read keyboard focus (<c>:focus</c>, <c>:focus-visible</c>, <c>:focus-within</c>), with how far
    /// a change at one of them reaches along the selector's combinators. A compound that reads focus inside <c>:has()</c> is marked: it can change
    /// only at the focused element's ancestors, and at the earlier siblings of those when the <c>:has()</c> argument uses a sibling combinator.
    /// </summary>
    public List<FocusAnchor>? FocusAnchors;

    /// <summary>
    /// The focus anchors bucketed by the id, class or type their compound requires, so that testing an element touches only the anchors it
    /// could satisfy. Each entry's <c>Sel</c> is the position in <see cref="FocusAnchors"/>; the rule is unused.
    /// </summary>
    public RuleIndex? FocusAnchorIndex;

    /// <summary>True when some focus anchor reads focus inside a <c>:has()</c> that uses a sibling combinator.</summary>
    public bool HasSidewaysFocus;

    /// <summary>True when a rule styling <c>::before</c>, <c>::after</c> or <c>::marker</c> reads an interaction state (<c>:hover</c>, <c>:focus</c>, …).</summary>
    public bool PseudoElementDynamic;

    /// <summary>Rules whose selector ends in <c>::before</c>, <c>::after</c> or <c>::marker</c>, by pseudo-element name; the index keys come from the originating element's compound.</summary>
    public readonly Dictionary<string, RuleIndex> PseudoIndex = new(StringComparer.Ordinal);
    public bool Quirks;

    /// <summary>Every rule identified by what it says, for comparing two gathers.</summary>
    public string[] Signatures = [];

    public string LayerSignature = string.Empty;

    private RefIndex? _ref;

    /// <summary>
    /// The rules whose selectors read one of the given attribute names, class names or ids (lower-cased), or whose
    /// pseudo-classes read attributes that selector text does not name (<c>:checked</c>, <c>:disabled</c>, <c>:lang()</c>, …)
    /// when any attribute changed. Used to find what an attribute change can reach.
    /// </summary>
    public List<StyleRule> RulesReading(HashSet<string> attributes, HashSet<string> classes, HashSet<string> ids, bool anyChange)
    {
        RefIndex r;
        lock (this)
        {
            r = _ref ??= new RefIndex(this);
        }

        var result = new HashSet<StyleRule>();
        Gather(r.ByAttribute, attributes, result);
        Gather(r.ByClass, classes, result);
        Gather(r.ById, ids, result);
        if (anyChange && attributes.Count > 0)
        {
            foreach (StyleRule rule in r.Stateful)
            {
                result.Add(rule);
            }
        }

        return [.. result];

        static void Gather(Dictionary<string, List<StyleRule>> map, HashSet<string> keys, HashSet<StyleRule> into)
        {
            foreach (string k in keys)
            {
                if (map.TryGetValue(k, out List<StyleRule>? l))
                {
                    into.UnionWith(l);
                }
            }
        }
    }

    private sealed class RefIndex
    {
        public readonly Dictionary<string, List<StyleRule>> ByAttribute = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<StyleRule>> ByClass = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<StyleRule>> ById = new(StringComparer.Ordinal);
        public readonly List<StyleRule> Stateful = [];

        public RefIndex(RuleDatabase db)
        {
            foreach (StyleRule r in db.Rules)
            {
                var a = new HashSet<string>(StringComparer.Ordinal);
                var c = new HashSet<string>(StringComparer.Ordinal);
                var i = new HashSet<string>(StringComparer.Ordinal);
                r.Selectors!.CollectDependencies(a, c, i);
                Add(ByAttribute, a, r);
                Add(ByClass, c, r);
                Add(ById, i, r);
                if ((r.Selectors.Dependencies & (SelectorDependencies.FormState | SelectorDependencies.LangOrDir)) != 0)
                {
                    Stateful.Add(r);
                }
            }
        }

        private static void Add(Dictionary<string, List<StyleRule>> map, HashSet<string> names, StyleRule r)
        {
            foreach (string n in names)
            {
                string k = n.AsciiLower();
                if (!map.TryGetValue(k, out List<StyleRule>? l))
                {
                    map[k] = l = [];
                }

                l.Add(r);
            }
        }
    }
}

/// <summary>Gathers and flattens the style sources of a document (CSS Cascade 5 §2, §6; CSS Nesting 1; HTML 4.2.6 and 4.6.7).</summary>
internal sealed class RuleGatherer
{
    private const int MaxDiagnostics = 256;

    private readonly Document _doc;
    private readonly StyleContext _ctx;
    private readonly StyleSourceOptions _opts;
    private readonly CancellationToken _ct;
    private readonly RuleDatabase _db = new();
    private readonly CssParseOptions _parse;
    private readonly Dictionary<string, Stylesheet> _fetched = new(StringComparer.Ordinal);
    private readonly SupportsOptions _supports;
    private string? _baseUrl;
    private int _sheetCount;
    private int _order;
    private int _ruleCount;
    private bool _limitReported;

    public RuleGatherer(Document doc, StyleContext ctx, StyleSourceOptions opts, CancellationToken ct)
    {
        _doc = doc;
        _ctx = ctx;
        _opts = opts;
        _ct = ct;
        _parse = opts.ParseOptions ?? CssParseOptions.Default;
        _supports = new SupportsOptions { SelectorSupported = SelectorSupported };
    }

    // selector() in @supports (CSS Conditional 4 §2.3; CSS Nesting 1 §3): "&" is valid; a leading combinator is not.
    private bool SelectorSupported(string text)
    {
        var sb = new StringBuilder();
        AppendReplacingAmpersand(sb, text, ":scope");
        string t = sb.ToString();
        return SelectorList.IsValidComplexSelector(t);
    }

    private sealed class SheetCtx
    {
        public StyleOrigin Origin;
        public Stylesheet Sheet = null!;
        public Node Owner;
        public string? Url;
        public int Depth;
        public SheetCtx? Parent;
        public SelectorParseOptions? Selectors;
    }

    private enum FrameKind : byte
    {
        Sheet,
        Group,
        Body,
    }

    private sealed class Frame
    {
        public FrameKind Kind;
        public CssNodeList.Enumerator Children;
        public SheetCtx Sheet = null!;
        public int Layer;

        /// <summary>The enclosing style rule's selector (canonical text) while inside a style rule, else null.</summary>
        public string? ParentText;
        public SelectorList? ParentList;
        public CssNode ParentRule;
        public List<CssNode>? Run;
        public bool SawNonImport;
    }

    public RuleDatabase Build()
    {
        _db.Quirks = _doc.Mode == QuirksMode.Quirks;
        _baseUrl = ResolveBase();
        var frames = new Stack<Frame>();

        if (_opts.IncludeUserAgentSheet)
        {
            Run(frames, UserAgentSheets.Standard, StyleOrigin.UserAgent, default, null, null);
            if (_db.Quirks)
            {
                Run(frames, UserAgentSheets.Quirks, StyleOrigin.UserAgent, default, null, null);
            }
        }

        if (_opts.UserSheets is { } user)
        {
            foreach (Stylesheet s in user)
            {
                Run(frames, s, StyleOrigin.User, default, s.Location, null);
            }
        }

        if (!_doc.DocumentElement.IsNull)
        {
            GatherAuthor(frames);
        }

        _db.Layers.Rank();
        BuildIndexes();
        return _db;
    }

    private string? ResolveBase()
    {
        string? documentUrl = _opts.BaseUrl;
        foreach (Node e in _doc.Root.DescendantElements)
        {
            if (e.Namespace == ElementNamespace.Html && e.LocalName == "base" && e.GetAttribute("href") is { } href)
            {
                return ResolveUrl(documentUrl, href) ?? documentUrl;
            }
        }

        return documentUrl;
    }

    private static string? ResolveUrl(string? baseUrl, string href)
    {
        href = href.Trim();
        if (href.Length == 0)
        {
            return null;
        }

        try
        {
            Uri? result;
            if (baseUrl is not null && Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? b))
            {
                if (!Uri.TryCreate(b, href, out result))
                {
                    return null;
                }
            }
            else if (!Uri.TryCreate(href, UriKind.Absolute, out result))
            {
                return null;
            }

            return result.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Fragment, UriFormat.UriEscaped);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private void Report(StyleDiagnosticKind kind, string message, string? url = null)
    {
        if (_db.Diagnostics.Count < MaxDiagnostics)
        {
            _db.Diagnostics.Add(new StyleDiagnostic(kind, message, url));
        }
    }

    private bool CheckSheetLimit()
    {
        if (_sheetCount >= _opts.MaxSheets)
        {
            if (!_limitReported)
            {
                _limitReported = true;
                Report(StyleDiagnosticKind.LimitExceeded, "The number of style sheets exceeds the limit; the rest are ignored.");
            }

            return false;
        }

        return true;
    }

    // ---- author sources: style and link elements in tree order ----

    private void GatherAuthor(Stack<Frame> frames)
    {
        foreach (Node e in _doc.Root.DescendantElements)
        {
            _ct.ThrowIfCancellationRequested();
            if (e.Namespace == ElementNamespace.Html || e.Namespace == ElementNamespace.Svg)
            {
                string name = e.LocalName;
                if (name == "style")
                {
                    GatherStyleElement(frames, e);
                }
                else if (name == "link" && e.Namespace == ElementNamespace.Html)
                {
                    GatherLinkElement(frames, e);
                }
            }
        }
    }

    private bool ElementApplies(Node e, bool isLink)
    {
        if (e.HasAttribute("disabled"))
        {
            return false;
        }

        if (e.GetAttribute("type") is { } type && type.Length > 0 && !string.Equals(type.Trim(), "text/css", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (e.GetAttribute("media") is { } media && !MediaQueryList.Parse(media).Matches(_ctx.Media))
        {
            return false;
        }

        _ = isLink;
        return true;
    }

    private void GatherStyleElement(Stack<Frame> frames, Node e)
    {
        if (!ElementApplies(e, false) || !CheckSheetLimit())
        {
            return;
        }

        string text = e.TextContent ?? string.Empty;
        Stylesheet sheet;
        try
        {
            sheet = Stylesheet.Parse(text, null, _parse, _ct);
        }
        catch (CssLimitExceededException ex)
        {
            Report(StyleDiagnosticKind.LimitExceeded, "A style element exceeds a CSS parse limit: " + ex.Message);
            return;
        }

        Run(frames, sheet, StyleOrigin.Author, e, null, null);
    }

    private void GatherLinkElement(Stack<Frame> frames, Node e)
    {
        string? rel = e.GetAttribute("rel");
        if (rel is null || !HasToken(rel, "stylesheet") || HasToken(rel, "alternate") || !ElementApplies(e, true))
        {
            return;
        }

        string? href = e.GetAttribute("href");
        if (href is null || href.Trim().Length == 0)
        {
            return;
        }

        string? url = ResolveUrl(_baseUrl, href);
        if (url is null)
        {
            Report(StyleDiagnosticKind.MissingStylesheet, "The href \"" + href + "\" cannot be resolved to an absolute URL.");
            return;
        }

        if (!CheckSheetLimit())
        {
            return;
        }

        Stylesheet? sheet = Fetch(url, e.GetAttribute("charset"));
        if (sheet is null)
        {
            return;
        }

        Run(frames, sheet, StyleOrigin.Author, e, url, null);
    }

    private static bool HasToken(string list, string token)
    {
        int i = 0;
        while (i < list.Length)
        {
            while (i < list.Length && list[i] is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                i++;
            }

            int s = i;
            while (i < list.Length && !(list[i] is ' ' or '\t' or '\n' or '\r' or '\f'))
            {
                i++;
            }

            if (i - s == token.Length && string.Compare(list, s, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private Stylesheet? Fetch(string url, string? charsetHint)
    {
        if (_fetched.TryGetValue(url, out Stylesheet? cached))
        {
            return cached;
        }

        StylesheetResource? res = _opts.Resolver?.Invoke(url);
        if (res is null)
        {
            Report(StyleDiagnosticKind.MissingStylesheet, "No style sheet was supplied for " + url + ".", url);
            return null;
        }

        try
        {
            Stylesheet sheet = res.Text is not null
                ? Stylesheet.Parse(res.Text, url, _parse, _ct)
                : Stylesheet.Parse(res.Bytes!, WhatwgCssEncodingProvider.Instance, res.ProtocolEncodingLabel, charsetHint, url, _parse, _ct);
            _fetched[url] = sheet;
            return sheet;
        }
        catch (CssLimitExceededException ex)
        {
            Report(StyleDiagnosticKind.LimitExceeded, "A style sheet exceeds a CSS parse limit: " + ex.Message, url);
            return null;
        }
    }

    // ---- flattening ----

    private void Run(Stack<Frame> frames, Stylesheet sheet, StyleOrigin origin, Node owner, string? url, SheetCtx? parent)
    {
        int layer = LayerTree.RootOf(origin);
        PushSheet(frames, sheet, origin, owner, url, parent, layer);
        Drain(frames);
    }

    private void PushSheet(Stack<Frame> frames, Stylesheet sheet, StyleOrigin origin, Node owner, string? url, SheetCtx? parent, int layer)
    {
        _sheetCount++;
        _db.Sheets.Add(new StyleSheetInfo(sheet, origin, owner, url, parent is not null));
        var sc = new SheetCtx { Origin = origin, Sheet = sheet, Owner = owner, Url = url, Parent = parent, Depth = parent is null ? 0 : parent.Depth + 1 };
        Dictionary<string, string>? ns = null;
        foreach (CssNode n in sheet.Rules)
        {
            if (n.Kind == CssNodeKind.AtRule && n.AtRuleKind == AtRuleKind.Namespace && n.AsNamespace() is { } nr && nr.Uri is not null)
            {
                (ns ??= new Dictionary<string, string>(StringComparer.Ordinal))[nr.Prefix ?? string.Empty] = nr.Uri;
            }
        }

        if (ns is not null)
        {
            sc.Selectors = new SelectorParseOptions { Namespaces = ns };
        }

        frames.Push(new Frame { Kind = FrameKind.Sheet, Children = sheet.Rules.GetEnumerator(), Sheet = sc, Layer = layer });
    }

    private void Drain(Stack<Frame> frames)
    {
        while (frames.Count > 0)
        {
            Frame f = frames.Peek();
            if (!f.Children.MoveNext())
            {
                Flush(f);
                frames.Pop();
                continue;
            }

            if ((++_ruleCount & 1023) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            CssNode n = f.Children.Current;
            switch (n.Kind)
            {
                case CssNodeKind.Declaration:
                    if (f.ParentText is not null)
                    {
                        (f.Run ??= []).Add(n);
                    }

                    break;
                case CssNodeKind.QualifiedRule:
                    f.SawNonImport = true;
                    Flush(f);
                    StyleRuleNode(frames, f, n);
                    break;
                case CssNodeKind.AtRule:
                    AtRule(frames, f, n);
                    break;
            }
        }
    }

    private void Flush(Frame f)
    {
        if (f.Run is { Count: > 0 } run && f.ParentList is not null)
        {
            AddRule(f.Sheet, f.ParentRule, f.ParentList, f.ParentText!, f.Layer, run);
        }

        f.Run = null;
    }

    private void AddRule(SheetCtx sc, CssNode ruleNode, SelectorList list, string text, int layer, List<CssNode> decls)
    {
        if (_db.Rules.Count >= _opts.MaxRules)
        {
            if (!_limitReported)
            {
                _limitReported = true;
                Report(StyleDiagnosticKind.LimitExceeded, "The number of style rules exceeds the limit; the rest are ignored.");
            }

            return;
        }

        var rule = new StyleRule
        {
            Id = _db.Rules.Count,
            Kind = DeclarationKind.Sheet,
            Origin = sc.Origin,
            Sheet = sc.Sheet,
            Owner = sc.Owner,
            Url = sc.Url,
            RuleNode = ruleNode,
            DeclNodes = decls,
            Selectors = list,
            SelectorText = text,
            Layer = layer,
            Order = _order++,
            Layers = _db.Layers,
            Dynamic = IsDynamic(list),
            Validator = _opts.DeclarationValidator,
        };
        _db.Rules.Add(rule);
    }

    private static bool IsDynamic(SelectorList list) =>
        (list.Dependencies & (SelectorDependencies.Hover | SelectorDependencies.Active | SelectorDependencies.Focus | SelectorDependencies.FocusWithin | SelectorDependencies.Target)) != 0;

    private void StyleRuleNode(Stack<Frame> frames, Frame f, CssNode n)
    {
        if (!TryParseSelectors(f, n, out SelectorList? list))
        {
            return;
        }

        string text = list!.ToString();
        var body = new Frame
        {
            Kind = FrameKind.Body,
            Children = n.Children.GetEnumerator(),
            Sheet = f.Sheet,
            Layer = f.Layer,
            ParentText = text,
            ParentList = list,
            ParentRule = n,
        };
        frames.Push(body);
    }

    private bool TryParseSelectors(Frame f, CssNode n, out SelectorList? list)
    {
        list = null;
        ComponentValueList prelude = n.Prelude.Trimmed();
        try
        {
            if (f.ParentText is null && prelude.Raw.IndexOf('&') < 0)
            {
                list = SelectorList.Parse(prelude, f.Sheet.Selectors);
                return true;
            }

            string? text = NestSelectors(prelude, f.ParentText);
            if (text is null)
            {
                Report(StyleDiagnosticKind.InvalidSelector, "A nested selector is too long.");
                return false;
            }

            list = SelectorList.Parse(text, f.Sheet.Selectors);
            return true;
        }
        catch (SelectorSyntaxException)
        {
            Report(StyleDiagnosticKind.InvalidSelector, "The rule's selector \"" + Truncate(prelude.Raw.ToString()) + "\" is invalid, so the rule is dropped.");
            return false;
        }
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..80] + "...";

    /// <summary>
    /// Resolves a nested selector list against its parent (CSS Nesting 1 §3): every <c>&amp;</c> becomes <c>:is(parent)</c>
    /// (<c>:scope</c> at the top level), and an item without <c>&amp;</c> is relative to the parent, a descendant unless it
    /// starts with a combinator. Specificity follows because <c>:is()</c> takes its most specific argument.
    /// </summary>
    private static string? NestSelectors(ComponentValueList prelude, string? parent)
    {
        string text = prelude.Raw.ToString();
        string amp = parent is null ? ":scope" : ":is(" + parent + ")";
        var sb = new StringBuilder();
        foreach (string item in SplitTopLevelCommas(text))
        {
            string t = item.Trim();
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            bool hasAmp = ContainsAmpersand(t);
            if (parent is not null && !hasAmp)
            {
                if (t.Length > 0 && t[0] is '>' or '+' or '~')
                {
                    sb.Append(amp).Append(' ');
                }
                else
                {
                    sb.Append(amp).Append(' ');
                }

                sb.Append(t);
            }
            else
            {
                AppendReplacingAmpersand(sb, t, amp);
            }

            if (sb.Length > (1 << 16))
            {
                return null;
            }
        }

        return sb.ToString();
    }

    private static bool ContainsAmpersand(string t)
    {
        char quote = '\0';
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\\')
            {
                i++;
            }
            else if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '&')
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendReplacingAmpersand(StringBuilder sb, string t, string amp)
    {
        char quote = '\0';
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\\' && i + 1 < t.Length)
            {
                sb.Append(c).Append(t[++i]);
            }
            else if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                sb.Append(c);
            }
            else if (c == '&')
            {
                sb.Append(amp);
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    private static IEnumerable<string> SplitTopLevelCommas(string text)
    {
        int depth = 0;
        char quote = '\0';
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
            }
            else if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    private void AtRule(Stack<Frame> frames, Frame f, CssNode n)
    {
        switch (n.AtRuleKind)
        {
            case AtRuleKind.Charset:
            case AtRuleKind.Namespace:
                break;
            case AtRuleKind.Import:
                if (f.Kind == FrameKind.Sheet && !f.SawNonImport)
                {
                    Import(frames, f, n);
                }

                break;
            case AtRuleKind.Layer:
                LayerRule(frames, f, n);
                break;
            case AtRuleKind.Media:
                f.SawNonImport = true;
                if (n.HasBlock && MediaQueryList.Parse(n.Prelude.Trimmed()).Matches(_ctx.Media))
                {
                    PushGroup(frames, f, n, f.Layer);
                }

                break;
            case AtRuleKind.Supports:
                f.SawNonImport = true;
                if (n.HasBlock && SupportsCondition.Parse(n.Prelude.Trimmed(), _supports).Evaluate())
                {
                    PushGroup(frames, f, n, f.Layer);
                }

                break;
            case AtRuleKind.Container:
                f.SawNonImport = true;
                Report(StyleDiagnosticKind.UnsupportedRule, "@container rules need layout and are skipped.");
                break;
            default:
                f.SawNonImport = true;
                if (string.Equals(n.Name, "scope", StringComparison.OrdinalIgnoreCase))
                {
                    Report(StyleDiagnosticKind.UnsupportedRule, "@scope rules are not evaluated and are skipped.");
                }

                break;
        }
    }

    private void PushGroup(Stack<Frame> frames, Frame f, CssNode n, int layer)
    {
        Flush(f);
        frames.Push(new Frame
        {
            Kind = FrameKind.Group,
            Children = n.Children.GetEnumerator(),
            Sheet = f.Sheet,
            Layer = layer,
            ParentText = f.ParentText,
            ParentList = f.ParentList,
            ParentRule = f.ParentRule,
        });
    }

    private void LayerRule(Stack<Frame> frames, Frame f, CssNode n)
    {
        LayerRule lr = n.AsLayer()!.Value;
        IReadOnlyList<string> names = lr.Names;
        if (lr.IsStatement)
        {
            // A layer statement is not a valid nested rule (CSS Nesting 1 §3.2: only rules that contain style rules nest).
            if (names.Count == 0 || f.ParentText is not null)
            {
                return;
            }

            // Statements may precede @import; any other position ends the import prologue.
            foreach (string name in names)
            {
                if (_db.Layers.Resolve(f.Layer, name, _opts.MaxLayers) < 0)
                {
                    Report(StyleDiagnosticKind.LimitExceeded, "A layer name is invalid or the layer limit was reached.");
                }
            }

            return;
        }

        f.SawNonImport = true;
        if (!n.HasBlock || names.Count > 1)
        {
            return;
        }

        int layer = names.Count == 0
            ? _db.Layers.CreateAnonymous(f.Layer, _opts.MaxLayers)
            : _db.Layers.Resolve(f.Layer, names[0], _opts.MaxLayers);
        if (layer < 0)
        {
            Report(StyleDiagnosticKind.LimitExceeded, "A layer name is invalid or the layer limit was reached.");
            return;
        }

        PushGroup(frames, f, n, layer);
    }

    private void Import(Stack<Frame> frames, Frame f, CssNode n)
    {
        ImportRule ir = n.AsImport()!.Value;
        if (ir.Url is null)
        {
            return;
        }

        if (!ir.SupportsRange.IsEmpty)
        {
            string cond = n.Tree.Text.Substring(ir.SupportsRange.Start, ir.SupportsRange.Length).Trim();
            bool functionLike = cond.StartsWith('(') || cond.StartsWith("not", StringComparison.OrdinalIgnoreCase) || cond.StartsWith("selector(", StringComparison.OrdinalIgnoreCase);
            if (!SupportsCondition.Parse(functionLike ? cond : "(" + cond + ")", _supports).Evaluate())
            {
                return;
            }
        }

        if (!ir.MediaRange.IsEmpty && !MediaQueryList.Parse(ir.MediaText).Matches(_ctx.Media))
        {
            return;
        }

        string? baseUrl = f.Sheet.Url ?? _baseUrl;
        string? url = ResolveUrl(baseUrl, ir.Url);
        if (url is null)
        {
            Report(StyleDiagnosticKind.MissingStylesheet, "The @import URL \"" + Truncate(ir.Url) + "\" cannot be resolved to an absolute URL.");
            return;
        }

        if (f.Sheet.Depth + 1 > _opts.MaxImportDepth)
        {
            Report(StyleDiagnosticKind.ImportDepthExceeded, "An @import chain is deeper than " + _opts.MaxImportDepth.ToString(CultureInfo.InvariantCulture) + ".", url);
            return;
        }

        for (SheetCtx? s = f.Sheet; s is not null; s = s.Parent)
        {
            if (string.Equals(s.Url, url, StringComparison.Ordinal))
            {
                Report(StyleDiagnosticKind.ImportCycle, "@import of " + url + " would import a sheet that is already being imported.", url);
                return;
            }
        }

        if (!CheckSheetLimit())
        {
            return;
        }

        int layer = f.Layer;
        if (ir.HasLayer)
        {
            layer = ir.LayerName is null
                ? _db.Layers.CreateAnonymous(f.Layer, _opts.MaxLayers)
                : _db.Layers.Resolve(f.Layer, ir.LayerName.Trim(), _opts.MaxLayers);
            if (layer < 0)
            {
                Report(StyleDiagnosticKind.LimitExceeded, "A layer name is invalid or the layer limit was reached.");
                return;
            }
        }

        // A layered import establishes its layer even when the sheet cannot be fetched (CSS Cascade 5 §6.4.1).
        Stylesheet? sheet = Fetch(url, null);
        if (sheet is null)
        {
            return;
        }

        PushSheet(frames, sheet, f.Sheet.Origin, f.Sheet.Owner, url, f.Sheet, layer);
    }

    // ---- index ----

    private void BuildIndexes()
    {
        var all = new RuleIndex(_db.Quirks);
        var dynamic = new RuleIndex(_db.Quirks);
        List<FocusAnchor>? focusAnchors = [];
        var sigs = new string[_db.Rules.Count];
        foreach (StyleRule r in _db.Rules)
        {
            SelectorList list = r.Selectors!;
            for (int i = 0; i < list.Count; i++)
            {
                if (list.HasPseudoElement(i))
                {
                    // Only a plain ::before, ::after or ::marker as the last simple selector generates a style here.
                    Simple[] parts = list.Items[i].Compounds[^1].Parts;
                    if (parts.Length > 0 && parts[^1].Kind == SimpleKind.PseudoElement && parts[^1].Name is "before" or "after" or "marker")
                    {
                        string pn = parts[^1].Name!;
                        if (!_db.PseudoIndex.TryGetValue(pn, out RuleIndex? pi))
                        {
                            _db.PseudoIndex[pn] = pi = new RuleIndex(_db.Quirks);
                        }

                        pi.Add(new RuleSel(r, i, list.GetSpecificity(i)), list.GetKey(i));
                        _db.PseudoElementDynamic |= r.Dynamic;
                    }

                    continue;
                }

                var rs = new RuleSel(r, i, list.GetSpecificity(i));
                SelectorKey key = list.GetKey(i);
                all.Add(rs, key);
                if (r.Dynamic)
                {
                    dynamic.Add(rs, key);
                    foreach (ComplexSelector cs in new[] { list.Items[i] })
                    {
                        if ((cs.Deps & (SelectorDependencies.Focus | SelectorDependencies.FocusWithin)) == 0)
                        {
                            continue;
                        }

                        for (int j = 0; j < cs.Compounds.Length; j++)
                        {
                            bool reads = false, within = false, viaHas = false, sideways = false;
                            foreach (Simple part in cs.Compounds[j].Parts)
                            {
                                if ((part.Deps & (SelectorDependencies.Focus | SelectorDependencies.FocusWithin)) != 0)
                                {
                                    reads = true;
                                    within |= (part.Deps & SelectorDependencies.FocusWithin) != 0;
                                    // Focus inside a :has() is read from the ancestors (and, for a sibling combinator inside the
                                    // :has(), from the earlier siblings of the ancestors) of the focused element.
                                    if ((part.Deps & SelectorDependencies.Has) != 0)
                                    {
                                        viaHas = true;
                                        sideways |= (part.Deps & SelectorDependencies.SiblingCombinator) != 0;
                                    }
                                }
                            }

                            if (reads)
                            {
                                bool below = false, beside = false, deep = false;
                                int children = 0;
                                for (int k = j; k < cs.Combs.Length; k++)
                                {
                                    below |= cs.Combs[k] is Combinator.Descendant or Combinator.Child;
                                    beside |= cs.Combs[k] is Combinator.NextSibling or Combinator.SubsequentSibling;
                                    deep |= cs.Combs[k] is not (Combinator.Child or Combinator.NextSibling or Combinator.SubsequentSibling);
                                    children += cs.Combs[k] == Combinator.Child ? 1 : 0;
                                }

                                focusAnchors!.Add(new FocusAnchor(cs, cs.Compounds[j], below, beside, within, viaHas, sideways, deep ? int.MaxValue : children));
                            }
                        }
                    }
                }
            }

            sigs[r.Id] = Signature(r);
        }

        _db.Index = all;
        _db.DynamicIndex = dynamic;
        _db.FocusAnchors = focusAnchors;
        var anchorIndex = new RuleIndex(_db.Quirks);
        for (int i = 0; i < focusAnchors.Count; i++)
        {
            anchorIndex.Add(new RuleSel(null!, i, default), CompoundKey(focusAnchors[i].Compound));
        }

        _db.FocusAnchorIndex = anchorIndex;
        _db.HasSidewaysFocus = focusAnchors.Exists(a => a.HasSideways);
        _db.Signatures = sigs;
        _db.LayerSignature = _db.Layers.Signature();
    }

    // The bucket key of a compound, chosen as the parser chooses the key of a selector's rightmost compound: id, else class, else type.
    private static SelectorKey CompoundKey(Compound compound)
    {
        SelectorKey key = default;
        foreach (Simple p in compound.Parts)
        {
            if (p.Kind == SimpleKind.Id)
            {
                return new SelectorKey(SelectorKeyKind.Id, p.Name);
            }

            if (p.Kind == SimpleKind.Class && key.Kind != SelectorKeyKind.Class)
            {
                key = new SelectorKey(SelectorKeyKind.Class, p.Name);
            }
            else if (p.Kind == SimpleKind.Type && !p.IsUniversal && key.Kind == SelectorKeyKind.None)
            {
                key = new SelectorKey(SelectorKeyKind.Tag, p.LowerName);
            }
        }

        return key;
    }

    private static string Signature(StyleRule r)
    {
        var sb = new StringBuilder();
        sb.Append((int)r.Origin).Append('|').Append(r.Layer).Append('|').Append(r.SelectorText).Append('|');
        foreach (CssNode d in r.DeclNodes)
        {
            sb.Append(d.Raw).Append(';');
        }

        return sb.ToString();
    }
}
