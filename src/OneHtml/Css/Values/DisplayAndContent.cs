using System;
using System.Collections.Generic;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The outer display type of a box (CSS Display 3 §2.2).</summary>
public enum DisplayOutside : byte
{
    /// <summary><c>block</c>.</summary>
    Block,

    /// <summary><c>inline</c>.</summary>
    Inline,

    /// <summary><c>run-in</c>.</summary>
    RunIn,
}

/// <summary>The inner display type of a box (CSS Display 3 §2.3).</summary>
public enum DisplayInside : byte
{
    /// <summary><c>flow</c>.</summary>
    Flow,

    /// <summary><c>flow-root</c>.</summary>
    FlowRoot,

    /// <summary><c>table</c>.</summary>
    Table,

    /// <summary><c>flex</c>.</summary>
    Flex,

    /// <summary><c>grid</c>.</summary>
    Grid,

    /// <summary><c>ruby</c>.</summary>
    Ruby,

    /// <summary><c>math</c>.</summary>
    Math,
}

/// <summary>Which of the forms of <c>display</c> a value is (CSS Display 3 §2).</summary>
public enum DisplayKind : byte
{
    /// <summary>A box with an outer type, an inner type and perhaps <c>list-item</c>.</summary>
    Box,

    /// <summary><c>none</c>: no box, and no descendants' boxes.</summary>
    None,

    /// <summary><c>contents</c>: no box of its own; children are laid out as if they were the parent's.</summary>
    Contents,

    /// <summary>A table-internal or ruby-internal display type, see <see cref="DisplayValue.InternalName"/>.</summary>
    Internal,
}

/// <summary>A parsed <c>display</c> value, covering the multi-keyword syntax and the legacy single keywords (CSS Display 3 §2).</summary>
public readonly struct DisplayValue : IEquatable<DisplayValue>
{
    internal DisplayValue(DisplayKind kind, DisplayOutside outside, DisplayInside inside, bool listItem, string? internalName)
    {
        Kind = kind;
        Outside = outside;
        Inside = inside;
        IsListItem = listItem;
        InternalName = internalName;
    }

    /// <summary>The form.</summary>
    public DisplayKind Kind { get; }

    /// <summary>The outer type when <see cref="Kind"/> is <see cref="DisplayKind.Box"/>.</summary>
    public DisplayOutside Outside { get; }

    /// <summary>The inner type when <see cref="Kind"/> is <see cref="DisplayKind.Box"/>.</summary>
    public DisplayInside Inside { get; }

    /// <summary>True when the box also generates a marker (<c>list-item</c>).</summary>
    public bool IsListItem { get; }

    /// <summary>For <see cref="DisplayKind.Internal"/>, the keyword (<c>table-row</c>, <c>table-cell</c>, ...), lower case.</summary>
    public string? InternalName { get; }

    /// <summary>False only for <c>none</c>; <c>contents</c> generates no box of its own but its children are rendered.</summary>
    public bool IsRendered => Kind != DisplayKind.None;

    /// <summary>Parses a <c>display</c> value. CSS-wide keywords and <c>var()</c> are not values; handle them before.</summary>
    /// <param name="value">The declaration value.</param>
    /// <param name="display">The parsed value.</param>
    public static bool TryParse(ComponentValueList value, out DisplayValue display)
    {
        display = default;
        TokenRange r = TokenRange.Of(value).Trim();
        var words = new List<string>(3);
        int i = r.From;
        while (i < r.To)
        {
            if (r.Kind(i) != CssTokenKind.Ident || words.Count >= 3)
            {
                return false;
            }

            words.Add(r.IdentString(i).AsciiLower());
            i = r.SkipWs(i + 1);
        }

        if (words.Count == 0)
        {
            return false;
        }

        if (words.Count == 1)
        {
            switch (words[0])
            {
                case "none":
                    display = new DisplayValue(DisplayKind.None, DisplayOutside.Block, DisplayInside.Flow, false, null);
                    return true;
                case "contents":
                    display = new DisplayValue(DisplayKind.Contents, DisplayOutside.Block, DisplayInside.Flow, false, null);
                    return true;
                case "table-row-group":
                case "table-header-group":
                case "table-footer-group":
                case "table-row":
                case "table-cell":
                case "table-column-group":
                case "table-column":
                case "table-caption":
                case "ruby-base":
                case "ruby-text":
                case "ruby-base-container":
                case "ruby-text-container":
                    display = new DisplayValue(DisplayKind.Internal, DisplayOutside.Block, DisplayInside.Flow, false, words[0]);
                    return true;
                case "inline-block":
                    display = new DisplayValue(DisplayKind.Box, DisplayOutside.Inline, DisplayInside.FlowRoot, false, null);
                    return true;
                case "inline-table":
                    display = new DisplayValue(DisplayKind.Box, DisplayOutside.Inline, DisplayInside.Table, false, null);
                    return true;
                case "inline-flex":
                    display = new DisplayValue(DisplayKind.Box, DisplayOutside.Inline, DisplayInside.Flex, false, null);
                    return true;
                case "inline-grid":
                    display = new DisplayValue(DisplayKind.Box, DisplayOutside.Inline, DisplayInside.Grid, false, null);
                    return true;
            }
        }

        DisplayOutside? outside = null;
        DisplayInside? inside = null;
        bool listItem = false;
        foreach (string w in words)
        {
            switch (w)
            {
                case "block" or "inline" or "run-in":
                    if (outside is not null)
                    {
                        return false;
                    }

                    outside = w == "block" ? DisplayOutside.Block : w == "inline" ? DisplayOutside.Inline : DisplayOutside.RunIn;
                    break;
                case "flow" or "flow-root" or "table" or "flex" or "grid" or "ruby" or "math":
                    if (inside is not null)
                    {
                        return false;
                    }

                    inside = w switch
                    {
                        "flow" => DisplayInside.Flow,
                        "flow-root" => DisplayInside.FlowRoot,
                        "table" => DisplayInside.Table,
                        "flex" => DisplayInside.Flex,
                        "grid" => DisplayInside.Grid,
                        "ruby" => DisplayInside.Ruby,
                        _ => DisplayInside.Math,
                    };
                    break;
                case "list-item":
                    if (listItem)
                    {
                        return false;
                    }

                    listItem = true;
                    break;
                default:
                    return false;
            }
        }

        // <display-listitem> only combines with flow and flow-root (CSS Display 3 §2.7).
        if (listItem && inside is not (null or DisplayInside.Flow or DisplayInside.FlowRoot))
        {
            return false;
        }

        DisplayInside ins = inside ?? DisplayInside.Flow;
        DisplayOutside outs = outside ?? (ins == DisplayInside.Ruby ? DisplayOutside.Inline : DisplayOutside.Block);
        display = new DisplayValue(DisplayKind.Box, outs, ins, listItem, null);
        return true;
    }

    /// <summary>The shortest serialization (CSS Display 3 §2.8): <c>block flow</c> is <c>block</c>, <c>inline flow-root</c> is <c>inline-block</c>, and so on.</summary>
    public string ToCss()
    {
        switch (Kind)
        {
            case DisplayKind.None:
                return "none";
            case DisplayKind.Contents:
                return "contents";
            case DisplayKind.Internal:
                return InternalName!;
        }

        string o = Outside switch { DisplayOutside.Block => "block", DisplayOutside.Inline => "inline", _ => "run-in" };
        if (IsListItem)
        {
            var sb = new StringBuilder();
            if (Outside != DisplayOutside.Block)
            {
                sb.Append(o).Append(' ');
            }

            if (Inside == DisplayInside.FlowRoot)
            {
                sb.Append("flow-root ");
            }

            return sb.Append("list-item").ToString();
        }

        return Inside switch
        {
            DisplayInside.Flow => o,
            DisplayInside.FlowRoot => Outside == DisplayOutside.Block ? "flow-root" : Outside == DisplayOutside.Inline ? "inline-block" : "run-in flow-root",
            DisplayInside.Table => Outside == DisplayOutside.Block ? "table" : Outside == DisplayOutside.Inline ? "inline-table" : "run-in table",
            DisplayInside.Flex => Outside == DisplayOutside.Block ? "flex" : Outside == DisplayOutside.Inline ? "inline-flex" : "run-in flex",
            DisplayInside.Grid => Outside == DisplayOutside.Block ? "grid" : Outside == DisplayOutside.Inline ? "inline-grid" : "run-in grid",
            DisplayInside.Ruby => Outside == DisplayOutside.Inline ? "ruby" : o + " ruby",
            _ => Outside == DisplayOutside.Block ? "math" : o + " math",
        };
    }

    /// <inheritdoc />
    public override string ToString() => ToCss();

    /// <inheritdoc />
    public bool Equals(DisplayValue other) => ToCss() == other.ToCss();

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DisplayValue d && Equals(d);

    /// <inheritdoc />
    public override int GetHashCode() => ToCss().GetHashCode(StringComparison.Ordinal);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(DisplayValue left, DisplayValue right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(DisplayValue left, DisplayValue right) => !left.Equals(right);
}

/// <summary>The values of <c>visibility</c> (CSS Display 3 §... CSS 2 §11.2).</summary>
public enum VisibilityValue : byte
{
    /// <summary><c>visible</c>.</summary>
    Visible,

    /// <summary><c>hidden</c>.</summary>
    Hidden,

    /// <summary><c>collapse</c>.</summary>
    Collapse,
}

/// <summary>The kind of one item of a <c>content</c> value (CSS Generated Content 3 §2).</summary>
public enum ContentItemKind : byte
{
    /// <summary>A string; see <see cref="ContentItem.Text"/>.</summary>
    Text,

    /// <summary><c>attr()</c>; see <see cref="ContentItem.Text"/> (the attribute name), <see cref="ContentItem.Fallback"/>.</summary>
    Attr,

    /// <summary><c>counter()</c> or <c>counters()</c>; <see cref="ContentItem.Text"/> is the counter name, <see cref="ContentItem.Separator"/> is set for <c>counters()</c>, <see cref="ContentItem.Fallback"/> holds the style text.</summary>
    Counter,

    /// <summary><c>open-quote</c>.</summary>
    OpenQuote,

    /// <summary><c>close-quote</c>.</summary>
    CloseQuote,

    /// <summary><c>no-open-quote</c>.</summary>
    NoOpenQuote,

    /// <summary><c>no-close-quote</c>.</summary>
    NoCloseQuote,

    /// <summary>An image (<c>url()</c>, a gradient, <c>image-set()</c>, ...); <see cref="ContentItem.Text"/> is its source text.</summary>
    Image,

    /// <summary><c>contents</c>.</summary>
    Contents,

    /// <summary>A function this library accepts but does not interpret (<c>leader()</c>, <c>target-counter()</c>, <c>string()</c>, <c>content()</c>); <see cref="ContentItem.Text"/> is its source text.</summary>
    Other,
}

/// <summary>One item of a <c>content</c> list or of its alternative text.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">The string, attribute name, counter name or source text.</param>
/// <param name="Fallback">For <c>attr()</c> the fallback text; for counters the counter-style text; otherwise null.</param>
/// <param name="Separator">For <c>counters()</c>, the separator string; otherwise null.</param>
public readonly record struct ContentItem(ContentItemKind Kind, string? Text, string? Fallback = null, string? Separator = null);

/// <summary>A parsed <c>content</c> value: <c>none</c>, <c>normal</c>, or a list of items with optional alternative text after <c>/</c>.</summary>
public sealed class ContentValue
{
    internal ContentValue(bool none, bool normal, ContentItem[] items, ContentItem[] alt)
    {
        IsNone = none;
        IsNormal = normal;
        Items = items;
        AlternativeText = alt;
    }

    /// <summary>True for <c>none</c>.</summary>
    public bool IsNone { get; }

    /// <summary>True for <c>normal</c>.</summary>
    public bool IsNormal { get; }

    /// <summary>The items (empty for <c>none</c> and <c>normal</c>).</summary>
    public IReadOnlyList<ContentItem> Items { get; }

    /// <summary>The alternative text items after <c>/</c> (CSS Generated Content 3 §2.1); empty when there is none. Accessible name computation prefers it.</summary>
    public IReadOnlyList<ContentItem> AlternativeText { get; }

    /// <summary>True when the list has alternative text.</summary>
    public bool HasAlternativeText => AlternativeText.Count > 0;

    /// <summary>Parses a <c>content</c> value.</summary>
    /// <param name="value">The declaration value.</param>
    /// <param name="content">The parsed value.</param>
    public static bool TryParse(ComponentValueList value, out ContentValue? content)
    {
        content = null;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return false;
        }

        if (r.Kind(r.From) == CssTokenKind.Ident && r.SkipWs(r.From + 1) >= r.To)
        {
            if (r.IsIdent(r.From, "none"))
            {
                content = new ContentValue(true, false, [], []);
                return true;
            }

            if (r.IsIdent(r.From, "normal"))
            {
                content = new ContentValue(false, true, [], []);
                return true;
            }
        }

        var items = new List<ContentItem>();
        var alt = new List<ContentItem>();
        List<ContentItem> target = items;
        bool sawSlash = false;
        int i = r.SkipWs(r.From);
        while (i < r.To)
        {
            int next = r.Next(i);
            if (r.IsDelim(i, '/'))
            {
                if (sawSlash || items.Count == 0)
                {
                    return false;
                }

                sawSlash = true;
                target = alt;
                i = r.SkipWs(next);
                continue;
            }

            if (!TryItem(r, i, next, sawSlash, out ContentItem item))
            {
                return false;
            }

            target.Add(item);
            i = r.SkipWs(next);
        }

        if (items.Count == 0 || (sawSlash && alt.Count == 0))
        {
            return false;
        }

        content = new ContentValue(false, false, items.ToArray(), alt.ToArray());
        return true;
    }

    private static string Raw(TokenRange r, int from, int to) => r.Tree.Text.Substring(r.Tree.Tokens[from].Start, r.Tree.Tokens[to - 1].End - r.Tree.Tokens[from].Start);

    private static bool TryItem(TokenRange r, int i, int next, bool inAlt, out ContentItem item)
    {
        item = default;
        switch (r.Kind(i))
        {
            case CssTokenKind.QuotedString:
                item = new ContentItem(ContentItemKind.Text, r.StringValue(i));
                return true;
            case CssTokenKind.Ident when !inAlt:
            {
                switch (r.IdentString(i).AsciiLower())
                {
                    case "open-quote": item = new ContentItem(ContentItemKind.OpenQuote, null); return true;
                    case "close-quote": item = new ContentItem(ContentItemKind.CloseQuote, null); return true;
                    case "no-open-quote": item = new ContentItem(ContentItemKind.NoOpenQuote, null); return true;
                    case "no-close-quote": item = new ContentItem(ContentItemKind.NoCloseQuote, null); return true;
                    case "contents": item = new ContentItem(ContentItemKind.Contents, null); return true;
                    default: return false;
                }
            }

            case CssTokenKind.Url when !inAlt:
                item = new ContentItem(ContentItemKind.Image, Raw(r, i, next));
                return true;
            case CssTokenKind.Function:
            {
                string name = r.IdentString(i).AsciiLower();
                TokenRange inner = r.Inner(i);
                if (name == "attr")
                {
                    return TryAttr(inner, out item);
                }

                if (name is "counter" or "counters")
                {
                    return TryCounter(inner, name == "counters", out item);
                }

                if (inAlt)
                {
                    return false;
                }

                if (ValueKinds.In(ValueKinds.ImageFunctions, name))
                {
                    item = new ContentItem(ContentItemKind.Image, Raw(r, i, next));
                    return true;
                }

                if (name is "leader" or "target-counter" or "target-counters" or "target-text" or "string" or "content")
                {
                    item = new ContentItem(ContentItemKind.Other, Raw(r, i, next));
                    return true;
                }

                return false;
            }

            default:
                return false;
        }
    }

    // attr( [<ns>|]<name> [<type>]? [, <fallback>]? ) per CSS Values 5 §? (attr() with types); only the name and fallback matter here.
    private static bool TryAttr(TokenRange inner, out ContentItem item)
    {
        item = default;
        int i = inner.SkipWs(inner.From);
        if (i >= inner.To)
        {
            return false;
        }

        var name = new StringBuilder();
        while (i < inner.To && inner.Kind(i) is CssTokenKind.Ident or CssTokenKind.Delim && !inner.IsDelim(i, '/'))
        {
            if (inner.Kind(i) == CssTokenKind.Delim && !inner.IsDelim(i, '|') && !inner.IsDelim(i, '*'))
            {
                return false;
            }

            name.Append(inner.Kind(i) == CssTokenKind.Ident ? inner.IdentString(i) : inner.Text(i).ToString());
            i++;
            if (name.Length > 0 && i < inner.To && inner.Kind(i) == CssTokenKind.Whitespace)
            {
                break;
            }
        }

        if (name.Length == 0)
        {
            return false;
        }

        i = inner.SkipWs(i);
        string? fallback = null;
        while (i < inner.To)
        {
            if (inner.Kind(i) == CssTokenKind.Comma)
            {
                fallback = inner.Sub(i + 1, inner.To).Trim().RawText();
                break;
            }

            i = inner.Next(i); // an optional type: ident, <unit> or type()
        }

        item = new ContentItem(ContentItemKind.Attr, name.ToString(), fallback);
        return true;
    }

    private static bool TryCounter(TokenRange inner, bool plural, out ContentItem item)
    {
        item = default;
        var args = new List<int>();
        int i = inner.SkipWs(inner.From);
        while (i < inner.To)
        {
            args.Add(i);
            i = inner.SkipWs(inner.Next(i));
            if (i < inner.To)
            {
                if (inner.Kind(i) != CssTokenKind.Comma)
                {
                    return false;
                }

                i = inner.SkipWs(i + 1);
            }
        }

        int min = plural ? 2 : 1;
        int max = plural ? 3 : 2;
        if (args.Count < min || args.Count > max || inner.Kind(args[0]) != CssTokenKind.Ident)
        {
            return false;
        }

        string? sep = null;
        int styleIdx = 1;
        if (plural)
        {
            if (inner.Kind(args[1]) != CssTokenKind.QuotedString)
            {
                return false;
            }

            sep = inner.StringValue(args[1]);
            styleIdx = 2;
        }

        string? style = null;
        if (args.Count > styleIdx)
        {
            int a = args[styleIdx];
            style = inner.Tree.Text.Substring(inner.Tree.Tokens[a].Start, inner.Tree.Tokens[inner.Next(a) - 1].End - inner.Tree.Tokens[a].Start);
        }

        item = new ContentItem(ContentItemKind.Counter, inner.IdentString(args[0]), style, sep);
        return true;
    }
}
