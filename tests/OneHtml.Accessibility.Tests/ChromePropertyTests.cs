using System;
using System.Text.Json;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// The properties Chrome states on form controls, widgets and editing hosts, one minimal page per observation. Each expectation is what
/// Chrome 156 reported for the same markup in a reference capture (<c>docs/accessibility-chromium-profile.md</c>, the entries named
/// in the test names); null means the property is absent. Pages are written here.
/// </summary>
public sealed class ChromePropertyTests
{
    private static (Document Doc, AccessibilityTree Tree, JsonDocument Export) Build(string html)
    {
        Document doc = Document.Parse("<!DOCTYPE html><html><body>" + html + "</body></html>");
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc), null, null);
        return (doc, tree, JsonDocument.Parse(tree.ToChromeJson()));
    }

    private static string? ExportedProp(string html, string name)
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build(html);
        AxNode node = tree.Find(doc.Root.QuerySelector("#x")) ?? throw new InvalidOperationException("#x is not in the tree");
        JsonElement n = export.RootElement.GetProperty("nodes")[node.Id];
        Assert.False(n.TryGetProperty("ignored", out JsonElement ig) && ig.GetBoolean(), "#x is ignored in the export");
        foreach (JsonElement p in n.GetProperty("properties").EnumerateArray())
        {
            if (p.GetProperty("name").GetString() == name)
            {
                JsonElement v = p.GetProperty("value").GetProperty("value");
                return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
            }
        }

        return null;
    }

    private static string? ExportedValue(string html)
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build(html);
        AxNode node = tree.Find(doc.Root.QuerySelector("#x")) ?? throw new InvalidOperationException("#x is not in the tree");
        JsonElement n = export.RootElement.GetProperty("nodes")[node.Id];
        return n.TryGetProperty("value", out JsonElement v) ? v.GetProperty("value").GetString() : null;
    }

    [Theory]
    // A disabled ancestor reaches only the descendants that can take focus; the author's own aria-disabled=false ends it.
    [InlineData("<div aria-disabled=true><p id=x>t</p></div>", null)]
    [InlineData("<div aria-disabled=true><a id=x href=#>a</a></div>", "true")]
    [InlineData("<div aria-disabled=true><div id=x tabindex=0>t</div></div>", "true")]
    [InlineData("<div aria-disabled=true><div id=x role=button>b</div></div>", null)]
    [InlineData("<div aria-disabled=true><div id=x role=button tabindex=0 aria-disabled=false>b</div></div>", null)]
    [InlineData("<button disabled><div id=x tabindex=0>x</div></button>", "true")]
    [InlineData("<fieldset disabled><div id=x tabindex=0>x</div></fieldset>", null)]
    [InlineData("<select disabled><option id=x>o</option></select>", "true")]
    [InlineData("<select><optgroup disabled label=g><option id=x>o</option></optgroup></select>", "true")]
    public void Disabled_reaches_the_focusable_descendants_and_the_options_of_a_disabled_group(string html, string? expected) =>
        Assert.Equal(expected, ExportedProp(html, "disabled"));

    [Theory]
    // An item that says nothing is unselected, unless a sibling carries aria-selected; a disabled native option states nothing.
    [InlineData("<div role=listbox><div role=option id=x>a</div><div role=option>b</div></div>", "false")]
    [InlineData("<div role=listbox><div role=option id=x>a</div><div role=option aria-selected=true>b</div></div>", null)]
    [InlineData("<div role=tablist><div role=tab id=x>a</div><div role=tab aria-selected=false>b</div></div>", null)]
    [InlineData("<div role=tree><div role=treeitem id=x>a</div></div>", "false")]
    [InlineData("<div role=listbox><ul><li id=x role=option aria-selected=false tabindex=0>a</li></ul></div>", null)]
    [InlineData("<div><button id=x role=option aria-selected=false>a</button></div>", null)]
    [InlineData("<div role=listbox><div><button id=x role=option aria-selected=false>a</button></div></div>", "false")]
    [InlineData("<select><option id=x disabled>a</option><option>b</option></select>", null)]
    [InlineData("<select><option id=x>a</option></select>", "true")]
    public void Selected_states_follow_the_siblings(string html, string? expected) => Assert.Equal(expected, ExportedProp(html, "selected"));

    [Theory]
    // Chrome reports the constraint failures of the controls whose value is not just text.
    [InlineData("<input id=x type=radio name=r required><input type=radio name=r>", "true")]
    [InlineData("<input id=x type=radio name=r required checked><input type=radio name=r>", null)]
    [InlineData("<input id=x type=date required>", "true")]
    [InlineData("<input id=x type=file required>", "true")]
    [InlineData("<select id=x multiple required><option value=''>p</option></select>", "true")]
    [InlineData("<select id=x required><option value='' disabled selected>Pick</option><option>a</option></select>", "true")]
    [InlineData("<select id=x required><option value=''>Pick</option><option selected>a</option></select>", null)]
    [InlineData("<input id=x type=email value=foo>", "true")]
    [InlineData("<input id=x type=email value='a@b.co'>", null)]
    [InlineData("<input id=x type=url value=foo>", "true")]
    [InlineData("<input id=x pattern='[0-9]+' value=abc>", "true")]
    [InlineData("<input id=x pattern='[0-9]+' value=abc readonly>", null)]
    [InlineData("<input id=x type=date min=2030-01-01 value=2020-01-01>", "true")]
    [InlineData("<input id=x type=number min=5.56 max=106.64 value=106.64>", "true")]
    [InlineData("<input id=x type=number step=2 value=3>", null)]
    [InlineData("<input id=x type=number min=1 step=2 value=4>", "true")]
    // An empty required text field is not reported, and an explicit aria-invalid=false, a disabled control or the readonly attribute removes the report.
    [InlineData("<input id=x required>", null)]
    [InlineData("<input id=x type=checkbox required aria-invalid=false>", null)]
    [InlineData("<input id=x type=checkbox required disabled>", null)]
    [InlineData("<input id=x type=number min=1 max=0 value=1 readonly>", null)]
    [InlineData("<input id=x type=checkbox required>", "true")]
    public void Invalid_follows_the_constraints_Chrome_reports(string html, string? expected) => Assert.Equal(expected, ExportedProp(html, "invalid") is "false" ? null : ExportedProp(html, "invalid"));

    [Theory]
    [InlineData("<input id=x readonly>", "true")]
    [InlineData("<input id=x type=password readonly>", "true")]
    [InlineData("<input id=x type=number readonly>", null)]
    [InlineData("<input id=x type=checkbox readonly>", null)]
    [InlineData("<input id=x type=search readonly>", null)]
    [InlineData("<input id=x readonly disabled>", null)]
    [InlineData("<div id=x role=textbox aria-readonly=true tabindex=0></div>", "true")]
    [InlineData("<div id=x role=combobox aria-readonly=true tabindex=0></div>", null)]
    [InlineData("<div role=grid><div role=row><div id=x role=gridcell aria-readonly=true>c</div></div></div>", "true")]
    public void ReadOnly_is_stated_on_text_boxes_and_grids_only(string html, string? expected) =>
        Assert.Equal(expected, ExportedProp(html, "readonly") is "false" ? null : ExportedProp(html, "readonly"));

    [Theory]
    [InlineData("<button id=x role=combobox>Mileage</button>", "hasPopup", "listbox")]
    [InlineData("<div id=x role=combobox aria-haspopup=false tabindex=0></div>", "hasPopup", "listbox")]
    [InlineData("<div id=x role=combobox aria-haspopup=menu tabindex=0></div>", "hasPopup", "menu")]
    [InlineData("<a id=x href=# aria-haspopup=1>l</a>", "hasPopup", null)]
    [InlineData("<a id=x href=# aria-haspopup=true>l</a>", "hasPopup", "menu")]
    [InlineData("<input id=x type=text aria-autocomplete=list>", "hasPopup", null)]
    [InlineData("<details><summary id=x aria-haspopup=dialog>s</summary>t</details>", "hasPopup", "dialog")]
    [InlineData("<select id=x><option>a</option></select>", "hasPopup", "menu")]
    [InlineData("<div id=x role=combobox aria-autocomplete=list tabindex=0></div>", "autocomplete", null)]
    [InlineData("<input id=x type=text aria-autocomplete=list>", "autocomplete", "list")]
    [InlineData("<input id=x type=text role=combobox aria-autocomplete=both>", "autocomplete", "both")]
    [InlineData("<input id=x list=dl disabled value=MIT><datalist id=dl><option>MIT</option></datalist>", "autocomplete", "list")]
    public void Popup_and_autocomplete_follow_Chromes_defaults(string html, string name, string? expected) => Assert.Equal(expected, ExportedProp(html, name));

    [Theory]
    [InlineData("<a id=x href=# accesskey=''>k</a>", "Alt+")]
    [InlineData("<button id=x accesskey=l01>k</button>", "Alt+l01")]
    [InlineData("<a id=x href=# accesskey='a b'>k</a>", "Alt+a b")]
    [InlineData("<a id=x href=# accesskey=x aria-keyshortcuts='Control+Z'>k</a>", "Control+Z")]
    public void Accesskeys_are_stated_as_written(string html, string expected) => Assert.Equal(expected, ExportedProp(html, "keyshortcuts"));

    [Theory]
    [InlineData("<div id=x aria-roledescription=slide>t</div>", null)]
    [InlineData("<section id=x aria-label=c aria-roledescription=carousel>t</section>", "carousel")]
    [InlineData("<button id=x aria-roledescription='caf\u00e9'>t</button>", "caf\u00c3\u00a9")]
    public void Role_descriptions_skip_generics_and_arrive_as_Latin1_bytes(string html, string? expected) => Assert.Equal(expected, ExportedProp(html, "roledescription"));

    [Theory]
    [InlineData("<div contenteditable=true><p id=x>t</p></div>", "richtext")]
    [InlineData("<div contenteditable=true><button id=x>b</button></div>", "richtext")]
    [InlineData("<span id=x contenteditable=plaintext-only tabindex=0>t</span>", "plaintext")]
    [InlineData("<div contenteditable=true><div contenteditable=false><button id=x>b</button></div></div>", null)]
    public void Editable_reaches_the_content_of_an_editing_host(string html, string? expected) => Assert.Equal(expected, ExportedProp(html, "editable"));

    [Theory]
    [InlineData("<input id=x type=number min=5.56 max=106.64 value=5.56>", "valuemin", "5.559999942779541")]
    [InlineData("<input id=x type=number min=5.56 max=106.64 value=5.56>", "valuemax", "106.63999938964844")]
    [InlineData("<div id=x role=spinbutton aria-valuenow=3 tabindex=0></div>", "valuemin", "0")]
    [InlineData("<div id=x role=spinbutton aria-valuenow=3 tabindex=0></div>", "valuemax", "0")]
    [InlineData("<div id=x role=progressbar aria-valuenow=50 aria-valuetext=half></div>", "valuetext", null)]
    [InlineData("<input id=x type=text role=spinbutton aria-valuenow=675000 value='$675,000'>", "valuetext", "$675,000")]
    [InlineData("<input id=x type=range min=0 max=174 aria-valuemax=100 value=0>", "valuemax", "100")]
    [InlineData("<input id=x type=number value=1.00>", "valuetext", "1.00")]
    // posinset and setsize never appear.
    [InlineData("<div role=menu><div id=x role=menuitem aria-posinset=2 aria-setsize=5>m</div></div>", "posinset", null)]
    [InlineData("<div role=menu><div id=x role=menuitem aria-posinset=2 aria-setsize=5>m</div></div>", "setsize", null)]
    public void Range_bounds_are_single_precision_and_value_text_is_the_native_value(string html, string name, string? expected) =>
        Assert.Equal(expected, ExportedProp(html, name));

    [Theory]
    [InlineData("<button id=x role=combobox>Stampante</button>", "Stampante")]
    [InlineData("<input id=x type=file>", "No file chosen")]
    [InlineData("<input id=x type=password value=secret>", "\u2022\u2022\u2022\u2022\u2022\u2022")]
    [InlineData("<div id=x role=textbox tabindex=0>some  text</div>", "some text")]
    [InlineData("<input id=x value='a&#10;b'>", "ab")]
    [InlineData("<input id=x type=color value=#ABCDEF>", "#abcdef")]
    [InlineData("<input id=x type=date value=today>", "")]
    [InlineData("<select id=x style='text-transform:uppercase' aria-label=s><option>All</option></select>", "ALL")]
    [InlineData("<div id=x role=progressbar aria-valuenow=Native aria-valuemin=0></div>", "0")]
    public void Values_follow_what_Chrome_shows(string html, string expected) => Assert.Equal(expected, ExportedValue(html));
}
