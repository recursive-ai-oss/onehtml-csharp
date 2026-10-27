using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Form state changed from script (<c>checked</c>, <c>disabled</c> and <c>indeterminate</c> properties, an option's <c>selected</c>), and the
/// <c>::selection</c> and <c>::placeholder</c> pseudo-elements. Every expectation is what Chrome for Testing 156.0.8078.47 returned from
/// <c>getComputedStyle</c> for the same markup after the same script, with colours written as the keywords they came from (<c>rgb(255, 0, 0)</c>
/// is <c>red</c>; the grey of a placeholder is <c>#757575</c>). The script's changes are given to the library as overrides, for each element
/// separately, because overrides are literal where the browser's radio groups and single-choice selects adjust their other members.
/// Chrome answers for a pseudo-element that does not exist with a style of its own; the library answers null, so only existing ones are probed here.
/// </summary>
public class ChromeFormAndPseudoTests
{
    private static readonly Dictionary<string, (string Html, (string Op, string Id, bool Value)[] Ops, (string Id, string? Pseudo, string Prop, string Expected)[] Probes)> Cases = new()
    {
        ["checkbox"] = ("<!doctype html><html><head><style>input:checked{list-style-type:square} input:checked + label{text-transform:uppercase} input:checked ~ .hint{font-style:italic} li:has(input:checked){list-style-type:square} li:not(:has(:checked)){list-style-type:circle} label:not(input:checked + label){font-weight:bold}</style></head><body><input type=checkbox id=a><label id=l for=a>L</label><input type=checkbox id=b checked><label id=m for=b>M</label><span id=h class=hint>h</span><ul><li id=li1><input type=checkbox id=c></li><li id=li2><input type=checkbox id=d checked></li></ul></body></html>",
            [("checked", "a", true), ("checked", "b", false), ("checked", "c", true), ("checked", "d", false)],
            [("a", null, "list-style-type", "square"), ("b", null, "list-style-type", "disc"), ("l", null, "text-transform", "uppercase"), ("l", null, "font-weight", "400"), ("m", null, "text-transform", "none"), ("m", null, "font-weight", "700"), ("h", null, "font-style", "italic"), ("li1", null, "list-style-type", "square"), ("li2", null, "list-style-type", "circle")]),
        ["fieldset"] = ("<!doctype html><html><head><style>input:disabled,textarea:disabled,button:disabled,select:disabled,option:disabled{list-style-type:decimal} input:enabled,textarea:enabled,button:enabled{list-style-type:circle} fieldset:disabled{text-transform:uppercase} #x:has(:disabled){font-weight:bold}</style></head><body><fieldset id=f1><legend><input id=lg></legend><input id=a><textarea id=t></textarea><button id=b>b</button><select id=s><option>o</option></select><div id=x><input id=n></div></fieldset><fieldset id=f2 disabled><input id=c></fieldset><input id=free></body></html>",
            [("disabled", "f1", true), ("disabled", "f2", false)],
            [("lg", null, "list-style-type", "circle"), ("a", null, "list-style-type", "decimal"), ("t", null, "list-style-type", "decimal"), ("b", null, "list-style-type", "decimal"), ("s", null, "list-style-type", "decimal"), ("n", null, "list-style-type", "decimal"), ("c", null, "list-style-type", "circle"), ("free", null, "list-style-type", "circle"), ("f1", null, "text-transform", "uppercase"), ("f2", null, "text-transform", "none"), ("x", null, "font-weight", "700")]),
        ["disable-controls"] = ("<!doctype html><html><head><style>input:read-write,textarea:read-write{list-style-type:circle} input:read-only,textarea:read-only{list-style-type:none} :disabled{text-transform:uppercase} :enabled{font-style:italic}</style></head><body><input id=a><input id=b disabled><input id=c readonly><textarea id=t></textarea><button id=u>u</button><optgroup id=og></optgroup><select id=s><optgroup id=g><option id=o1>1</option><option id=o2>2</option></optgroup></select></body></html>",
            [("disabled", "a", true), ("disabled", "b", false), ("disabled", "t", true), ("disabled", "g", true), ("disabled", "u", true)],
            [("a", null, "list-style-type", "none"), ("a", null, "text-transform", "uppercase"), ("a", null, "font-style", "normal"), ("b", null, "list-style-type", "circle"), ("b", null, "text-transform", "none"), ("b", null, "font-style", "italic"), ("c", null, "list-style-type", "none"), ("c", null, "text-transform", "none"), ("c", null, "font-style", "italic"), ("t", null, "list-style-type", "none"), ("t", null, "text-transform", "uppercase"), ("t", null, "font-style", "normal"), ("u", null, "list-style-type", "disc"), ("u", null, "text-transform", "uppercase"), ("u", null, "font-style", "normal"), ("s", null, "list-style-type", "disc"), ("s", null, "text-transform", "none"), ("s", null, "font-style", "italic"), ("g", null, "list-style-type", "disc"), ("g", null, "text-transform", "uppercase"), ("g", null, "font-style", "italic"), ("o1", null, "list-style-type", "disc"), ("o1", null, "text-transform", "uppercase"), ("o1", null, "font-style", "italic"), ("o2", null, "list-style-type", "disc"), ("o2", null, "text-transform", "uppercase"), ("o2", null, "font-style", "italic")]),
        ["indeterminate"] = ("<!doctype html><html><head><style>:indeterminate{list-style-type:lower-roman} input:indeterminate + input{text-transform:uppercase}</style></head><body><input type=checkbox id=a><input type=checkbox id=b indeterminate><input type=radio id=r name=g><input type=radio id=q name=g><input type=radio id=nn><progress id=p></progress><input type=text id=t><label id=l for=a>l</label></body></html>",
            [("indeterminate", "a", true), ("indeterminate", "t", true)],
            [("a", null, "list-style-type", "lower-roman"), ("a", null, "text-transform", "none"), ("b", null, "list-style-type", "disc"), ("b", null, "text-transform", "uppercase"), ("r", null, "list-style-type", "lower-roman"), ("r", null, "text-transform", "none"), ("q", null, "list-style-type", "lower-roman"), ("q", null, "text-transform", "uppercase"), ("nn", null, "list-style-type", "lower-roman"), ("nn", null, "text-transform", "uppercase"), ("p", null, "list-style-type", "lower-roman"), ("p", null, "text-transform", "none"), ("t", null, "list-style-type", "disc"), ("t", null, "text-transform", "none")]),
        ["radio-group"] = ("<!doctype html><html><head><style>input:checked{list-style-type:square} input:indeterminate{text-transform:uppercase}</style></head><body><form><input type=radio id=a name=g checked><input type=radio id=b name=g><input type=radio id=c name=g><input type=radio id=d name=h><input type=radio id=e name=h><input type=radio id=n checked></form></body></html>",
            [("checked", "a", false), ("checked", "d", true), ("checked", "n", false)],
            [("a", null, "list-style-type", "disc"), ("a", null, "text-transform", "uppercase"), ("b", null, "list-style-type", "disc"), ("b", null, "text-transform", "uppercase"), ("c", null, "list-style-type", "disc"), ("c", null, "text-transform", "uppercase"), ("d", null, "list-style-type", "square"), ("d", null, "text-transform", "none"), ("e", null, "list-style-type", "disc"), ("e", null, "text-transform", "none"), ("n", null, "list-style-type", "disc"), ("n", null, "text-transform", "uppercase")]),
        ["radio-group-moves"] = ("<!doctype html><html><head><style>input:checked{list-style-type:square} input:indeterminate{text-transform:uppercase}</style></head><body><form><input type=radio id=a name=g checked><input type=radio id=b name=g><input type=radio id=c name=g></form></body></html>",
            [("checked", "a", false), ("checked", "c", true)],
            [("a", null, "list-style-type", "disc"), ("a", null, "text-transform", "none"), ("b", null, "list-style-type", "disc"), ("b", null, "text-transform", "none"), ("c", null, "list-style-type", "square"), ("c", null, "text-transform", "none")]),
        ["options"] = ("<!doctype html><html><head><style>option:checked{font-style:italic} select:has(option:checked) + select{text-transform:uppercase}</style></head><body><select id=s><option id=a>a</option><option id=b selected>b</option><option id=c>c</option></select><select id=m multiple><option id=d>d</option><option id=e selected>e</option></select></body></html>",
            [("checked", "a", true), ("checked", "b", false), ("checked", "d", true)],
            [("a", null, "font-style", "italic"), ("b", null, "font-style", "normal"), ("c", null, "font-style", "normal"), ("d", null, "font-style", "italic"), ("e", null, "font-style", "italic"), ("m", null, "text-transform", "uppercase")]),
        ["inherit-through-disabled"] = ("<!doctype html><html><head><style>fieldset:disabled{font-weight:bold} fieldset:disabled input{list-style-type:decimal} fieldset:not(:disabled) input{list-style-type:circle}</style></head><body><fieldset id=f><div id=d><input id=i></div></fieldset></body></html>",
            [("disabled", "f", true)],
            [("f", null, "font-weight", "700"), ("d", null, "font-weight", "700"), ("f", null, "list-style-type", "disc"), ("d", null, "list-style-type", "disc"), ("i", null, "list-style-type", "decimal")]),
        ["sel-basic"] = ("<!doctype html><html><head><style>p::selection{color:blue;background-color:yellow}</style></head><body><div id=d><p id=p>t <span id=s>u</span></p></div></body></html>",
            [],
            [("p", "selection", "color", "blue"), ("p", "selection", "background-color", "yellow"), ("p", "selection", "text-decoration-line", "none"), ("s", "selection", "color", "blue"), ("s", "selection", "background-color", "yellow"), ("s", "selection", "text-decoration-line", "none")]),
        ["sel-inherit"] = ("<!doctype html><html><head><style>p{color:red;background-color:green;text-decoration:underline} div::selection{color:blue;background-color:yellow;text-decoration:overline} p::selection{letter-spacing:1px}</style></head><body><div id=d><p id=p>t <span id=s>u</span></p></div></body></html>",
            [],
            [("d", "selection", "color", "blue"), ("d", "selection", "background-color", "yellow"), ("d", "selection", "text-decoration-line", "overline"), ("d", "selection", "text-decoration-color", "blue"), ("d", "selection", "letter-spacing", "normal"), ("p", "selection", "color", "blue"), ("p", "selection", "background-color", "yellow"), ("p", "selection", "text-decoration-line", "overline"), ("p", "selection", "text-decoration-color", "blue"), ("p", "selection", "letter-spacing", "normal"), ("s", "selection", "color", "blue"), ("s", "selection", "background-color", "yellow"), ("s", "selection", "text-decoration-line", "overline"), ("s", "selection", "text-decoration-color", "blue"), ("s", "selection", "letter-spacing", "normal")]),
        ["sel-universal"] = ("<!doctype html><html><head><style>::selection{background-color:green}</style></head><body><div id=d><p id=p>t <span id=s>u</span></p></div></body></html>",
            [],
            [("d", "selection", "color", "black"), ("d", "selection", "background-color", "green"), ("p", "selection", "color", "black"), ("p", "selection", "background-color", "green"), ("s", "selection", "color", "black"), ("s", "selection", "background-color", "green")]),
        ["sel-ignored"] = ("<!doctype html><html><head><style>p::selection{font-size:30px;margin-top:9px;display:none;font-weight:300;color:green;line-height:5;cursor:pointer}</style></head><body><p id=p style=\"color:red;font-size:20px;font-weight:bold\">t</p></body></html>",
            [],
            [("p", "selection", "color", "green"), ("p", "selection", "font-size", "20px"), ("p", "selection", "font-weight", "700"), ("p", "selection", "margin-top", "0px"), ("p", "selection", "display", "inline"), ("p", "selection", "line-height", "normal"), ("p", "selection", "cursor", "auto")]),
        ["sel-origin"] = ("<!doctype html><html><head><style>p::selection{background-color:yellow}</style></head><body><div id=d style=\"color:red;font-size:19px;font-weight:bold\"><p id=p>t</p></div></body></html>",
            [],
            [("p", "selection", "color", "red"), ("p", "selection", "font-size", "19px"), ("p", "selection", "font-weight", "700"), ("p", "selection", "background-color", "yellow")]),
        ["sel-cascade"] = ("<!doctype html><html><head><style>p::selection{color:red} .c::selection{color:blue} p::selection{color:green !important}</style></head><body><p id=p class=c>t</p></body></html>",
            [],
            [("p", "selection", "color", "green")]),
        ["sel-specificity"] = ("<!doctype html><html><head><style>.c::selection{color:blue} p::selection{color:red}</style></head><body><p id=p class=c>t</p></body></html>",
            [],
            [("p", "selection", "color", "blue")]),
        ["sel-var"] = ("<!doctype html><html><head><style>p::selection{color:var(--c);background-color:var(--c)}</style></head><body><p id=p style=\"--c:green\">t <span id=s>u</span></p></body></html>",
            [],
            [("p", "selection", "color", "green"), ("p", "selection", "background-color", "green"), ("s", "selection", "color", "green"), ("s", "selection", "background-color", "green")]),
        ["sel-shorthand"] = ("<!doctype html><html><head><style>p::selection{text-decoration:underline wavy red;background:yellow;font:20px serif}</style></head><body><div id=d><p id=p>t <span id=s>u</span></p></div></body></html>",
            [],
            [("p", "selection", "background-color", "yellow"), ("p", "selection", "text-decoration-line", "underline"), ("p", "selection", "text-decoration-style", "wavy"), ("p", "selection", "text-decoration-color", "red"), ("p", "selection", "font-size", "16px"), ("s", "selection", "background-color", "yellow"), ("s", "selection", "text-decoration-line", "underline"), ("s", "selection", "text-decoration-style", "wavy"), ("s", "selection", "text-decoration-color", "red"), ("s", "selection", "font-size", "16px")]),
        ["sel-nopseudo-rules"] = ("<!doctype html><html><head><style>p{color:red}</style></head><body><div id=d><p id=p>t <span id=s>u</span></p></div></body></html>",
            [],
            []),
        ["sel-focus-state"] = ("<!doctype html><html><head><style>input:focus::selection{color:blue} :focus-within::selection{background-color:yellow}</style></head><body><div id=d><input id=a><p id=p>t</p></div></body></html>",
            [("focus", "a", false)],
            [("d", "selection", "color", "black"), ("d", "selection", "background-color", "yellow"), ("a", "selection", "color", "blue"), ("a", "selection", "background-color", "yellow"), ("p", "selection", "color", "black"), ("p", "selection", "background-color", "yellow")]),
        ["ph-default"] = ("<!doctype html><html><head><style></style></head><body><input id=a placeholder=hi style=\"font-size:20px;font-weight:bold;text-transform:uppercase;cursor:pointer\"><input id=c placeholder=hi value=v><textarea id=d placeholder=t style=\"letter-spacing:2px\"></textarea><input id=e type=search placeholder=s><input id=f type=number placeholder=1><input id=g type=email placeholder=e></body></html>",
            [],
            [("a", "placeholder", "color", "#757575"), ("a", "placeholder", "display", "block"), ("a", "placeholder", "pointer-events", "none"), ("a", "placeholder", "font-size", "20px"), ("a", "placeholder", "font-weight", "700"), ("a", "placeholder", "text-transform", "uppercase"), ("a", "placeholder", "background-color", "transparent"), ("c", "placeholder", "color", "#757575"), ("c", "placeholder", "display", "none"), ("c", "placeholder", "pointer-events", "none"), ("d", "placeholder", "color", "#757575"), ("d", "placeholder", "display", "block"), ("d", "placeholder", "pointer-events", "none"), ("e", "placeholder", "color", "#757575"), ("e", "placeholder", "display", "block"), ("e", "placeholder", "pointer-events", "none"), ("f", "placeholder", "color", "#757575"), ("f", "placeholder", "display", "block"), ("f", "placeholder", "pointer-events", "none"), ("g", "placeholder", "color", "#757575"), ("g", "placeholder", "display", "block"), ("g", "placeholder", "pointer-events", "none"), ("d", "placeholder", "letter-spacing", "2px")]),
        ["ph-author"] = ("<!doctype html><html><head><style>input::placeholder,textarea::placeholder{color:blue;background-color:yellow;font-size:30px;display:inline;text-transform:uppercase;pointer-events:all;line-height:3;margin-top:10px;font-style:italic;opacity:.5}</style></head><body><input id=a placeholder=hi style=\"font-size:20px\"><input id=c placeholder=hi value=v><textarea id=d placeholder=t></textarea></body></html>",
            [],
            [("a", "placeholder", "color", "blue"), ("a", "placeholder", "background-color", "yellow"), ("a", "placeholder", "font-size", "30px"), ("a", "placeholder", "display", "block"), ("a", "placeholder", "text-transform", "uppercase"), ("a", "placeholder", "pointer-events", "none"), ("a", "placeholder", "line-height", "normal"), ("a", "placeholder", "margin-top", "10px"), ("a", "placeholder", "font-style", "italic"), ("a", "placeholder", "opacity", "0.5"), ("c", "placeholder", "color", "blue"), ("c", "placeholder", "background-color", "yellow"), ("c", "placeholder", "font-size", "30px"), ("c", "placeholder", "display", "none"), ("c", "placeholder", "text-transform", "uppercase"), ("c", "placeholder", "pointer-events", "none"), ("c", "placeholder", "line-height", "normal"), ("c", "placeholder", "margin-top", "10px"), ("c", "placeholder", "font-style", "italic"), ("c", "placeholder", "opacity", "0.5"), ("d", "placeholder", "color", "blue"), ("d", "placeholder", "background-color", "yellow"), ("d", "placeholder", "font-size", "30px"), ("d", "placeholder", "display", "block"), ("d", "placeholder", "text-transform", "uppercase"), ("d", "placeholder", "pointer-events", "none"), ("d", "placeholder", "line-height", "90px"), ("d", "placeholder", "margin-top", "10px"), ("d", "placeholder", "font-style", "italic"), ("d", "placeholder", "opacity", "0.5")]),
        ["ph-important"] = ("<!doctype html><html><head><style>input::placeholder{color:blue !important;display:inline !important;pointer-events:all !important;line-height:3 !important}</style></head><body><input id=a placeholder=hi><input id=c placeholder=hi value=v></body></html>",
            [],
            [("a", "placeholder", "color", "blue"), ("a", "placeholder", "display", "block"), ("a", "placeholder", "pointer-events", "none"), ("a", "placeholder", "line-height", "normal"), ("c", "placeholder", "color", "blue"), ("c", "placeholder", "display", "none"), ("c", "placeholder", "pointer-events", "none"), ("c", "placeholder", "line-height", "normal")]),
        ["ph-alias"] = ("<!doctype html><html><head><style>::-webkit-input-placeholder{color:green} textarea::placeholder{background-color:yellow}</style></head><body><input id=a placeholder=hi><textarea id=d placeholder=t></textarea></body></html>",
            [],
            [("a", "placeholder", "color", "green"), ("a", "placeholder", "background-color", "transparent"), ("d", "placeholder", "color", "green"), ("d", "placeholder", "background-color", "yellow")]),
        ["ph-state"] = ("<!doctype html><html><head><style>input:placeholder-shown::placeholder{color:green} input:focus::placeholder{color:red} input:not(:placeholder-shown)::placeholder{background-color:yellow}</style></head><body><input id=a placeholder=hi><input id=b placeholder=hi value=v><input id=c placeholder=hi></body></html>",
            [("focus", "c", false)],
            [("a", "placeholder", "color", "green"), ("a", "placeholder", "background-color", "transparent"), ("a", "placeholder", "display", "block"), ("b", "placeholder", "color", "#757575"), ("b", "placeholder", "background-color", "yellow"), ("b", "placeholder", "display", "none"), ("c", "placeholder", "color", "red"), ("c", "placeholder", "background-color", "transparent"), ("c", "placeholder", "display", "block")]),

    };

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (string n in Cases.Keys)
        {
            data.Add(n);
        }

        return data;
    }

    private static (Document Doc, StyleContext Context) Setup(string name)
    {
        Document doc = Document.Parse(Cases[name].Html);
        FormStateOverrides form = FormStateOverrides.Empty;
        Node focus = default;
        foreach ((string op, string id, bool value) in Cases[name].Ops)
        {
            Node e = doc.GetElementById(id);
            switch (op)
            {
                case "checked": form = form.WithChecked(e, value); break;
                case "disabled": form = form.WithDisabled(e, value); break;
                case "indeterminate": form = form.WithIndeterminate(e, value); break;
                default: focus = e; break;
            }
        }

        return (doc, StyleContext.Default.WithFormState(form).WithFocus(focus));
    }

    private static void Check(string name, Document doc, Func<Node, ComputedStyle> style, Func<Node, PseudoElementKind, ComputedStyle?> pseudo)
    {
        foreach ((string id, string? kind, string prop, string expected) in Cases[name].Probes)
        {
            Node e = doc.GetElementById(id);
            ComputedStyle? s = kind switch
            {
                null => style(e),
                "selection" => pseudo(e, PseudoElementKind.Selection),
                _ => pseudo(e, PseudoElementKind.Placeholder),
            };
            Assert.True(s is not null, name + " #" + id + "::" + kind + " has no style");
            string actual = Normalize(s!, prop);
            Assert.True(expected == actual, name + " #" + id + (kind is null ? "" : "::" + kind) + " " + prop + ": Chrome " + expected + ", library " + actual);
        }
    }

    // Values the library keeps as written or as keywords, in the form Chrome serializes them: the initial colour is the keyword CanvasText (black),
    // currentcolor is the element's colour, bold and normal are 700 and 400, medium is 16px, a number line-height is a length, and a leading zero is written.
    private static string Normalize(ComputedStyle s, string prop)
    {
        string v = s.GetPropertyValue(prop);
        if (v.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
        {
            v = s.GetPropertyValue("color");
        }

        if (prop == "line-height" && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double factor))
        {
            return (factor * s.FontSizePx).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "px";
        }

        return v switch
        {
            "CanvasText" => "black",
            "bold" when prop == "font-weight" => "700",
            "normal" when prop == "font-weight" => "400",
            "medium" when prop == "font-size" => "16px",
            ".5" when prop == "opacity" => "0.5",
            "0" when prop == "margin-top" => "0px",
            _ => v,
        };
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Snapshot_styles_agree_with_Chrome(string name)
    {
        (Document doc, StyleContext ctx) = Setup(name);
        DocumentStyles styles = DocumentStyles.Create(doc, ctx);
        Check(name, doc, styles.GetStyle, styles.GetPseudoStyle);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Overlay_and_live_styles_agree_with_Chrome(string name)
    {
        (Document doc, StyleContext ctx) = Setup(name);
        DocumentStyles overlay = DocumentStyles.Create(doc).WithContext(ctx);
        Check(name, doc, overlay.GetStyle, overlay.GetPseudoStyle);

        var live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        var changes = new List<FormStateChange>();
        foreach ((string op, string id, bool value) in Cases[name].Ops)
        {
            Node e = doc.GetElementById(id);
            switch (op)
            {
                case "checked": changes.Add(FormStateChange.Checked(e, value)); break;
                case "disabled": changes.Add(FormStateChange.Disabled(e, value)); break;
                case "indeterminate": changes.Add(FormStateChange.Indeterminate(e, value)); break;
                default: live.ApplyState(ElementStateChange.Focus(e)); break;
            }
        }

        live.ApplyFormState(changes.ToArray());
        Check(name, doc, live.GetStyle, live.GetPseudoStyle);
    }
}
