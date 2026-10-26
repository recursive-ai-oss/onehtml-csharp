using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Locators;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>What replaying one saved edit found.</summary>
public sealed class ReplayOutcome
{
    internal ReplayOutcome(int index, SavedEdit saved)
    {
        Index = index;
        Saved = saved;
    }

    /// <summary>The edit's position in the envelope.</summary>
    public int Index { get; }

    /// <summary>The saved edit.</summary>
    public SavedEdit Saved { get; }

    /// <summary>
    /// <see cref="EditStatus.Applied"/>, <see cref="EditStatus.Stale"/> (the target is gone or no longer looks like it did),
    /// <see cref="EditStatus.Ambiguous"/> (it matches several places), <see cref="EditStatus.Invalid"/> (the edit is
    /// malformed or does not fit its target), <see cref="EditStatus.Failed"/> (the result cannot be written) or
    /// <see cref="EditStatus.NotAttempted"/> (this edit resolved, but another did not, so nothing was applied).
    /// </summary>
    public EditStatus Status { get; internal set; } = EditStatus.NotAttempted;

    /// <summary>Why the edit was not applied.</summary>
    public string? Reason { get; internal set; }

    /// <summary>True when the target was found by its source anchor, false when by locator and fingerprint.</summary>
    public bool UsedAnchor { get; internal set; }

    /// <summary>
    /// False when the target was found by locator and its ancestor-path fingerprint differed (only possible for a locator
    /// that names the target by itself, such as an <c>id</c>), true otherwise.
    /// </summary>
    public bool PathMatched { get; internal set; } = true;

    /// <summary>The node the edit resolved to, in the document replayed onto; the null node when it did not resolve or targets a rule.</summary>
    public Node Node { get; internal set; }

    /// <summary>The rule the edit resolved to, when it targets a rule.</summary>
    public CssNode? Rule { get; internal set; }

    /// <summary>The edit's metadata as JSON text, handed back unread.</summary>
    public string? MetaJson => Saved.MetaJson;
}

/// <summary>The result of replaying an envelope.</summary>
public sealed class ReplayResult
{
    internal ReplayResult(bool succeeded, string? failure, IReadOnlyList<ReplayOutcome> outcomes, BatchResult? batch)
    {
        Succeeded = succeeded;
        Failure = failure;
        Outcomes = outcomes;
        Batch = batch;
    }

    /// <summary>True when every edit resolved and applied.</summary>
    public bool Succeeded { get; }

    /// <summary>A sentence saying why nothing was applied; <see langword="null"/> on success.</summary>
    public string? Failure { get; }

    /// <summary>One entry per saved edit, in order.</summary>
    public IReadOnlyList<ReplayOutcome> Outcomes { get; }

    /// <summary>The applied batch (new snapshot, change sets, new sheet bytes); <see langword="null"/> when nothing was applied.</summary>
    public BatchResult? Batch { get; }
}

/// <summary>Replaying saved edits.</summary>
public static class EditReplay
{
    /// <summary>
    /// Applies saved edits to a document and its stylesheets. Every target is resolved against the document as it is
    /// now; an edit whose target is not found, no longer looks like it did, or matches several places is reported as
    /// <see cref="EditStatus.Stale"/> or <see cref="EditStatus.Ambiguous"/>, and then nothing is applied: all edits
    /// apply or none does. The library never guesses a target.
    /// </summary>
    /// <param name="envelope">The saved edits.</param>
    /// <param name="document">The document to edit; <see langword="null"/> when the envelope has only stylesheet edits on linked sheets.</param>
    /// <param name="linkedSheets">Linked stylesheets the envelope may name by URL.</param>
    /// <param name="options">Bounds for the applied batch; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between edits.</param>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static ReplayResult Replay(this EditEnvelope envelope, Document? document, IEnumerable<LinkedStylesheet>? linkedSheets = null, EditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new Replayer(envelope, document, linkedSheets, cancellationToken).Run(options);
    }

    private sealed class Replayer
    {
        private readonly EditEnvelope _envelope;
        private readonly Document? _doc;
        private readonly List<LinkedStylesheet> _linked;
        private readonly CancellationToken _ct;
        private readonly Dictionary<string, SelectorList?> _selectors = [];
        private readonly Dictionary<Stylesheet, RuleCatalog> _catalogs = new(ReferenceEqualityComparer.Instance);
        private Dictionary<(long, long, NodeKind), int>? _anchors;

        public Replayer(EditEnvelope envelope, Document? doc, IEnumerable<LinkedStylesheet>? linked, CancellationToken ct)
        {
            _envelope = envelope;
            _doc = doc;
            _linked = new List<LinkedStylesheet>(linked ?? []);
            _ct = ct;
        }

        public ReplayResult Run(EditOptions? options)
        {
            var outcomes = new List<ReplayOutcome>(_envelope.Edits.Count);
            var edits = new List<Edit>();
            var editOf = new List<int>();
            var sheetEdits = new List<SheetEdit>();
            var sheetOf = new List<int>();
            for (int i = 0; i < _envelope.Edits.Count; i++)
            {
                _ct.ThrowIfCancellationRequested();
                var o = new ReplayOutcome(i, _envelope.Edits[i]);
                outcomes.Add(o);
                try
                {
                    Resolve(o, edits, editOf, sheetEdits, sheetOf);
                }
                catch (Exception ex) when (ex is ArgumentException or CssLimitExceededException or HtmlLimitExceededException)
                {
                    o.Status = EditStatus.Invalid;
                    o.Reason = ex.Message;
                }
            }

            int bad = outcomes.FindIndex(static x => x.Status != EditStatus.NotAttempted);
            if (bad >= 0)
            {
                return new ReplayResult(false, "edit " + bad + " (" + outcomes[bad].Saved.Op + ") " + Describe(outcomes[bad].Status) + ": " + outcomes[bad].Reason + "; nothing was applied", outcomes, null);
            }

            BatchResult batch;
            try
            {
                batch = new EditBatch(_doc, edits, sheetEdits, _linked).Apply(options, _ct);
            }
            catch (ArgumentException ex)
            {
                return new ReplayResult(false, ex.Message, outcomes, null);
            }

            for (int k = 0; k < edits.Count; k++)
            {
                Copy(batch.Outcomes[k], outcomes[editOf[k]]);
            }

            for (int k = 0; k < sheetEdits.Count; k++)
            {
                Copy(batch.Outcomes[edits.Count + k], outcomes[sheetOf[k]]);
            }

            return new ReplayResult(batch.Succeeded, batch.Failure, outcomes, batch.Succeeded ? batch : null);
        }

        private static void Copy(BatchOutcome from, ReplayOutcome to)
        {
            to.Status = from.Status;
            to.Reason = from.Reason;
        }

        private static string Describe(EditStatus s) => s switch
        {
            EditStatus.Stale => "is stale",
            EditStatus.Ambiguous => "is ambiguous",
            EditStatus.Invalid => "is invalid",
            _ => "did not resolve",
        };

        private static void Fail(ReplayOutcome o, EditStatus status, string reason)
        {
            o.Status = status;
            o.Reason = reason;
        }

        // ---- one saved edit ----

        private void Resolve(ReplayOutcome o, List<Edit> edits, List<int> editOf, List<SheetEdit> sheetEdits, List<int> sheetOf)
        {
            SavedEdit s = o.Saved;
            object? meta = null;
            if (s.MetaJson is not null)
            {
                using JsonDocument jd = JsonDocument.Parse(s.MetaJson);
                meta = jd.RootElement.Clone();
            }

            switch (s.Op)
            {
                case "setAttribute":
                case "removeAttribute":
                case "setStyle":
                case "removeStyle":
                case "unwrap":
                case "prepend":
                case "append":
                    {
                        if (!NodeTarget(o, s, wantElement: true, out Node n))
                        {
                            return;
                        }

                        Edit? e = s.Op switch
                        {
                            "setAttribute" => Need(o, s.Name, "name") is null || Need(o, s.Value, "value") is null ? null : Edit.SetAttribute(n, s.Name!, s.Value!),
                            "removeAttribute" => Need(o, s.Name, "name") is null ? null : Edit.RemoveAttribute(n, s.Name!),
                            "setStyle" => Need(o, s.Property, "property") is null || Need(o, s.Value, "value") is null ? null : Edit.SetStyle(n, s.Property!, s.Value!, s.Important),
                            "removeStyle" => Need(o, s.Property, "property") is null ? null : Edit.RemoveStyle(n, s.Property!),
                            "unwrap" => Edit.Unwrap(n),
                            "prepend" => Need(o, s.Html, "html") is null ? null : Edit.Prepend(n, s.Html!),
                            _ => Need(o, s.Html, "html") is null ? null : Edit.Append(n, s.Html!),
                        };
                        Add(o, e, meta, edits, editOf);
                        return;
                    }

                case "replaceText":
                case "insertBefore":
                case "insertAfter":
                case "wrap":
                case "replace":
                case "remove":
                    {
                        if (!NodeTarget(o, s, wantElement: false, out Node n))
                        {
                            return;
                        }

                        Edit? e = s.Op switch
                        {
                            "replaceText" => Need(o, s.Text, "text") is null ? null : Edit.ReplaceText(n, s.Text!),
                            "insertBefore" => Need(o, s.Html, "html") is null ? null : Edit.InsertBefore(n, s.Html!),
                            "insertAfter" => Need(o, s.Html, "html") is null ? null : Edit.InsertAfter(n, s.Html!),
                            "wrap" => Need(o, s.Html, "html") is null ? null : Edit.Wrap(n, s.Html!, s.Count),
                            "replace" => Need(o, s.Html, "html") is null ? null : Edit.Replace(n, s.Html!),
                            _ => Edit.Remove(n),
                        };
                        Add(o, e, meta, edits, editOf);
                        return;
                    }

                case "setDeclaration":
                case "removeDeclaration":
                case "removeRule":
                case "replaceRule":
                    {
                        if (!RuleTarget(o, s.Target, out Stylesheet? sheet, out CssNode rule))
                        {
                            return;
                        }

                        SheetEdit? e = s.Op switch
                        {
                            "setDeclaration" => Need(o, s.Property, "property") is null || Need(o, s.Value, "value") is null ? null : SheetEdit.SetDeclaration(sheet!, rule, s.Property!, s.Value!, s.Important),
                            "removeDeclaration" => Need(o, s.Property, "property") is null ? null : SheetEdit.RemoveDeclaration(sheet!, rule, s.Property!),
                            "removeRule" => SheetEdit.RemoveRule(sheet!, rule),
                            _ => Need(o, s.Css, "css") is null ? null : SheetEdit.ReplaceRule(sheet!, rule, s.Css!),
                        };
                        AddSheet(o, e, meta, sheetEdits, sheetOf);
                        return;
                    }

                case "insertRule":
                    {
                        if (Need(o, s.Css, "css") is null)
                        {
                            return;
                        }

                        SheetEdit? e;
                        if (s.Placement is "before" or "after" || (s.Placement == "end" && s.Target is not null))
                        {
                            if (!RuleTarget(o, s.Target, out Stylesheet? sheet, out CssNode rule))
                            {
                                return;
                            }

                            e = s.Placement == "before" ? SheetEdit.InsertRuleBefore(sheet!, rule, s.Css!)
                                : s.Placement == "after" ? SheetEdit.InsertRuleAfter(sheet!, rule, s.Css!)
                                : SheetEdit.AppendRule(sheet!, s.Css!, rule);
                        }
                        else if (s.Placement == "end")
                        {
                            Stylesheet? sheet = FindSheet(o, s.Sheet);
                            if (sheet is null)
                            {
                                return;
                            }

                            e = SheetEdit.AppendRule(sheet, s.Css!);
                        }
                        else
                        {
                            Fail(o, EditStatus.Invalid, "insertRule needs a before, after or end placement");
                            return;
                        }

                        AddSheet(o, e, meta, sheetEdits, sheetOf);
                        return;
                    }

                default:
                    Fail(o, EditStatus.Invalid, "unknown operation \"" + (s.Op.Length > 40 ? s.Op[..40] : s.Op) + "\"");
                    return;
            }
        }

        private static string? Need(ReplayOutcome o, string? value, string field)
        {
            if (value is null)
            {
                o.Status = EditStatus.Invalid;
                o.Reason = "the edit has no " + field;
            }

            return value;
        }

        private static void Add(ReplayOutcome o, Edit? e, object? meta, List<Edit> edits, List<int> editOf)
        {
            if (e is null || o.Status != EditStatus.NotAttempted)
            {
                return;
            }

            edits.Add(meta is null ? e : CopyMeta(e, meta));
            editOf.Add(o.Index);
        }

        private static Edit CopyMeta(Edit e, object meta) => e.WithMeta(meta);

        private static void AddSheet(ReplayOutcome o, SheetEdit? e, object? meta, List<SheetEdit> sheetEdits, List<int> sheetOf)
        {
            if (e is null || o.Status != EditStatus.NotAttempted)
            {
                return;
            }

            sheetEdits.Add(meta is null ? e : e.WithMeta(meta));
            sheetOf.Add(o.Index);
        }

        // ---- node targets ----

        private bool NodeTarget(ReplayOutcome o, SavedEdit s, bool wantElement, out Node node)
        {
            node = default;
            SavedTarget? t = s.Target;
            if (t is null)
            {
                Fail(o, EditStatus.Invalid, "the edit has no target");
                return false;
            }

            if (t.Kind == SavedTargetKind.Rule)
            {
                Fail(o, EditStatus.Invalid, "the operation " + s.Op + " does not apply to a rule");
                return false;
            }

            if (wantElement && t.Kind != SavedTargetKind.Element)
            {
                Fail(o, EditStatus.Invalid, "the operation " + s.Op + " needs an element target, not a " + KindName(t.Kind));
                return false;
            }

            if (_doc is null)
            {
                Fail(o, EditStatus.Stale, "there is no document to apply the edit to");
                return false;
            }

            if (!ResolveNode(t, o, out node))
            {
                return false;
            }

            o.Node = node;
            return true;
        }

        private bool ResolveNode(SavedTarget t, ReplayOutcome o, out Node node)
        {
            node = default;
            Document doc = _doc!;

            // 1. The anchor: the bytes the node had are at the same place and are still exactly one node of that kind.
            Node anchored = default;
            if (t.AnchorStart >= 0 && t.AnchorEnd >= t.AnchorStart && t.AnchorBytes is not null)
            {
                anchored = FindByAnchor(t);
            }

            if (!anchored.IsNull)
            {
                if (t.Kind == SavedTargetKind.Element && t.Locator is not null)
                {
                    SelectorList? list = ParseLocator(t.Locator);
                    if (list is not null)
                    {
                        List<Node> found = doc.Root.QuerySelectorAll(list);
                        if (found.Count == 1 && found[0] != anchored)
                        {
                            Fail(o, EditStatus.Stale, "the bytes of the target are where they were, but the locator now selects a different element");
                            return false;
                        }
                    }
                }

                node = anchored;
                o.UsedAnchor = true;
                return true;
            }

            // 2. The locator (elements), or the parent and the position (text and comments).
            if (t.Kind != SavedTargetKind.Element)
            {
                return ResolveChild(t, o, out node);
            }

            if (string.IsNullOrEmpty(t.Locator))
            {
                Fail(o, EditStatus.Stale, "the target has no locator and its source bytes are no longer where they were");
                return false;
            }

            SelectorList? sel = ParseLocator(t.Locator);
            if (sel is null)
            {
                Fail(o, EditStatus.Invalid, "the locator is not a valid selector: " + Shorten(t.Locator));
                return false;
            }

            List<Node> matches = doc.Root.QuerySelectorAll(sel);
            if (matches.Count == 0)
            {
                Fail(o, EditStatus.Stale, "the locator matches nothing: " + Shorten(t.Locator));
                return false;
            }

            if (matches.Count > 1)
            {
                Fail(o, EditStatus.Ambiguous, "the locator matches " + matches.Count + " elements: " + Shorten(t.Locator));
                return false;
            }

            Node cand = matches[0];
            if (!t.HasFingerprint || t.Tag is null || t.TextHash is null)
            {
                Fail(o, EditStatus.Invalid, "the target has neither a matching source anchor nor a complete fingerprint (tag, text) to verify the locator's match");
                return false;
            }

            ElementFingerprint fp = ElementFingerprint.Create(cand);
            if (!string.Equals(fp.Tag, t.Tag, StringComparison.Ordinal) || !SameAttributes(fp.Attributes, t.Attributes))
            {
                Fail(o, EditStatus.Stale, "the element the locator finds has a different tag or key attributes than the target had");
                return false;
            }

            if (!string.Equals(fp.TextHash, t.TextHash, StringComparison.Ordinal))
            {
                Fail(o, EditStatus.Stale, "the element the locator finds has different text than the target had");
                return false;
            }

            if (t.PathHash is not null && !string.Equals(fp.PathHash, t.PathHash, StringComparison.Ordinal))
            {
                // A locator made of position steps selects whatever sits at that position now; a different path means
                // the position may hold another element with the same tag, attributes and text. Only a locator that
                // names the element by itself may ignore it.
                if (!IsStandalone(t.Locator))
                {
                    Fail(o, EditStatus.Stale, "the element at the locator's position has the same tag, attributes and text but sits under a different path");
                    return false;
                }

                o.PathMatched = false;
            }

            node = cand;
            return true;
        }

        private bool ResolveChild(SavedTarget t, ReplayOutcome o, out Node node)
        {
            node = default;
            if (t.Parent is null || t.ChildIndex < 0 || t.TextHash is null)
            {
                Fail(o, EditStatus.Stale, "the " + KindName(t.Kind) + " target's source bytes are no longer where they were, and it has no parent, position and text hash to find it by");
                return false;
            }

            if (!ResolveNode(t.Parent, o, out Node parent))
            {
                return false;
            }

            Node child = parent.FirstChild;
            for (int i = 0; i < t.ChildIndex && !child.IsNull; i++)
            {
                child = child.NextSibling;
            }

            NodeKind want = t.Kind == SavedTargetKind.Text ? NodeKind.Text : NodeKind.Comment;
            if (child.IsNull || child.Kind != want)
            {
                Fail(o, EditStatus.Stale, "the parent has no " + KindName(t.Kind) + " node at position " + t.ChildIndex);
                return false;
            }

            string hash = "sha256:" + Sha256.HashHex(Encoding.UTF8.GetBytes(ElementFingerprint.NormalizeText(child.Data.ToString())));
            if (!string.Equals(hash, t.TextHash, StringComparison.Ordinal))
            {
                Fail(o, EditStatus.Stale, "the " + KindName(t.Kind) + " node at that position has different text");
                return false;
            }

            node = child;
            return true;
        }

        private static bool SameAttributes(IReadOnlyList<KeyValuePair<string, string>> a, IReadOnlyList<KeyValuePair<string, string>> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].Key, b[i].Key, StringComparison.Ordinal) || !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private SelectorList? ParseLocator(string locator)
        {
            if (!_selectors.TryGetValue(locator, out SelectorList? list))
            {
                if (!SelectorList.TryParse(locator, out list))
                {
                    list = null;
                }

                _selectors[locator] = list;
            }

            return list;
        }

        /// <summary>True when the locator is one compound selector (an id, a data hook, a tag with an attribute): no combinator and no whitespace outside quotes and brackets.</summary>
        internal static bool IsStandalone(string locator)
        {
            char quote = '\0';
            int bracket = 0;
            for (int i = 0; i < locator.Length; i++)
            {
                char c = locator[i];
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                switch (c)
                {
                    case '\\':
                        i++;
                        break;
                    case '"' or '\'':
                        quote = c;
                        break;
                    case '[' or '(':
                        bracket++;
                        break;
                    case ']' or ')':
                        bracket--;
                        break;
                    case ' ' or '\t' or '\n' or '\r' or '\f' or '>' or '+' or '~' or ',' when bracket == 0:
                        return false;
                    default:
                        break;
                }
            }

            return true;
        }

        private Node FindByAnchor(SavedTarget t)
        {
            Document doc = _doc!;
            if (_anchors is null)
            {
                _anchors = [];
                Arena a = doc.Arena;
                for (int i = 1; i < a.Count; i++)
                {
                    NodeKind k = (NodeKind)a.Kind[i];
                    if (k is not (NodeKind.Element or NodeKind.Text or NodeKind.Comment) || !SourceInfo.TryExtent(a, i, out int s, out int e) || e > doc.Source.Text.Length || a.Parent[i] < 0)
                    {
                        continue;
                    }

                    long bs = doc.Source.Bytes.Length > 0 ? doc.Source.ToByteOffset(s) : s;
                    long be = doc.Source.Bytes.Length > 0 ? doc.Source.ToByteOffset(e) : e;
                    var key = (bs, be, k);
                    _anchors[key] = _anchors.ContainsKey(key) ? -1 : i;
                }
            }

            NodeKind want = t.Kind switch { SavedTargetKind.Text => NodeKind.Text, SavedTargetKind.Comment => NodeKind.Comment, _ => NodeKind.Element };
            if (!_anchors.TryGetValue((t.AnchorStart, t.AnchorEnd, want), out int idx) || idx < 0)
            {
                return default;
            }

            // The node must also be where the document says it is, with the tag the target had, and its bytes must hash the same.
            var node = new Node(doc, idx);
            if (want == NodeKind.Element && t.Tag is not null && !string.Equals(node.LocalName, t.Tag, StringComparison.Ordinal))
            {
                return default;
            }

            if (!SourceInfo.TryExtent(doc.Arena, idx, out int ts, out int te))
            {
                return default;
            }

            EditJson.Anchor now = EditJson.AnchorAt(doc, ts, te);
            return string.Equals(now.Hash, t.AnchorBytes, StringComparison.Ordinal) ? node : default;
        }

        // ---- rule targets ----

        private Stylesheet? FindSheet(ReplayOutcome o, string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Fail(o, EditStatus.Invalid, "the edit names no stylesheet");
                return null;
            }

            if (name.StartsWith("inline:", StringComparison.Ordinal))
            {
                if (_doc is null || !int.TryParse(name.AsSpan(7), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n))
                {
                    Fail(o, EditStatus.Stale, "there is no style element for " + Shorten(name));
                    return null;
                }

                IReadOnlyList<InlineStylesheet> inline = InlineStylesheets.Of(_doc);
                if (n >= inline.Count)
                {
                    Fail(o, EditStatus.Stale, "the document has no style element " + name);
                    return null;
                }

                return inline[n].Sheet;
            }

            LinkedStylesheet? found = null;
            foreach (LinkedStylesheet l in _linked)
            {
                if (string.Equals(l.Url, name, StringComparison.Ordinal))
                {
                    if (found is not null)
                    {
                        Fail(o, EditStatus.Ambiguous, "several supplied stylesheets are named " + Shorten(name));
                        return null;
                    }

                    found = l;
                }
            }

            if (found is null)
            {
                Fail(o, EditStatus.Stale, "no stylesheet named " + Shorten(name) + " was supplied");
                return null;
            }

            return found.Sheet;
        }

        private bool RuleTarget(ReplayOutcome o, SavedTarget? t, out Stylesheet? sheet, out CssNode rule)
        {
            rule = default;
            sheet = null;
            if (t is null)
            {
                Fail(o, EditStatus.Invalid, "the edit has no target");
                return false;
            }

            if (t.Kind != SavedTargetKind.Rule || t.Selector is null)
            {
                Fail(o, EditStatus.Invalid, "the operation needs a rule target with a selector");
                return false;
            }

            sheet = FindSheet(o, t.Sheet);
            if (sheet is null)
            {
                return false;
            }

            if (!_catalogs.TryGetValue(sheet, out RuleCatalog? catalog))
            {
                catalog = new RuleCatalog(sheet);
                _catalogs[sheet] = catalog;
            }

            string[] at = [.. t.AtRules];
            List<CssNode> candidates = catalog.Candidates(at, t.Selector);
            if (candidates.Count == 0)
            {
                Fail(o, EditStatus.Stale, "no rule with the selector " + Shorten(t.Selector) + " is in the sheet");
                return false;
            }

            CssNode pick;
            if (t.Index >= 0)
            {
                if (t.Index >= candidates.Count)
                {
                    Fail(o, EditStatus.Stale, "the sheet has only " + candidates.Count + " rules with the selector " + Shorten(t.Selector) + "; the target was number " + (t.Index + 1));
                    return false;
                }

                pick = candidates[t.Index];
            }
            else if (candidates.Count == 1)
            {
                pick = candidates[0];
            }
            else
            {
                Fail(o, EditStatus.Ambiguous, candidates.Count + " rules have the selector " + Shorten(t.Selector) + " and the target gives no position");
                return false;
            }

            if (t.DeclarationsHash is null)
            {
                Fail(o, EditStatus.Invalid, "the rule target has no declarations fingerprint to verify the match");
                return false;
            }

            if (!string.Equals(RuleCatalog.DeclarationsHash(pick), t.DeclarationsHash, StringComparison.Ordinal))
            {
                Fail(o, EditStatus.Stale, "the rule at that position has different declarations than the target had");
                return false;
            }

            rule = pick;
            o.Rule = pick;
            return true;
        }

        private static string KindName(SavedTargetKind k) => k switch
        {
            SavedTargetKind.Text => "text",
            SavedTargetKind.Comment => "comment",
            SavedTargetKind.Rule => "rule",
            _ => "element",
        };

        private static string Shorten(string s) => s.Length <= 60 ? s : s[..60] + "...";
    }
}
