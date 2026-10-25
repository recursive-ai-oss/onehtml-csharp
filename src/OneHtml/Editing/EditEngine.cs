using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// Runs a transaction: applies the edits to a working copy of the tree, turns the changed regions into replacements
/// of the original text, builds the output, parses it again and compares the result with the working tree. A
/// transaction whose output does not parse back to the expected tree is retried edit by edit, moving the regions
/// that failed up to the smallest enclosing element that does.
/// </summary>
internal static class EditEngine
{
    private sealed class Attempt
    {
        public bool Ok;
        public string? Reason;
        public BuildResult Build = new();
        public Document? NewDocument;
        public int[] Map = [];
        public string NewText = string.Empty;
    }

    public static EditResult Run(EditTransaction transaction, EditOptions options, CancellationToken ct)
    {
        Document doc = transaction.Document;
        IReadOnlyList<Edit> edits = transaction.Edits;
        var outcomes = new List<EditOutcome>(edits.Count);
        for (int i = 0; i < edits.Count; i++)
        {
            outcomes.Add(new EditOutcome(i, edits[i]));
        }

        if (doc.IsFragment)
        {
            return Fail("editing a fragment snapshot is not supported", outcomes, 0);
        }

        if (edits.Count > options.MaxEdits)
        {
            return Fail("the transaction has " + edits.Count + " edits; the limit is " + options.MaxEdits, outcomes, 0);
        }

        long inserted = 0;
        foreach (Edit e in edits)
        {
            inserted += e.Value?.Length ?? 0;
        }

        if (inserted > options.MaxInsertedLength)
        {
            return Fail("the transaction inserts " + inserted + " characters; the limit is " + options.MaxInsertedLength, outcomes, 0);
        }

        if (edits.Count == 0)
        {
            return Success(doc, new ChangeSet(doc, doc, [], [], [], [], Identity(doc.NodeCount)), outcomes, [], 0);
        }

        bool hasBytes = doc.Source.Bytes.Length > 0;
        DocumentEncoder? encoder = null;
        if (hasBytes)
        {
            encoder = DocumentEncoder.Create(doc.Source.Encoding);
            if (encoder is null)
            {
                return Fail("documents in the " + doc.Source.Encoding.Name + " encoding cannot be edited", outcomes, 0);
            }
        }

        LocalStyle style = LocalStyle.Infer(doc);
        int verifications = 0;

        // First try every edit's local splice at once: one re-parse in the common case.
        EditModel model = new(doc, edits.Count);
        for (int i = 0; i < edits.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string? error = ApplyOne(model, edits[i], i);
            if (error is not null)
            {
                outcomes[i].Status = EditStatus.Invalid;
                outcomes[i].Reason = error;
                return Fail("edit " + i + " (" + edits[i].Operation + ") does not fit its target: " + error, outcomes, verifications);
            }
        }

        if (model.Regions.Count == 0)
        {
            MarkApplied(outcomes);
            return Success(doc, new ChangeSet(doc, doc, [], [], [], [], Identity(doc.NodeCount)), outcomes, [], 0);
        }

        var tiers = new Dictionary<RegionKey, int>();
        ct.ThrowIfCancellationRequested();
        Attempt att = TryBuild(doc, model, tiers, style, encoder, ct);
        verifications++;
        if (!att.Ok)
        {
            // Redo the edits one at a time, each checked on its own, raising only the regions that need it.
            model = new EditModel(doc, edits.Count);
            tiers = [];
            att = new Attempt();
            for (int i = 0; i < edits.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                string? error = ApplyOne(model, edits[i], i);
                if (error is not null)
                {
                    outcomes[i].Status = EditStatus.Invalid;
                    outcomes[i].Reason = error;
                    return Fail("edit " + i + " (" + edits[i].Operation + ") does not fit its target: " + error, outcomes, verifications);
                }

                List<RegionKey> touched = model.TouchedBy[i];
                if (touched.Count == 0)
                {
                    continue;
                }

                while (true)
                {
                    if (verifications >= options.MaxVerifications)
                    {
                        outcomes[i].Status = EditStatus.Failed;
                        outcomes[i].Reason = "the output was re-parsed " + verifications + " times without matching";
                        return Fail("edit " + i + " (" + edits[i].Operation + "): " + outcomes[i].Reason, outcomes, verifications);
                    }

                    ct.ThrowIfCancellationRequested();
                    att = TryBuild(doc, model, tiers, style, encoder, ct);
                    verifications++;
                    if (att.Ok)
                    {
                        break;
                    }

                    bool raised = false;
                    if (att.Build.Error is null)
                    {
                        foreach (RegionKey r in touched)
                        {
                            tiers[r] = att.Build.Levels.GetValueOrDefault(r, tiers.GetValueOrDefault(r)) + 1;
                            raised = true;
                        }
                    }

                    if (!raised)
                    {
                        outcomes[i].Status = EditStatus.Failed;
                        outcomes[i].Reason = att.Reason;
                        return Fail("edit " + i + " (" + edits[i].Operation + ") cannot be written as markup: " + att.Reason, outcomes, verifications);
                    }
                }
            }
        }

        return Finish(doc, model, att, outcomes, verifications);
    }

    private static string? ApplyOne(EditModel model, Edit edit, int index)
    {
        try
        {
            return model.Apply(edit, index);
        }
        catch (HtmlLimitExceededException ex)
        {
            return "the edit exceeds a limit: " + ex.Message;
        }
    }

    // ---- building and checking the output ----

    private static Attempt TryBuild(Document doc, EditModel model, Dictionary<RegionKey, int> tiers, LocalStyle style, DocumentEncoder? encoder, CancellationToken ct)
    {
        var att = new Attempt();
        var builder = new SpliceBuilder(model, style, encoder);
        att.Build = builder.Build(tiers);
        if (att.Build.Error is not null)
        {
            att.Reason = att.Build.Error;
            return att;
        }

        SourceText src = doc.Source;
        string text = src.Text;
        var tb = new System.Text.StringBuilder(text.Length + 64);
        ArrayBufferWriter<byte>? bytes = src.Bytes.Length > 0 ? new ArrayBufferWriter<byte>(src.Bytes.Length + 64) : null;
        ReadOnlySpan<byte> original = src.Bytes.Span;
        int pos = 0;
        int bytePos = 0;
        try
        {
            foreach (Splice s in att.Build.Items)
            {
                tb.Append(text, pos, s.Start - pos);
                tb.Append(s.Text);
                if (bytes is not null)
                {
                    int bs = src.ToByteOffset(s.Start);
                    bytes.Write(original[bytePos..bs]);
                    string piece = style.CrLf ? s.Text.Replace("\n", "\r\n", StringComparison.Ordinal) : s.Text;
                    encoder!.Encode(piece, bytes);
                    bytePos = src.ToByteOffset(s.End);
                }

                pos = s.End;
            }

            tb.Append(text, pos, text.Length - pos);
            if (bytes is not null)
            {
                bytes.Write(original[bytePos..]);
            }
        }
        catch (InvalidOperationException ex)
        {
            att.Reason = ex.Message;
            return att;
        }

        att.NewText = tb.ToString();
        try
        {
            var parse = new ParseOptions
            {
                ScriptingEnabled = doc.ScriptingEnabled,
                Limits = src.Limits,
                OverrideEncoding = bytes is not null ? src.Encoding : null,
            };
            att.NewDocument = bytes is not null
                ? Document.Parse(bytes.WrittenMemory.ToArray(), parse, ct)
                : Document.Parse(att.NewText, parse, ct);
        }
        catch (HtmlLimitExceededException ex)
        {
            att.Reason = "the edited document exceeds a limit: " + ex.Message;
            att.Build.Error = att.Reason;
            return att;
        }

        if (!string.Equals(att.NewDocument.Source.Text, att.NewText, StringComparison.Ordinal))
        {
            att.Reason = "the edited bytes decode to different text than the edit wrote";
            return att;
        }

        model.Flush();
        att.Map = new int[model.A.Count];
        string? mismatch = TreeComparer.Compare(model.View, att.NewDocument, att.Map);
        if (mismatch is not null)
        {
            att.Reason = "re-parsing the output gave a different tree: " + mismatch;
            return att;
        }

        att.Ok = true;
        return att;
    }

    // ---- results ----

    private static EditResult Fail(string reason, List<EditOutcome> outcomes, int verifications) =>
        new(false, reason, null, null, outcomes, [], verifications);

    private static EditResult Success(Document doc, ChangeSet changes, List<EditOutcome> outcomes, IReadOnlyList<FallbackInfo> fallbacks, int verifications)
    {
        MarkApplied(outcomes);
        return new EditResult(true, null, doc, changes, outcomes, fallbacks, verifications);
    }

    private static void MarkApplied(List<EditOutcome> outcomes)
    {
        foreach (EditOutcome o in outcomes)
        {
            o.Status = EditStatus.Applied;
        }
    }

    private static int[] Identity(int n)
    {
        int[] map = new int[n];
        for (int i = 0; i < n; i++)
        {
            map[i] = i;
        }

        return map;
    }

    private static EditResult Finish(Document doc, EditModel model, Attempt att, List<EditOutcome> outcomes, int verifications)
    {
        Document newDoc = att.NewDocument!;
        SourceText oldSrc = doc.Source;
        SourceText newSrc = newDoc.Source;
        bool hasBytes = oldSrc.Bytes.Length > 0;

        // The audit diff, and which edits caused each range.
        var ranges = new List<EditedRange>();
        var fallbacks = new List<FallbackInfo>();
        int delta = 0;
        foreach (Splice s in att.Build.Items)
        {
            int ns = s.Start + delta;
            int ne = ns + s.Text.Length;
            delta += s.Text.Length - (s.End - s.Start);
            List<int> causes = model.Regions[s.Region];
            SourceRange oldText = new(s.Start, s.End);
            SourceRange newText = new(ns, ne);
            SourceRange oldBytes = hasBytes ? new SourceRange(oldSrc.ToByteOffset(s.Start), oldSrc.ToByteOffset(s.End)) : oldText;
            SourceRange newBytes = hasBytes ? new SourceRange(newSrc.ToByteOffset(ns), newSrc.ToByteOffset(ne)) : newText;
            ReadOnlyMemory<byte> data = hasBytes ? newSrc.Bytes.Slice(newBytes.Start, newBytes.End - newBytes.Start) : ReadOnlyMemory<byte>.Empty;
            int index = ranges.Count;
            ranges.Add(new EditedRange(oldText, oldBytes, newText, newBytes, data, s.Text, causes.ToArray(), s.Level));
            foreach (int e in causes)
            {
                EditOutcome o = outcomes[e];
                o.FallbackLevel = Math.Max(o.FallbackLevel, s.Level);
                var list = o.RangeIndexes as List<int>;
                if (list is null)
                {
                    list = [];
                    o.RangeIndexes = list;
                }

                list.Add(index);
            }

            if (s.Level > 0)
            {
                fallbacks.Add(new FallbackInfo(causes[0], oldText, s.Level, att.Build.Reasons.GetValueOrDefault(s.Region, "the local splice did not re-parse to the expected tree")));
            }
        }

        ChangeSet changes = BuildChangeSet(doc, newDoc, model, att, ranges);
        return Success(newDoc, changes, outcomes, fallbacks, verifications);
    }

    private static ChangeSet BuildChangeSet(Document doc, Document newDoc, EditModel model, Attempt att, List<EditedRange> ranges)
    {
        Arena a = model.A;
        Arena ba = model.BA;
        int baseCount = model.BaseCount;
        int[] map = att.Map;
        int[] oldToNew = new int[doc.NodeCount];
        Array.Copy(map, oldToNew, baseCount);

        // Which new nodes came from original ones (several original text nodes can merge into one).
        int[] gotToOld = new int[newDoc.NodeCount];
        Array.Fill(gotToOld, -1);
        var mergedTargets = new HashSet<int>();
        for (int i = 0; i < baseCount; i++)
        {
            int g = map[i];
            if (g < 0)
            {
                continue;
            }

            if (gotToOld[g] >= 0)
            {
                mergedTargets.Add(g);
            }
            else
            {
                gotToOld[g] = i;
            }
        }

        var removed = new List<Node>();
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            ushort f = a.Flags[n];
            if ((f & EditModel.Dead) != 0)
            {
                removed.Add(new Node(doc, n));
                continue;
            }

            if ((f & EditModel.Unwrapped) != 0)
            {
                removed.Add(new Node(doc, n));
            }

            if (ba.Kind[n] == (byte)NodeKind.Element && ba.Aux[n] >= 0)
            {
                stack.Push(ba.Aux[n]);
            }

            for (int c = ba.Last[n]; c >= 0; c = ba.Prev[c])
            {
                stack.Push(c);
            }
        }

        var changed = new Dictionary<int, NodeChanges>();
        void Mark(int oldIndex, NodeChanges what)
        {
            int g = map[oldIndex];
            if (g >= 0)
            {
                changed[g] = changed.GetValueOrDefault(g) | what;
            }
        }

        var added = new List<(int Got, Node Node)>();
        var seenAdded = new HashSet<int>();
        for (int i = baseCount; i < a.Count; i++)
        {
            int g = map[i];
            if ((a.Flags[i] & (EditModel.Dead | EditModel.Unwrapped)) != 0 || g < 0)
            {
                continue;
            }

            int parent = a.Parent[i];
            if (gotToOld[g] >= 0)
            {
                // New text that the parser merged into an original text node.
                Mark(gotToOld[g], NodeChanges.Data);
                continue;
            }

            if (parent >= 0 && parent < baseCount && seenAdded.Add(g))
            {
                added.Add((g, new Node(newDoc, g)));
            }
        }

        foreach (int g in mergedTargets)
        {
            changed[g] = changed.GetValueOrDefault(g) | NodeChanges.Data;
        }

        foreach (KeyValuePair<RegionKey, List<int>> kv in model.Regions)
        {
            int n = kv.Key.Node;
            if ((a.Flags[n] & (EditModel.Dead | EditModel.Unwrapped)) != 0)
            {
                continue;
            }

            switch (kv.Key.Kind)
            {
                case RegionKind.Attrs:
                    if (AttributesDiffer(model, n))
                    {
                        Mark(n, NodeChanges.Attributes);
                    }

                    break;
                case RegionKind.Text:
                    if (!new Node(model.View, n).Data.SequenceEqual(new Node(doc, n).Data))
                    {
                        Mark(n, NodeChanges.Data);
                    }

                    break;
                default:
                    if (ChildrenDiffer(model, n))
                    {
                        Mark(n, NodeChanges.Children);
                    }

                    break;
            }
        }

        foreach (int b in model.Moved)
        {
            if ((a.Flags[b] & (EditModel.Dead | EditModel.Unwrapped)) == 0 && a.Parent[b] != ba.Parent[b])
            {
                Mark(b, NodeChanges.Parent);
            }
        }

        added.Sort(static (x, y) => x.Got.CompareTo(y.Got));
        var addedNodes = new List<Node>(added.Count);
        foreach ((int _, Node node) in added)
        {
            addedNodes.Add(node);
        }

        var keys = new List<int>(changed.Keys);
        keys.Sort();
        var changedNodes = new List<NodeChange>(keys.Count);
        foreach (int g in keys)
        {
            int old = gotToOld[g];
            if (old >= 0)
            {
                changedNodes.Add(new NodeChange(new Node(newDoc, g), new Node(doc, old), changed[g]));
            }
        }

        return new ChangeSet(doc, newDoc, addedNodes, removed, changedNodes, ranges, oldToNew);
    }

    private static bool AttributesDiffer(EditModel model, int e)
    {
        List<ModelAttr> now = model.AttrsOf(e);
        if (now.Count != model.BA.AttrCount[e])
        {
            return true;
        }

        for (int i = 0; i < now.Count; i++)
        {
            int row = model.BA.AttrStart[e] + i;
            if (now[i].Orig != row || !new Attr(model.Base, row).Value.SequenceEqual(now[i].Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ChildrenDiffer(EditModel model, int p)
    {
        int b = model.BA.First[p];
        int m = model.A.First[p];
        while (b >= 0 && m >= 0)
        {
            if (b != m)
            {
                return true;
            }

            b = model.BA.Next[b];
            m = model.A.Next[m];
        }

        return b != m;
    }
}
