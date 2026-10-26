using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Locators;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// The saved-edit JSON, version 1: parsing and writing envelopes, and turning in-memory edits into durable ones.
/// The format is specified in <c>docs/edit-format.md</c>.
/// </summary>
public static class EditJson
{
    /// <summary>Reads an envelope.</summary>
    /// <param name="utf8Json">The JSON text as UTF-8 (a leading byte order mark is ignored).</param>
    /// <param name="limits">Bounds; <see langword="null"/> means <see cref="EditJsonLimits.Default"/>.</param>
    /// <exception cref="EditJsonException">The text is not valid JSON, is not an edit envelope of a supported version, or exceeds a limit.</exception>
    public static EditEnvelope Parse(ReadOnlySpan<byte> utf8Json, EditJsonLimits? limits = null) =>
        EditJsonIo.Read(utf8Json, limits ?? EditJsonLimits.Default);

    /// <summary>Reads an envelope from a string.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="limits">Bounds; <see langword="null"/> means <see cref="EditJsonLimits.Default"/>.</param>
    /// <exception cref="EditJsonException">See <see cref="Parse(ReadOnlySpan{byte}, EditJsonLimits?)"/>.</exception>
    public static EditEnvelope Parse(string json, EditJsonLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        EditJsonLimits l = limits ?? EditJsonLimits.Default;
        if (json.Length > l.MaxBytes)
        {
            throw new EditJsonException("the edit JSON is " + json.Length + " characters; the limit is " + l.MaxBytes + " bytes");
        }

        int bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > l.MaxBytes)
        {
            throw new EditJsonException("the edit JSON is " + bytes + " bytes; the limit is " + l.MaxBytes);
        }

        return EditJsonIo.Read(Encoding.UTF8.GetBytes(json), l);
    }

    /// <summary>Writes the envelope as JSON. Member order is fixed, so the same envelope always gives the same text.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="indented">Whether to indent with two spaces.</param>
    /// <exception cref="EditJsonException">A string holds an unpaired surrogate or <c>MetaJson</c> is not valid JSON.</exception>
    public static string ToJson(this EditEnvelope envelope, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return EditJsonIo.ToJson(envelope, indented);
    }

    /// <summary>Writes the envelope to a JSON writer.</summary>
    public static void WriteTo(this EditEnvelope envelope, Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(writer);
        EditJsonIo.Write(writer, envelope);
    }

    /// <summary>
    /// Describes edits made on a snapshot so they can be saved and replayed on other documents: each element target gets
    /// a locator (when one exists), a fingerprint and a source anchor; each rule target gets its sheet, selector,
    /// position, enclosing at-rules and a declarations fingerprint. Edits keep their order; document edits come first,
    /// then stylesheet edits. <see cref="Edit.Meta"/> and <see cref="SheetEdit.Meta"/> are written into <c>meta</c>.
    /// </summary>
    /// <param name="document">The snapshot the edits target; <see langword="null"/> when there are only stylesheet edits.</param>
    /// <param name="edits">Document edits.</param>
    /// <param name="sheetEdits">Stylesheet edits.</param>
    /// <param name="linkedSheets">Linked sheets the stylesheet edits may target, by their URLs.</param>
    /// <param name="documentUrl">Informative: recorded as the document's URL.</param>
    /// <exception cref="ArgumentException">An edit targets another snapshot, a node that cannot be described, a stylesheet with no name, or <c>Meta</c> of an unsupported type.</exception>
    public static EditEnvelope Create(Document? document, IEnumerable<Edit>? edits, IEnumerable<SheetEdit>? sheetEdits = null, IEnumerable<LinkedStylesheet>? linkedSheets = null, string? documentUrl = null)
    {
        var c = new Creator(document, linkedSheets);
        var list = new List<SavedEdit>();
        foreach (Edit e in edits ?? [])
        {
            ArgumentNullException.ThrowIfNull(e);
            list.Add(c.Make(e));
        }

        foreach (SheetEdit e in sheetEdits ?? [])
        {
            ArgumentNullException.ThrowIfNull(e);
            list.Add(c.Make(e));
        }

        string? sha = null;
        if (document is not null && document.Source.Bytes.Length > 0)
        {
            sha = Sha256.HashHex(document.Source.Bytes.Span);
        }

        return new EditEnvelope(list, documentUrl, sha);
    }

    private sealed class Creator
    {
        private readonly Document? _doc;
        private readonly List<LinkedStylesheet> _linked;
        private readonly LocatorGenerator? _locators;
        private readonly Dictionary<Stylesheet, RuleCatalog> _catalogs = new(ReferenceEqualityComparer.Instance);

        public Creator(Document? doc, IEnumerable<LinkedStylesheet>? linked)
        {
            _doc = doc;
            _linked = new List<LinkedStylesheet>(linked ?? []);
            _locators = doc is null ? null : new LocatorGenerator(doc);
        }

        public SavedEdit Make(Edit e)
        {
            if (_doc is null || !ReferenceEquals(e.Target.Document, _doc))
            {
                throw new ArgumentException("An edit targets a node of a different snapshot: " + e);
            }

            SavedTarget target = NodeTarget(e.Target);
            string? meta = MetaWriter.ToJson(e.Meta);
            switch (e.Operation)
            {
                case EditOperation.SetAttribute: return new SavedEdit { Op = "setAttribute", Target = target, Name = e.Name, Value = e.Value, MetaJson = meta };
                case EditOperation.RemoveAttribute: return new SavedEdit { Op = "removeAttribute", Target = target, Name = e.Name, MetaJson = meta };
                case EditOperation.ReplaceText: return new SavedEdit { Op = "replaceText", Target = target, Text = e.Value, MetaJson = meta };
                case EditOperation.InsertBefore: return new SavedEdit { Op = "insertBefore", Target = target, Html = e.Value, MetaJson = meta };
                case EditOperation.InsertAfter: return new SavedEdit { Op = "insertAfter", Target = target, Html = e.Value, MetaJson = meta };
                case EditOperation.Prepend: return new SavedEdit { Op = "prepend", Target = target, Html = e.Value, MetaJson = meta };
                case EditOperation.Append: return new SavedEdit { Op = "append", Target = target, Html = e.Value, MetaJson = meta };
                case EditOperation.Wrap: return new SavedEdit { Op = "wrap", Target = target, Html = e.Value, Count = e.Count, MetaJson = meta };
                case EditOperation.Unwrap: return new SavedEdit { Op = "unwrap", Target = target, MetaJson = meta };
                case EditOperation.Replace: return new SavedEdit { Op = "replace", Target = target, Html = e.Value, MetaJson = meta };
                case EditOperation.Remove: return new SavedEdit { Op = "remove", Target = target, MetaJson = meta };
                case EditOperation.SetStyle: return new SavedEdit { Op = "setStyle", Target = target, Property = e.Name, Value = e.Value, Important = e.Important, MetaJson = meta };
                case EditOperation.RemoveStyle: return new SavedEdit { Op = "removeStyle", Target = target, Property = e.Name, MetaJson = meta };
                default: throw new ArgumentException("Unknown edit operation: " + e.Operation);
            }
        }

        public SavedEdit Make(SheetEdit e)
        {
            string sheet = SheetName(e.Sheet);
            string? meta = MetaWriter.ToJson(e.Meta);
            SavedTarget? target = e.HasTarget ? RuleTarget(e.Sheet, sheet, e.Target) : null;
            switch (e.Operation)
            {
                case SheetEditOperation.SetDeclaration: return new SavedEdit { Op = "setDeclaration", Target = target, Property = e.Property, Value = e.Value, Important = e.Important, MetaJson = meta };
                case SheetEditOperation.RemoveDeclaration: return new SavedEdit { Op = "removeDeclaration", Target = target, Property = e.Property, MetaJson = meta };
                case SheetEditOperation.RemoveRule: return new SavedEdit { Op = "removeRule", Target = target, MetaJson = meta };
                case SheetEditOperation.ReplaceRule: return new SavedEdit { Op = "replaceRule", Target = target, Css = e.Css, MetaJson = meta };
                case SheetEditOperation.InsertRule:
                    return new SavedEdit
                    {
                        Op = "insertRule",
                        Target = target,
                        Css = e.Css,
                        Placement = e.Placement switch { RulePlacement.Before => "before", RulePlacement.After => "after", _ => "end" },
                        Sheet = target is null ? sheet : null,
                        MetaJson = meta,
                    };
                default: throw new ArgumentException("Unknown stylesheet operation: " + e.Operation);
            }
        }

        private string SheetName(Stylesheet sheet)
        {
            if (_doc is not null)
            {
                foreach (InlineStylesheet i in InlineStylesheets.Of(_doc))
                {
                    if (ReferenceEquals(i.Sheet, sheet))
                    {
                        return i.Name;
                    }
                }
            }

            foreach (LinkedStylesheet l in _linked)
            {
                if (ReferenceEquals(l.Sheet, sheet))
                {
                    return l.Url;
                }
            }

            if (!string.IsNullOrEmpty(sheet.Location))
            {
                return sheet.Location;
            }

            throw new ArgumentException("A stylesheet edit targets a sheet that is neither a style element of the document nor a supplied linked sheet and has no location, so it has no name to save.");
        }

        private SavedTarget RuleTarget(Stylesheet sheet, string name, CssNode rule)
        {
            if (!_catalogs.TryGetValue(sheet, out RuleCatalog? catalog))
            {
                catalog = new RuleCatalog(sheet);
                _catalogs[sheet] = catalog;
            }

            RuleCatalog.RuleInfo info = catalog.Describe(rule) ?? throw new ArgumentException("The rule is not a rule of its stylesheet's tree: " + rule);
            return new SavedTarget
            {
                Kind = SavedTargetKind.Rule,
                Sheet = name,
                Selector = info.Selector,
                Index = info.Index,
                AtRules = info.AtRules,
                HasFingerprint = true,
                DeclarationsHash = RuleCatalog.DeclarationsHash(rule),
            };
        }

        private SavedTarget NodeTarget(Node node)
        {
            Document doc = node.Document!;
            switch (node.Kind)
            {
                case NodeKind.Element:
                    {
                        ElementFingerprint fp = ElementFingerprint.Create(node);
                        _locators!.TryGenerate(node, out string? locator);
                        Anchor a = AnchorOf(doc, node.Index);
                        return new SavedTarget
                        {
                            Kind = SavedTargetKind.Element,
                            Locator = locator,
                            HasFingerprint = true,
                            Tag = fp.Tag,
                            Attributes = fp.Attributes,
                            TextHash = fp.TextHash,
                            PathHash = fp.PathHash,
                            AnchorStart = a.Start,
                            AnchorEnd = a.End,
                            AnchorBytes = a.Hash,
                        };
                    }

                case NodeKind.Text:
                case NodeKind.Comment:
                    {
                        Node parent = node.Parent;
                        int index = 0;
                        for (Node s = parent.IsNull ? default : parent.FirstChild; !s.IsNull && s != node; s = s.NextSibling)
                        {
                            index++;
                        }

                        Anchor a = AnchorOf(doc, node.Index);
                        string text = ElementFingerprint.NormalizeText(node.Data.ToString());
                        return new SavedTarget
                        {
                            Kind = node.Kind == NodeKind.Text ? SavedTargetKind.Text : SavedTargetKind.Comment,
                            Parent = parent.IsElement ? NodeTarget(parent) : null,
                            ChildIndex = parent.IsNull ? -1 : index,
                            HasFingerprint = true,
                            TextHash = "sha256:" + Sha256.HashHex(Encoding.UTF8.GetBytes(text)),
                            AnchorStart = a.Start,
                            AnchorEnd = a.End,
                            AnchorBytes = a.Hash,
                        };
                    }

                default:
                    throw new ArgumentException("Only elements, text nodes and comments can be edit targets: " + node);
            }
        }
    }

    internal readonly record struct Anchor(long Start, long End, string? Hash);

    /// <summary>
    /// The source extent of a node: byte offsets and the hash of those bytes, or text offsets and the hash of their
    /// UTF-8 form when the document was parsed from a string. No anchor (-1) for nodes the parser made.
    /// </summary>
    internal static Anchor AnchorOf(Document doc, int index)
    {
        if (!SourceInfo.TryExtent(doc.Arena, index, out int s, out int e) || e > doc.Source.Text.Length)
        {
            return new Anchor(-1, -1, null);
        }

        return AnchorAt(doc, s, e);
    }

    internal static Anchor AnchorAt(Document doc, int textStart, int textEnd)
    {
        SourceText src = doc.Source;
        if (src.Bytes.Length > 0)
        {
            int bs = src.ToByteOffset(textStart);
            int be = src.ToByteOffset(textEnd);
            return new Anchor(bs, be, "sha256:" + Sha256.HashHex(src.Bytes.Span[bs..be]));
        }

        return new Anchor(textStart, textEnd, "sha256:" + Sha256.HashHex(Encoding.UTF8.GetBytes(src.Text.Substring(textStart, textEnd - textStart))));
    }
}

/// <summary>Turns the <c>Meta</c> object of an edit into JSON text.</summary>
internal static class MetaWriter
{
    private const int MaxDepth = 32;

    public static string? ToJson(object? meta)
    {
        if (meta is null)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            try
            {
                Write(w, meta, 0);
            }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException("Meta cannot be written as JSON: " + ex.Message, ex);
            }
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(Utf8JsonWriter w, object? value, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException("Meta is nested more than " + MaxDepth + " levels deep.");
        }

        switch (value)
        {
            case null: w.WriteNullValue(); return;
            case string s: w.WriteStringValue(s); return;
            case bool b: w.WriteBooleanValue(b); return;
            case int i: w.WriteNumberValue(i); return;
            case long l: w.WriteNumberValue(l); return;
            case short sh: w.WriteNumberValue(sh); return;
            case byte by: w.WriteNumberValue(by); return;
            case uint ui: w.WriteNumberValue(ui); return;
            case ulong ul: w.WriteNumberValue(ul); return;
            case decimal m: w.WriteNumberValue(m); return;
            case double d when double.IsFinite(d): w.WriteNumberValue(d); return;
            case float f when float.IsFinite(f): w.WriteNumberValue(f); return;
            case JsonElement je: je.WriteTo(w); return;
            case IReadOnlyDictionary<string, object?> ro:
                w.WriteStartObject();
                foreach (KeyValuePair<string, object?> kv in ro)
                {
                    w.WritePropertyName(kv.Key);
                    Write(w, kv.Value, depth + 1);
                }

                w.WriteEndObject();
                return;
            case IDictionary<string, object?> d2:
                w.WriteStartObject();
                foreach (KeyValuePair<string, object?> kv in d2)
                {
                    w.WritePropertyName(kv.Key);
                    Write(w, kv.Value, depth + 1);
                }

                w.WriteEndObject();
                return;
            case IEnumerable<object?> seq:
                w.WriteStartArray();
                foreach (object? item in seq)
                {
                    Write(w, item, depth + 1);
                }

                w.WriteEndArray();
                return;
            default:
                throw new ArgumentException("Meta holds a value of type " + value.GetType().Name + ", which has no JSON form. Use null, strings, booleans, numbers, JsonElement, string-keyed dictionaries and sequences of those.");
        }
    }
}
