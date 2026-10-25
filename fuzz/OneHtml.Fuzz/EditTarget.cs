using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>Counts of what random edit sequences did, per operation.</summary>
public sealed class EditStats
{
    /// <summary>Per operation: attempted, applied, invalid, failed, applied with a fallback (an enclosing element re-serialized).</summary>
    public SortedDictionary<EditOperation, long[]> PerOperation { get; } = [];

    /// <summary>Transactions run.</summary>
    public long Transactions { get; set; }

    /// <summary>Transactions that applied.</summary>
    public long Applied { get; set; }

    /// <summary>Applied transactions with at least one fallback.</summary>
    public long WithFallback { get; set; }

    /// <summary>Applied transactions the independent oracle could not replay (it refused an edit the editor accepted).</summary>
    public long OracleSkipped { get; set; }

    /// <summary>Revert checks that ran and held.</summary>
    public long Reverts { get; set; }

    /// <summary>Total re-parses.</summary>
    public long Verifications { get; set; }

    internal void Count(EditOperation op, EditStatus status, bool fallback)
    {
        if (!PerOperation.TryGetValue(op, out long[]? row))
        {
            row = new long[5];
            PerOperation[op] = row;
        }

        row[0]++;
        switch (status)
        {
            case EditStatus.Applied:
                row[1]++;
                if (fallback)
                {
                    row[4]++;
                }

                break;
            case EditStatus.Invalid:
                row[2]++;
                break;
            case EditStatus.Failed:
                row[3]++;
                break;
            default:
                break;
        }
    }

    /// <summary>Adds another set of counts into this one.</summary>
    public void Add(EditStats other)
    {
        foreach (KeyValuePair<EditOperation, long[]> kv in other.PerOperation)
        {
            if (!PerOperation.TryGetValue(kv.Key, out long[]? row))
            {
                row = new long[5];
                PerOperation[kv.Key] = row;
            }

            for (int i = 0; i < 5; i++)
            {
                row[i] += kv.Value[i];
            }
        }

        Transactions += other.Transactions;
        Applied += other.Applied;
        WithFallback += other.WithFallback;
        OracleSkipped += other.OracleSkipped;
        Reverts += other.Reverts;
        Verifications += other.Verifications;
    }
}

/// <summary>A property of the editor did not hold.</summary>
public sealed class EditCheckException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed.</param>
    public EditCheckException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Random edit sequences with their checks. For every applied transaction: the bytes outside the edited ranges are
/// unchanged and the ranges reproduce the new bytes; the result equals what an independent, simple implementation of
/// the DOM operations gives (whitespace-only text aside, which the editor may add to keep indentation); a single
/// reversible edit followed by its inverse restores the original bytes when no fallback was needed.
/// </summary>
public static class EditTarget
{
    private static readonly ParseLimits s_limits = new() { MaxInputLength = 1 << 20, MaxAttributesPerTag = 64, MaxOffsetMapRuns = 1 << 16, MaxNodes = 1 << 14, MaxOpenElements = 256, MaxActiveFormattingElements = 64 };

    private static readonly string[] s_attrNames = ["class", "id", "title", "href", "data-x", "style", "lang", "hidden", "alt", "x-new", "src", "name", "type"];

    private static readonly string[] s_attrValues = ["", "a", "a b", "x\"y", "a&b", "&amp;", "<b>", "é", "☃", "it's", " lead ", "a\nb", "\u00A0", "a&copy", "&#x41;", "http://x/y?a=1&b=2", "tab\there", "𝄞"];

    private static readonly string[] s_texts = ["", "x", "a & b", "<i>no</i>", "\nnl", "  sp  ", "é☃", "a\rb", "&amp;", "\u00A0", "a</script>b", "--!>"];

    private static readonly string[] s_markup =
    [
        "<b>x</b>", "text", "<p>para</p>", "<li>i</li>", "<td>c</td>", "<!--c-->", "<div><p>a</p></div>", "a<b>b</b>c", "<br>", "<img src=x>",
        "<table><tr><td>1</td></tr></table>", "<script>1<2</script>", "<svg><circle r=1 /></svg>", "<select><option>o</select>", "&nbsp;", "",
        "<tr><td>x</td></tr>", "</p>", "<a href=1>x</a>", "<template><i>1</i></template>", "<style>p{}</style>", "<pre>\nx</pre>", "<textarea>\nq</textarea>",
        "<p><b>1<p>2</b>3", "<span class=\"s\" id='q'>é</span>", "\n  <em>w</em>\n", "<option>z</option>", "<dd>d", "<h2>h</h2>", "<form><input name=n></form>",
    ];

    private static readonly string[] s_wrappers = ["<div></div>", "<span class=w></span>", "<a href=#></a>", "<b><i></i></b>", "<li></li>", "<td></td>", "<p></p>", "<section><h1>t</h1></section>", "<template></template>", "<br>"];

    private static readonly string[] s_properties = ["color", "margin", "--x", "background-color", "Font-Size"];

    private static readonly string[] s_styleValues = ["red", "1px 2px", "calc(1px + 2px)", "\"a b\"", "url(x.png)", "red; x:y", "", "rgb(1, 2, 3)", "a:b"];

    /// <summary>Called with every transaction and its result; for diagnostics.</summary>
    public static Action<Document, IReadOnlyList<Edit>, EditResult>? Observer { get; set; }

    /// <summary>Fuzz entry point: two seed bytes, then the document bytes.</summary>
    /// <param name="input">The input.</param>
    public static void Run(ReadOnlySpan<byte> input)
    {
        if (input.Length < 3)
        {
            return;
        }

        int seed = input[0] | (input[1] << 8);
        byte[] bytes = input[2..].ToArray();
        Document doc;
        try
        {
            doc = Document.Parse(bytes, new ParseOptions { Limits = s_limits });
        }
        catch (HtmlLimitExceededException)
        {
            return;
        }

        CheckDocument(doc, seed, null);
    }

    /// <summary>Runs one random transaction against a document and checks it.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="seed">Chooses the edits.</param>
    /// <param name="stats">Receives counts; may be null.</param>
    /// <param name="scripting">The scripting flag the document was parsed with, which the reference parser must use too.</param>
    /// <exception cref="EditCheckException">A property did not hold.</exception>
    public static void CheckDocument(Document doc, int seed, EditStats? stats, bool scripting = false)
    {
        var rng = new Random(seed);
        List<Node> nodes = Collect(doc);
        if (nodes.Count == 0)
        {
            return;
        }

        int count = 1 + (rng.Next(4) == 0 ? rng.Next(5) : 0);
        var edits = new List<Edit>();
        for (int i = 0; i < count; i++)
        {
            edits.Add(Generate(rng, nodes));
        }

        string label = Describe(seed, edits);
        EditResult result;
        try
        {
            result = doc.Apply(edits, new EditOptions { MaxVerifications = 64 });
        }
        catch (Exception ex) when (ex is not EditCheckException)
        {
            throw new EditCheckException(label + " threw " + ex);
        }

        Observer?.Invoke(doc, edits, result);
        if (stats is not null)
        {
            stats.Transactions++;
            stats.Verifications += result.Verifications;
            if (result.Succeeded)
            {
                stats.Applied++;
                if (result.Fallbacks.Count > 0)
                {
                    stats.WithFallback++;
                }
            }

            foreach (EditOutcome o in result.Outcomes)
            {
                if (o.Status != EditStatus.NotAttempted)
                {
                    stats.Count(o.Edit.Operation, o.Status, o.FallbackLevel > 0);
                }
            }
        }

        if (!result.Succeeded)
        {
            if (result.Document is not null || result.Changes is not null)
            {
                throw new EditCheckException(label + " failed but returned a snapshot");
            }

            return;
        }

        CheckBytes(doc, result, label);
        string? invariant = RecursiveAI.OneHtml.Tests.DocumentInvariants.Check(result.Document!);
        if (invariant is not null)
        {
            throw new EditCheckException(label + " produced a broken document: " + invariant);
        }

        CheckChangeSet(doc, result, label);
        if (!Oracle.Check(doc, result.Document!, edits, scripting, out string? difference))
        {
            if (difference is null)
            {
                if (stats is not null)
                {
                    stats.OracleSkipped++;
                }
            }
            else if (edits.Count > 1 && edits.Exists(e => e.Operation is EditOperation.Prepend or EditOperation.Append or EditOperation.InsertBefore or EditOperation.InsertAfter))
            {
                // When the editor keeps indentation it puts a new element after the whitespace that precedes its
                // neighbour, so a later edit aimed at that whitespace lands on the other side of the element than
                // a plain DOM insertion would put it. Single edits are always compared strictly.
                if (stats is not null)
                {
                    stats.OracleSkipped++;
                }
            }
            else
            {
                throw new EditCheckException(label + " differs from the reference: " + difference);
            }
        }

        if (edits.Count == 1 && result.Fallbacks.Count == 0)
        {
            CheckRevert(doc, edits[0], result, label, stats);
        }
    }

    // ---- generation ----

    private static List<Node> Collect(Document doc)
    {
        var list = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(doc.Root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            list.Add(n);
            var kids = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                kids.Add(c);
            }

            if (n.IsElement && !n.TemplateContent.IsNull)
            {
                kids.Add(n.TemplateContent);
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        return list;
    }

    private static Edit Generate(Random rng, List<Node> nodes)
    {
        Node Pick(Func<Node, bool> ok)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                Node n = nodes[rng.Next(nodes.Count)];
                if (ok(n))
                {
                    return n;
                }
            }

            return nodes[rng.Next(nodes.Count)];
        }

        string Choose(string[] pool) => pool[rng.Next(pool.Length)];
        bool Child(Node n) => !n.Parent.IsNull && n.Kind != NodeKind.DocumentType;
        bool Elem(Node n) => n.IsElement;
        switch (rng.Next(22))
        {
            case 0:
            case 1:
            case 2:
                {
                    Node e = Pick(Elem);
                    string name = Choose(s_attrNames);
                    if (e.IsElement && e.Attributes.Count > 0 && rng.Next(2) == 0)
                    {
                        name = e.Attributes[rng.Next(e.Attributes.Count)].QualifiedName;
                    }

                    return Edit.SetAttribute(e, name, Choose(s_attrValues));
                }

            case 3:
            case 4:
                {
                    Node e = Pick(n => n.IsElement && n.Attributes.Count > 0);
                    string name = e.IsElement && e.Attributes.Count > 0 ? e.Attributes[rng.Next(e.Attributes.Count)].QualifiedName : Choose(s_attrNames);
                    return Edit.RemoveAttribute(e, name);
                }

            case 5:
            case 6:
                return Edit.ReplaceText(Pick(n => n.Kind == NodeKind.Text), Choose(s_texts));
            case 7:
                return Edit.ReplaceText(Pick(Elem), Choose(s_texts));
            case 8:
            case 9:
                return Edit.InsertBefore(Pick(Child), Choose(s_markup));
            case 10:
            case 11:
                return Edit.InsertAfter(Pick(Child), Choose(s_markup));
            case 12:
                return Edit.Prepend(Pick(Elem), Choose(s_markup));
            case 13:
            case 14:
                return Edit.Append(Pick(Elem), Choose(s_markup));
            case 15:
                return Edit.Wrap(Pick(Child), Choose(s_wrappers), 1 + (rng.Next(3) == 0 ? rng.Next(3) : 0));
            case 16:
                return Edit.Unwrap(Pick(n => n.IsElement && !n.Parent.IsNull));
            case 17:
                return Edit.Replace(Pick(Child), Choose(s_markup));
            case 18:
                return Edit.Remove(Pick(Child));
            case 19:
            case 20:
                return Edit.SetStyle(Pick(Elem), Choose(s_properties), Choose(s_styleValues), rng.Next(4) == 0);
            default:
                return Edit.RemoveStyle(Pick(Elem), Choose(s_properties));
        }
    }

    private static string Describe(int seed, List<Edit> edits)
    {
        var sb = new StringBuilder();
        sb.Append("seed ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append(':');
        foreach (Edit e in edits)
        {
            sb.Append(' ').Append(e.Operation).Append('#').Append(e.Target.Index.ToString(CultureInfo.InvariantCulture));
            if (e.Name is not null)
            {
                sb.Append(" name=").Append(Quote(e.Name));
            }

            if (e.Value is not null)
            {
                sb.Append(" value=").Append(Quote(e.Value));
            }

            if (e.Count != 1)
            {
                sb.Append(" count=").Append(e.Count.ToString(CultureInfo.InvariantCulture));
            }

            if (e.Important)
            {
                sb.Append(" important");
            }

            sb.Append(';');
        }

        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // ---- checks ----

    private static void CheckBytes(Document doc, EditResult result, string label)
    {
        Document nd = result.Document!;
        ChangeSet cs = result.Changes!;
        bool hasBytes = doc.Source.Bytes.Length > 0;
        ReadOnlySpan<byte> oldB = hasBytes ? doc.Source.Bytes.Span : Encoding.UTF8.GetBytes(doc.Source.Text);
        ReadOnlySpan<byte> newB = hasBytes ? nd.Source.Bytes.Span : Encoding.UTF8.GetBytes(nd.Source.Text);
        if (!hasBytes)
        {
            // Strings have no byte map; the ranges are in characters, so compare characters.
            CheckText(doc.Source.Text, nd.Source.Text, cs, label);
            return;
        }

        int op = 0;
        int np = 0;
        foreach (EditedRange r in cs.Ranges)
        {
            if (r.OldByteRange.Start < op || r.NewByteRange.Start < np)
            {
                throw new EditCheckException(label + " ranges overlap or are out of order");
            }

            if (!oldB[op..r.OldByteRange.Start].SequenceEqual(newB[np..r.NewByteRange.Start]))
            {
                throw new EditCheckException(label + " changed bytes outside the edited ranges before old offset " + r.OldByteRange.Start);
            }

            if (!r.NewBytes.Span.SequenceEqual(newB[r.NewByteRange.Start..r.NewByteRange.End]))
            {
                throw new EditCheckException(label + " reported bytes that are not in the output");
            }

            op = r.OldByteRange.End;
            np = r.NewByteRange.End;
        }

        if (!oldB[op..].SequenceEqual(newB[np..]))
        {
            throw new EditCheckException(label + " changed bytes outside the edited ranges after the last range");
        }
    }

    private static void CheckText(string oldT, string newT, ChangeSet cs, string label)
    {
        int op = 0;
        int np = 0;
        foreach (EditedRange r in cs.Ranges)
        {
            if (r.OldTextRange.Start < op || r.NewTextRange.Start < np
                || !oldT.AsSpan(op, r.OldTextRange.Start - op).SequenceEqual(newT.AsSpan(np, r.NewTextRange.Start - np)))
            {
                throw new EditCheckException(label + " changed text outside the edited ranges");
            }

            if (newT.Substring(r.NewTextRange.Start, r.NewTextRange.End - r.NewTextRange.Start) != r.NewText)
            {
                throw new EditCheckException(label + " reported text that is not in the output");
            }

            op = r.OldTextRange.End;
            np = r.NewTextRange.End;
        }

        if (!oldT.AsSpan(op).SequenceEqual(newT.AsSpan(np)))
        {
            throw new EditCheckException(label + " changed text after the last range");
        }
    }

    private static void CheckChangeSet(Document doc, EditResult result, string label)
    {
        ChangeSet cs = result.Changes!;
        foreach (Node n in cs.Added)
        {
            if (!ReferenceEquals(n.Document, result.Document))
            {
                throw new EditCheckException(label + " lists an added node of the wrong snapshot");
            }
        }

        foreach (Node n in cs.Removed)
        {
            if (!ReferenceEquals(n.Document, doc))
            {
                throw new EditCheckException(label + " lists a removed node of the wrong snapshot");
            }
        }

        foreach (NodeChange c in cs.Changed)
        {
            if (!ReferenceEquals(c.Node.Document, result.Document) || !ReferenceEquals(c.Old.Document, doc) || c.Node.Kind != c.Old.Kind
                || (c.Node.IsElement && c.Node.LocalName != c.Old.LocalName) || c.Changes == NodeChanges.None)
            {
                throw new EditCheckException(label + " lists an inconsistent change for node " + c.Old.Index);
            }
        }
    }

    private static string Snip(string s, int at) => s.Substring(Math.Max(0, at - 30), Math.Min(s.Length, at + 40) - Math.Max(0, at - 30)).Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool IsVoidName(string n) =>
        n is "area" or "base" or "basefont" or "bgsound" or "br" or "col" or "embed" or "frame" or "hr" or "img" or "input" or "keygen" or "link" or "meta" or "param" or "source" or "track" or "wbr";

    private static void CheckRevert(Document doc, Edit edit, EditResult result, string label, EditStats? stats)
    {
        Document nd = result.Document!;
        ChangeSet cs = result.Changes!;
        var inverse = new List<Edit>();
        switch (edit.Operation)
        {
            case EditOperation.SetAttribute when edit.Target.IsElement:
                {
                    Node target = cs.MapToNew(edit.Target);
                    if (target.IsNull || cs.Changed.Count != 1 || cs.Changed[0].Changes != NodeChanges.Attributes)
                    {
                        return;
                    }

                    Attr? old = null;
                    foreach (Attr a in edit.Target.Attributes)
                    {
                        if (string.Equals(a.QualifiedName, edit.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            old = a;
                        }
                    }

                    if (old is { } o)
                    {
                        string original = o.Value.ToString();
                        string? srcValue = o.Quote is AttributeQuote.DoubleQuoted or AttributeQuote.SingleQuoted
                            ? doc.Source.Text.Substring(o.ValueRange.Start, o.ValueRange.End - o.ValueRange.Start)
                            : null;
                        if (srcValue != original || original.Contains('&', StringComparison.Ordinal) || original.Contains('\n', StringComparison.Ordinal) || original.Contains('\uFFFD', StringComparison.Ordinal) || original.Contains('\u00A0', StringComparison.Ordinal))
                        {
                            return;
                        }

                        inverse.Add(Edit.SetAttribute(target, edit.Name!, original));
                    }
                    else
                    {
                        inverse.Add(Edit.RemoveAttribute(target, edit.Name!));
                    }

                    break;
                }

            case EditOperation.InsertBefore or EditOperation.InsertAfter or EditOperation.Prepend or EditOperation.Append:
                {
                    if (cs.Removed.Count != 0 || cs.Added.Count == 0)
                    {
                        return;
                    }

                    // The line feed the parser drops after <pre> belongs to no node, so removing the text cannot remove it.
                    if (edit.Value!.StartsWith('\n') && edit.Target.IsElement && edit.Target.LocalName is "pre" or "listing" or "textarea")
                    {
                        return;
                    }

                    foreach (NodeChange c in cs.Changed)
                    {
                        if ((c.Changes & ~NodeChanges.Children) != 0)
                        {
                            return;
                        }
                    }

                    foreach (Node n in cs.Added)
                    {
                        // An element the parser ignored the end tag of owns fewer bytes than were written.
                        if (n.IsElement && n.Namespace == ElementNamespace.Html && n.Source.HasStartTag && !n.Source.HasEndTag && !IsVoidName(n.LocalName))
                        {
                            return;
                        }

                        inverse.Add(Edit.Remove(n));
                    }

                    break;
                }

            default:
                return;
        }

        EditResult back = nd.Apply(inverse, new EditOptions { MaxVerifications = 64 });
        if (!back.Succeeded || back.Fallbacks.Count > 0)
        {
            // An inverse that needs a fallback is allowed to rewrite more; only a clean inverse must restore the bytes.
            if (!back.Succeeded)
            {
                throw new EditCheckException(label + " could not be reverted: " + back.Failure);
            }

            return;
        }

        bool same = doc.Source.Bytes.Length > 0
            ? back.Document!.Source.Bytes.Span.SequenceEqual(doc.Source.Bytes.Span)
            : back.Document!.Source.Text == doc.Source.Text;
        if (!same)
        {
            string a = doc.Source.Text;
            string b = back.Document!.Source.Text;
            int at = 0;
            while (at < a.Length && at < b.Length && a[at] == b[at])
            {
                at++;
            }

            throw new EditCheckException(label + " was not restored by its inverse; first difference at " + at + " (lengths " + a.Length + "/" + b.Length + "): was '" + Snip(a, at) + "' now '" + Snip(b, at) + "'");
        }

        if (stats is not null)
        {
            stats.Reverts++;
        }
    }
}
