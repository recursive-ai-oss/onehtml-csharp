using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The at-rules the cascade and the editor treat specially. Matching is ASCII case-insensitive.</summary>
public enum AtRuleKind : byte
{
    /// <summary>The node is not an at-rule.</summary>
    None = 0,

    /// <summary>An at-rule with no typed view.</summary>
    Unknown,

    /// <summary><c>@media</c>.</summary>
    Media,

    /// <summary><c>@supports</c>.</summary>
    Supports,

    /// <summary><c>@layer</c>.</summary>
    Layer,

    /// <summary><c>@import</c>.</summary>
    Import,

    /// <summary><c>@container</c>.</summary>
    Container,

    /// <summary><c>@scope</c>.</summary>
    Scope,

    /// <summary><c>@font-face</c>.</summary>
    FontFace,

    /// <summary><c>@keyframes</c> (and <c>@-webkit-keyframes</c>).</summary>
    Keyframes,

    /// <summary><c>@namespace</c>.</summary>
    Namespace,

    /// <summary><c>@page</c>.</summary>
    Page,

    /// <summary><c>@charset</c>.</summary>
    Charset,
}

internal static class CssAtRules
{
    public static AtRuleKind Classify(string name)
    {
        if (name.Equals("media", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Media;
        }

        if (name.Equals("supports", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Supports;
        }

        if (name.Equals("layer", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Layer;
        }

        if (name.Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Import;
        }

        if (name.Equals("container", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Container;
        }

        if (name.Equals("scope", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Scope;
        }

        if (name.Equals("font-face", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.FontFace;
        }

        if (name.Equals("keyframes", StringComparison.OrdinalIgnoreCase) || name.Equals("-webkit-keyframes", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Keyframes;
        }

        if (name.Equals("namespace", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Namespace;
        }

        if (name.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            return AtRuleKind.Page;
        }

        return name.Equals("charset", StringComparison.OrdinalIgnoreCase) ? AtRuleKind.Charset : AtRuleKind.Unknown;
    }

    // The string a "<string> | <url>" prelude value stands for, or null.
    public static string? UrlOrString(in ComponentValue v)
    {
        if (v.Kind == ComponentValueKind.Token)
        {
            CssToken t = v.Token;
            return t.Kind is CssTokenKind.QuotedString or CssTokenKind.Url ? t.Value : null;
        }

        if (v.IsFunctionNamed("url"))
        {
            foreach (ComponentValue c in v.Children)
            {
                if (c.IsWhitespace)
                {
                    continue;
                }

                return c.Kind == ComponentValueKind.Token && c.Token.Kind == CssTokenKind.QuotedString ? c.Token.Value : null;
            }
        }

        return null;
    }
}

/// <summary>A typed view of an <c>@media</c> rule. The query list is left as component values for the media-query parser.</summary>
public readonly struct MediaRule
{
    internal MediaRule(CssNode node) => Node = node;

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>The media query list as component values.</summary>
    public ComponentValueList Queries => Node.Prelude.Trimmed();

    /// <summary>The rules inside the block (empty for a block-less rule).</summary>
    public RuleEnumerable Rules => Node.Rules;
}

/// <summary>A typed view of an <c>@supports</c> rule.</summary>
public readonly struct SupportsRule
{
    internal SupportsRule(CssNode node) => Node = node;

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>The supports condition as component values.</summary>
    public ComponentValueList Condition => Node.Prelude.Trimmed();

    /// <summary>The rules inside the block.</summary>
    public RuleEnumerable Rules => Node.Rules;
}

/// <summary>A typed view of an <c>@layer</c> rule: a statement naming layers, or a block adding rules to one layer.</summary>
public readonly struct LayerRule
{
    internal LayerRule(CssNode node) => Node = node;

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>True for <c>@layer a, b;</c> (no block).</summary>
    public bool IsStatement => !Node.HasBlock;

    /// <summary>
    /// The layer names written in the prelude, each as its trimmed source text (a dotted name such as <c>a.b</c> is one
    /// entry). A block rule has zero entries (anonymous layer) or one; a statement has one or more.
    /// </summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            var names = new List<string>();
            var current = new System.Text.StringBuilder();
            foreach (ComponentValue v in Node.Prelude.Trimmed())
            {
                if (v.Kind == ComponentValueKind.Token && v.Token.Kind == CssTokenKind.Comma)
                {
                    Flush(names, current);
                }
                else if (!v.IsWhitespace)
                {
                    current.Append(v.Raw);
                }
            }

            Flush(names, current);
            return names;
        }
    }

    private static void Flush(List<string> names, System.Text.StringBuilder current)
    {
        if (current.Length > 0)
        {
            names.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>The rules inside the block.</summary>
    public RuleEnumerable Rules => Node.Rules;
}

/// <summary>A typed view of an <c>@import</c> rule (CSS Cascade 5/6 §2).</summary>
public readonly struct ImportRule
{
    private readonly string? _url;
    private readonly bool _hasLayer;
    private readonly string? _layerName;
    private readonly CssRange _supports;
    private readonly CssRange _media;

    internal ImportRule(CssNode node)
    {
        Node = node;
        _url = null;
        _hasLayer = false;
        _layerName = null;
        _supports = default;
        _media = default;
        bool first = true;
        int mediaStart = -1;
        int mediaEnd = -1;
        foreach (ComponentValue v in node.Prelude.Trimmed())
        {
            if (v.IsWhitespace)
            {
                continue;
            }

            if (first)
            {
                first = false;
                _url = CssAtRules.UrlOrString(v);
                continue;
            }

            if (mediaStart < 0)
            {
                if (v.IsIdent("layer"))
                {
                    _hasLayer = true;
                    continue;
                }

                if (v.IsFunctionNamed("layer"))
                {
                    _hasLayer = true;
                    _layerName = v.Children.Trimmed().Raw.ToString();
                    continue;
                }

                if (v.IsFunctionNamed("supports"))
                {
                    _supports = v.Children.Trimmed().Range;
                    continue;
                }
            }

            CssRange r = v.Range;
            if (mediaStart < 0)
            {
                mediaStart = r.Start;
            }

            mediaEnd = r.End;
        }

        if (mediaStart >= 0)
        {
            _media = new CssRange(mediaStart, mediaEnd);
        }
    }

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>The imported URL as written (a string or <c>url()</c>), or <c>null</c> if the prelude does not start with one.</summary>
    public string? Url => _url;

    /// <summary>True when the rule has a <c>layer</c> or <c>layer(name)</c> clause.</summary>
    public bool HasLayer => _hasLayer;

    /// <summary>The layer name in <c>layer(name)</c>; <c>null</c> for no layer or an anonymous one.</summary>
    public string? LayerName => _layerName;

    /// <summary>Source range of the inside of <c>supports(...)</c>; empty when absent.</summary>
    public CssRange SupportsRange => _supports;

    /// <summary>Source range of the media query list after the other clauses; empty when absent.</summary>
    public CssRange MediaRange => _media;

    /// <summary>The media query list text, or an empty string.</summary>
    public string MediaText => _media.IsEmpty ? string.Empty : Node.Tree.Text.Substring(_media.Start, _media.Length);
}

/// <summary>A typed view of an <c>@container</c> rule: an optional container name, then the query.</summary>
public readonly struct ContainerRule
{
    internal ContainerRule(CssNode node)
    {
        Node = node;
        string? name = null;
        int condStart = -1;
        foreach (ComponentValue v in node.Prelude.Trimmed())
        {
            if (condStart < 0 && v.IsWhitespace)
            {
                continue;
            }

            if (condStart < 0 && name is null && v.Kind == ComponentValueKind.Token && v.Token.Kind == CssTokenKind.Ident
                && !v.IsIdent("not") && !v.IsIdent("and") && !v.IsIdent("or"))
            {
                name = v.Token.Value;
                continue;
            }

            condStart = v.Range.Start;
            break;
        }

        ContainerName = name;
        Condition = condStart < 0 ? default : new CssRange(condStart, node.Prelude.Trimmed().Range.End);
    }

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>The container name before the condition, or <c>null</c>.</summary>
    public string? ContainerName { get; }

    /// <summary>Source range of the container condition; empty if none.</summary>
    public CssRange Condition { get; }

    /// <summary>The rules inside the block.</summary>
    public RuleEnumerable Rules => Node.Rules;
}

/// <summary>A typed view of an <c>@namespace</c> rule.</summary>
public readonly struct NamespaceRule
{
    internal NamespaceRule(CssNode node)
    {
        Node = node;
        string? prefix = null;
        string? uri = null;
        foreach (ComponentValue v in node.Prelude.Trimmed())
        {
            if (v.IsWhitespace)
            {
                continue;
            }

            if (uri is null && prefix is null && v.Kind == ComponentValueKind.Token && v.Token.Kind == CssTokenKind.Ident)
            {
                prefix = v.Token.Value;
            }
            else
            {
                uri ??= CssAtRules.UrlOrString(v);
            }
        }

        Prefix = prefix;
        Uri = uri;
    }

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>The namespace prefix, or <c>null</c> for the default namespace.</summary>
    public string? Prefix { get; }

    /// <summary>The namespace URI, or <c>null</c> if the prelude has none.</summary>
    public string? Uri { get; }
}

/// <summary>A typed view of <c>@charset "..."</c>. It is not a rule (CSS Syntax §8.3); the encoding comes from the bytes.</summary>
public readonly struct CharsetRule
{
    internal CharsetRule(CssNode node)
    {
        Node = node;
        string? enc = null;
        foreach (ComponentValue v in node.Prelude.Trimmed())
        {
            if (!v.IsWhitespace && v.Kind == ComponentValueKind.Token && v.Token.Kind == CssTokenKind.QuotedString)
            {
                enc = v.Token.Value;
                break;
            }
        }

        Encoding = enc;
    }

    /// <summary>The underlying node.</summary>
    public CssNode Node { get; }

    /// <summary>The string in the rule, or <c>null</c>.</summary>
    public string? Encoding { get; }
}
