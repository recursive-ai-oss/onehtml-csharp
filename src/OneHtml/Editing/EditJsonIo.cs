using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// Reads and writes the saved-edit JSON (RFC 8259) with <see cref="Utf8JsonReader"/> and <see cref="Utf8JsonWriter"/>
/// only: no reflection, no recursion on input depth (unknown members are skipped by the reader's own skip, and the
/// reader's depth limit applies to everything), every limit checked before the allocation it guards.
/// </summary>
internal static class EditJsonIo
{
    private static readonly JavaScriptEncoder s_encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    // ---- reading ----

    public static EditEnvelope Read(ReadOnlySpan<byte> json, EditJsonLimits limits)
    {
        if (json.Length > limits.MaxBytes)
        {
            throw new EditJsonException("the edit JSON is " + json.Length + " bytes; the limit is " + limits.MaxBytes);
        }

        if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
        {
            json = json[3..];
        }

        var options = new JsonReaderOptions { MaxDepth = Math.Max(1, limits.MaxDepth), CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };
        var r = new Utf8JsonReader(json, options);
        try
        {
            return ReadEnvelope(ref r, json, limits);
        }
        catch (EditJsonException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new EditJsonException("the edit JSON is not valid: " + ex.Message, r.BytesConsumed);
        }
    }

    private static EditEnvelope ReadEnvelope(ref Utf8JsonReader r, ReadOnlySpan<byte> json, EditJsonLimits lim)
    {
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject)
        {
            throw Bad(ref r, "the edit JSON must be an object");
        }

        string? format = null;
        long version = -1;
        string? url = null;
        string? sha = null;
        List<SavedEdit>? edits = null;
        long metaTotal = 0;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("format"u8))
            {
                r.Read();
                format = Str(ref r, lim);
            }
            else if (r.ValueTextEquals("version"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out version))
                {
                    throw Bad(ref r, "version must be an integer");
                }
            }
            else if (r.ValueTextEquals("document"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartObject)
                {
                    throw Bad(ref r, "document must be an object");
                }

                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    if (r.ValueTextEquals("url"u8))
                    {
                        r.Read();
                        url = StrOrNull(ref r, lim);
                    }
                    else if (r.ValueTextEquals("sha256"u8))
                    {
                        r.Read();
                        sha = StrOrNull(ref r, lim);
                    }
                    else
                    {
                        r.Skip();
                    }
                }
            }
            else if (r.ValueTextEquals("edits"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartArray)
                {
                    throw Bad(ref r, "edits must be an array");
                }

                edits = [];
                while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                {
                    if (edits.Count >= lim.MaxEdits)
                    {
                        throw Bad(ref r, "the envelope has more than " + lim.MaxEdits + " edits");
                    }

                    edits.Add(ReadEdit(ref r, json, lim, ref metaTotal));
                }
            }
            else
            {
                r.Skip();
            }
        }

        if (format != EditEnvelope.FormatName)
        {
            throw Bad(ref r, "format must be \"" + EditEnvelope.FormatName + "\"");
        }

        if (version != EditEnvelope.CurrentVersion)
        {
            throw Bad(ref r, version < 0 ? "version is missing" : "version " + version.ToString(CultureInfo.InvariantCulture) + " is not supported; this reader handles version " + EditEnvelope.CurrentVersion);
        }

        if (edits is null)
        {
            throw Bad(ref r, "edits is missing");
        }

        if (r.Read())
        {
            throw Bad(ref r, "unexpected content after the envelope");
        }

        return new EditEnvelope(edits, url, sha);
    }

    private static SavedEdit ReadEdit(ref Utf8JsonReader r, ReadOnlySpan<byte> json, EditJsonLimits lim, ref long metaTotal)
    {
        if (r.TokenType != JsonTokenType.StartObject)
        {
            throw Bad(ref r, "an edit must be an object");
        }

        string op = string.Empty;
        SavedTarget? target = null;
        string? placement = null;
        string? name = null, value = null, text = null, html = null, css = null, property = null, sheet = null, meta = null;
        bool important = false;
        long count = 1;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("op"u8))
            {
                r.Read();
                op = Str(ref r, lim);
            }
            else if (r.ValueTextEquals("target"u8))
            {
                r.Read();
                target = ReadTarget(ref r, lim, allowParent: true);
            }
            else if (r.ValueTextEquals("before"u8) || r.ValueTextEquals("after"u8))
            {
                placement = r.ValueTextEquals("before"u8) ? "before" : "after";
                r.Read();
                target = ReadTarget(ref r, lim, allowParent: true);
            }
            else if (r.ValueTextEquals("end"u8))
            {
                r.Read();
                if (r.TokenType == JsonTokenType.True)
                {
                    placement = "end";
                }
                else if (r.TokenType == JsonTokenType.StartObject)
                {
                    placement = "end";
                    target = ReadTarget(ref r, lim, allowParent: true);
                }
                else if (r.TokenType != JsonTokenType.False && r.TokenType != JsonTokenType.Null)
                {
                    throw Bad(ref r, "end must be true or a rule target");
                }
            }
            else if (r.ValueTextEquals("name"u8)) { r.Read(); name = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("value"u8)) { r.Read(); value = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("text"u8)) { r.Read(); text = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("html"u8)) { r.Read(); html = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("css"u8)) { r.Read(); css = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("property"u8)) { r.Read(); property = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("sheet"u8)) { r.Read(); sheet = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("important"u8))
            {
                r.Read();
                important = r.TokenType switch
                {
                    JsonTokenType.True => true,
                    JsonTokenType.False or JsonTokenType.Null => false,
                    _ => throw Bad(ref r, "important must be a boolean"),
                };
            }
            else if (r.ValueTextEquals("count"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out count) || count < 1 || count > int.MaxValue)
                {
                    throw Bad(ref r, "count must be a positive integer");
                }
            }
            else if (r.ValueTextEquals("meta"u8))
            {
                r.Read();
                meta = ReadMeta(ref r, json, lim, ref metaTotal);
            }
            else
            {
                r.Skip();
            }
        }

        return new SavedEdit
        {
            Op = op,
            Target = target,
            Placement = placement,
            Name = name,
            Value = value,
            Text = text,
            Html = html,
            Css = css,
            Property = property,
            Sheet = sheet,
            Important = important,
            Count = (int)count,
            MetaJson = meta,
        };
    }

    private static SavedTarget ReadTarget(ref Utf8JsonReader r, EditJsonLimits lim, bool allowParent)
    {
        if (r.TokenType != JsonTokenType.StartObject)
        {
            throw Bad(ref r, "a target must be an object");
        }

        var kind = SavedTargetKind.Element;
        string? locator = null, tag = null, textHash = null, pathHash = null, bytes = null, sheet = null, selector = null, decl = null;
        bool hasFp = false;
        List<KeyValuePair<string, string>>? attrs = null;
        long start = -1, end = -1, index = -1;
        SavedTarget? parent = null;
        List<string>? atRules = null;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("kind"u8))
            {
                r.Read();
                string k = Str(ref r, lim);
                kind = k switch
                {
                    "element" => SavedTargetKind.Element,
                    "text" => SavedTargetKind.Text,
                    "comment" => SavedTargetKind.Comment,
                    "rule" => SavedTargetKind.Rule,
                    _ => throw Bad(ref r, "unknown target kind \"" + Shorten(k) + "\""),
                };
            }
            else if (r.ValueTextEquals("locator"u8)) { r.Read(); locator = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("sheet"u8)) { r.Read(); sheet = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("selector"u8)) { r.Read(); selector = StrOrNull(ref r, lim); }
            else if (r.ValueTextEquals("index"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out index) || index < 0 || index > int.MaxValue)
                {
                    throw Bad(ref r, "index must be a non-negative integer");
                }
            }
            else if (r.ValueTextEquals("atRules"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartArray)
                {
                    throw Bad(ref r, "atRules must be an array");
                }

                atRules = [];
                while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                {
                    if (atRules.Count >= lim.MaxAtRules)
                    {
                        throw Bad(ref r, "a rule target has more than " + lim.MaxAtRules + " enclosing at-rules");
                    }

                    atRules.Add(Str(ref r, lim));
                }
            }
            else if (r.ValueTextEquals("parent"u8))
            {
                r.Read();
                if (!allowParent)
                {
                    throw Bad(ref r, "a parent target cannot have a parent");
                }

                parent = ReadTarget(ref r, lim, allowParent: false);
            }
            else if (r.ValueTextEquals("fingerprint"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartObject)
                {
                    throw Bad(ref r, "fingerprint must be an object");
                }

                hasFp = true;
                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    if (r.ValueTextEquals("tag"u8)) { r.Read(); tag = StrOrNull(ref r, lim); }
                    else if (r.ValueTextEquals("text"u8)) { r.Read(); textHash = StrOrNull(ref r, lim); }
                    else if (r.ValueTextEquals("path"u8)) { r.Read(); pathHash = StrOrNull(ref r, lim); }
                    else if (r.ValueTextEquals("declarations"u8)) { r.Read(); decl = StrOrNull(ref r, lim); }
                    else if (r.ValueTextEquals("attrs"u8))
                    {
                        r.Read();
                        if (r.TokenType != JsonTokenType.StartObject)
                        {
                            throw Bad(ref r, "attrs must be an object");
                        }

                        attrs = [];
                        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                        {
                            if (attrs.Count >= lim.MaxAttributes)
                            {
                                throw Bad(ref r, "a fingerprint has more than " + lim.MaxAttributes + " attributes");
                            }

                            string key = Str(ref r, lim, name: true);
                            r.Read();
                            attrs.Add(new KeyValuePair<string, string>(key, Str(ref r, lim)));
                        }
                    }
                    else
                    {
                        r.Skip();
                    }
                }
            }
            else if (r.ValueTextEquals("anchor"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartObject)
                {
                    throw Bad(ref r, "anchor must be an object");
                }

                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    if (r.ValueTextEquals("start"u8))
                    {
                        r.Read();
                        if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out start) || start < 0)
                        {
                            throw Bad(ref r, "anchor start must be a non-negative integer");
                        }
                    }
                    else if (r.ValueTextEquals("end"u8))
                    {
                        r.Read();
                        if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out end) || end < 0)
                        {
                            throw Bad(ref r, "anchor end must be a non-negative integer");
                        }
                    }
                    else if (r.ValueTextEquals("bytes"u8)) { r.Read(); bytes = StrOrNull(ref r, lim); }
                    else
                    {
                        r.Skip();
                    }
                }
            }
            else
            {
                r.Skip();
            }
        }

        attrs?.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return new SavedTarget
        {
            Kind = kind,
            Locator = locator,
            Tag = tag,
            Attributes = attrs ?? [],
            TextHash = textHash,
            PathHash = pathHash,
            HasFingerprint = hasFp,
            AnchorStart = start,
            AnchorEnd = end,
            AnchorBytes = bytes,
            Parent = parent,
            ChildIndex = kind is SavedTargetKind.Text or SavedTargetKind.Comment ? (int)index : -1,
            Sheet = sheet,
            Selector = selector,
            Index = kind == SavedTargetKind.Rule ? (int)index : -1,
            AtRules = atRules ?? [],
            DeclarationsHash = decl,
        };
    }

    // The meta value is kept as compact JSON text. Its extent is found on a copy of the reader (a struct) so the size
    // limit applies before anything is copied out.
    private static string? ReadMeta(ref Utf8JsonReader r, ReadOnlySpan<byte> json, EditJsonLimits lim, ref long total)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        int start = checked((int)r.TokenStartIndex);
        Utf8JsonReader probe = r;
        probe.Skip();
        int end = checked((int)probe.BytesConsumed);
        int size = end - start;
        total += size;
        if (size > lim.MaxMetaBytes || total > (long)lim.MaxMetaBytes * 256)
        {
            throw Bad(ref r, "meta is larger than the limit of " + lim.MaxMetaBytes + " bytes");
        }

        string compact = Compact(json.Slice(start, size), lim);
        r.Skip();
        return compact;
    }

    /// <summary>Re-writes a JSON value without insignificant white space, token by token (no recursion).</summary>
    internal static string Compact(ReadOnlySpan<byte> value, EditJsonLimits lim)
    {
        var buffer = new ArrayBufferWriter<byte>(value.Length);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = s_encoder, SkipValidation = false }))
        {
            var rd = new Utf8JsonReader(value, new JsonReaderOptions { MaxDepth = Math.Max(1, lim.MaxDepth) });
            while (rd.Read())
            {
                switch (rd.TokenType)
                {
                    case JsonTokenType.StartObject: w.WriteStartObject(); break;
                    case JsonTokenType.EndObject: w.WriteEndObject(); break;
                    case JsonTokenType.StartArray: w.WriteStartArray(); break;
                    case JsonTokenType.EndArray: w.WriteEndArray(); break;
                    case JsonTokenType.PropertyName: w.WritePropertyName(rd.GetString()!); break;
                    case JsonTokenType.String: w.WriteStringValue(rd.GetString()); break;
                    case JsonTokenType.Number: w.WriteRawValue(rd.ValueSpan); break;
                    case JsonTokenType.True: w.WriteBooleanValue(true); break;
                    case JsonTokenType.False: w.WriteBooleanValue(false); break;
                    case JsonTokenType.Null: w.WriteNullValue(); break;
                    default: break;
                }
            }
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Str(ref Utf8JsonReader r, EditJsonLimits lim, bool name = false)
    {
        if (name ? r.TokenType != JsonTokenType.PropertyName : r.TokenType != JsonTokenType.String)
        {
            throw Bad(ref r, "a string was expected");
        }

        if (r.ValueSpan.Length > lim.MaxStringBytes)
        {
            throw Bad(ref r, "a string is longer than the limit of " + lim.MaxStringBytes + " bytes");
        }

        return r.GetString()!;
    }

    private static string? StrOrNull(ref Utf8JsonReader r, EditJsonLimits lim) =>
        r.TokenType == JsonTokenType.Null ? null : Str(ref r, lim);

    private static EditJsonException Bad(ref Utf8JsonReader r, string message) => new(message, r.TokenStartIndex);

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..40] + "...";

    // ---- writing ----

    public static void Write(Utf8JsonWriter w, EditEnvelope e)
    {
        w.WriteStartObject();
        w.WriteString("format", EditEnvelope.FormatName);
        w.WriteNumber("version", EditEnvelope.CurrentVersion);
        if (e.DocumentUrl is not null || e.DocumentSha256 is not null)
        {
            w.WriteStartObject("document");
            if (e.DocumentUrl is not null)
            {
                w.WriteString("url", e.DocumentUrl);
            }

            if (e.DocumentSha256 is not null)
            {
                w.WriteString("sha256", e.DocumentSha256);
            }

            w.WriteEndObject();
        }

        w.WriteStartArray("edits");
        foreach (SavedEdit edit in e.Edits)
        {
            WriteEdit(w, edit);
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteEdit(Utf8JsonWriter w, SavedEdit e)
    {
        w.WriteStartObject();
        w.WriteString("op", e.Op);
        if (e.Placement is "before" or "after" && e.Target is not null)
        {
            w.WritePropertyName(e.Placement);
            WriteTarget(w, e.Target);
        }
        else if (e.Placement == "end")
        {
            if (e.Target is not null)
            {
                w.WritePropertyName("end");
                WriteTarget(w, e.Target);
            }
            else
            {
                w.WriteBoolean("end", true);
            }
        }
        else if (e.Target is not null)
        {
            w.WritePropertyName("target");
            WriteTarget(w, e.Target);
        }

        WriteIf(w, "name", e.Name);
        WriteIf(w, "value", e.Value);
        WriteIf(w, "text", e.Text);
        WriteIf(w, "html", e.Html);
        if (e.Count != 1)
        {
            w.WriteNumber("count", e.Count);
        }

        WriteIf(w, "property", e.Property);
        if (e.Important)
        {
            w.WriteBoolean("important", true);
        }

        WriteIf(w, "css", e.Css);
        WriteIf(w, "sheet", e.Sheet);
        if (e.MetaJson is not null)
        {
            w.WritePropertyName("meta");
            w.WriteRawValue(e.MetaJson);
        }

        w.WriteEndObject();
    }

    private static void WriteIf(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    private static void WriteTarget(Utf8JsonWriter w, SavedTarget t)
    {
        w.WriteStartObject();
        if (t.Kind != SavedTargetKind.Element)
        {
            w.WriteString("kind", t.Kind switch { SavedTargetKind.Text => "text", SavedTargetKind.Comment => "comment", _ => "rule" });
        }

        WriteIf(w, "locator", t.Locator);
        WriteIf(w, "sheet", t.Sheet);
        WriteIf(w, "selector", t.Selector);
        if (t.Kind == SavedTargetKind.Rule)
        {
            if (t.Index >= 0)
            {
                w.WriteNumber("index", t.Index);
            }

            if (t.AtRules.Count > 0)
            {
                w.WriteStartArray("atRules");
                foreach (string a in t.AtRules)
                {
                    w.WriteStringValue(a);
                }

                w.WriteEndArray();
            }
        }
        else
        {
            if (t.Parent is not null)
            {
                w.WritePropertyName("parent");
                WriteTarget(w, t.Parent);
            }

            if (t.ChildIndex >= 0)
            {
                w.WriteNumber("index", t.ChildIndex);
            }
        }

        if (t.HasFingerprint || t.Tag is not null || t.TextHash is not null || t.PathHash is not null || t.DeclarationsHash is not null)
        {
            w.WriteStartObject("fingerprint");
            WriteIf(w, "tag", t.Tag);
            if (t.Attributes.Count > 0)
            {
                w.WriteStartObject("attrs");
                foreach (KeyValuePair<string, string> a in t.Attributes)
                {
                    w.WriteString(a.Key, a.Value);
                }

                w.WriteEndObject();
            }

            WriteIf(w, "text", t.TextHash);
            WriteIf(w, "path", t.PathHash);
            WriteIf(w, "declarations", t.DeclarationsHash);
            w.WriteEndObject();
        }

        if (t.AnchorStart >= 0 && t.AnchorEnd >= t.AnchorStart)
        {
            w.WriteStartObject("anchor");
            w.WriteNumber("start", t.AnchorStart);
            w.WriteNumber("end", t.AnchorEnd);
            WriteIf(w, "bytes", t.AnchorBytes);
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    public static string ToJson(EditEnvelope e, bool indented)
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented, Encoder = s_encoder }))
            {
                Write(w, e);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new EditJsonException("the edits cannot be written as JSON: " + ex.Message);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
