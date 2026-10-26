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
        IReadOnlyList<string> exportReasons = n.IsIgnored ? ExportReasons(n) : [];
        if (n.IsIgnored && exportReasons.Count > 0)
        {
            w.WritePropertyName("ignoredReasons");
            w.WriteStartArray();
            foreach (string reason in exportReasons)
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
        if (n.Kind is not (AxNodeKind.Document or AxNodeKind.Generated))
        {
            w.WriteNumber("backendDOMNodeId", n.DomNode.Index);
        }

        w.WriteEndObject();
    }

    private static readonly string[] ReasonOrder =
    [
        "notRendered", "ariaHiddenElement", "ariaHiddenSubtree", "inertElement", "inertSubtree", "notVisible", "emptyAlt", "labelFor", "presentationalRole", "uninteresting",
    ];

    /// <summary>
    /// The ignored reasons the way Chromium reports them (profile entry ignored-reasons): one reason, the first that applies in the order
    /// not rendered, aria-hidden, inert, not visible, empty alt, presentational, uninteresting; an <c>img</c> whose role is none because its
    /// <c>alt</c> is empty is <c>emptyAlt</c>.
    /// </summary>
    private static IReadOnlyList<string> ExportReasons(AxNode n)
    {
        if (n.IgnoredReasons.Count == 0)
        {
            return n.IgnoredReasons;
        }

        var set = new List<string>(n.IgnoredReasons);
        if (set.Contains("presentationalRole") && n.Kind == AxNodeKind.Element && n.DomNode.Namespace == ElementNamespace.Html && n.DomNode.LocalName == "img"
            && n.RoleOrigin == Aria.RoleOrigin.Implicit && n.DomNode.GetAttribute("alt") is { Length: 0 })
        {
            set.Remove("presentationalRole");
            set.Add("emptyAlt");
        }

        foreach (string reason in ReasonOrder)
        {
            if (set.Contains(reason))
            {
                return [reason];
            }
        }

        return [set[0]];
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

        if (n.InternalRole is { } internalRole)
        {
            // Chromium's own role names (profile entry internal-roles).
            return ("internalRole", internalRole);
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

        // Chromium states disabled on the controls and options, not on the fieldset or optgroup that disables them.
        if (s.Disabled && !(n.DomNode.Namespace == ElementNamespace.Html && n.DomNode.LocalName is "fieldset" or "optgroup"))
        {
            Prop(w, "disabled", x => WriteValue(x, "boolean", true));
        }

        if (s.Editable is { } ed)
        {
            Prop(w, "editable", x => WriteValue(x, "token", ed));
        }

        if (n.Focus.IsFocusable && n.InternalRole != "Iframe")
        {
            Prop(w, "focusable", x => WriteValue(x, "booleanOrUndefined", true));
        }

        if (n.IsAutofocus)
        {
            Prop(w, "focused", x => WriteValue(x, "booleanOrUndefined", true));
        }

        if (s.Invalid is { } inv)
        {
            // Chromium reports aria-invalid=spelling as true (profile entry invalid-spelling).
            Prop(w, "invalid", x => WriteValue(x, "token", inv == "spelling" ? "true" : inv));
        }
        else if (s.ConstraintInvalid)
        {
            Prop(w, "invalid", x => WriteValue(x, "token", "true"));
        }

        if (s.KeyShortcuts is { } ks)
        {
            Prop(w, "keyshortcuts", x => WriteValue(x, "string", ks));
        }

        if (s.RoleDescription is { } rd)
        {
            // Chromium hands the description over as UTF-8 bytes read as Latin-1: "café" arrives as "cafÃ©" (profile entry
            // roledescription-latin1).
            Prop(w, "roledescription", x => WriteValue(x, "string", Latin1View(rd)));
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

        if (s.ReadOnly && !s.Disabled && ExposesReadOnly(n))
        {
            Prop(w, "readonly", x => WriteValue(x, "boolean", true));
        }

        if (s.Required && ExposesRequired(n))
        {
            Prop(w, "required", x => WriteValue(x, "boolean", true));
        }

        // Chromium keeps range bounds as single-precision floats and reports them widened to double (profile entry range-single-precision).
        if (s.ValueMin is { } vmin)
        {
            Prop(w, "valuemin", x => WriteValue(x, "number", (double)(float)vmin));
        }

        if (s.ValueMax is { } vmax)
        {
            Prop(w, "valuemax", x => WriteValue(x, "number", (double)(float)vmax));
        }

        if (ExportedValueText(n) is { } vt)
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

        // posinset and setsize are not in the export at all: Chromium states neither, whether the author gave them or not (profile entry
        // export-set-position). The tree keeps both.

        if (n.GetRelation(AxRelationKind.LabelledBy) is null && n.Name.Source == AxTextSource.NativeLabelElement && n.Name.RelatedElements.Count > 0)
        {
            // A label, caption or legend that names the node is reported as a labelledby relation, with every label of a labelable control;
            // a figcaption naming an img is not (profile entry export-native-labelledby).
            List<Node> labels = n.NativeLabels.Count > 0 ? [.. n.NativeLabels] : [.. n.Name.RelatedElements];
            if (labels[0].Namespace == ElementNamespace.Html && labels[0].LocalName is "caption" or "legend" or "label")
            {
                Prop(w, "labelledby", x => WriteRelated(x, "idrefList", labels));
            }
        }

        foreach ((AxRelationKind kind, string name) in RelationNames)
        {
            if (n.GetRelation(kind) is { } rel && ReportedTargets(rel, kind) is { Count: > 0 } targets)
            {
                Prop(w, name, x => WriteRelated(x, kind is AxRelationKind.Owns or AxRelationKind.Controls or AxRelationKind.DescribedBy or AxRelationKind.FlowTo or AxRelationKind.LabelledBy ? "idrefList" : "idref", targets));
            }
        }

        if (s.Url is { } url)
        {
            Prop(w, "url", x => WriteValue(x, "string", url));
        }
    }

    /// <summary>
    /// The targets of a relation as Chromium reports them. Controls, details, error message, flow-to and active descendant name only
    /// nodes that are exposed in the tree; labelledby, describedby and owns name every element the attribute resolves to, whether or not it is
    /// rendered, because Chromium makes a node for it to compute the name (profile entry relation-targets).
    /// </summary>
    private static List<Node> ReportedTargets(AxRelation rel, AxRelationKind kind)
    {
        if (kind is AxRelationKind.LabelledBy or AxRelationKind.DescribedBy or AxRelationKind.Owns)
        {
            return [.. rel.Elements];
        }

        var result = new List<Node>();
        foreach (AxNode t in rel.Targets)
        {
            if (!t.IsIgnored)
            {
                result.Add(t.DomNode);
            }
        }

        return result;
    }

    /// <summary>
    /// Chromium states <c>readonly</c> on text boxes and on grids and their cells, and nowhere else: not on a spin button, a combobox, a checkbox, a
    /// radio button, a search box, a slider or a list box, although the attribute is there (profile entry readonly-exposure).
    /// </summary>
    private static bool ExposesReadOnly(AxNode n) =>
        n.InternalRole is null && n.Role is "textbox" or "grid" or "treegrid" or "gridcell" or "columnheader" or "rowheader"
        && !(n.DomNode.Namespace == ElementNamespace.Html && n.DomNode.LocalName == "input" && HtmlSemantics.GetInputType(n.DomNode) is not (HtmlInputType.Text or HtmlInputType.Telephone or HtmlInputType.Url or HtmlInputType.Email or HtmlInputType.Password));

    /// <summary>
    /// Chromium's value text of a slider or spin button is the text of its native <c>value</c> attribute ("1.00", "$675,000"), or the number a
    /// slider rests at; it never reports <c>aria-valuetext</c> (profile entry valuetext-native).
    /// </summary>
    private static string? ExportedValueText(AxNode n)
    {
        if (n.DomNode.Namespace != ElementNamespace.Html || n.DomNode.LocalName != "input" || n.Role is not ("slider" or "spinbutton"))
        {
            return null;
        }

        // A value that is not a number is sanitized away from a number or range field (HTML 4.10.5.1.12).
        return n.DomNode.GetAttribute("value") is { Length: > 0 } text
            ? double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) || HtmlSemantics.GetInputType(n.DomNode) is not (HtmlInputType.Number or HtmlInputType.Range) ? text : null
            : n.Role == "slider" ? n.States.Value : null;
    }

    private static string Latin1View(string s)
    {
        bool ascii = true;
        foreach (char c in s)
        {
            ascii &= c < 0x80;
        }

        if (ascii)
        {
            return s;
        }

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }

        return new string(chars);
    }

    /// <summary>
    /// Chromium states <c>required</c> on text fields, spin buttons, and the other roles that take it, but not on a search box, a native
    /// select, a checkbox, a radio button, a date field or a button, although <c>aria-required</c> or the <c>required</c> attribute is there
    /// (profile entry required-exposure).
    /// </summary>
    private static bool ExposesRequired(AxNode n)
    {
        if (n.InternalRole is not null)
        {
            return false;
        }

        if (n.DomNode.Namespace == ElementNamespace.Html && n.DomNode.LocalName == "select" && n.Role != "listbox")
        {
            return false;
        }

        return n.Role is "textbox" or "spinbutton" or "combobox" or "listbox" or "radiogroup" or "tree" or "treegrid" or "grid";
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
