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

return htmlOk && cssOk && lossless && elements > 15 && valueApiOk && queryOk && catalogueOk && editOk && rolesOk ? 0 : 1;
