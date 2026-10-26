using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// CSS counters (CSS Lists 3 section 4) for generated content: one walk of the rendered tree applies
/// <c>counter-reset</c>, <c>counter-increment</c> and <c>counter-set</c> (and the implicit <c>list-item</c> increment) and
/// records the counters visible at the <c>::before</c> and <c>::after</c> of the elements that ask. Iterative; runs once, on
/// the first <c>counter()</c> or <c>counters()</c> a name computation meets.
/// </summary>
internal sealed class CounterResolver
{
    private readonly Document _doc;
    private readonly DocumentStyles _styles;
    private readonly HiddenMap _hidden;
    private Dictionary<long, Dictionary<string, List<int>>>? _snapshots;

    /// <summary>True once a name has asked for a counter value.</summary>
    public bool Walked => _snapshots is not null;

    public CounterResolver(Document doc, DocumentStyles styles, HiddenMap hidden)
    {
        _doc = doc;
        _styles = styles;
        _hidden = hidden;
    }

    private sealed class Counter
    {
        public required string Name;
        public int Value;
        public int Creator; // the parent whose children share this counter's scope
    }

    /// <summary>The text of one counter item of a <c>content</c> value at the pseudo-element of an element.</summary>
    public string Resolve(Node element, PseudoElementKind kind, ContentItem item)
    {
        _snapshots ??= Walk();
        _snapshots.TryGetValue(Key(element.Index, kind), out Dictionary<string, List<int>>? snap);
        string name = item.Text ?? string.Empty;
        List<int>? values = null;
        snap?.TryGetValue(name, out values);
        string style = item.Fallback ?? "decimal";
        if (item.Separator is null)
        {
            // counter(): the innermost counter; a missing counter counts as zero (CSS Lists 3 section 4.5).
            int v = values is { Count: > 0 } ? values[^1] : 0;
            return FormatCounter(v, style);
        }

        if (values is null || values.Count == 0)
        {
            return FormatCounter(0, style);
        }

        var sb = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(item.Separator);
            }

            sb.Append(FormatCounter(values[i], style));
        }

        return sb.ToString();
    }

    private static long Key(int index, PseudoElementKind kind) => ((long)index << 2) | (long)kind;

    private Dictionary<long, Dictionary<string, List<int>>> Walk()
    {
        var result = new Dictionary<long, Dictionary<string, List<int>>>();
        var live = new List<Counter>();
        Node cur = _doc.Root.FirstChild;
        // Stack of open elements for the leave events.
        var open = new List<int>();
        while (!cur.IsNull)
        {
            bool descend = false;
            if (cur.IsElement && !_hidden.NotRendered(cur.Index))
            {
                Enter(cur, live, result);
                open.Add(cur.Index);
                descend = !cur.FirstChild.IsNull;
                if (!descend)
                {
                    Leave(cur, live, result);
                    open.RemoveAt(open.Count - 1);
                }
            }

            if (descend)
            {
                cur = cur.FirstChild;
                continue;
            }

            // Next sibling, leaving elements while there is none.
            while (!cur.IsNull)
            {
                Node next = cur.NextSibling;
                if (!next.IsNull)
                {
                    cur = next;
                    break;
                }

                Node parent = cur.Parent;
                if (parent.IsNull || parent.Index == 0)
                {
                    cur = default;
                    break;
                }

                if (open.Count > 0 && open[^1] == parent.Index)
                {
                    Leave(parent, live, result);
                    open.RemoveAt(open.Count - 1);
                }

                cur = parent;
            }
        }

        return result;
    }

    private void Enter(Node e, List<Counter> live, Dictionary<long, Dictionary<string, List<int>>> result)
    {
        ComputedStyle style = _styles.GetStyle(e);
        int level = e.Parent.Index;
        Apply(style, level, live, implicitListItem: true);

        // ::before is the element's first child, so its own counter properties apply inside the element's scope.
        ComputedStyle? before = _styles.GetPseudoStyle(e, PseudoElementKind.Before);
        if (before is not null && HasBox(before))
        {
            Apply(before, e.Index, live, implicitListItem: false);
        }

        if (WantsCounters(before))
        {
            result[Key(e.Index, PseudoElementKind.Before)] = Snapshot(live);
        }
    }

    private void Leave(Node e, List<Counter> live, Dictionary<long, Dictionary<string, List<int>>> result)
    {
        // ::after is the element's last child; the counters its children made are still in scope for it.
        ComputedStyle? after = _styles.GetPseudoStyle(e, PseudoElementKind.After);
        if (after is not null && HasBox(after))
        {
            Apply(after, e.Index, live, implicitListItem: false);
        }

        if (WantsCounters(after))
        {
            result[Key(e.Index, PseudoElementKind.After)] = Snapshot(live);
        }

        // Counters created by the element's children go out of scope with it.
        while (live.Count > 0 && live[^1].Creator == e.Index)
        {
            live.RemoveAt(live.Count - 1);
        }
    }

    private static bool HasBox(ComputedStyle ps) =>
        ps.Display.IsRendered && ps.Content is { IsNone: false, IsNormal: false };

    /// <summary>CSS Lists 3 section 4.4: counter-reset, then counter-increment, then counter-set; a list item increments list-item on its own.</summary>
    private static void Apply(ComputedStyle style, int level, List<Counter> live, bool implicitListItem)
    {
        string reset = style.GetPropertyValue("counter-reset");
        string inc = style.GetPropertyValue("counter-increment");
        string set = style.GetPropertyValue("counter-set");

        foreach ((string name, int? value) in ParseList(reset))
        {
            Counter? sibling = Innermost(live, name);
            if (sibling is not null && sibling.Creator == level)
            {
                sibling.Value = value ?? 0;
            }
            else
            {
                live.Add(new Counter { Name = name, Value = value ?? 0, Creator = level });
            }
        }

        bool explicitListItem = false;
        foreach ((string name, int? value) in ParseList(inc))
        {
            explicitListItem |= name == "list-item";
            Counter c = Innermost(live, name) ?? Instantiate(live, name, level);
            c.Value = Saturate((long)c.Value + (value ?? 1));
        }

        if (implicitListItem && !explicitListItem && style.Display.IsListItem)
        {
            bool setsIt = false;
            foreach ((string name, int? _) in ParseList(set))
            {
                setsIt |= name == "list-item";
            }

            if (!setsIt)
            {
                Counter c = Innermost(live, "list-item") ?? Instantiate(live, "list-item", level);
                c.Value = Saturate((long)c.Value + 1);
            }
        }

        foreach ((string name, int? value) in ParseList(set))
        {
            Counter c = Innermost(live, name) ?? Instantiate(live, name, level);
            c.Value = value ?? 0;
        }
    }

    private static bool WantsCounters(ComputedStyle? ps)
    {
        if (ps is null || !HasBox(ps) || ps.Content is not { } content)
        {
            return false;
        }

        foreach (ContentItem i in content.Items)
        {
            if (i.Kind == ContentItemKind.Counter)
            {
                return true;
            }
        }

        foreach (ContentItem i in content.AlternativeText)
        {
            if (i.Kind == ContentItemKind.Counter)
            {
                return true;
            }
        }

        return false;
    }

    private static Counter Instantiate(List<Counter> live, string name, int level)
    {
        var c = new Counter { Name = name, Value = 0, Creator = level };
        live.Add(c);
        return c;
    }

    private static Counter? Innermost(List<Counter> live, string name)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (live[i].Name == name)
            {
                return live[i];
            }
        }

        return null;
    }

    private static Dictionary<string, List<int>> Snapshot(List<Counter> live)
    {
        var d = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (Counter c in live)
        {
            if (!d.TryGetValue(c.Name, out List<int>? l))
            {
                d[c.Name] = l = [];
            }

            l.Add(c.Value);
        }

        return d;
    }

    private static int Saturate(long v) => v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;

    /// <summary>Reads a counter list: names, each optionally followed by an integer. <c>none</c> and anything unreadable give nothing.</summary>
    internal static List<(string Name, int? Value)> ParseList(string text)
    {
        var list = new List<(string, int?)>();
        if (text.Length == 0)
        {
            return list;
        }

        string[] parts = text.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0] == "none")
        {
            return list;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (long.TryParse(p, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            {
                continue; // an integer belongs to the name before it
            }

            if (p.StartsWith("reversed(", StringComparison.Ordinal) && p.EndsWith(')'))
            {
                p = p[9..^1];
            }

            int? v = null;
            if (i + 1 < parts.Length && long.TryParse(parts[i + 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
            {
                v = Saturate(n);
            }

            list.Add((p, v));
        }

        return list;
    }

    internal static string FormatCounter(int value, string style)
    {
        switch (TreeText.AsciiLower(TreeText.Trim(style)))
        {
            case "none":
                return string.Empty;
            case "disc":
                return "\u2022";
            case "circle":
                return "\u25E6";
            case "square":
                return "\u25AA";
            case "decimal-leading-zero":
                return value is >= 0 and < 10 ? "0" + value.ToString(CultureInfo.InvariantCulture) : value is > -10 and < 0 ? "-0" + (-value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
            case "lower-roman":
                return value is > 0 and < 4000 ? TreeText.AsciiLower(Roman(value)) : value.ToString(CultureInfo.InvariantCulture);
            case "upper-roman":
                return value is > 0 and < 4000 ? Roman(value) : value.ToString(CultureInfo.InvariantCulture);
            case "lower-alpha":
            case "lower-latin":
                return value > 0 ? Alpha(value, 'a') : value.ToString(CultureInfo.InvariantCulture);
            case "upper-alpha":
            case "upper-latin":
                return value > 0 ? Alpha(value, 'A') : value.ToString(CultureInfo.InvariantCulture);
            default:
                return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string Roman(int v)
    {
        ReadOnlySpan<int> n = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] s = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var sb = new StringBuilder();
        for (int i = 0; i < n.Length; i++)
        {
            while (v >= n[i])
            {
                sb.Append(s[i]);
                v -= n[i];
            }
        }

        return sb.ToString();
    }

    private static string Alpha(int v, char a)
    {
        var sb = new StringBuilder();
        while (v > 0)
        {
            v--;
            sb.Insert(0, (char)(a + (v % 26)));
            v /= 26;
        }

        return sb.ToString();
    }
}
