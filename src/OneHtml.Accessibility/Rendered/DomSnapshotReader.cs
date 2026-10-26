using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>
/// Reads <c>DOMSnapshot.captureSnapshot</c> JSON with <see cref="Utf8JsonReader"/>: no reflection, no recursion on
/// input depth (nested values are skipped by the reader's own explicit stack), every size checked before the array grows.
/// Table shapes follow the DevTools protocol reference for the DOMSnapshot domain.
/// </summary>
internal static class DomSnapshotReader
{
    private sealed class Ctx(DomSnapshotLimits limits)
    {
        public readonly DomSnapshotLimits Limits = limits;

        public int NodesSoFar;

        public int LayoutSoFar;

        public int TextBoxesSoFar;

        public string[]? Strings;

        public List<DomSnapshotDocumentData>? Documents;

        public string[]? StyleNames;

        public string? Product;

        public string? ProtocolVersion;

        public bool SawFormat;
    }

    public static DomSnapshot Read(ReadOnlySpan<byte> json, IReadOnlyList<string>? styleNames, DomSnapshotLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (json.Length > limits.MaxInputBytes)
        {
            throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxInputBytes), limits.MaxInputBytes, json.Length);
        }

        var ctx = new Ctx(limits);
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = Math.Max(4, limits.MaxJsonDepth), CommentHandling = JsonCommentHandling.Disallow });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new DomSnapshotFormatException("The snapshot must be a JSON object.");
            }

            ReadRoot(ref reader, ctx, 0);
        }
        catch (JsonException e)
        {
            throw new DomSnapshotFormatException("The snapshot is not valid JSON: " + e.Message, e);
        }
        catch (InvalidOperationException e)
        {
            throw new DomSnapshotFormatException("The snapshot has a value of an unexpected type: " + e.Message, e);
        }
        catch (FormatException e)
        {
            throw new DomSnapshotFormatException("The snapshot has a malformed value: " + e.Message, e);
        }

        if (ctx.Strings is null)
        {
            throw new DomSnapshotFormatException("The snapshot has no \"strings\" table.");
        }

        if (ctx.Documents is null)
        {
            throw new DomSnapshotFormatException("The snapshot has no \"documents\" array.");
        }

        string[] names = ctx.StyleNames ?? [];
        if (names.Length == 0 && styleNames is not null)
        {
            if (styleNames.Count > limits.MaxComputedStyleNames)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxComputedStyleNames), limits.MaxComputedStyleNames, styleNames.Count);
            }

            names = new string[styleNames.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = styleNames[i];
            }
        }

        DomSnapshotDocumentData[] docs = [.. ctx.Documents];
        Validate(ctx.Strings, docs, names, limits);
        return new DomSnapshot(ctx.Strings, docs, names, ctx.Product, ctx.ProtocolVersion);
    }

    // The root object is the protocol result, a protocol response wrapping it in "result", or the capture file wrapping it in
    // "snapshot". Wrapping nests at most two levels, so this recursion is bounded by a constant, not by the input.
    private static void ReadRoot(ref Utf8JsonReader r, Ctx c, int level)
    {
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("documents"u8))
            {
                r.Read();
                ReadDocuments(ref r, c);
            }
            else if (r.ValueTextEquals("strings"u8))
            {
                r.Read();
                ReadStrings(ref r, c);
            }
            else if (r.ValueTextEquals("format"u8))
            {
                r.Read();
                ReadFormat(ref r, c);
            }
            else if ((r.ValueTextEquals("snapshot"u8) || r.ValueTextEquals("result"u8)) && level < 2)
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartObject)
                {
                    throw new DomSnapshotFormatException("\"snapshot\" must be an object.");
                }

                ReadRoot(ref r, c, level + 1);
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
    }

    private static void ReadFormat(ref Utf8JsonReader r, Ctx c)
    {
        if (r.TokenType != JsonTokenType.StartObject)
        {
            throw new DomSnapshotFormatException("\"format\" must be an object.");
        }

        c.SawFormat = true;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("domain"u8))
            {
                r.Read();
                string? v = ReadStringOrNull(ref r);
                if (v is not null && v != "DOMSnapshot")
                {
                    throw new DomSnapshotFormatException($"The capture is for the \"{Clip(v)}\" domain, not DOMSnapshot.");
                }
            }
            else if (r.ValueTextEquals("method"u8))
            {
                r.Read();
                string? v = ReadStringOrNull(ref r);
                if (v is not null && v != "captureSnapshot")
                {
                    throw new DomSnapshotFormatException($"The capture is for the \"{Clip(v)}\" method, not captureSnapshot.");
                }
            }
            else if (r.ValueTextEquals("protocolVersion"u8))
            {
                r.Read();
                c.ProtocolVersion = ReadStringOrNull(ref r);
                if (c.ProtocolVersion is { } pv && !(pv == "1" || pv.StartsWith("1.", StringComparison.Ordinal)))
                {
                    throw new DomSnapshotFormatException($"The capture used protocol version \"{Clip(pv)}\"; this reader is pinned to {DomSnapshot.PinnedProtocolVersion}.");
                }
            }
            else if (r.ValueTextEquals("product"u8))
            {
                r.Read();
                c.Product = ReadStringOrNull(ref r);
            }
            else if (r.ValueTextEquals("parameters"u8))
            {
                r.Read();
                if (r.TokenType != JsonTokenType.StartObject)
                {
                    r.Skip();
                    continue;
                }

                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    if (r.ValueTextEquals("computedStyles"u8))
                    {
                        r.Read();
                        ReadStyleNames(ref r, c);
                    }
                    else
                    {
                        r.Read();
                        r.Skip();
                    }
                }
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
    }

    private static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "...";

    private static string? ReadStringOrNull(ref Utf8JsonReader r)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (r.TokenType != JsonTokenType.String)
        {
            r.Skip();
            return null;
        }

        return r.GetString();
    }

    private static void ReadStyleNames(ref Utf8JsonReader r, Ctx c)
    {
        Expect(ref r, JsonTokenType.StartArray, "computedStyles");
        var list = new List<string>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (list.Count >= c.Limits.MaxComputedStyleNames)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxComputedStyleNames), c.Limits.MaxComputedStyleNames, list.Count + 1);
            }

            Expect(ref r, JsonTokenType.String, "computedStyles entry");
            list.Add(r.GetString()!);
        }

        c.StyleNames = [.. list];
    }

    private static void ReadStrings(ref Utf8JsonReader r, Ctx c)
    {
        Expect(ref r, JsonTokenType.StartArray, "strings");
        var list = new List<string>();
        long chars = 0;
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (list.Count >= c.Limits.MaxStrings)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxStrings), c.Limits.MaxStrings, list.Count + 1);
            }

            Expect(ref r, JsonTokenType.String, "strings entry");

            // A string is at most as many UTF-16 units as it has bytes, so this bound holds before it is materialised.
            chars += r.HasValueSequence ? r.ValueSequence.Length : r.ValueSpan.Length;
            if (chars > c.Limits.MaxStringChars * 3)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxStringChars), c.Limits.MaxStringChars, chars);
            }

            list.Add(r.GetString()!);
        }

        c.Strings = [.. list];
    }

    private static void ReadDocuments(ref Utf8JsonReader r, Ctx c)
    {
        Expect(ref r, JsonTokenType.StartArray, "documents");
        var docs = new List<DomSnapshotDocumentData>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (docs.Count >= c.Limits.MaxDocuments)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxDocuments), c.Limits.MaxDocuments, docs.Count + 1);
            }

            Expect(ref r, JsonTokenType.StartObject, "documents entry");
            docs.Add(ReadDocument(ref r, c));
        }

        c.Documents = docs;
    }

    private static DomSnapshotDocumentData ReadDocument(ref Utf8JsonReader r, Ctx c)
    {
        var d = new DomSnapshotDocumentData();
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("nodes"u8))
            {
                r.Read();
                ReadNodes(ref r, c, d);
            }
            else if (r.ValueTextEquals("layout"u8))
            {
                r.Read();
                ReadLayout(ref r, c, d);
            }
            else if (r.ValueTextEquals("textBoxes"u8))
            {
                r.Read();
                ReadTextBoxes(ref r, c, d);
            }
            else if (r.ValueTextEquals("documentURL"u8))
            {
                r.Read();
                d.DocumentUrl = ReadInt(ref r, "documentURL");
            }
            else if (r.ValueTextEquals("baseURL"u8))
            {
                r.Read();
                d.BaseUrl = ReadInt(ref r, "baseURL");
            }
            else if (r.ValueTextEquals("title"u8))
            {
                r.Read();
                d.Title = ReadInt(ref r, "title");
            }
            else if (r.ValueTextEquals("frameId"u8))
            {
                r.Read();
                d.FrameId = ReadInt(ref r, "frameId");
            }
            else if (r.ValueTextEquals("contentLanguage"u8))
            {
                r.Read();
                d.ContentLanguage = ReadInt(ref r, "contentLanguage");
            }
            else if (r.ValueTextEquals("encodingName"u8))
            {
                r.Read();
                d.EncodingName = ReadInt(ref r, "encodingName");
            }
            else if (r.ValueTextEquals("publicId"u8))
            {
                r.Read();
                d.PublicId = ReadInt(ref r, "publicId");
            }
            else if (r.ValueTextEquals("systemId"u8))
            {
                r.Read();
                d.SystemId = ReadInt(ref r, "systemId");
            }
            else if (r.ValueTextEquals("contentWidth"u8))
            {
                r.Read();
                d.ContentWidth = ReadDouble(ref r, "contentWidth");
            }
            else if (r.ValueTextEquals("contentHeight"u8))
            {
                r.Read();
                d.ContentHeight = ReadDouble(ref r, "contentHeight");
            }
            else if (r.ValueTextEquals("scrollOffsetX"u8))
            {
                r.Read();
                d.ScrollOffsetX = ReadDouble(ref r, "scrollOffsetX");
            }
            else if (r.ValueTextEquals("scrollOffsetY"u8))
            {
                r.Read();
                d.ScrollOffsetY = ReadDouble(ref r, "scrollOffsetY");
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }

        return d;
    }

    private static void ReadNodes(ref Utf8JsonReader r, Ctx c, DomSnapshotDocumentData d)
    {
        Expect(ref r, JsonTokenType.StartObject, "nodes");
        int nodeCap = c.Limits.MaxNodes;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("nodeType"u8))
            {
                r.Read();
                d.NodeType = ReadInts(ref r, c, "nodeType", Math.Max(0, nodeCap - c.NodesSoFar), nameof(DomSnapshotLimits.MaxNodes));
                c.NodesSoFar += d.NodeType.Length;
            }
            else if (r.ValueTextEquals("parentIndex"u8))
            {
                r.Read();
                d.ParentIndex = ReadInts(ref r, c, "parentIndex", nodeCap, nameof(DomSnapshotLimits.MaxNodes));
            }
            else if (r.ValueTextEquals("nodeName"u8))
            {
                r.Read();
                d.NodeName = ReadInts(ref r, c, "nodeName", nodeCap, nameof(DomSnapshotLimits.MaxNodes));
            }
            else if (r.ValueTextEquals("nodeValue"u8))
            {
                r.Read();
                d.NodeValue = ReadInts(ref r, c, "nodeValue", nodeCap, nameof(DomSnapshotLimits.MaxNodes));
            }
            else if (r.ValueTextEquals("backendNodeId"u8))
            {
                r.Read();
                d.BackendNodeId = ReadLongs(ref r, c, "backendNodeId", nodeCap);
            }
            else if (r.ValueTextEquals("attributes"u8))
            {
                r.Read();
                (d.AttrData, d.AttrOffsets) = ReadIntArrays(ref r, c, "attributes", nodeCap);
            }
            else if (r.ValueTextEquals("contentDocumentIndex"u8))
            {
                r.Read();
                d.ContentDocumentIndex = ReadRare(ref r, c, "contentDocumentIndex", true);
            }
            else if (r.ValueTextEquals("shadowRootType"u8))
            {
                r.Read();
                d.ShadowRootType = ReadRare(ref r, c, "shadowRootType", true);
            }
            else if (r.ValueTextEquals("pseudoType"u8))
            {
                r.Read();
                d.PseudoType = ReadRare(ref r, c, "pseudoType", true);
            }
            else if (r.ValueTextEquals("pseudoIdentifier"u8))
            {
                r.Read();
                d.PseudoIdentifier = ReadRare(ref r, c, "pseudoIdentifier", true);
            }
            else if (r.ValueTextEquals("inputValue"u8))
            {
                r.Read();
                d.InputValue = ReadRare(ref r, c, "inputValue", true);
            }
            else if (r.ValueTextEquals("textValue"u8))
            {
                r.Read();
                d.TextValue = ReadRare(ref r, c, "textValue", true);
            }
            else if (r.ValueTextEquals("inputChecked"u8))
            {
                r.Read();
                d.InputChecked = ReadRare(ref r, c, "inputChecked", false);
            }
            else if (r.ValueTextEquals("optionSelected"u8))
            {
                r.Read();
                d.OptionSelected = ReadRare(ref r, c, "optionSelected", false);
            }
            else if (r.ValueTextEquals("isClickable"u8))
            {
                r.Read();
                d.IsClickable = ReadRare(ref r, c, "isClickable", false);
            }
            else if (r.ValueTextEquals("currentSourceURL"u8))
            {
                r.Read();
                d.CurrentSourceUrl = ReadRare(ref r, c, "currentSourceURL", true);
            }
            else if (r.ValueTextEquals("originURL"u8))
            {
                r.Read();
                d.OriginUrl = ReadRare(ref r, c, "originURL", true);
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
    }

    private static void ReadLayout(ref Utf8JsonReader r, Ctx c, DomSnapshotDocumentData d)
    {
        Expect(ref r, JsonTokenType.StartObject, "layout");
        int cap = c.Limits.MaxLayoutNodes;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("nodeIndex"u8))
            {
                r.Read();
                d.LayoutNodeIndex = ReadInts(ref r, c, "layout.nodeIndex", Math.Max(0, cap - c.LayoutSoFar), nameof(DomSnapshotLimits.MaxLayoutNodes));
                c.LayoutSoFar += d.LayoutNodeIndex.Length;
            }
            else if (r.ValueTextEquals("bounds"u8))
            {
                r.Read();
                d.Bounds = ReadRects(ref r, c, "layout.bounds", cap);
            }
            else if (r.ValueTextEquals("clientRects"u8))
            {
                r.Read();
                d.ClientRects = ReadRects(ref r, c, "layout.clientRects", cap);
            }
            else if (r.ValueTextEquals("offsetRects"u8))
            {
                r.Read();
                d.OffsetRects = ReadRects(ref r, c, "layout.offsetRects", cap);
            }
            else if (r.ValueTextEquals("scrollRects"u8))
            {
                r.Read();
                d.ScrollRects = ReadRects(ref r, c, "layout.scrollRects", cap);
            }
            else if (r.ValueTextEquals("styles"u8))
            {
                r.Read();
                (d.StyleData, d.StyleOffsets) = ReadIntArrays(ref r, c, "layout.styles", cap);
            }
            else if (r.ValueTextEquals("text"u8))
            {
                r.Read();
                d.LayoutText = ReadInts(ref r, c, "layout.text", cap, nameof(DomSnapshotLimits.MaxLayoutNodes));
            }
            else if (r.ValueTextEquals("paintOrders"u8))
            {
                r.Read();
                d.PaintOrders = ReadInts(ref r, c, "layout.paintOrders", cap, nameof(DomSnapshotLimits.MaxLayoutNodes));
            }
            else if (r.ValueTextEquals("blendedBackgroundColors"u8))
            {
                r.Read();
                d.BlendedBackground = ReadIntsLoose(ref r, c, "layout.blendedBackgroundColors", cap);
            }
            else if (r.ValueTextEquals("textColorOpacities"u8))
            {
                r.Read();
                d.TextColorOpacities = ReadDoubles(ref r, c, "layout.textColorOpacities", cap);
            }
            else if (r.ValueTextEquals("stackingContexts"u8))
            {
                r.Read();
                Rare rare = ReadRare(ref r, c, "layout.stackingContexts", false, cap);
                d.StackingContexts = rare.Index;
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
    }

    private static void ReadTextBoxes(ref Utf8JsonReader r, Ctx c, DomSnapshotDocumentData d)
    {
        Expect(ref r, JsonTokenType.StartObject, "textBoxes");
        int cap = c.Limits.MaxTextBoxes;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("layoutIndex"u8))
            {
                r.Read();
                d.TextBoxLayoutIndex = ReadInts(ref r, c, "textBoxes.layoutIndex", Math.Max(0, cap - c.TextBoxesSoFar), nameof(DomSnapshotLimits.MaxTextBoxes));
                c.TextBoxesSoFar += d.TextBoxLayoutIndex.Length;
            }
            else if (r.ValueTextEquals("bounds"u8))
            {
                r.Read();
                d.TextBoxBounds = ReadRects(ref r, c, "textBoxes.bounds", cap);
            }
            else if (r.ValueTextEquals("start"u8))
            {
                r.Read();
                d.TextBoxStart = ReadInts(ref r, c, "textBoxes.start", cap, nameof(DomSnapshotLimits.MaxTextBoxes));
            }
            else if (r.ValueTextEquals("length"u8))
            {
                r.Read();
                d.TextBoxLength = ReadInts(ref r, c, "textBoxes.length", cap, nameof(DomSnapshotLimits.MaxTextBoxes));
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }
    }

    // ---- value helpers: each starts on the value's first token and ends on its last ----

    private static void Expect(ref Utf8JsonReader r, JsonTokenType type, string what)
    {
        if (r.TokenType != type)
        {
            throw new DomSnapshotFormatException($"\"{what}\" must be a JSON {Describe(type)}, not {r.TokenType}.");
        }
    }

    private static string Describe(JsonTokenType t) => t switch
    {
        JsonTokenType.StartArray => "array",
        JsonTokenType.StartObject => "object",
        JsonTokenType.String => "string",
        _ => "number",
    };

    private static int ReadInt(ref Utf8JsonReader r, string what)
    {
        if (r.TokenType != JsonTokenType.Number)
        {
            throw new DomSnapshotFormatException($"\"{what}\" must be a number, not {r.TokenType}.");
        }

        if (r.TryGetInt32(out int v))
        {
            return v;
        }

        double dv = r.GetDouble();
        if (dv == Math.Floor(dv) && dv >= int.MinValue && dv <= int.MaxValue)
        {
            return (int)dv;
        }

        throw new DomSnapshotFormatException($"\"{what}\" must be an integer.");
    }

    private static long ReadLong(ref Utf8JsonReader r, string what)
    {
        if (r.TokenType != JsonTokenType.Number)
        {
            throw new DomSnapshotFormatException($"\"{what}\" must be a number, not {r.TokenType}.");
        }

        if (r.TryGetInt64(out long v))
        {
            return v;
        }

        throw new DomSnapshotFormatException($"\"{what}\" must be an integer.");
    }

    private static double ReadDouble(ref Utf8JsonReader r, string what)
    {
        if (r.TokenType != JsonTokenType.Number)
        {
            throw new DomSnapshotFormatException($"\"{what}\" must be a number, not {r.TokenType}.");
        }

        double v = r.GetDouble();
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            throw new DomSnapshotFormatException($"\"{what}\" is not a finite number.");
        }

        return v;
    }

    private static int[] ReadInts(ref Utf8JsonReader r, Ctx c, string name, int cap, string limitName)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        Expect(ref r, JsonTokenType.StartArray, name);
        var list = new List<int>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (list.Count >= cap)
            {
                throw new DomSnapshotFormatException(limitName, cap, (long)list.Count + 1);
            }

            list.Add(ReadInt(ref r, name));
        }

        return list.ToArray();
    }

    // Accepts a number or null per entry, or a nested array (the first element is kept): blendedBackgroundColors is documented as
    // an array of string indexes.
    private static int[] ReadIntsLoose(ref Utf8JsonReader r, Ctx c, string name, int cap)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        Expect(ref r, JsonTokenType.StartArray, name);
        var list = new List<int>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (list.Count >= cap)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxLayoutNodes), cap, (long)list.Count + 1);
            }

            switch (r.TokenType)
            {
                case JsonTokenType.Null:
                    list.Add(-1);
                    break;
                case JsonTokenType.StartArray:
                    {
                        int first = -1;
                        bool have = false;
                        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                        {
                            if (!have && r.TokenType == JsonTokenType.Number)
                            {
                                first = ReadInt(ref r, name);
                                have = true;
                            }
                            else
                            {
                                r.Skip();
                            }
                        }

                        list.Add(first);
                        break;
                    }

                default:
                    list.Add(ReadInt(ref r, name));
                    break;
            }
        }

        return list.ToArray();
    }

    private static long[] ReadLongs(ref Utf8JsonReader r, Ctx c, string name, int cap)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        Expect(ref r, JsonTokenType.StartArray, name);
        var list = new List<long>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (list.Count >= cap)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxNodes), cap, (long)list.Count + 1);
            }

            list.Add(ReadLong(ref r, name));
        }

        return list.ToArray();
    }

    private static double[] ReadDoubles(ref Utf8JsonReader r, Ctx c, string name, int cap)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        Expect(ref r, JsonTokenType.StartArray, name);
        var list = new List<double>();
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (list.Count >= cap)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxLayoutNodes), cap, (long)list.Count + 1);
            }

            list.Add(r.TokenType == JsonTokenType.Null ? double.NaN : ReadDouble(ref r, name));
        }

        return list.ToArray();
    }

    // Rectangles: [x, y, width, height]. Chrome writes [] for items without one; that becomes four NaNs.
    private static double[] ReadRects(ref Utf8JsonReader r, Ctx c, string name, int cap)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        Expect(ref r, JsonTokenType.StartArray, name);
        var list = new List<double>();
        int count = 0;
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (count >= cap)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxLayoutNodes), cap, (long)count + 1);
            }

            count++;
            if (r.TokenType == JsonTokenType.Null)
            {
                AddNaN4(list);
                continue;
            }

            Expect(ref r, JsonTokenType.StartArray, name + " entry");
            int n = 0;
            while (r.Read() && r.TokenType != JsonTokenType.EndArray)
            {
                if (++n > 4)
                {
                    throw new DomSnapshotFormatException($"\"{name}\" has a rectangle with more than four numbers.");
                }

                list.Add(ReadDouble(ref r, name));
            }

            if (n == 0)
            {
                AddNaN4(list);
            }
            else if (n != 4)
            {
                throw new DomSnapshotFormatException($"\"{name}\" has a rectangle with {n} numbers; expected 0 or 4.");
            }

        }

        return list.ToArray();
    }

    private static void AddNaN4(List<double> list)
    {
        list.Add(double.NaN);
        list.Add(double.NaN);
        list.Add(double.NaN);
        list.Add(double.NaN);
    }

    // An array of integer arrays, flattened: data plus offsets (offsets[i]..offsets[i+1] is entry i).
    private static (int[] Data, int[] Offsets) ReadIntArrays(ref Utf8JsonReader r, Ctx c, string name, int cap)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return ([], [0]);
        }

        Expect(ref r, JsonTokenType.StartArray, name);
        var data = new List<int>();
        var offsets = new List<int> { 0 };
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (offsets.Count > cap)
            {
                throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxNodes), cap, offsets.Count);
            }

            if (r.TokenType == JsonTokenType.Null)
            {
                offsets.Add(data.Count);
                continue;
            }

            Expect(ref r, JsonTokenType.StartArray, name + " entry");
            while (r.Read() && r.TokenType != JsonTokenType.EndArray)
            {
                if (data.Count >= c.Limits.MaxArrayElements)
                {
                    throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxArrayElements), c.Limits.MaxArrayElements, (long)data.Count + 1);
                }

                data.Add(ReadInt(ref r, name));
            }

            offsets.Add(data.Count);
        }

        return (data.ToArray(), offsets.ToArray());
    }

    private static Rare ReadRare(ref Utf8JsonReader r, Ctx c, string name, bool hasValue, int cap = -1)
    {
        if (r.TokenType == JsonTokenType.Null)
        {
            return Rare.Empty;
        }

        Expect(ref r, JsonTokenType.StartObject, name);
        int limit = cap < 0 ? c.Limits.MaxNodes : cap;
        int[] index = [];
        int[] value = [];
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("index"u8))
            {
                r.Read();
                index = ReadInts(ref r, c, name + ".index", limit, cap < 0 ? nameof(DomSnapshotLimits.MaxNodes) : nameof(DomSnapshotLimits.MaxLayoutNodes));
            }
            else if (hasValue && r.ValueTextEquals("value"u8))
            {
                r.Read();
                value = ReadInts(ref r, c, name + ".value", limit, nameof(DomSnapshotLimits.MaxNodes));
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }

        if (hasValue && value.Length != index.Length)
        {
            throw new DomSnapshotFormatException($"\"{name}\" has {index.Length} indexes but {value.Length} values.");
        }

        return new Rare(index, value);
    }

    // ---- validation: lengths agree and every index is in range, so later stages index arrays without checks ----

    private static void Validate(string[] strings, DomSnapshotDocumentData[] docs, string[] styleNames, DomSnapshotLimits limits)
    {
        long totalChars = 0;
        foreach (string s in strings)
        {
            totalChars += s.Length;
        }

        if (totalChars > limits.MaxStringChars)
        {
            throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxStringChars), limits.MaxStringChars, totalChars);
        }

        for (int di = 0; di < docs.Length; di++)
        {
            DomSnapshotDocumentData d = docs[di];
            string w = $"documents[{di}]";
            int n = d.NodeType.Length;

            RequireLength(d.ParentIndex.Length, n, w + ".nodes.parentIndex", required: true);
            RequireLength(d.NodeName.Length, n, w + ".nodes.nodeName", required: true);
            RequireLength(d.NodeValue.Length, n, w + ".nodes.nodeValue", required: false);
            RequireLength(d.BackendNodeId.Length, n, w + ".nodes.backendNodeId", required: false);
            RequireLength(d.AttrOffsets.Length - 1, n, w + ".nodes.attributes", required: false);

            CheckStrings(d.NodeName, strings.Length, w + ".nodes.nodeName");
            CheckStrings(d.NodeValue, strings.Length, w + ".nodes.nodeValue");
            CheckStrings(d.AttrData, strings.Length, w + ".nodes.attributes");
            for (int i = 0; i + 1 < d.AttrOffsets.Length; i++)
            {
                if (((d.AttrOffsets[i + 1] - d.AttrOffsets[i]) & 1) != 0)
                {
                    throw new DomSnapshotFormatException($"{w}.nodes.attributes[{i}] is not a list of name/value pairs.");
                }
            }

            int[] depth = n == 0 ? [] : new int[n];
            int roots = 0;
            for (int i = 0; i < n; i++)
            {
                int p = d.ParentIndex[i];
                if (p == -1)
                {
                    roots++;
                    continue;
                }

                if ((uint)p >= (uint)i)
                {
                    throw new DomSnapshotFormatException($"{w}.nodes.parentIndex[{i}] is {p}; a parent must come before its children.");
                }

                int dep = depth[p] + 1;
                if (dep > limits.MaxNodeDepth)
                {
                    throw new DomSnapshotFormatException(nameof(DomSnapshotLimits.MaxNodeDepth), limits.MaxNodeDepth, dep);
                }

                depth[i] = dep;
            }

            if (n > 0 && roots != 1)
            {
                throw new DomSnapshotFormatException($"{w} has {roots} root nodes; expected one.");
            }

            if (n > 0 && d.ParentIndex[0] != -1)
            {
                throw new DomSnapshotFormatException($"{w}: node 0 must be the root.");
            }

            CheckRare(d.ContentDocumentIndex, n, strings.Length, w + ".nodes.contentDocumentIndex", valuesAreStrings: false);
            for (int i = 0; i < d.ContentDocumentIndex.Value.Length; i++)
            {
                int v = d.ContentDocumentIndex.Value[i];
                if (v < -1 || v >= docs.Length)
                {
                    throw new DomSnapshotFormatException($"{w}.nodes.contentDocumentIndex refers to document {v}, which does not exist.");
                }
            }

            CheckRare(d.ShadowRootType, n, strings.Length, w + ".nodes.shadowRootType", true);
            CheckRare(d.PseudoType, n, strings.Length, w + ".nodes.pseudoType", true);
            CheckRare(d.PseudoIdentifier, n, strings.Length, w + ".nodes.pseudoIdentifier", true);
            CheckRare(d.InputValue, n, strings.Length, w + ".nodes.inputValue", true);
            CheckRare(d.TextValue, n, strings.Length, w + ".nodes.textValue", true);
            CheckRare(d.CurrentSourceUrl, n, strings.Length, w + ".nodes.currentSourceURL", true);
            CheckRare(d.OriginUrl, n, strings.Length, w + ".nodes.originURL", true);
            CheckRare(d.InputChecked, n, strings.Length, w + ".nodes.inputChecked", false);
            CheckRare(d.OptionSelected, n, strings.Length, w + ".nodes.optionSelected", false);
            CheckRare(d.IsClickable, n, strings.Length, w + ".nodes.isClickable", false);

            CheckString(d.DocumentUrl, strings.Length, w + ".documentURL");
            CheckString(d.BaseUrl, strings.Length, w + ".baseURL");
            CheckString(d.Title, strings.Length, w + ".title");
            CheckString(d.FrameId, strings.Length, w + ".frameId");
            CheckString(d.ContentLanguage, strings.Length, w + ".contentLanguage");
            CheckString(d.EncodingName, strings.Length, w + ".encodingName");
            CheckString(d.PublicId, strings.Length, w + ".publicId");
            CheckString(d.SystemId, strings.Length, w + ".systemId");

            int l = d.LayoutNodeIndex.Length;
            for (int i = 0; i < l; i++)
            {
                if ((uint)d.LayoutNodeIndex[i] >= (uint)n)
                {
                    throw new DomSnapshotFormatException($"{w}.layout.nodeIndex[{i}] is {d.LayoutNodeIndex[i]}; there are {n} nodes.");
                }
            }

            RequireLength(d.Bounds.Length / 4, l, w + ".layout.bounds", required: false);
            RequireLength(d.ClientRects.Length / 4, l, w + ".layout.clientRects", required: false);
            RequireLength(d.OffsetRects.Length / 4, l, w + ".layout.offsetRects", required: false);
            RequireLength(d.ScrollRects.Length / 4, l, w + ".layout.scrollRects", required: false);
            RequireLength(d.LayoutText.Length, l, w + ".layout.text", required: false);
            RequireLength(d.PaintOrders.Length, l, w + ".layout.paintOrders", required: false);
            RequireLength(d.BlendedBackground.Length, l, w + ".layout.blendedBackgroundColors", required: false);
            RequireLength(d.TextColorOpacities.Length, l, w + ".layout.textColorOpacities", required: false);
            RequireLength(d.StyleOffsets.Length - 1, l, w + ".layout.styles", required: false);
            CheckStrings(d.LayoutText, strings.Length, w + ".layout.text");
            CheckStrings(d.BlendedBackground, strings.Length, w + ".layout.blendedBackgroundColors");
            CheckStrings(d.StyleData, strings.Length, w + ".layout.styles");
            for (int i = 0; i < d.StackingContexts.Length; i++)
            {
                if ((uint)d.StackingContexts[i] >= (uint)l)
                {
                    throw new DomSnapshotFormatException($"{w}.layout.stackingContexts refers to layout item {d.StackingContexts[i]}; there are {l}.");
                }
            }

            if (styleNames.Length > 0)
            {
                for (int i = 0; i + 1 < d.StyleOffsets.Length; i++)
                {
                    int count = d.StyleOffsets[i + 1] - d.StyleOffsets[i];
                    if (count != 0 && count != styleNames.Length)
                    {
                        throw new DomSnapshotFormatException($"{w}.layout.styles[{i}] has {count} values; {styleNames.Length} computed styles were requested.");
                    }
                }
            }

            int t = d.TextBoxLayoutIndex.Length;
            RequireLength(d.TextBoxBounds.Length / 4, t, w + ".textBoxes.bounds", required: t > 0);
            RequireLength(d.TextBoxStart.Length, t, w + ".textBoxes.start", required: t > 0);
            RequireLength(d.TextBoxLength.Length, t, w + ".textBoxes.length", required: t > 0);
            for (int i = 0; i < t; i++)
            {
                if ((uint)d.TextBoxLayoutIndex[i] >= (uint)l)
                {
                    throw new DomSnapshotFormatException($"{w}.textBoxes.layoutIndex[{i}] is {d.TextBoxLayoutIndex[i]}; there are {l} layout items.");
                }
            }
        }
    }

    private static void RequireLength(int actual, int expected, string what, bool required)
    {
        if (actual == expected || (!required && actual == 0))
        {
            return;
        }

        throw new DomSnapshotFormatException($"{what} has {actual} entries; expected {expected}.");
    }

    private static void CheckString(int index, int count, string what)
    {
        if (index < -1 || index >= count)
        {
            throw new DomSnapshotFormatException($"{what} is string {index}; the table has {count}.");
        }
    }

    private static void CheckStrings(int[] indexes, int count, string what)
    {
        foreach (int v in indexes)
        {
            if (v < -1 || v >= count)
            {
                throw new DomSnapshotFormatException($"{what} refers to string {v}; the table has {count}.");
            }
        }
    }

    private static void CheckRare(Rare rare, int nodeCount, int stringCount, string what, bool valuesAreStrings)
    {
        foreach (int v in rare.Index)
        {
            if ((uint)v >= (uint)nodeCount)
            {
                throw new DomSnapshotFormatException($"{what} refers to node {v}; there are {nodeCount}.");
            }
        }

        if (valuesAreStrings)
        {
            CheckStrings(rare.Value, stringCount, what);
        }
    }
}
