using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>A replacement of one stretch of a stylesheet's text.</summary>
internal sealed class SheetPiece
{
    public int Start;
    public int End;
    public string Text = string.Empty;

    /// <summary>The indexes (into the edit list given to the engine) of the edits this piece carries out.</summary>
    public List<int> Edits = [];

    /// <summary>Order among pieces that start at the same offset: appended declarations, then insertions after a rule, before a rule, at the end of a block.</summary>
    public int Rank;

    /// <summary>For a piece that closes an unterminated rule so something can follow it: the rule; otherwise -1.</summary>
    public int Anchor = -1;

    /// <summary>0 for a local splice; otherwise how many enclosing rules up the rewritten rule is.</summary>
    public int Level;

    public string? Reason;
}

/// <summary>What the engine decided for one stylesheet.</summary>
internal sealed class SheetPlan
{
    public bool Ok;

    /// <summary>The reason, when not <see cref="Ok"/>.</summary>
    public string? Error;

    /// <summary>The edit the error is about, or -1.</summary>
    public int ErrorEdit = -1;

    /// <summary>True when the edit does not fit its target; false when the result simply cannot be written.</summary>
    public bool Invalid;

    public List<SheetPiece> Pieces = [];

    public string NewText = string.Empty;

    public int Verifications;

    public int[] Levels = [];
}

/// <summary>
/// Stylesheet edits. Every edit becomes a local splice of the original text; the spliced text is parsed again and its
/// rule events (rules, declarations, in order) must equal the events the edits were expected to produce, which are
/// derived from the original tree and the edits, not from the new text. A mismatch rewrites the smallest enclosing rule
/// from those expected events instead, then the next one out, until the rewrite verifies or there is no rule left.
/// </summary>
internal sealed class SheetEngine
{
    private enum EvType : byte { Open, Close, Decl, Leaf, Invalid }

    private struct Ev
    {
        public EvType Type;
        public bool Flag;
        public string A;
        public string B;
        public int Orig;
        public string? Src;
        public int RawStart;
        public int RawEnd;
        public int Skip;
        public string? Canon;
        public bool Unclosed;
    }

    private sealed class Op
    {
        public SheetEdit Edit = null!;
        public int Index;
        public int Group;
        public int Target = -1;
        public string? Value;
        public string? ValueNorm;
        public string? InsertText;
        public CssTree? InsertTree;
        public List<SheetPiece> Pieces = [];
    }

    private enum EntryKind : byte { Orig, NewDecl, ModDecl, Inserted }

    private struct Entry
    {
        public EntryKind Kind;
        public int Node;
        public string Name;
        public string Value;
        public string Norm;
        public bool Important;
        public Op? Op;
    }

    private readonly Stylesheet _sheet;
    private readonly CssTree _tree;
    private readonly SheetLayout _lay;
    private readonly string _text;
    private readonly CancellationToken _ct;
    private readonly List<Op> _ops = [];
    private int[]? _top;
    private bool _wholeSheet;

    private SheetEngine(Stylesheet sheet, CancellationToken ct)
    {
        _sheet = sheet;
        _tree = sheet.Tree;
        _text = sheet.Text;
        _lay = new SheetLayout(_tree);
        _ct = ct;
    }

    public static SheetPlan Run(Stylesheet sheet, IReadOnlyList<SheetEdit> edits, int maxVerifications, bool verifyWholeSheet, CancellationToken ct)
    {
        var engine = new SheetEngine(sheet, ct) { _wholeSheet = verifyWholeSheet };
        return engine.Execute(edits, maxVerifications);
    }

    private static SheetPlan Bad(string error, int edit, bool invalid) => new() { Ok = false, Error = error, ErrorEdit = edit, Invalid = invalid };

    // ---- the run ----

    private SheetPlan Execute(IReadOnlyList<SheetEdit> edits, int maxVerifications)
    {
        for (int i = 0; i < edits.Count; i++)
        {
            string? error = Prepare(edits[i], i);
            if (error is not null)
            {
                return Bad(error, i, invalid: true);
            }
        }

        if (_ops.Count == 0)
        {
            return new SheetPlan { Ok = true, NewText = _text, Levels = new int[edits.Count] };
        }

        int[] tiers = new int[edits.Count];
        int verifications = 0;

        // Everything at once first: the common case costs one re-parse.
        SheetPlan result = Attempt(_ops, tiers, ref verifications);
        if (result.Ok || result.Invalid)
        {
            result.Verifications = verifications;
            return result;
        }

        // Otherwise add the edits one at a time and raise the tier of whichever one breaks the output.
        var applied = new List<Op>();
        tiers = new int[edits.Count];
        SheetPlan last = result;
        foreach (Op op in _ops)
        {
            applied.Add(op);
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                if (verifications >= maxVerifications)
                {
                    return Bad("the output was re-parsed " + verifications + " times without matching the expected rules", op.Index, invalid: false);
                }

                last = Attempt(applied, tiers, ref verifications);
                if (last.Ok)
                {
                    break;
                }

                if (last.Invalid)
                {
                    return last;
                }

                tiers[op.Index]++;
                if (!HasRoot(op, tiers[op.Index]))
                {
                    return Bad("edit " + op.Index + " cannot be written: " + (last.Error ?? "the output does not re-parse to the expected rules"), op.Index, invalid: false);
                }
            }
        }

        last.Verifications = verifications;
        return last;
    }

    private SheetPlan Attempt(List<Op> ops, int[] tiers, ref int verifications)
    {
        _ct.ThrowIfCancellationRequested();
        bool windowed = TryWindow(ops, out int lo, out int hi, out int windowStart, out int windowEnd);
        List<Ev> expected = BuildExpected(ops, out Dictionary<int, int> openAt, windowed ? lo : -1, hi);
        var pieces = new List<SheetPiece>();
        var roots = new List<int>();
        var rootOps = new Dictionary<int, List<int>>();
        var rootLevel = new Dictionary<int, int>();
        foreach (Op op in ops)
        {
            if (tiers[op.Index] > 0)
            {
                int r = RootOf(op, tiers[op.Index]);
                if (r > 0)
                {
                    roots.Add(r);
                }
            }
        }

        // Keep only the outermost roots.
        var rootSet = new HashSet<int>(roots);
        var outer = new List<int>();
        foreach (int r in rootSet)
        {
            bool inside = false;
            for (int x = ParentOf(r); x > 0; x = ParentOf(x))
            {
                if (rootSet.Contains(x))
                {
                    inside = true;
                    break;
                }
            }

            if (!inside)
            {
                outer.Add(r);
            }
        }

        outer.Sort();
        var doomed = new HashSet<int>();
        foreach (Op op in ops)
        {
            if (op.Edit.Operation is SheetEditOperation.RemoveRule or SheetEditOperation.ReplaceRule)
            {
                doomed.Add(op.Target);
            }
        }

        foreach (Op op in ops)
        {
            int covering = -1;
            for (int x = op.Group; x > 0; x = ParentOf(x))
            {
                if (outer.Contains(x))
                {
                    covering = x;
                    break;
                }
            }

            if (covering >= 0)
            {
                if (!rootOps.TryGetValue(covering, out List<int>? l))
                {
                    l = [];
                    rootOps[covering] = l;
                }

                l.Add(op.Index);
                rootLevel[covering] = Math.Max(rootLevel.GetValueOrDefault(covering), tiers[op.Index]);
                continue;
            }

            foreach (SheetPiece p in op.Pieces)
            {
                if (p.Anchor >= 0 && doomed.Contains(p.Anchor))
                {
                    continue;
                }

                pieces.Add(new SheetPiece { Start = p.Start, End = p.End, Text = p.Text, Edits = [.. p.Edits], Rank = p.Rank, Anchor = p.Anchor });
            }
        }

        foreach (int r in outer)
        {
            if (!openAt.TryGetValue(r, out int at))
            {
                return Bad("the rule to rewrite is gone", -1, invalid: false);
            }

            CssNode rule = new(_tree, r);
            pieces.Add(new SheetPiece
            {
                Start = rule.Range.Start,
                End = rule.Range.End,
                Text = Render(expected, at),
                Edits = rootOps.GetValueOrDefault(r) ?? [],
                Level = rootLevel.GetValueOrDefault(r, 1),
                Reason = "the local splice did not re-parse to the expected rules",
            });
        }

        string? overlap = Resolve(pieces, out string? whichEdit);
        if (overlap is not null)
        {
            int e = whichEdit is null ? -1 : int.Parse(whichEdit, System.Globalization.CultureInfo.InvariantCulture);
            return Bad(overlap, e, invalid: true);
        }

        var sb = new StringBuilder(_text.Length + 64);
        int pos = 0;
        foreach (SheetPiece p in pieces)
        {
            sb.Append(_text, pos, p.Start - pos);
            sb.Append(p.Text);
            pos = p.End;
        }

        sb.Append(_text, pos, _text.Length - pos);
        string newText = sb.ToString();

        // Narrow check first: the edits sit inside a window of whole top-level rules, and a rule list parses the same
        // from any rule boundary, so when the window alone re-parses to the expected rules the whole sheet does too
        // (see TryWindow). A mismatch proves nothing, so it falls through to the whole-sheet check below.
        bool verified = false;
        if (windowed && pieces.Count > 0 && pieces[0].Start >= windowStart && pieces[^1].End <= windowEnd && newText.Length < NarrowLimit)
        {
            verifications++;
            try
            {
                string windowText = newText.Substring(windowStart, newText.Length - windowStart - (_text.Length - windowEnd));
                verified = Same(expected, EventsOf(CssTree.ParseStylesheet(windowText, null, _ct)), out _);
            }
            catch (CssLimitExceededException)
            {
                verified = false;
            }
        }

        if (!verified)
        {
            if (windowed)
            {
                expected = BuildExpected(ops, out _, -1, -1);
            }

            verifications++;
            CssTree reparsed;
            try
            {
                reparsed = CssTree.ParseStylesheet(newText, null, _ct);
            }
            catch (CssLimitExceededException ex)
            {
                return Bad("the edited stylesheet exceeds a limit: " + ex.Message, -1, invalid: false);
            }

            List<Ev> actual = EventsOf(reparsed);
            if (!Same(expected, actual, out string? why))
            {
                return Bad(why!, -1, invalid: false);
            }
        }

        int[] levels = new int[tiers.Length];
        Array.Copy(tiers, levels, tiers.Length);
        return new SheetPlan { Ok = true, Pieces = pieces, NewText = newText, Levels = levels };
    }

    private int ParentOf(int item)
    {
        int p = _tree.Items[item].Parent;
        return p < 0 ? 0 : p;
    }

    /// <summary>The rule rewritten at <paramref name="tier"/>: the tier-th enclosing rule from the edit's group, or 0 when there is none.</summary>
    private int RootOf(Op op, int tier)
    {
        int x = op.Group;
        for (int i = 1; i < tier && x > 0; i++)
        {
            x = ParentOf(x);
        }

        return x;
    }

    private bool HasRoot(Op op, int tier) => RootOf(op, tier) > 0;

    // ---- preparing edits ----

    private string? Prepare(SheetEdit edit, int index)
    {
        if (!ReferenceEquals(edit.Sheet, _sheet))
        {
            return "the edit targets a different stylesheet snapshot";
        }

        var op = new Op { Edit = edit, Index = index };
        CssNode t = edit.Target;
        if (edit.HasTarget)
        {
            op.Target = t.Index;
        }

        switch (edit.Operation)
        {
            case SheetEditOperation.SetDeclaration:
            case SheetEditOperation.RemoveDeclaration:
                {
                    string? bad = CheckBlockRule(t);
                    if (bad is not null)
                    {
                        return bad;
                    }

                    bad = PropertyName(edit.Property!);
                    if (bad is not null)
                    {
                        return bad;
                    }

                    op.Group = t.Index;
                    if (edit.Operation == SheetEditOperation.SetDeclaration)
                    {
                        bad = ProbeValue(edit.Property!, edit.Value!, edit.Important, op);
                        if (bad is not null)
                        {
                            return bad;
                        }

                        PlanSet(op, t);
                    }
                    else
                    {
                        PlanRemoveDeclaration(op, t);
                    }

                    break;
                }

            case SheetEditOperation.InsertRule:
                {
                    CssNode container;
                    if (edit.Placement == RulePlacement.End)
                    {
                        container = edit.HasTarget ? t : _tree.Root;
                        if (edit.HasTarget)
                        {
                            string? bad = CheckBlockRule(t);
                            if (bad is not null)
                            {
                                return bad;
                            }
                        }
                    }
                    else
                    {
                        if (!SheetLayout.IsRule(t))
                        {
                            return "the reference is not a rule";
                        }

                        container = t.Parent;
                    }

                    op.Group = container.Index;
                    string indent = _lay.ChildIndent(container);
                    string? text = _lay.FormatRule(edit.Css!, indent, out string? error);
                    if (text is null)
                    {
                        return error;
                    }

                    op.InsertText = text;
                    op.InsertTree = CssTree.ParseRule(text);
                    PlanInsert(op, container, t, edit.Placement);
                    break;
                }

            case SheetEditOperation.RemoveRule:
                {
                    if (!SheetLayout.IsRule(t))
                    {
                        return "the target is not a rule";
                    }

                    op.Group = t.Parent.Index;
                    PlanRemove(op, t.Parent, [t]);
                    break;
                }

            case SheetEditOperation.ReplaceRule:
                {
                    if (!SheetLayout.IsRule(t))
                    {
                        return "the target is not a rule";
                    }

                    op.Group = t.Parent.Index;
                    string indent = _lay.LineIndent(t.Range.Start) ?? _lay.ChildIndent(t.Parent);
                    string? text = _lay.FormatRule(edit.Css!, indent, out string? error);
                    if (text is null)
                    {
                        return error;
                    }

                    op.InsertText = text;
                    op.InsertTree = CssTree.ParseRule(text);
                    op.Pieces.Add(NewPiece(op, t.Range.Start, t.Range.End, text));
                    break;
                }

            default:
                return "unknown stylesheet operation";
        }

        _ops.Add(op);
        return null;
    }

    private static SheetPiece NewPiece(Op op, int start, int end, string text, int rank = 0) =>
        new() { Start = start, End = end, Text = text, Edits = [op.Index], Rank = rank };

    private static string? CheckBlockRule(CssNode rule)
    {
        if (rule.Tree is null || !SheetLayout.IsRule(rule))
        {
            return "the target is not a rule";
        }

        if (!rule.HasBlock)
        {
            return "the rule has no block";
        }

        return null;
    }

    private static string? PropertyName(string property)
    {
        if (property.Length == 0)
        {
            return "the property name is empty";
        }

        for (int i = 0; i < property.Length; i++)
        {
            char c = property[i];
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-' or '_' || (i > 0 && c is >= '0' and <= '9');
            if (!ok)
            {
                return "the property name is not a CSS identifier: " + property;
            }
        }

        return null;
    }

    private static string? ProbeValue(string property, string value, bool important, Op op)
    {
        string v = value.Trim(' ', '\t', '\n', '\r', '\f');
        // The pair must parse as exactly one declaration with the importance asked for: this rejects values that end
        // the declaration early or carry their own !important.
        string probe = property + ":" + v + (important ? " !important" : string.Empty);
        CssTree check;
        try
        {
            check = CssTree.ParseDeclarationList(probe);
        }
        catch (CssLimitExceededException ex)
        {
            return ex.Message;
        }

        CssNode? only = null;
        int count = 0;
        foreach (CssNode n in check.Nodes)
        {
            count++;
            only ??= n;
        }

        if (count != 1 || only is not { } d || d.Kind != CssNodeKind.Declaration || d.IsImportant != important || v.Length == 0)
        {
            return "the value is not a single CSS declaration value: " + value;
        }

        op.Value = v;
        op.ValueNorm = SheetLayout.Norm(d.ValueText);
        return null;
    }

    // ---- local splices ----

    private static bool SameProperty(string a, string b) =>
        a.StartsWith("--", StringComparison.Ordinal)
            ? string.Equals(a, b, StringComparison.Ordinal)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void PlanSet(Op op, CssNode rule)
    {
        string property = op.Edit.Property!;
        bool important = op.Edit.Important;
        string value = op.Value!;
        CssNode? existing = null;
        CssNode? lastDecl = null;
        foreach (CssNode d in rule.Declarations)
        {
            lastDecl = d;
            if (SameProperty(d.Name, property))
            {
                existing = d;
            }
        }

        if (existing is { } e)
        {
            CssRange vr = e.ValueRange;
            string newValue = value;
            int end = vr.End;
            if (important && !e.IsImportant)
            {
                newValue += _lay.ImportantSeparator() + "!important";
            }
            else if (!important && e.IsImportant)
            {
                end = e.ImportantRange.End;
            }

            if (vr.Length == 0)
            {
                newValue = " " + newValue;
            }

            if (!_text.AsSpan(vr.Start, end - vr.Start).SequenceEqual(newValue))
            {
                op.Pieces.Add(NewPiece(op, vr.Start, end, newValue));
            }

            return;
        }

        string text = property + _lay.ColonSeparator(rule) + value + (important ? _lay.ImportantSeparator() + "!important" : string.Empty);
        if (lastDecl is { } last)
        {
            bool terminated = last.SemicolonRange.Length > 0;
            int start = _lay.WsBefore(last.Range.Start);
            string gap = _text[start..last.Range.Start];
            int at = last.Range.End;
            op.Pieces.Add(NewPiece(op, at, at, terminated ? gap + text + ";" : ";" + gap + text));
            return;
        }

        List<CssNode> kids = SheetLayout.Kids(rule);
        if (kids.Count > 0)
        {
            CssNode first = kids[0];
            int ws = _lay.WsBefore(first.Range.Start);
            string gap = _text[ws..first.Range.Start];
            op.Pieces.Add(NewPiece(op, first.Range.Start, first.Range.Start, text + ";" + gap));
            return;
        }

        string term = _lay.TerminateLast() ? ";" : string.Empty;
        CssRange cr = rule.ContentRange;
        string content = _text[cr.Start..cr.End];
        if (content.AsSpan().IndexOfAnyExcept(" \t\n\r\f") < 0)
        {
            if (content.AsSpan().IndexOfAny('\r', '\n') >= 0)
            {
                string closing = _lay.LineIndent(rule.Range.Start) ?? string.Empty;
                op.Pieces.Add(NewPiece(op, cr.Start, cr.End, _lay.NewLine + _lay.ChildIndent(rule) + text + term + _lay.NewLine + closing));
            }
            else
            {
                op.Pieces.Add(NewPiece(op, cr.Start, cr.End, content + text + term + content));
            }
        }
        else
        {
            op.Pieces.Add(NewPiece(op, cr.End, cr.End, text + term));
        }
    }

    private void PlanRemoveDeclaration(Op op, CssNode rule)
    {
        var matches = new List<CssNode>();
        foreach (CssNode d in rule.Declarations)
        {
            if (SameProperty(d.Name, op.Edit.Property!))
            {
                matches.Add(d);
            }
        }

        PlanRemove(op, rule, matches);
    }

    /// <summary>
    /// Deletes items of a container. A run of adjacent items, when something follows it, goes with the white space after
    /// it (so the white space before it keeps separating what remains); the last items of a container go with the white
    /// space before them instead.
    /// </summary>
    private void PlanRemove(Op op, CssNode container, List<CssNode> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        List<CssNode> kids = SheetLayout.Kids(container);
        var doomed = new HashSet<int>();
        foreach (CssNode n in items)
        {
            doomed.Add(n.Index);
        }

        int contentEnd = _lay.ContentEnd(container);
        int i = 0;
        while (i < kids.Count)
        {
            if (!doomed.Contains(kids[i].Index))
            {
                i++;
                continue;
            }

            int j = i;
            while (j + 1 < kids.Count && doomed.Contains(kids[j + 1].Index))
            {
                j++;
            }

            int start = kids[i].Range.Start;
            int end = kids[j].Range.End;
            int after = _lay.WsAfter(end);
            if (after < contentEnd)
            {
                end = after;
            }
            else
            {
                start = _lay.WsBefore(start);
            }

            op.Pieces.Add(NewPiece(op, start, end, string.Empty));
            i = j + 1;
        }
    }

    private void PlanInsert(Op op, CssNode container, CssNode reference, RulePlacement placement)
    {
        string x = op.InsertText!;
        List<CssNode> kids = SheetLayout.Kids(container);
        if (placement == RulePlacement.Before)
        {
            string gap = _lay.RuleGap(container, reference.Index, before: true);
            op.Pieces.Add(NewPiece(op, reference.Range.Start, reference.Range.Start, x + gap, 2));
            return;
        }

        CssNode? anchor = placement == RulePlacement.After ? reference : null;
        if (anchor is null && kids.Count > 0)
        {
            anchor = kids[^1];
        }

        if (anchor is { } a)
        {
            string gap = _lay.RuleGap(container, a.Index, before: false);
            int at = a.Range.End;
            string close = string.Empty;
            if ((a.Kind == CssNodeKind.AtRule && !a.HasBlock || a.Kind == CssNodeKind.Declaration) && a.SemicolonRange.Length == 0)
            {
                close = ";";
            }
            else if (a.HasBlock && !a.IsBlockClosed)
            {
                close = "}";
            }

            if (close.Length > 0)
            {
                SheetPiece cp = NewPiece(op, at, at, close, -1);
                cp.Anchor = a.Index;
                op.Pieces.Add(cp);
            }

            op.Pieces.Add(NewPiece(op, at, at, gap + x, placement == RulePlacement.After ? 1 : 3));
            return;
        }

        // Nothing in the container yet.
        if (container.Index == 0)
        {
            string text = _text;
            string lead = text.Length == 0 || text.EndsWith('\n') || text.EndsWith('\r') ? string.Empty : _lay.NewLine;
            op.Pieces.Add(NewPiece(op, text.Length, text.Length, lead + x + _lay.NewLine));
            return;
        }

        CssRange cr = container.ContentRange;
        string content = _text[cr.Start..cr.End];
        if (content.AsSpan().IndexOfAnyExcept(" \t\n\r\f") < 0)
        {
            if (content.AsSpan().IndexOfAny('\r', '\n') >= 0)
            {
                string closing = _lay.LineIndent(container.Range.Start) ?? string.Empty;
                op.Pieces.Add(NewPiece(op, cr.Start, cr.End, _lay.NewLine + _lay.ChildIndent(container) + x + _lay.NewLine + closing));
            }
            else
            {
                op.Pieces.Add(NewPiece(op, cr.Start, cr.End, content + x + content));
            }
        }
        else
        {
            op.Pieces.Add(NewPiece(op, cr.End, cr.End, x));
        }
    }

    // ---- pieces to text ----

    /// <summary>
    /// Orders the pieces and settles overlaps. Pieces that only delete merge when they touch or overlap; any other overlap
    /// (two edits changing the same bytes, an insertion inside a replaced stretch) makes the transaction invalid.
    /// </summary>
    private static string? Resolve(List<SheetPiece> pieces, out string? edit)
    {
        edit = null;
        var order = new List<(SheetPiece P, int Seq)>(pieces.Count);
        for (int i = 0; i < pieces.Count; i++)
        {
            order.Add((pieces[i], i));
        }

        order.Sort(static (a, b) =>
        {
            int c = a.P.Start.CompareTo(b.P.Start);
            if (c != 0)
            {
                return c;
            }

            c = (a.P.End == a.P.Start ? 0 : 1).CompareTo(b.P.End == b.P.Start ? 0 : 1);
            if (c != 0)
            {
                return c;
            }

            c = a.P.Rank.CompareTo(b.P.Rank);
            if (c != 0)
            {
                return c;
            }

            int ea = a.P.Edits.Count > 0 ? a.P.Edits[0] : -1;
            int eb = b.P.Edits.Count > 0 ? b.P.Edits[0] : -1;
            c = ea.CompareTo(eb);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });

        var merged = new List<SheetPiece>(pieces.Count);
        foreach ((SheetPiece p, _) in order)
        {
            if (merged.Count > 0)
            {
                SheetPiece prev = merged[^1];
                bool prevDelete = prev.Text.Length == 0 && prev.End > prev.Start;
                bool curDelete = p.Text.Length == 0 && p.End > p.Start;
                if (p.Rank == -1 && prev.Rank == -1 && p.Start == prev.Start && p.End == prev.End && p.Text == prev.Text)
                {
                    prev.Edits.AddRange(p.Edits);
                    continue;
                }

                if (p.Start < prev.End)
                {
                    if (prevDelete && curDelete)
                    {
                        prev.End = Math.Max(prev.End, p.End);
                        prev.Edits.AddRange(p.Edits);
                        continue;
                    }

                    edit = (p.Edits.Count > 0 ? p.Edits[0] : -1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return "edits " + string.Join(", ", prev.Edits) + " and " + string.Join(", ", p.Edits) + " change the same part of the stylesheet";
                }

                if (p.Start == prev.End && prevDelete && curDelete)
                {
                    prev.End = p.End;
                    prev.Edits.AddRange(p.Edits);
                    continue;
                }
            }

            merged.Add(p);
        }

        pieces.Clear();
        pieces.AddRange(merged);
        return null;
    }

    // ---- rule events ----

    private static List<Ev> EventsOf(CssTree tree)
    {
        var list = new List<Ev>();
        foreach (CssNode n in tree.Nodes)
        {
            EmitSubtree(list, n, tree);
        }

        return list;
    }

    private static string RuleName(CssNode n) => n.Kind == CssNodeKind.AtRule ? n.Name : string.Empty;

    /// <summary>Appends the events of a node and all its descendants, walking with an explicit stack.</summary>
    private static void EmitSubtree(List<Ev> list, CssNode top, CssTree tree)
    {
        Item[] items = tree.Items;
        var stack = new Stack<(int Item, int Open)>();
        int cur = top.Index;
        while (true)
        {
            if (EmitEnter(list, tree, cur))
            {
                stack.Push((cur, list.Count - 1));
                cur = items[cur].FirstChild;
                continue;
            }

            while (true)
            {
                if (cur == top.Index)
                {
                    return;
                }

                int next = items[cur].NextSibling;
                if (next >= 0)
                {
                    cur = next;
                    break;
                }

                (int parent, int open) = stack.Pop();
                CloseRule(list, open, parent);
                if (parent == top.Index)
                {
                    return;
                }

                cur = parent;
            }
        }
    }

    // Adds the event for one node; true when it is a rule with children still to come.
    private static bool EmitEnter(List<Ev> list, CssTree tree, int cur)
    {
        var n = new CssNode(tree, cur);
        ref readonly Item it = ref tree.Items[cur];
        CssRange r = n.Range;
        if (it.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule)
        {
            if ((it.Flags & Item.HasBlock) != 0)
            {
                list.Add(new Ev { Type = EvType.Open, A = RuleName(n), B = SheetLayout.Norm(n.PreludeText), Orig = cur, Unclosed = (it.Flags & Item.BlockClosed) == 0, RawStart = r.Start, RawEnd = r.End, Src = tree.Text });
                if (it.FirstChild >= 0)
                {
                    return true;
                }

                CloseRule(list, list.Count - 1, cur);
                return false;
            }

            list.Add(new Ev { Type = EvType.Leaf, A = RuleName(n), B = SheetLayout.Norm(n.PreludeText), Orig = cur, RawStart = r.Start, RawEnd = r.End, Src = tree.Text });
        }
        else if (it.Kind == CssNodeKind.Declaration)
        {
            list.Add(new Ev { Type = EvType.Decl, A = n.Name, B = SheetLayout.Norm(n.ValueText), Flag = n.IsImportant, Orig = cur, RawStart = r.Start, RawEnd = r.End, Src = tree.Text });
        }
        else
        {
            list.Add(new Ev { Type = EvType.Invalid, A = string.Empty, B = SheetLayout.Norm(n.Raw), Orig = cur, RawStart = r.Start, RawEnd = r.End, Src = tree.Text });
        }

        return false;
    }

    private static void CloseRule(List<Ev> list, int open, int item)
    {
        list.Add(new Ev { Type = EvType.Close, RawStart = -1, A = string.Empty, B = string.Empty, Orig = item });
        Ev o = list[open];
        o.Skip = list.Count - 1;
        list[open] = o;
    }

    private static bool Same(List<Ev> a, List<Ev> b, out string? why)
    {
        why = null;
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            Ev x = a[i];
            Ev y = b[i];
            if (x.Type != y.Type || x.Flag != y.Flag || !string.Equals(x.A, y.A, StringComparison.Ordinal) || !string.Equals(x.B, y.B, StringComparison.Ordinal))
            {
                why = "the output re-parses differently at item " + i + ": expected " + x.Type + " " + Shorten(x.A + " " + x.B) + ", found " + y.Type + " " + Shorten(y.A + " " + y.B);
                return false;
            }
        }

        if (a.Count != b.Count)
        {
            why = "the output re-parses to " + b.Count + " items; expected " + a.Count;
            return false;
        }

        return true;
    }

    private static string Shorten(string s) => s.Length <= 50 ? s : s[..50] + "...";

    // ---- the narrow verification window ----

    // Bounds the narrow check by what the whole-sheet parse would have to enforce too (tokens at most one per character).
    private static readonly int NarrowLimit = Math.Min(CssParseOptions.Default.MaxInputLength, CssParseOptions.Default.MaxTokens);

    private int[] Top()
    {
        if (_top is null)
        {
            var list = new List<int>();
            for (int c = _tree.Items[0].FirstChild; c >= 0; c = _tree.Items[c].NextSibling)
            {
                list.Add(c);
            }

            _top = [.. list];
        }

        return _top;
    }

    // The position among the top-level rules of an original item (items are numbered in document order).
    private int TopPosition(int item) => Array.BinarySearch(Top(), item);

    /// <summary>
    /// The stretch of whole top-level rules that holds every piece of the ops, widened by one rule on each side (positions
    /// <paramref name="lo"/> to <paramref name="hi"/>), and its text range. Soundness of checking only this stretch: the
    /// text before it is unchanged and every rule there ended at its own delimiter in the original parse; the text from
    /// the stretch's last rule on is unchanged too, and the stretch ends either at the end of the sheet or after an
    /// untouched rule that ended at its delimiter. A CSS rule list is parsed rule by rule, so the full parse equals the
    /// rules before, the stretch parsed alone, and the rules after, provided the stretch alone gives the expected rules.
    /// </summary>
    private bool TryWindow(List<Op> ops, out int lo, out int hi, out int start, out int end)
    {
        lo = hi = start = end = 0;
        int[] top = Top();
        if (_wholeSheet || top.Length == 0)
        {
            return false;
        }

        int first = int.MaxValue;
        int last = -1;
        foreach (Op op in ops)
        {
            foreach (SheetPiece p in op.Pieces)
            {
                first = Math.Min(first, p.Start);
                last = Math.Max(last, p.End);
            }

            // A rule rewritten from the expected events covers its whole top-level rule at most.
            int x = op.Group;
            if (x > 0)
            {
                while (ParentOf(x) > 0)
                {
                    x = ParentOf(x);
                }

                CssRange r = new CssNode(_tree, x).Range;
                first = Math.Min(first, r.Start);
                last = Math.Max(last, r.End);
            }
        }

        if (last < 0)
        {
            return false;
        }

        // First rule that ends at or after the first piece; last rule that starts at or before the last piece.
        int a = 0;
        while (a < top.Length && new CssNode(_tree, top[a]).Range.End < first)
        {
            a++;
        }

        int b = top.Length - 1;
        while (b > 0 && new CssNode(_tree, top[b]).Range.Start > last)
        {
            b--;
        }

        lo = Math.Max(0, Math.Min(a, top.Length - 1) - 1);
        hi = Math.Min(top.Length - 1, b + 1);
        if (hi < lo)
        {
            return false;
        }

        start = lo == 0 ? 0 : new CssNode(_tree, top[lo]).Range.Start;
        end = hi == top.Length - 1 ? _text.Length : new CssNode(_tree, top[hi]).Range.End;
        return true;
    }

    // ---- the expected events ----

    private List<Ev> BuildExpected(List<Op> ops, out Dictionary<int, int> openAt, int windowLo, int windowHi)
    {
        openAt = [];
        var byGroup = new Dictionary<int, List<Op>>();
        var touched = new HashSet<int>();
        foreach (Op op in ops)
        {
            if (!byGroup.TryGetValue(op.Group, out List<Op>? l))
            {
                l = [];
                byGroup[op.Group] = l;
            }

            l.Add(op);
            for (int x = op.Group; ; x = ParentOf(x))
            {
                if (!touched.Add(x) || x == 0)
                {
                    break;
                }
            }
        }

        var list = new List<Ev>();
        var frames = new Stack<(List<Entry> Entries, int Pos, int Open, int Item)>();
        List<Entry> top = EntriesOf(0, byGroup);
        if (windowLo >= 0)
        {
            // Only the rules of the window: inserted rules always belong to it, original ones by position.
            top = top.FindAll(e => e.Kind != EntryKind.Orig || TopPosition(e.Node) is var pos && pos >= windowLo && pos <= windowHi);
        }

        frames.Push((top, 0, -1, 0));
        while (frames.Count > 0)
        {
            _ct.ThrowIfCancellationRequested();
            (List<Entry> entries, int pos, int open, int item) = frames.Pop();
            if (pos >= entries.Count)
            {
                if (open >= 0)
                {
                    CloseRule(list, open, item);
                }

                continue;
            }

            frames.Push((entries, pos + 1, open, item));
            Entry e = entries[pos];
            switch (e.Kind)
            {
                case EntryKind.Orig:
                    {
                        var n = new CssNode(_tree, e.Node);
                        if (SheetLayout.IsRule(n) && n.HasBlock && touched.Contains(e.Node))
                        {
                            int at = list.Count;
                            openAt[e.Node] = at;
                            list.Add(new Ev { Type = EvType.Open, A = RuleName(n), B = SheetLayout.Norm(n.PreludeText), Orig = e.Node, RawStart = -1, Unclosed = !n.IsBlockClosed });
                            frames.Push((EntriesOf(e.Node, byGroup), 0, at, e.Node));
                        }
                        else
                        {
                            EmitSubtree(list, n, _tree);
                        }

                        break;
                    }

                case EntryKind.NewDecl:
                case EntryKind.ModDecl:
                    list.Add(new Ev { Type = EvType.Decl, A = e.Name, B = e.Norm, Flag = e.Important, Orig = -1, RawStart = -1, Canon = e.Value });
                    break;
                case EntryKind.Inserted:
                    {
                        CssTree it = e.Op!.InsertTree!;
                        foreach (CssNode n in it.Nodes)
                        {
                            EmitSubtree(list, n, it);
                        }

                        break;
                    }
            }
        }

        return list;
    }

    private List<Entry> EntriesOf(int container, Dictionary<int, List<Op>> byGroup)
    {
        var entries = new List<Entry>();
        var cn = new CssNode(_tree, container);
        for (int c = _tree.Items[container].FirstChild; c >= 0; c = _tree.Items[c].NextSibling)
        {
            entries.Add(new Entry { Kind = EntryKind.Orig, Node = c });
        }

        if (!byGroup.TryGetValue(container, out List<Op>? ops))
        {
            return entries;
        }

        _ = cn;
        // Declarations first: they are placed relative to declarations only.
        foreach (Op op in ops)
        {
            if (op.Edit.Operation is not (SheetEditOperation.SetDeclaration or SheetEditOperation.RemoveDeclaration))
            {
                continue;
            }

            string prop = op.Edit.Property!;
            if (op.Edit.Operation == SheetEditOperation.RemoveDeclaration)
            {
                entries.RemoveAll(e => IsDeclNamed(e, prop));
                continue;
            }

            int found = entries.FindLastIndex(e => IsDeclNamed(e, prop));
            string canon = CanonicalDecl(prop, op.Value!, op.Edit.Important, container);
            if (found >= 0)
            {
                Entry old = entries[found];
                entries[found] = new Entry { Kind = EntryKind.ModDecl, Node = old.Node, Name = old.Kind == EntryKind.Orig ? new CssNode(_tree, old.Node).Name : old.Name, Value = canon, Norm = op.ValueNorm!, Important = op.Edit.Important };
            }
            else
            {
                int lastDecl = entries.FindLastIndex(e => IsDecl(e));
                entries.Insert(lastDecl + 1, new Entry { Kind = EntryKind.NewDecl, Name = prop, Value = canon, Norm = op.ValueNorm!, Important = op.Edit.Important });
            }
        }

        var before = new Dictionary<int, List<Entry>>();
        var after = new Dictionary<int, List<Entry>>();
        var removed = new HashSet<int>();
        var replaced = new Dictionary<int, Entry>();
        var end = new List<Entry>();
        foreach (Op op in ops)
        {
            switch (op.Edit.Operation)
            {
                case SheetEditOperation.InsertRule:
                    {
                        var ins = new Entry { Kind = EntryKind.Inserted, Op = op };
                        switch (op.Edit.Placement)
                        {
                            case RulePlacement.Before: Add(before, op.Target, ins); break;
                            case RulePlacement.After: Add(after, op.Target, ins); break;
                            default: end.Add(ins); break;
                        }

                        break;
                    }

                case SheetEditOperation.RemoveRule:
                    removed.Add(op.Target);
                    break;
                case SheetEditOperation.ReplaceRule:
                    replaced[op.Target] = new Entry { Kind = EntryKind.Inserted, Op = op };
                    break;
                default:
                    break;
            }
        }

        if (before.Count + after.Count + removed.Count + replaced.Count + end.Count == 0)
        {
            return entries;
        }

        var result = new List<Entry>(entries.Count + end.Count);
        foreach (Entry e in entries)
        {
            if (e.Kind != EntryKind.Orig)
            {
                result.Add(e);
                continue;
            }

            if (before.TryGetValue(e.Node, out List<Entry>? b))
            {
                result.AddRange(b);
            }

            if (removed.Contains(e.Node))
            {
                // gone
            }
            else if (replaced.TryGetValue(e.Node, out Entry r))
            {
                result.Add(r);
            }
            else
            {
                result.Add(e);
            }

            if (after.TryGetValue(e.Node, out List<Entry>? a))
            {
                result.AddRange(a);
            }
        }

        result.AddRange(end);
        return result;
    }

    private static void Add(Dictionary<int, List<Entry>> d, int key, Entry e)
    {
        if (!d.TryGetValue(key, out List<Entry>? l))
        {
            l = [];
            d[key] = l;
        }

        l.Add(e);
    }

    private bool IsDecl(Entry e) => e.Kind != EntryKind.Orig || _tree.Items[e.Node].Kind == CssNodeKind.Declaration;

    private bool IsDeclNamed(Entry e, string prop)
    {
        if (!IsDecl(e))
        {
            return false;
        }

        string name = e.Kind == EntryKind.Orig ? new CssNode(_tree, e.Node).Name : e.Name;
        return SameProperty(name, prop);
    }

    private string CanonicalDecl(string prop, string value, bool important, int container) =>
        prop + _lay.ColonSeparator(new CssNode(_tree, container)) + value + (important ? _lay.ImportantSeparator() + "!important" : string.Empty) + ";";

    // ---- canonical rewrite of one rule ----

    /// <summary>
    /// Writes the rule whose Open event is at <paramref name="openIndex"/> from the expected events: untouched children are
    /// copied from their source, touched rules and changed declarations are laid out afresh, one item per line (or on the
    /// same line when the rule's block was).
    /// </summary>
    private string Render(List<Ev> ev, int openIndex)
    {
        var sb = new StringBuilder();
        var rule = new CssNode(_tree, ev[openIndex].Orig);
        string baseIndent = _lay.LineIndent(rule.Range.Start) ?? string.Empty;
        var stack = new Stack<(bool Inline, int Depth)>();
        int i = openIndex;
        int depth = 0;
        while (i < ev.Count)
        {
            Ev e = ev[i];
            switch (e.Type)
            {
                case EvType.Open when e.RawStart >= 0:
                    sb.Append(e.Src!, e.RawStart, e.RawEnd - e.RawStart);
                    if (e.Unclosed)
                    {
                        sb.Append('}');
                    }

                    i = e.Skip + 1;
                    Separator(sb, stack, baseIndent, depth, ev, i);
                    continue;
                case EvType.Open:
                    {
                        var n = new CssNode(_tree, e.Orig);
                        int hs = n.Range.Start;
                        int he = n.BlockRange.Start + 1;
                        sb.Append(_text, hs, he - hs);
                        bool inlineBlock = !_lay.HasNewLine(n.ContentRange.Start, n.ContentRange.End);
                        stack.Push((inlineBlock, depth));
                        depth++;
                        bool empty = ev[i + 1].Type == EvType.Close;
                        if (!empty)
                        {
                            sb.Append(inlineBlock ? " " : _lay.NewLine + baseIndent + Repeat(depth));
                        }

                        i++;
                        continue;
                    }

                case EvType.Close:
                    {
                        (bool inlineBlock, int d) = stack.Pop();
                        depth = d;
                        bool empty = i > 0 && ev[i - 1].Type == EvType.Open && ev[i - 1].RawStart < 0;
                        if (!empty)
                        {
                            sb.Append(inlineBlock ? " " : _lay.NewLine + baseIndent + Repeat(depth));
                        }

                        sb.Append('}');
                        i++;
                        if (stack.Count == 0)
                        {
                            return sb.ToString();
                        }

                        Separator(sb, stack, baseIndent, depth + 0, ev, i);
                        continue;
                    }

                case EvType.Decl:
                    if (e.Canon is not null)
                    {
                        sb.Append(e.Canon);
                    }
                    else
                    {
                        sb.Append(e.Src!, e.RawStart, e.RawEnd - e.RawStart);
                        if (e.Src![e.RawEnd - 1] != ';')
                        {
                            sb.Append(';');
                        }
                    }

                    i++;
                    Separator(sb, stack, baseIndent, depth, ev, i);
                    continue;
                default:
                    sb.Append(e.Src!, e.RawStart, e.RawEnd - e.RawStart);
                    if (e.Type == EvType.Leaf && e.Src![e.RawEnd - 1] != ';')
                    {
                        sb.Append(';');
                    }

                    i++;
                    Separator(sb, stack, baseIndent, depth, ev, i);
                    continue;
            }
        }

        return sb.ToString();
    }

    private string Repeat(int depth) => string.Concat(System.Linq.Enumerable.Repeat(_lay.Unit, depth));

    // Between two items of the rule being written; nothing before a closing brace (Close writes its own line break).
    private void Separator(StringBuilder sb, Stack<(bool Inline, int Depth)> stack, string baseIndent, int depth, List<Ev> ev, int next)
    {
        if (stack.Count == 0 || next >= ev.Count || ev[next].Type == EvType.Close)
        {
            return;
        }

        bool inlineBlock = stack.Peek().Inline;
        sb.Append(inlineBlock ? " " : _lay.NewLine + baseIndent + Repeat(depth));
    }
}
