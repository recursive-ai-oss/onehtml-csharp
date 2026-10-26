using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// Reads the local conventions of a stylesheet's source text so new text can copy them: newline sequence, indentation
/// unit, the text around a declaration's colon and <c>!important</c>, whether the last declaration of a block ends in a
/// semicolon, and the white space between adjacent rules. Each answer is inferred from a bounded sample.
/// </summary>
internal sealed class SheetLayout
{
    private const int SampleLimit = 400;

    private readonly CssTree _tree;
    private string? _unit;
    private string? _colon;
    private string? _important;
    private bool? _terminate;

    public SheetLayout(CssTree tree)
    {
        _tree = tree;
        Text = tree.Text;
        NewLine = DetectNewLine(Text);
    }

    public string Text { get; }

    public string NewLine { get; }

    public static bool IsWs(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    public static string DetectNewLine(string text)
    {
        int i = text.AsSpan().IndexOfAny('\r', '\n');
        if (i < 0)
        {
            return "\n";
        }

        return text[i] == '\r' ? (i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r") : "\n";
    }

    public static string Norm(ReadOnlySpan<char> s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool pending = false;
        foreach (char c in s)
        {
            if (IsWs(c))
            {
                pending = sb.Length > 0;
                continue;
            }

            if (pending)
            {
                sb.Append(' ');
                pending = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    // ---- positions ----

    public int WsBefore(int pos)
    {
        int i = pos;
        while (i > 0 && IsWs(Text[i - 1]))
        {
            i--;
        }

        return i;
    }

    public int WsAfter(int pos)
    {
        int i = pos;
        while (i < Text.Length && IsWs(Text[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>The indentation of the line <paramref name="pos"/> starts on, when only blanks precede it on that line.</summary>
    public string? LineIndent(int pos)
    {
        int i = pos;
        while (i > 0 && Text[i - 1] is ' ' or '\t')
        {
            i--;
        }

        return i == 0 || Text[i - 1] is '\n' or '\r' or '\f' ? Text[i..pos] : null;
    }

    public bool HasNewLine(int start, int end) => Text.AsSpan(start, end - start).IndexOfAny('\r', '\n') >= 0;

    // ---- containers ----

    public static int ContentStart(CssNode container) => container.Index == 0 ? 0 : container.ContentRange.Start;

    public int ContentEnd(CssNode container) => container.Index == 0 ? Text.Length : container.ContentRange.End;

    public static List<CssNode> Kids(CssNode container)
    {
        var list = new List<CssNode>();
        foreach (CssNode k in container.Children)
        {
            list.Add(k);
        }

        return list;
    }

    public static bool IsRule(CssNode n) => n.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule;

    public int Depth(CssNode n)
    {
        int d = 0;
        for (int x = n.Index; x > 0 && d < 1000; x = _tree.Items[x].Parent < 0 ? 0 : _tree.Items[x].Parent)
        {
            d++;
        }

        return d;
    }

    public string Unit
    {
        get
        {
            if (_unit is not null)
            {
                return _unit;
            }

            _unit = "  ";
            int seen = 0;
            for (int i = 1; i < _tree.ItemCount && seen < SampleLimit; i++)
            {
                if (_tree.Items[i].Kind is not (CssNodeKind.QualifiedRule or CssNodeKind.AtRule) || _tree.Items[i].FirstChild < 0)
                {
                    continue;
                }

                seen++;
                string? ri = LineIndent(_tree.Items[i].Start);
                string? ci = LineIndent(_tree.Items[_tree.Items[i].FirstChild].Start);
                if (ri is not null && ci is not null && ci.Length > ri.Length && ci.StartsWith(ri, StringComparison.Ordinal))
                {
                    _unit = ci[ri.Length..];
                    break;
                }
            }

            return _unit;
        }
    }

    /// <summary>The indentation of items inside <paramref name="container"/>.</summary>
    public string ChildIndent(CssNode container)
    {
        foreach (CssNode k in container.Children)
        {
            if (LineIndent(k.Range.Start) is { } li)
            {
                return li;
            }
        }

        if (container.Index == 0)
        {
            return string.Empty;
        }

        string own = LineIndent(container.Range.Start) ?? string.Concat(System.Linq.Enumerable.Repeat(Unit, Math.Max(0, Depth(container) - 1)));
        return own + Unit;
    }

    // ---- declaration conventions ----

    public string ColonSeparator(CssNode near)
    {
        CssNode? d = FirstDeclaration(near) ?? FirstDeclaration(_tree.Root);
        if (d is { } decl)
        {
            CssRange nr = decl.NameRange;
            CssRange vr = decl.ValueRange;
            if (vr.Start > nr.End && vr.Start <= Text.Length && vr.Length > 0)
            {
                string sep = Text[nr.End..vr.Start];
                int colons = 0;
                bool ok = true;
                foreach (char c in sep)
                {
                    if (c == ':')
                    {
                        colons++;
                    }
                    else if (!IsWs(c))
                    {
                        ok = false;
                    }
                }

                if (ok && colons == 1 && sep.Length <= 8)
                {
                    return sep;
                }
            }
        }

        return _colon ??= ": ";
    }

    public string ImportantSeparator()
    {
        if (_important is not null)
        {
            return _important;
        }

        _important = " ";
        int seen = 0;
        for (int i = 1; i < _tree.ItemCount && seen < SampleLimit; i++)
        {
            seen++;
            if (_tree.Items[i].Kind == CssNodeKind.Declaration && (_tree.Items[i].Flags & Item.Important) != 0)
            {
                var n = new CssNode(_tree, i);
                CssRange vr = n.ValueRange;
                CssRange ir = n.ImportantRange;
                if (ir.Start >= vr.End && ir.Start - vr.End <= 4)
                {
                    string sep = Text[vr.End..ir.Start];
                    if (sep.AsSpan().IndexOfAnyExcept(' ', '\t') < 0)
                    {
                        _important = sep;
                    }
                }

                break;
            }
        }

        return _important;
    }

    /// <summary>Whether blocks conventionally end their last declaration with a semicolon.</summary>
    public bool TerminateLast()
    {
        if (_terminate is { } t)
        {
            return t;
        }

        int yes = 0;
        int no = 0;
        int seen = 0;
        for (int i = 1; i < _tree.ItemCount && seen < SampleLimit; i++)
        {
            if (_tree.Items[i].Kind is not (CssNodeKind.QualifiedRule or CssNodeKind.AtRule) || _tree.Items[i].FirstChild < 0)
            {
                continue;
            }

            seen++;
            int last = -1;
            for (int c = _tree.Items[i].FirstChild; c >= 0; c = _tree.Items[c].NextSibling)
            {
                if (_tree.Items[c].Kind == CssNodeKind.Declaration)
                {
                    last = c;
                }
            }

            if (last >= 0)
            {
                if (_tree.Items[last].Semi >= 0)
                {
                    yes++;
                }
                else
                {
                    no++;
                }
            }
        }

        _terminate = yes >= no;
        return _terminate.Value;
    }

    private CssNode? FirstDeclaration(CssNode near)
    {
        if (near.Kind is CssNodeKind.Root or CssNodeKind.QualifiedRule or CssNodeKind.AtRule)
        {
            // Only the node's own declarations for a rule; a bounded walk of the sheet for the root.
            if (near.Index != 0)
            {
                foreach (CssNode d in near.Declarations)
                {
                    return d;
                }

                return null;
            }

            for (int i = 1; i < _tree.ItemCount && i < SampleLimit * 4; i++)
            {
                if (_tree.Items[i].Kind == CssNodeKind.Declaration && _tree.Items[i].Colon >= 0)
                {
                    return new CssNode(_tree, i);
                }
            }
        }

        return null;
    }

    // ---- gaps ----

    /// <summary>The white space separating two adjacent items, or null when something other than white space sits between them.</summary>
    public string? PairGap(CssNode a, CssNode b)
    {
        int s = a.Range.End;
        int e = b.Range.Start;
        if (e < s)
        {
            return null;
        }

        string between = Text[s..e];
        return between.AsSpan().IndexOfAnyExcept(" \t\n\r\f") < 0 ? between : null;
    }

    /// <summary>
    /// The text to put between the new rule and its neighbour, copied from the nearest pair of adjacent rules: the pair
    /// around the reference rule first, then any pair in the container, then any pair at the top level.
    /// </summary>
    public string RuleGap(CssNode container, int refIndex, bool before)
    {
        List<CssNode> kids = Kids(container);
        int ix = refIndex >= 0 ? kids.FindIndex(k => k.Index == refIndex) : -1;
        if (ix >= 0)
        {
            int[] order = before ? [ix - 1, ix] : [ix, ix - 1];
            foreach (int a in order)
            {
                if (a >= 0 && a + 1 < kids.Count && PairGap(kids[a], kids[a + 1]) is { } g)
                {
                    return g;
                }
            }
        }

        if (PairInList(kids) is { } g2)
        {
            return g2;
        }

        if (container.Index != 0)
        {
            if (PairInList(Kids(_tree.Root)) is { } g3)
            {
                // A top-level gap may carry indentation of its own; keep only its line structure.
                string stripped = StripTrailingBlanks(g3);
                if (stripped.Length > 0 && HasNl(stripped))
                {
                    return stripped + ChildIndent(container);
                }
            }

            return HasNewLine(ContentStart(container), ContentEnd(container)) ? NewLine + ChildIndent(container) : " ";
        }

        return NewLine;
    }

    private string? PairInList(List<CssNode> kids)
    {
        for (int i = 0; i + 1 < kids.Count; i++)
        {
            if (IsRule(kids[i]) && IsRule(kids[i + 1]) && PairGap(kids[i], kids[i + 1]) is { } g)
            {
                return g;
            }
        }

        return null;
    }

    private static bool HasNl(string s) => s.AsSpan().IndexOfAny('\r', '\n') >= 0;

    private static string StripTrailingBlanks(string s)
    {
        int e = s.Length;
        while (e > 0 && s[e - 1] is ' ' or '\t')
        {
            e--;
        }

        return s[..e];
    }

    // ---- new rule text ----

    /// <summary>
    /// Parses <paramref name="css"/> as exactly one rule and returns its text laid out for a place with
    /// <paramref name="indent"/>: leading and trailing white space dropped, a block-less at-rule ended with a semicolon,
    /// and later lines re-indented with the sheet's newline. Returns null with an error when it is not a single rule.
    /// </summary>
    public string? FormatRule(string css, string indent, out string? error)
    {
        error = null;
        // Every line break of the new text becomes this sheet's, so the splice reads like the text around it.
        string t = css.Trim(' ', '\t', '\n', '\r', '\f').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (NewLine != "\n")
        {
            t = t.Replace("\n", NewLine, StringComparison.Ordinal);
        }
        if (t.Length == 0)
        {
            error = "the rule is empty";
            return null;
        }

        CssTree check;
        try
        {
            check = CssTree.ParseRule(t);
        }
        catch (CssLimitExceededException ex)
        {
            error = ex.Message;
            return null;
        }

        CssNodeList nodes = check.Nodes;
        CssNode? only = null;
        int count = 0;
        foreach (CssNode n in nodes)
        {
            count++;
            only ??= n;
        }

        if (check.Error != CssParseError.None || count != 1 || only is not { } rule || !IsRule(rule))
        {
            error = "the text is not exactly one CSS rule: " + Shorten(t);
            return null;
        }

        if (rule.HasBlock && !rule.IsBlockClosed)
        {
            error = "the rule's block is not closed: " + Shorten(t);
            return null;
        }

        if (!rule.HasBlock && !t.EndsWith(';'))
        {
            t += ";";
        }

        if (t.AsSpan().IndexOfAny('\r', '\n') < 0 || t.Contains('\\', StringComparison.Ordinal))
        {
            return t;
        }

        string[] lines = t.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        int common = int.MaxValue;
        for (int i = 1; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.AsSpan().IndexOfAnyExcept(" \t") < 0)
            {
                continue;
            }

            int w = 0;
            while (w < l.Length && l[w] is ' ' or '\t')
            {
                w++;
            }

            common = Math.Min(common, w);
        }

        if (common == int.MaxValue)
        {
            common = 0;
        }

        var sb = new System.Text.StringBuilder(t.Length + (lines.Length * indent.Length));
        sb.Append(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            sb.Append(NewLine);
            string l = lines[i];
            if (l.AsSpan().IndexOfAnyExcept(" \t") >= 0)
            {
                sb.Append(indent).Append(l.AsSpan(Math.Min(common, l.Length)));
            }
        }

        return sb.ToString();
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..60] + "...";
}
