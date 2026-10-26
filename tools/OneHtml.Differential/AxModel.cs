using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Accessibility.Tree;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// One node of an accessibility tree in the form the Chrome differential compares: the same shape for Chrome's
/// <c>Accessibility.getFullAXTree</c> nodes and for our own tree's Chrome-shaped export, so both sides go through the same
/// reader and the same <see cref="AxNormalizer"/>.
/// </summary>
internal sealed class AxEntry
{
    /// <summary>Index in the side's list.</summary>
    public int Index { get; init; }

    /// <summary>The protocol node id (Chrome) or the tree node id (ours), for messages.</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>Index in the side's list of the unpruned parent, or -1 for the root.</summary>
    public int Parent { get; set; } = -1;

    /// <summary>Unpruned children in order, as indexes in the side's list.</summary>
    public List<int> Children { get; } = [];

    /// <summary>The OneHtml node index of the DOM node, or -1 when the entry has none (or it has no OneHtml node).</summary>
    public int Dom { get; set; } = -1;

    /// <summary>The backend node id Chrome reported (Chrome side only; -1 when none).</summary>
    public long Backend { get; init; } = -1;

    public string Role { get; init; } = string.Empty;

    public bool IsRoot { get; init; }

    public bool IsText { get; init; }

    public bool Ignored { get; init; }

    /// <summary>Ignored reasons, sorted.</summary>
    public string[] IgnoredReasons { get; init; } = [];

    public string Name { get; init; } = string.Empty;

    /// <summary>Winning name source category, see <see cref="AxNormalizer.NameSourceOfChrome"/>; empty when the name is empty.</summary>
    public string NameSource { get; set; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? Value { get; init; }

    /// <summary>Compared properties by protocol name, values already normalized. Absent properties are not present.</summary>
    public SortedDictionary<string, string> Props { get; } = new(StringComparer.Ordinal);

    /// <summary>Relation properties (labelledby, owns, …): OneHtml node indexes (or <c>?backendId</c>) in order, comma separated.</summary>
    public SortedDictionary<string, string> Relations { get; } = new(StringComparer.Ordinal);

    /// <summary>A readable position: the DOM path (ours) or the backend id and role (Chrome).</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>The nodes of one side.</summary>
internal sealed class AxSide
{
    public List<AxEntry> Entries { get; } = [];
}

/// <summary>
/// The one place where representation differences between Chrome's output and ours are removed. Each rule changes
/// spelling only: it never maps one meaning onto another. Documented in <c>docs/chrome-ax-differential.md</c>.
/// </summary>
internal static class AxNormalizer
{
    /// <summary>Roles Chrome only uses for entries that are not accessibility nodes of the page and that are skipped on read.</summary>
    public static bool IsSkippedRole(string role) => role == "InlineTextBox";

    /// <summary>Collapses runs of ASCII white space (space, tab, line feed, form feed, carriage return) to one space and trims the ends. No other character is touched, so a no-break space stays.</summary>
    public static string Text(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pending = false;
        foreach (char c in s)
        {
            if (c is ' ' or '\t' or '\n' or '\f' or '\r')
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

    private static readonly HashSet<string> s_hiddenReasons = new(StringComparer.Ordinal)
    {
        "notRendered", "ariaHiddenElement", "ariaHiddenSubtree", "inertElement", "inertSubtree",
    };

    /// <summary>True for an ignored reason that says the node is hidden (not rendered, <c>aria-hidden</c>, inert).</summary>
    public static bool IsHiddenReason(string reason) => s_hiddenReasons.Contains(reason);

    /// <summary>Properties compared, by protocol name.</summary>
    public static readonly string[] ComparedProps =
    [
        "focusable", "focused", "disabled", "checked", "expanded", "selected", "pressed", "required", "invalid", "level", "live", "modal",
        "hasPopup", "multiselectable", "readonly", "valuemin", "valuemax", "valuetext", "orientation", "multiline", "editable", "url",
        "posinset", "setsize", "autocomplete", "busy", "atomic", "relevant", "roledescription", "keyshortcuts",
    ];

    /// <summary>Relation properties compared.</summary>
    public static readonly string[] RelationProps = ["activedescendant", "controls", "describedby", "details", "errormessage", "flowto", "labelledby", "owns"];

    // Properties Chrome reports as an explicit false (or empty) when the role supports them and we leave out: a false flag and an
    // absent flag say the same thing. expanded, selected, checked and pressed are not in the list: absent means "not supported by this
    // role", false means "supported, off", which are different.
    private static readonly HashSet<string> s_falseIsAbsent = new(StringComparer.Ordinal)
    {
        "focusable", "focused", "disabled", "required", "readonly", "modal", "multiselectable", "multiline", "busy",
    };

    /// <summary>
    /// Normalizes one property to its comparison text, or null when it is absent. <paramref name="type"/> is the protocol value type.
    /// </summary>
    public static string? Prop(string name, string type, JsonElement value)
    {
        string text = type is "tristate" or "token" or "tokenList" or "string" or "idref" or "idrefList"
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText()
            : value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => Number(value),
                JsonValueKind.String => value.GetString() ?? string.Empty,
                _ => value.GetRawText(),
            };
        if (s_falseIsAbsent.Contains(name) && text == "false")
        {
            return null;
        }

        if (name == "invalid" && text == "false")
        {
            return null;
        }

        if (name is "valuetext" or "roledescription" or "keyshortcuts" or "autocomplete" or "relevant" && text.Length == 0)
        {
            return null;
        }

        return name == "valuetext" || name == "roledescription" ? Text(text) : text;
    }

    /// <summary>
    /// A range widget's value as a number in 32-bit precision: Chrome keeps these values as single-precision floats and reports them widened to
    /// double (0.6 arrives as 0.6000000238418579). Only the values of range roles are touched; other strings compare as written.
    /// </summary>
    public static string RangeValue(string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsInfinity(d) && !double.IsNaN(d)
            ? ((float)d).ToString("R", CultureInfo.InvariantCulture)
            : v;

    private static string Number(JsonElement v) =>
        v.TryGetDouble(out double d) ? d.ToString("R", CultureInfo.InvariantCulture) : v.GetRawText();

    /// <summary>The comparison category of the source that produced a name in Chrome's <c>sources</c> list: the first source that is not superseded and has a non-empty value.</summary>
    public static string NameSourceOfChrome(JsonElement nameObject, string role)
    {
        if (!nameObject.TryGetProperty("value", out JsonElement nv) || nv.ValueKind != JsonValueKind.String || (nv.GetString() ?? string.Empty).Length == 0)
        {
            return string.Empty;
        }

        if (role == "StaticText")
        {
            return "text";
        }

        if (!nameObject.TryGetProperty("sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
        {
            return "unknown";
        }

        foreach (JsonElement s in sources.EnumerateArray())
        {
            if (s.TryGetProperty("superseded", out JsonElement sup) && sup.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!s.TryGetProperty("value", out JsonElement v) || !v.TryGetProperty("value", out JsonElement vv) || vv.ValueKind != JsonValueKind.String || (vv.GetString() ?? string.Empty).Length == 0)
            {
                continue;
            }

            string type = s.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
            string attr = s.TryGetProperty("attribute", out JsonElement a) ? a.GetString() ?? string.Empty : string.Empty;
            bool native = s.TryGetProperty("nativeSource", out _);
            return type switch
            {
                "relatedElement" when attr == "aria-labelledby" => "aria-labelledby",
                "relatedElement" => native ? "native-element" : "aria-labelledby",
                "attribute" when attr == "aria-label" => "aria-label",
                "attribute" when attr == "title" => "title",
                "attribute" => "native-attribute",
                "placeholder" => "placeholder",
                "contents" => "contents",
                _ => type,
            };
        }

        return "unknown";
    }

    /// <summary>Our name source in the same categories as <see cref="NameSourceOfChrome"/>.</summary>
    public static string NameSourceOfOurs(AxNode n)
    {
        if (n.Name.IsEmpty)
        {
            return string.Empty;
        }

        if (n.Kind == AxNodeKind.Text || n.Role == "StaticText")
        {
            return "text";
        }

        return n.Name.Source switch
        {
            AxTextSource.LabelledBy => "aria-labelledby",
            AxTextSource.AriaLabel => "aria-label",
            AxTextSource.NativeLabelElement or AxTextSource.DocumentTitle or AxTextSource.Summary => "native-element",
            AxTextSource.NativeAttribute => "native-attribute",
            AxTextSource.Title => "title",
            AxTextSource.Placeholder => "placeholder",
            AxTextSource.Contents => "contents",
            AxTextSource.TextNode => "text",
            _ => "unknown",
        };
    }
}

/// <summary>Reads Chrome-shaped AX node arrays (Chrome's own, or our export) into <see cref="AxSide"/>.</summary>
internal static class AxReader
{
    /// <summary>
    /// Reads the <c>nodes</c> array of one frame.
    /// </summary>
    /// <param name="nodes">The protocol <c>AXNode</c> array.</param>
    /// <param name="mapBackend">Translates a <c>backendDOMNodeId</c> to a OneHtml node index, or -1.</param>
    /// <param name="ours">Our tree when reading our export: its nodes give the name source.</param>
    public static AxSide Read(JsonElement nodes, Func<long, int> mapBackend, AccessibilityTree? ours)
    {
        var side = new AxSide();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        var parentIds = new List<string?>();
        var childIds = new List<List<string>>();
        foreach (JsonElement n in nodes.EnumerateArray())
        {
            string role = n.TryGetProperty("role", out JsonElement r) && r.TryGetProperty("value", out JsonElement rv) ? rv.GetString() ?? string.Empty : string.Empty;
            string id = Str(n, "nodeId") ?? string.Empty;
            if (AxNormalizer.IsSkippedRole(role))
            {
                continue;
            }

            bool ignored = n.TryGetProperty("ignored", out JsonElement ig) && ig.ValueKind == JsonValueKind.True;
            var reasons = new List<string>();
            if (n.TryGetProperty("ignoredReasons", out JsonElement ir) && ir.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement x in ir.EnumerateArray())
                {
                    if (Str(x, "name") is { } rn)
                    {
                        reasons.Add(rn);
                    }
                }
            }

            reasons.Sort(StringComparer.Ordinal);
            long backend = n.TryGetProperty("backendDOMNodeId", out JsonElement b) && b.ValueKind == JsonValueKind.Number ? b.GetInt64() : -1;
            string name = string.Empty;
            string nameSource = string.Empty;
            if (n.TryGetProperty("name", out JsonElement nm) && nm.ValueKind == JsonValueKind.Object)
            {
                name = AxNormalizer.Text(Str(nm, "value") ?? string.Empty);
                nameSource = AxNormalizer.NameSourceOfChrome(nm, role);
            }

            string desc = n.TryGetProperty("description", out JsonElement d) && d.ValueKind == JsonValueKind.Object ? AxNormalizer.Text(Str(d, "value") ?? string.Empty) : string.Empty;
            string? value = null;
            if (n.TryGetProperty("value", out JsonElement val) && val.ValueKind == JsonValueKind.Object && val.TryGetProperty("value", out JsonElement vv))
            {
                value = vv.ValueKind == JsonValueKind.String ? vv.GetString() : vv.GetRawText();
            }

            if (value is not null && role is "meter" or "progressbar" or "slider" or "spinbutton" or "scrollbar")
            {
                value = AxNormalizer.RangeValue(value);
            }

            int index = side.Entries.Count;
            var e = new AxEntry
            {
                Index = index,
                NodeId = id,
                Backend = backend,
                Role = role,
                IsRoot = role == "RootWebArea",
                IsText = role == "StaticText",
                Ignored = ignored,
                IgnoredReasons = [.. reasons],
                Name = name,
                NameSource = nameSource,
                Description = desc,
                Value = value,
                Dom = backend >= 0 ? mapBackend(backend) : -1,
            };
            if (n.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Array)
            {
                ReadProps(props, e, mapBackend);
            }

            side.Entries.Add(e);
            byId[id] = index;
            parentIds.Add(Str(n, "parentId"));
            var kids = new List<string>();
            if (n.TryGetProperty("childIds", out JsonElement ci) && ci.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement c in ci.EnumerateArray())
                {
                    if (c.GetString() is { } cs)
                    {
                        kids.Add(cs);
                    }
                }
            }

            childIds.Add(kids);
            if (ours is not null && int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int oid) && oid < ours.Nodes.Count)
            {
                e.NameSource = AxNormalizer.NameSourceOfOurs(ours.Nodes[oid]);
            }
        }

        // Structure. An InlineTextBox is skipped, so its (static text) parent keeps no children for it. A parent that was skipped or that
        // lives in another frame leaves the node a root of its own subtree.
        for (int i = 0; i < side.Entries.Count; i++)
        {
            foreach (string c in childIds[i])
            {
                if (byId.TryGetValue(c, out int ci))
                {
                    side.Entries[i].Children.Add(ci);
                    side.Entries[ci].Parent = i;
                }
            }
        }

        return side;
    }

    private static void ReadProps(JsonElement props, AxEntry e, Func<long, int> mapBackend)
    {
        foreach (JsonElement p in props.EnumerateArray())
        {
            string? name = Str(p, "name");
            if (name is null || !p.TryGetProperty("value", out JsonElement v))
            {
                continue;
            }

            if (Array.IndexOf(AxNormalizer.RelationProps, name) >= 0)
            {
                var ids = new List<string>();
                if (v.TryGetProperty("relatedNodes", out JsonElement rel) && rel.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement rn in rel.EnumerateArray())
                    {
                        if (rn.TryGetProperty("backendDOMNodeId", out JsonElement bid) && bid.ValueKind == JsonValueKind.Number)
                        {
                            long b = bid.GetInt64();
                            int dom = mapBackend(b);
                            ids.Add(dom >= 0 ? dom.ToString(CultureInfo.InvariantCulture) : "?" + b.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                if (ids.Count > 0)
                {
                    e.Relations[name] = string.Join(',', ids);
                }

                continue;
            }

            if (Array.IndexOf(AxNormalizer.ComparedProps, name) < 0 || !v.TryGetProperty("value", out JsonElement pv))
            {
                continue;
            }

            string type = Str(v, "type") ?? string.Empty;
            if (AxNormalizer.Prop(name, type, pv) is { } norm)
            {
                e.Props[name] = norm;
            }
        }
    }

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
