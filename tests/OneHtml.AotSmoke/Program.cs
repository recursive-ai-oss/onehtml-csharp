using System.Linq;
using System;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;

// Calls into each package so the NativeAOT publish has to trim and compile all three.
Console.WriteLine(RecursiveAI.OneHtml.PackageInfo.Name);
Console.WriteLine(RecursiveAI.OneHtml.Accessibility.PackageInfo.Name);
Console.WriteLine(RecursiveAI.OneHtml.Accessibility.Validation.PackageInfo.Name);

// Sniff, decode and tokenize a small page in a legacy encoding, then map a token back to bytes.
byte[] page = Encoding.Latin1.GetBytes("<!DOCTYPE html><meta charset=\"iso-8859-2\"><p class=a>caf\u00E9 &amp; <b>bar</b></p><!-- c -->");
SourceText text = SourceText.Decode(page);
var tokenizer = new HtmlTokenizer(text, new HtmlTokenizerOptions { RecordErrors = true });
int tags = 0;
int characters = 0;
int lastByte = 0;
HtmlTokenKind kind;
while ((kind = tokenizer.Next()) != HtmlTokenKind.EndOfFile)
{
    tags += kind is HtmlTokenKind.StartTag or HtmlTokenKind.EndTag ? 1 : 0;
    characters += kind == HtmlTokenKind.Character ? tokenizer.Data.Length : 0;
    lastByte = text.ToByteOffset(tokenizer.End);
}

Console.WriteLine($"{text.Encoding.Name} {text.Confidence} tags={tags} chars={characters} lastByte={lastByte}/{page.Length} errors={tokenizer.Errors?.Count}");
bool htmlOk = tags == 5 && lastByte == page.Length;

// CSS: parse a stylesheet and walk the result.
var sheet = RecursiveAI.OneHtml.Css.Stylesheet.Parse("@media screen { a { color: red !important } } b { c: d(e) }");
int rules = 0;
foreach (var rule in sheet.Rules)
{
    rules++;
    foreach (var child in rule.Children)
    {
        rules += child.Kind == RecursiveAI.OneHtml.Css.CssNodeKind.Declaration ? 1 : 0;
    }
}

Console.WriteLine(rules + " " + sheet.Serialize().Length + " " + sheet.ToSourceText().Length);
bool cssOk = rules == 3 && sheet.ToSourceText() == sheet.Text;

// Parse a page into a document, read it back, serialize it, and write it out byte for byte.
string html = "<!DOCTYPE html><title>t</title><table><tr><td>x<b>y<p>z</b>w</table><select><button><selectedcontent></button><option>o</select><svg viewbox='0 0 1 1'><circle/></svg>";
byte[] source = Encoding.UTF8.GetBytes(html);
RecursiveAI.OneHtml.Document doc = RecursiveAI.OneHtml.Document.Parse(source);
int elements = 0;
RecursiveAI.OneHtml.Node titleNode = default;
var pending = new System.Collections.Generic.Stack<RecursiveAI.OneHtml.Node>();
pending.Push(doc.Root);
while (pending.Count > 0)
{
    RecursiveAI.OneHtml.Node node = pending.Pop();
    elements += node.IsElement ? 1 : 0;
    if (node.IsElement && node.LocalName == "title" && titleNode.Document is null)
    {
        titleNode = node;
    }
    foreach (RecursiveAI.OneHtml.Node child in node.ChildNodes)
    {
        pending.Push(child);
    }
}

RecursiveAI.OneHtml.Document fragment = RecursiveAI.OneHtml.Document.ParseFragment("<li>a<li>b", new RecursiveAI.OneHtml.FragmentContext(RecursiveAI.OneHtml.ElementNamespace.Html, "ul"));
bool lossless = doc.ToLosslessBytes().AsSpan().SequenceEqual(source);
Console.WriteLine($"nodes={doc.NodeCount} elements={elements} lossless={lossless} mode={doc.Mode} fragment={fragment.SerializeChildren(fragment.Root)}");

// CSS values: property table, colours, math, var(), shorthands, media queries, @supports, stylesheet bytes.
bool valuesOk = PropertyTable.TryGet("margin", out PropertyInfo? margin) && margin.IsShorthand && PropertyTable.Count > 500;
bool colorOk = CssColor.TryParse("color-mix(in oklch, hsl(120 30% 50%), rgb(from red r g b / 50%))", out CssColor mixed) && mixed.ToCss().Length > 0
    && CssColor.TryParse("#336699", out CssColor hex) && hex.ToCss() == "rgb(51, 102, 153)";
bool calcOk = CssQuantity.TryParse(CssTree.ParseComponentValueList("clamp(1px, 2em + 10%, 100px)").Values, QuantityKinds.LengthPercentage, out CssQuantity length)
    && length.Resolve(LengthContext.Default, 200) == 52;
var custom = new System.Collections.Generic.Dictionary<string, ComponentValueList> { ["--a"] = CssTree.ParseComponentValueList("1px var(--b, 2px)").Values };
SubstitutionResult substituted = VarSubstitution.Substitute(CssTree.ParseComponentValueList("var(--a) var(--missing, 3em)").Values, VarSubstitution.ResolveCustomProperties(custom));
bool varOk = substituted.IsValid && substituted.Text == "1px 2px 3em" && VarSubstitution.IsValidSyntax(CssTree.ParseComponentValueList("var({--x}, 1px)").Values);
ShorthandExpansion expansion = Shorthands.Expand("margin", CssTree.ParseComponentValueList("1px 2px").Values);
bool shorthandOk = expansion.Status == ShorthandStatus.Expanded && expansion.Longhands.Count == 4;
MediaQueryList media = MediaQueryList.Parse("screen and (min-width: 600px) and (prefers-color-scheme: light), print");
bool mediaOk = media.Matches(StyleContext.Default.Media) && !MediaQueryList.Parse("(prefers-color-scheme: dark)").Matches(StyleContext.Default);
bool supportsOk = SupportsCondition.Parse("(display: grid) and (not (foo: bar))").Evaluate();
var sheetBytes = Encoding.Latin1.GetBytes("@charset \"iso-8859-2\"; a { content: \"\u00E9\" }");
int byteRules = 0;
foreach (CssNode unused in Stylesheet.Parse(sheetBytes, WhatwgCssEncodingProvider.Instance).Rules)
{
    byteRules++;
}

bool bytesOk = byteRules == 2; // the @charset rule and the style rule
Console.WriteLine($"var: {substituted.Status} [{substituted.Text}] bytes: {byteRules}");
Console.WriteLine($"values: table={valuesOk} color={colorOk} calc={calcOk} var={varOk} shorthand={shorthandOk} media={mediaOk} supports={supportsOk} bytes={bytesOk}");
bool valueApiOk = valuesOk && colorOk && calcOk && varOk && shorthandOk && mediaOk && supportsOk && bytesOk;

// Selectors and locators: query, match, validity with a pattern, then a locator verified against its target.
RecursiveAI.OneHtml.Document qdoc = RecursiveAI.OneHtml.Document.Parse(Encoding.UTF8.GetBytes("<ul><li>a<li class=x>b<li><a href=/x>c</a></ul><form><input pattern='[a-z]+' value='abc'><input pattern='[a-z]+' value='123'></form>"));
var found = qdoc.Root.QuerySelectorAll("li:not(.x) > a[href], li.x:nth-child(2), input:invalid, :is(ul, ol):has(> li.x)");
RecursiveAI.OneHtml.Node target = qdoc.Root.QuerySelector("li.x");
var generator = new RecursiveAI.OneHtml.Locators.LocatorGenerator(qdoc);
string locator = generator.Generate(target);
var fingerprint = RecursiveAI.OneHtml.Locators.ElementFingerprint.Create(target);
bool queryOk = found.Count == 4 && qdoc.Root.QuerySelector(locator).Equals(target) && fingerprint.TextHash.StartsWith("sha256:", StringComparison.Ordinal);
Console.WriteLine($"query={found.Count} locator={locator} queryOk={queryOk}");
// Validation catalogue: embedded reference data and profile selection.
var contrastCriterion = RecursiveAI.OneHtml.Accessibility.Validation.Catalogue.WcagCriteria.Get("1.4.3");
bool catalogueOk = RecursiveAI.OneHtml.Accessibility.Validation.Catalogue.ValidationProfile.Wcag20AA.Includes(contrastCriterion)
    && !RecursiveAI.OneHtml.Accessibility.Validation.Catalogue.ValidationProfile.Wcag22AA.Includes(RecursiveAI.OneHtml.Accessibility.Validation.Catalogue.WcagCriteria.Get("4.1.1"))
    && RecursiveAI.OneHtml.Accessibility.Validation.Catalogue.AxeRules.Find("image-alt") is not null
    && RecursiveAI.OneHtml.Accessibility.Validation.Catalogue.ActRules.All.Count > 90;
Console.WriteLine($"catalogue: {catalogueOk} {contrastCriterion}");

// Edit: set an attribute and append markup, then check the result and that the old snapshot is untouched.
RecursiveAI.OneHtml.Editing.EditResult edit = RecursiveAI.OneHtml.DocumentEditing.Apply(doc, new[]
{
    RecursiveAI.OneHtml.Editing.Edit.SetAttribute(titleNode, "id", "t\u00E9"),
    RecursiveAI.OneHtml.Editing.Edit.Append(titleNode, "<!--x-->"),
});
string edited = edit.Succeeded ? Encoding.Latin1.GetString(edit.Document!.ToLosslessBytes()) : "";
Console.WriteLine($"edit={edit.Succeeded} added={edit.Changes?.Added.Count} ranges={edit.Changes?.Ranges.Count} {edited.Length}");
// The page has no charset, so it is windows-1252: the edit keeps the bytes, quotes in the page's style, and escapes text in <title>.
bool editOk = edit.Succeeded && edited.StartsWith("<!DOCTYPE html><title id='t\u00E9'>t&lt;!--x--&gt;</title>", StringComparison.Ordinal)
    && doc.ToLosslessBytes().AsSpan().SequenceEqual(source);

// Accessibility roles: the embedded WAI-ARIA data, then roles, conflict resolution and diagnostics for a small page.
RecursiveAI.OneHtml.Document rdoc = RecursiveAI.OneHtml.Document.Parse("<header><ul role=none><li>a</ul><h1 role=none tabindex=0>t</h1><div role=checkbox aria-labelledby=gone>x</div><button><span id=in>y</span></button></header>");
var roleMap = RecursiveAI.OneHtml.Accessibility.Aria.RoleComputer.Compute(rdoc);
bool rolesOk = roleMap[rdoc.Root.QuerySelector("ul")].ComputedRole == "none"
    && roleMap[rdoc.Root.QuerySelector("li")].Origin == RecursiveAI.OneHtml.Accessibility.Aria.RoleOrigin.InheritedPresentational
    && roleMap[rdoc.Root.QuerySelector("h1")].ComputedRole == "heading"
    && roleMap[rdoc.Root.QuerySelector("#in")].Origin == RecursiveAI.OneHtml.Accessibility.Aria.RoleOrigin.ChildrenPresentational
    && roleMap[rdoc.Root.QuerySelector("header")].ComputedRole == "banner"
    && roleMap[rdoc.Root.QuerySelector("[role=checkbox]")].Diagnostics.Length == 2
    && RecursiveAI.OneHtml.Accessibility.Aria.AriaTaxonomy.Roles.Count > 100
    && RecursiveAI.OneHtml.Accessibility.Aria.HtmlAam.Elements.Count > 100;
Console.WriteLine($"roles: {rolesOk}");


// Cascade: UA sheet, author rules, layers, inheritance, a focus overlay and a restyle.
RecursiveAI.OneHtml.Document sdoc = RecursiveAI.OneHtml.Document.Parse("<style>@layer a,b; @layer b{p{color:red}} @layer a{p{color:blue}} .k{--w:2px; margin-left:var(--w)} a:focus{color:green} p.q{color:orange}</style><div class=k><p id=p>x<a id=l href=/x>y</a></p></div>");
DocumentStyles styles = DocumentStyles.Create(sdoc);
RecursiveAI.OneHtml.Node sp = sdoc.GetElementById("p");
RecursiveAI.OneHtml.Node sl = sdoc.GetElementById("l");
DocumentStyles focused = styles.WithContext(StyleContext.Default.WithFocus(sl));
RecursiveAI.OneHtml.Document sdoc2 = RecursiveAI.OneHtml.Document.Parse("<style>@layer a,b; @layer b{p{color:red}} @layer a{p{color:blue}} .k{--w:2px; margin-left:var(--w)} a:focus{color:green} p.q{color:orange}</style><div class=k><p id=p class=q>x<a id=l href=/x>y</a></p></div>");
RestyleResult restyled = styles.Restyle(sdoc2, new StyleChange { Elements = [new ElementChange(sdoc2.GetElementById("p").Index, ["class"])] });
bool styleOk = styles.GetStyle(sp).GetPropertyValue("color") == "red" && styles.GetStyle(sl).GetComputedValue("color") == "rgb(0, 0, 238)" && styles.GetStyle(sp).GetPropertyValue("margin-left") == "0"
    && styles.GetStyle(sdoc.Root.QuerySelector(".k")).GetPropertyValue("margin-left") == "2px" && focused.GetStyle(sl).GetComputedValue("color") == "rgb(0, 128, 0)"
    && styles.GetStyle(sp).Display.ToCss() == "block" && restyled.Styles.GetStyle(sdoc2.GetElementById("p")).GetComputedValue("color") == "rgb(255, 165, 0)" && !restyled.FullRecompute;
Console.WriteLine($"style={styleOk} sheets={styles.Sheets.Count} rules={styles.RuleCount} restyled={restyled.RestyledElements.Count}");

// An edit followed by a restyle through the one call: a class change on the document and a linked sheet edit.
RecursiveAI.OneHtml.Editing.LinkedStylesheet linkedSheet = RecursiveAI.OneHtml.Editing.LinkedStylesheet.Create("https://t.test/a.css", Encoding.UTF8.GetBytes("p { color: red }\n.q { color: orange }\n"));
RecursiveAI.OneHtml.Document editedPage = RecursiveAI.OneHtml.Document.Parse("<link rel=stylesheet href=a.css><div><p id=p>x</p><p id=r>y</p></div>");
DocumentStyles pageStyles = DocumentStyles.Create(editedPage, null, new StyleSourceOptions { BaseUrl = "https://t.test/", Resolver = _ => new StylesheetResource(Encoding.UTF8.GetBytes("p { color: red }\n.q { color: orange }\n")) });
RecursiveAI.OneHtml.Editing.EditResult pageEdit = RecursiveAI.OneHtml.DocumentEditing.Apply(editedPage, new[] { RecursiveAI.OneHtml.Editing.Edit.SetAttribute(editedPage.GetElementById("p"), "class", "q") });
RestyleResult pageRestyle = pageStyles.Restyle(pageEdit);
RecursiveAI.OneHtml.Css.CssNode firstRule = default;
foreach (RecursiveAI.OneHtml.Css.CssNode rule in linkedSheet.Sheet.Rules)
{
    firstRule = rule;
    break;
}

RecursiveAI.OneHtml.Editing.BatchResult sheetBatch = new RecursiveAI.OneHtml.Editing.EditBatch(null, null, new[] { RecursiveAI.OneHtml.Editing.SheetEdit.SetDeclaration(linkedSheet.Sheet, firstRule, "color", "blue") }, new[] { linkedSheet }).Apply();
RestyleResult sheetRestyle = pageRestyle.Styles.Restyle(sheetBatch);
bool restyleEditOk = pageEdit.Succeeded && !pageRestyle.FullRecompute && pageRestyle.Styles.GetStyle(pageRestyle.Styles.Document.GetElementById("p")).GetPropertyValue("color") == "orange"
    && sheetBatch.Succeeded && !sheetRestyle.FullRecompute && sheetRestyle.Styles.GetStyle(sheetRestyle.Styles.Document.GetElementById("r")).GetPropertyValue("color") == "blue";
Console.WriteLine($"edit+restyle={restyleEditOk} restyled={pageRestyle.RestyledElements.Count}/{sheetRestyle.RestyledElements.Count}");

// Saved edits: describe an element edit and a stylesheet edit, write the envelope, read it back, replay it on a fresh parse.
string savedPage = "<!DOCTYPE html><html><head><style>\n.a { color: red }\n.b { margin: 0 }\n</style></head><body><nav id=main-nav><a href=/x>X</a></nav></body></html>";
RecursiveAI.OneHtml.Document savedDoc = RecursiveAI.OneHtml.Document.Parse(Encoding.UTF8.GetBytes(savedPage));
RecursiveAI.OneHtml.Editing.InlineStylesheet inline = RecursiveAI.OneHtml.Editing.InlineStylesheets.Of(savedDoc)[0];
RecursiveAI.OneHtml.Css.CssNode secondRule = default;
int ruleNumber = 0;
foreach (RecursiveAI.OneHtml.Css.CssNode rule in inline.Sheet.Rules)
{
    if (ruleNumber++ == 1)
    {
        secondRule = rule;
    }
}

RecursiveAI.OneHtml.Editing.EditEnvelope envelope = RecursiveAI.OneHtml.Editing.EditJson.Create(
    savedDoc,
    new[] { RecursiveAI.OneHtml.Editing.Edit.SetAttribute(savedDoc.Root.QuerySelector("nav"), "title", "Main") },
    new[] { RecursiveAI.OneHtml.Editing.SheetEdit.SetDeclaration(inline.Sheet, secondRule, "padding", "1px") });
string envelopeJson = RecursiveAI.OneHtml.Editing.EditJson.ToJson(envelope);
RecursiveAI.OneHtml.Editing.EditEnvelope readBack = RecursiveAI.OneHtml.Editing.EditJson.Parse(envelopeJson);
RecursiveAI.OneHtml.Editing.ReplayResult replay = RecursiveAI.OneHtml.Editing.EditReplay.Replay(readBack, RecursiveAI.OneHtml.Document.Parse(Encoding.UTF8.GetBytes(savedPage)));
string replayed = replay.Succeeded ? replay.Batch!.Document!.Source.Text : "";
bool replayOk = replay.Succeeded && readBack.ToJson() == envelopeJson
    && replayed.Contains("<nav id=main-nav title=", StringComparison.Ordinal) && replayed.Contains("margin: 0; padding: 1px", StringComparison.Ordinal);
Console.WriteLine($"replay={replay.Succeeded} json={envelopeJson.Length} ok={replayOk}");

// Accessibility tree: names, hidden status, aria-owns, states, focus and the Chrome-shaped export.
RecursiveAI.OneHtml.Document tdoc = RecursiveAI.OneHtml.Document.Parse("<style>.g::before{content:'[' attr(data-k) ']'; counter-increment:c} </style><main><h1 id=h>Title <em>here</em></h1><button id=b class=g data-k=k aria-describedby=d>Go</button><span id=d>help</span><div id=o aria-owns=x></div><ul><li id=x>one</li></ul><div hidden id=hid>no</div><a href=/y tabindex=1>link</a></main>");
RecursiveAI.OneHtml.Accessibility.AccessibilityTree tree = RecursiveAI.OneHtml.Accessibility.AccessibilityTree.Build(tdoc, DocumentStyles.Create(tdoc));
RecursiveAI.OneHtml.Accessibility.AxNode tb = tree.Find(tdoc.GetElementById("b"))!;
string json = tree.ToChromeJson();
bool treeOk = tree.Find(tdoc.GetElementById("h"))!.Name.Value == "Title here" && tb.Name.Value == "[k]Go" && tb.Description.Value == "help"
    && tree.Find(tdoc.GetElementById("x"))!.Parent!.DomNode.Id == "o" && tree.Find(tdoc.GetElementById("hid")) is null
    && tree.GetHidden(tdoc.GetElementById("hid")).Reasons == RecursiveAI.OneHtml.Accessibility.HiddenReasons.HiddenAttribute
    && tree.FocusOrder[0].DomNode.LocalName == "a" && json.Contains("\"nodes\"", StringComparison.Ordinal);
Console.WriteLine($"tree={treeOk} nodes={tree.Nodes.Count} json={json.Length}");

// Rendered facts: a small DOMSnapshot.captureSnapshot result mapped onto the document it describes, then refused for another.
const string snapshotJson = """
{"documents":[{"documentURL":9,"nodes":{"nodeType":[9,1,1,1,1,3],"parentIndex":[-1,0,1,1,3,4],"nodeName":[0,1,2,3,4,5],"nodeValue":[-1,-1,-1,-1,-1,6],
"attributes":[[],[],[],[],[7,8],[]],"backendNodeId":[1,2,3,4,5,6]},
"layout":{"nodeIndex":[4,5],"bounds":[[8,8,100,18],[8,8,12,18]],"styles":[[10],[10]],"text":[-1,6],"paintOrders":[1,1],"stackingContexts":{"index":[0]}},
"textBoxes":{"layoutIndex":[1],"bounds":[[8,8,12,18]],"start":[0],"length":[2]}}],
"strings":["#document","HTML","HEAD","BODY","P","#text","hi","id","a","http://x.invalid/","block"]}
""";
var snapshot = RecursiveAI.OneHtml.Accessibility.Rendered.DomSnapshot.Parse(System.Text.Encoding.UTF8.GetBytes(snapshotJson), ["display"]);
RecursiveAI.OneHtml.Document factsDoc = RecursiveAI.OneHtml.Document.Parse("<p id=a>hi</p>");
var mapped = RecursiveAI.OneHtml.Accessibility.Rendered.DomSnapshotAdapter.Map(snapshot, factsDoc);
var refused = RecursiveAI.OneHtml.Accessibility.Rendered.DomSnapshotAdapter.Map(snapshot, RecursiveAI.OneHtml.Document.Parse("<p id=a>hi</p><p>more</p>"));
bool factsOk = mapped.Succeeded && mapped.Accounting!.IsComplete
    && mapped.Facts![factsDoc.Root.QuerySelector("p")].Layout.BorderBox.Width == 100
    && mapped.Facts[factsDoc.Root.QuerySelector("p")].GetStyle("display") == "block"
    && mapped.Facts[factsDoc.Root.QuerySelector("p").FirstChild].Layout.TextBoxes.Length == 1
    && !mapped.Facts[factsDoc.Root.QuerySelector("head")].HasLayout
    && !refused.Succeeded && refused.Refusal!.Reason == RecursiveAI.OneHtml.Accessibility.Rendered.RefusalReason.NodeCountMismatch;
Console.WriteLine($"rendered facts: {factsOk}");


// Validation: run the ARIA rules, read both exports, apply a suggested edit and check again.
RecursiveAI.OneHtml.Document vdoc = RecursiveAI.OneHtml.Document.Parse("<body><div role=lnik id=a>x</div><p aria-foo=1 id=p>y</p><ul role=list><span role=link>z</span></ul><button aria-hidden=true>h</button></body>");
RecursiveAI.OneHtml.Accessibility.Validation.CheckReport vreport = new RecursiveAI.OneHtml.Accessibility.Validation.Checker().Check(vdoc);
string nativeJson = RecursiveAI.OneHtml.Accessibility.Validation.ResultsExport.ToNativeJson(vreport);
string axeJson = RecursiveAI.OneHtml.Accessibility.Validation.ResultsExport.ToAxeJson(vreport);
RecursiveAI.OneHtml.Editing.EditResult fixedResult = RecursiveAI.OneHtml.DocumentEditing.Apply(vdoc, vreport.SuggestedEditList());
RecursiveAI.OneHtml.Accessibility.Validation.CheckReport vafter = new RecursiveAI.OneHtml.Accessibility.Validation.Checker().Check(fixedResult.Document!);
bool validationOk = vreport.Count(RecursiveAI.OneHtml.Accessibility.Validation.Outcome.Failed) >= 3
    && nativeJson.Contains("\"onehtml-results\"", StringComparison.Ordinal) && axeJson.Contains("\"violations\"", StringComparison.Ordinal)
    && fixedResult.Succeeded && vafter.Count(RecursiveAI.OneHtml.Accessibility.Validation.Outcome.Failed) == vreport.Count(RecursiveAI.OneHtml.Accessibility.Validation.Outcome.Failed) - 1;
Console.WriteLine($"validation={validationOk} results={vreport.Results.Count} native={nativeJson.Length} axe={axeJson.Length}");

// Edit -> restyle -> tree update -> diff, reading view and navigation lists.
DocumentStyles tstyles = DocumentStyles.Create(tdoc);
RecursiveAI.OneHtml.Accessibility.AccessibilityTree tree0 = RecursiveAI.OneHtml.Accessibility.AccessibilityTree.Build(tdoc, tstyles);
RecursiveAI.OneHtml.Editing.EditResult treeEdit = RecursiveAI.OneHtml.DocumentEditing.Apply(tdoc, new[] { RecursiveAI.OneHtml.Editing.Edit.SetAttribute(tdoc.GetElementById("b"), "aria-label", "Start") });
RestyleResult treeRestyle = tstyles.Restyle(treeEdit);
RecursiveAI.OneHtml.Accessibility.AccessibilityTree tree1 = tree0.Update(treeEdit, treeRestyle);
RecursiveAI.OneHtml.Accessibility.TreeDiff treeDiff = tree0.Diff(tree1, treeEdit.Changes);
RecursiveAI.OneHtml.Accessibility.ReadingView reading = tree1.CreateReadingView();
RecursiveAI.OneHtml.Accessibility.NavigationLists nav = tree1.CreateNavigationLists();
bool updateOk = tree1.UpdateInfo is { Incremental: true } && tree1.ToChromeJson() == RecursiveAI.OneHtml.Accessibility.AccessibilityTree.Build(treeEdit.Document!, DocumentStyles.Create(treeEdit.Document!)).ToChromeJson()
    && treeDiff.Changed.Count == 1 && treeDiff.Changed[0].Properties.Any(p => p.Property == "name" && p.NewValue == "Start")
    && reading.Items.Any(i => i.Text == "button: Start") && nav.Headings.Count == 1 && nav.Links.Count == 1
    && tree0.CreateReadingView().Diff(reading, treeEdit.Changes).Changed.Count == 1;
Console.WriteLine($"update={updateOk} recomputed={tree1.UpdateInfo?.NamesRecomputed}/{tree1.UpdateInfo?.ElementNodes} reading={reading.Items.Count}");
return validationOk && htmlOk && cssOk && lossless && elements > 15 && valueApiOk && queryOk && catalogueOk && editOk && rolesOk && styleOk && replayOk && restyleEditOk && treeOk && factsOk && updateOk ? 0 : 1;
