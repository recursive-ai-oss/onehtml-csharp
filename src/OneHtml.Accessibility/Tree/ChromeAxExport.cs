using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// Writes an <see cref="AccessibilityTree"/> in the shape of <c>Accessibility.getFullAXTree</c> (Chrome DevTools Protocol
/// <c>AXNode</c>): <c>nodeId</c>, <c>ignored</c>, <c>ignoredReasons</c>, <c>role</c>, <c>name</c> with <c>sources</c>,
/// <c>description</c>, <c>value</c>, <c>properties</c>, <c>parentId</c>, <c>childIds</c>, <c>backendDOMNodeId</c>. The tree is
/// the unpruned one: ignored nodes are present, and an ignored node's children stay its children. See
/// <c>docs/accessibility-tree.md</c> for the version, the property order and what is not exported.
/// </summary>
internal static class ChromeAxExport
{
    /// <summary>Version of the wrapper object and of the choices in the docs.</summary>
    public const string Schema = "onehtml-ax/1";

    public static string ToJson(AccessibilityTree tree)
    {
        var buffer = new ArrayBufferWriter<byte>(1 << 16);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("schema", Schema);
            w.WritePropertyName("nodes");
            WriteNodes(w, tree);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static void WriteNodes(Utf8JsonWriter w, AccessibilityTree tree)
    {
        w.WriteStartArray();
        foreach (AxNode n in tree.Nodes)
        {
            WriteNode(w, n);
        }

        w.WriteEndArray();
    }

    private static string Id(AxNode n) => n.Id.ToString(CultureInfo.InvariantCulture);

    private static void WriteNode(Utf8JsonWriter w, AxNode n)
    {
        w.WriteStartObject();
        w.WriteString("nodeId", Id(n));
        w.WriteBoolean("ignored", n.IsIgnored);
        if (n.IsIgnored && n.IgnoredReasons.Count > 0)
        {
            w.WritePropertyName("ignoredReasons");
            w.WriteStartArray();
            foreach (string reason in n.IgnoredReasons)
            {
                w.WriteStartObject();
                w.WriteString("name", reason);
                w.WritePropertyName("value");
                WriteValue(w, "boolean", true);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        (string roleType, string roleValue) = ExportRole(n);
        w.WritePropertyName("role");
        w.WriteStartObject();
        w.WriteString("type", roleType);
        w.WriteString("value", roleValue);
        w.WriteEndObject();

        if (!n.IsIgnored)
        {
            w.WritePropertyName("name");
            WriteText(w, n.Name, n.Kind == AxNodeKind.Text ? AxTextSource.TextNode : n.Name.Source, true);
            if (!n.Description.IsEmpty)
            {
                w.WritePropertyName("description");
                WriteText(w, n.Description, n.Description.Source, false);
            }

            if (n.States.Value is { } value)
            {
                w.WritePropertyName("value");
                WriteValue(w, "string", value);
            }
        }

        w.WritePropertyName("properties");
        w.WriteStartArray();
        WriteProperties(w, n);
        w.WriteEndArray();

        if (n.Parent is { } parent)
        {
            w.WriteString("parentId", Id(parent));
        }

        w.WritePropertyName("childIds");
        w.WriteStartArray();
        foreach (AxNode c in n.Children)
        {
            w.WriteStringValue(Id(c));
        }

        w.WriteEndArray();
        if (n.Kind != AxNodeKind.Document)
        {
            w.WriteNumber("backendDOMNodeId", n.DomNode.Index);
        }

        w.WriteEndObject();
    }

    private static (string Type, string Value) ExportRole(AxNode n)
    {
        switch (n.Kind)
        {
            case AxNodeKind.Document:
                return ("internalRole", "RootWebArea");
            case AxNodeKind.Text:
                return n.IsIgnored ? ("role", "none") : ("internalRole", "StaticText");
        }

        if (n.IsIgnored)
        {
            return ("role", "none");
        }

        Node el = n.DomNode;
        if (el.Namespace == ElementNamespace.Html && n.RoleOrigin is RoleOrigin.Implicit)
        {
            // Chrome's own role names for a few elements (profile entry export-role-names).
            switch (el.LocalName)
            {
                case "br":
                    return ("internalRole", "LineBreak");
                case "label":
                    return ("internalRole", "LabelText");
                case "legend":
                    return ("internalRole", "Legend");
                case "iframe":
                    return ("internalRole", "Iframe");
            }
        }

        return ("role", n.Role.Length == 0 ? "none" : n.Role);
    }

    private static void WriteValue(Utf8JsonWriter w, string type, string value)
    {
        w.WriteStartObject();
        w.WriteString("type", type);
        w.WriteString("value", value);
        w.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter w, string type, bool value)
    {
        w.WriteStartObject();
        w.WriteString("type", type);
        w.WriteBoolean("value", value);
        w.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter w, string type, double value)
    {
        w.WriteStartObject();
        w.WriteString("type", type);
        w.WriteNumber("value", value);
        w.WriteEndObject();
    }

    private static void WriteText(Utf8JsonWriter w, AxText t, AxTextSource source, bool includeSources)
    {
        w.WriteStartObject();
        w.WriteString("type", "computedString");
        w.WriteString("value", t.Value);
        if (includeSources && !t.IsEmpty)
        {
            w.WritePropertyName("sources");
            w.WriteStartArray();
            WriteSource(w, t, source);
            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    private static void WriteSource(Utf8JsonWriter w, AxText t, AxTextSource source)
    {
        w.WriteStartObject();
        string type = source switch
        {
            AxTextSource.LabelledBy or AxTextSource.NativeLabelElement or AxTextSource.DescribedBy => "relatedElement",
            AxTextSource.AriaLabel or AxTextSource.NativeAttribute or AxTextSource.Title or AxTextSource.AriaDescription => "attribute",
            AxTextSource.Placeholder => "placeholder",
            _ => "contents",
        };
        w.WriteString("type", type);
        w.WritePropertyName("value");
        WriteValue(w, "computedString", t.Value);
        if (source == AxTextSource.LabelledBy)
        {
            w.WriteString("attribute", "aria-labelledby");
            w.WritePropertyName("attributeValue");
            WriteRelated(w, "idrefList", t.RelatedElements);
        }
        else if (t.Attribute is not null)
        {
            w.WriteString("attribute", t.Attribute);
        }

        if (source == AxTextSource.NativeLabelElement)
        {
            w.WriteString("nativeSource", "labelfor");
        }
        else if (source == AxTextSource.Title)
        {
            w.WriteString("nativeSource", "title");
        }

        w.WriteEndObject();
    }

    private static void WriteRelated(Utf8JsonWriter w, string type, IReadOnlyList<Node> elements)
    {
        w.WriteStartObject();
        w.WriteString("type", type);
        var ids = new StringBuilder();
        foreach (Node e in elements)
        {
            if (ids.Length > 0)
            {
                ids.Append(' ');
            }

            ids.Append(e.Id);
        }

        w.WriteString("value", ids.ToString());
        w.WritePropertyName("relatedNodes");
        w.WriteStartArray();
        foreach (Node e in elements)
        {
            w.WriteStartObject();
            w.WriteNumber("backendDOMNodeId", e.Index);
            w.WriteString("idref", e.Id);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Prop(Utf8JsonWriter w, string name, Action<Utf8JsonWriter> value)
    {
        w.WriteStartObject();
        w.WriteString("name", name);
        w.WritePropertyName("value");
        value(w);
        w.WriteEndObject();
    }

    private static string Tri(AxTristate t) => t switch { AxTristate.True => "true", AxTristate.Mixed => "mixed", _ => "false" };

    private static void WriteProperties(Utf8JsonWriter w, AxNode n)
    {
        if (n.Kind == AxNodeKind.Document)
        {
            Prop(w, "focusable", x => WriteValue(x, "booleanOrUndefined", true));
            return;
        }

        AxStates s = n.States;
        if (n.IsIgnored)
        {
            if (n.Hidden.IsHidden)
            {
                Prop(w, "hidden", x => WriteValue(x, "boolean", true));
            }

            return;
        }

        if (s.Busy)
        {
            Prop(w, "busy", x => WriteValue(x, "boolean", true));
        }

        if (s.Disabled)
        {
            Prop(w, "disabled", x => WriteValue(x, "boolean", true));
        }

        if (s.Editable is { } ed)
        {
            Prop(w, "editable", x => WriteValue(x, "token", ed));
        }

        if (n.Focus.IsFocusable)
        {
            Prop(w, "focusable", x => WriteValue(x, "booleanOrUndefined", true));
        }

        if (s.Invalid is { } inv)
        {
            Prop(w, "invalid", x => WriteValue(x, "token", inv));
        }

        if (s.KeyShortcuts is { } ks)
        {
            Prop(w, "keyshortcuts", x => WriteValue(x, "string", ks));
        }

        if (s.RoleDescription is { } rd)
        {
            Prop(w, "roledescription", x => WriteValue(x, "string", rd));
        }

        if (s.Live is { } live)
        {
            Prop(w, "live", x => WriteValue(x, "token", live));
        }

        if (s.Atomic is { } atomic)
        {
            Prop(w, "atomic", x => WriteValue(x, "boolean", atomic));
        }

        if (s.Relevant is { } relevant)
        {
            Prop(w, "relevant", x => WriteValue(x, "tokenList", relevant));
        }

        if (s.AutoComplete is { } ac)
        {
            Prop(w, "autocomplete", x => WriteValue(x, "token", ac));
        }

        if (s.HasPopup is { } hp)
        {
            Prop(w, "hasPopup", x => WriteValue(x, "token", hp));
        }

        if (s.Level is { } level)
        {
            Prop(w, "level", x => WriteValue(x, "integer", level));
        }

        if (s.MultiSelectable)
        {
            Prop(w, "multiselectable", x => WriteValue(x, "boolean", true));
        }

        if (s.Orientation is { } or)
        {
            Prop(w, "orientation", x => WriteValue(x, "token", or));
        }

        if (s.Multiline)
        {
            Prop(w, "multiline", x => WriteValue(x, "boolean", true));
        }

        if (s.ReadOnly)
        {
            Prop(w, "readonly", x => WriteValue(x, "boolean", true));
        }

        if (s.Required)
        {
            Prop(w, "required", x => WriteValue(x, "boolean", true));
        }

        if (s.ValueMin is { } vmin)
        {
            Prop(w, "valuemin", x => WriteValue(x, "number", vmin));
        }

        if (s.ValueMax is { } vmax)
        {
            Prop(w, "valuemax", x => WriteValue(x, "number", vmax));
        }

        if (s.ValueText is { } vt)
        {
            Prop(w, "valuetext", x => WriteValue(x, "string", vt));
        }

        if (s.Checked is { } chk)
        {
            Prop(w, "checked", x => WriteValue(x, "tristate", Tri(chk)));
        }

        if (s.Expanded is { } exp)
        {
            Prop(w, "expanded", x => WriteValue(x, "booleanOrUndefined", exp));
        }

        if (s.Modal)
        {
            Prop(w, "modal", x => WriteValue(x, "boolean", true));
        }

        if (s.Pressed is { } pr)
        {
            Prop(w, "pressed", x => WriteValue(x, "tristate", Tri(pr)));
        }

        if (s.Selected is { } sel)
        {
            Prop(w, "selected", x => WriteValue(x, "booleanOrUndefined", sel));
        }

        if (s.PosInSet is { } pos)
        {
            Prop(w, "posinset", x => WriteValue(x, "integer", pos));
        }

        if (s.SetSize is { } size)
        {
            Prop(w, "setsize", x => WriteValue(x, "integer", size));
        }

        foreach ((AxRelationKind kind, string name) in RelationNames)
        {
            if (n.GetRelation(kind) is { } rel && rel.Elements.Count > 0)
            {
                Prop(w, name, x => WriteRelated(x, kind is AxRelationKind.Owns or AxRelationKind.Controls or AxRelationKind.DescribedBy or AxRelationKind.FlowTo or AxRelationKind.LabelledBy ? "idrefList" : "idref", rel.Elements));
            }
        }

        if (s.Url is { } url)
        {
            Prop(w, "url", x => WriteValue(x, "string", url));
        }
    }

    private static readonly (AxRelationKind, string)[] RelationNames =
    [
        (AxRelationKind.ActiveDescendant, "activedescendant"),
        (AxRelationKind.Controls, "controls"),
        (AxRelationKind.DescribedBy, "describedby"),
        (AxRelationKind.Details, "details"),
        (AxRelationKind.ErrorMessage, "errormessage"),
        (AxRelationKind.FlowTo, "flowto"),
        (AxRelationKind.LabelledBy, "labelledby"),
        (AxRelationKind.Owns, "owns"),
    ];
}
