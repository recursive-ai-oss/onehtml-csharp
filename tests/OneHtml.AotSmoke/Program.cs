using System;
using System.Text;
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
var pending = new System.Collections.Generic.Stack<RecursiveAI.OneHtml.Node>();
pending.Push(doc.Root);
while (pending.Count > 0)
{
    RecursiveAI.OneHtml.Node node = pending.Pop();
    elements += node.IsElement ? 1 : 0;
    foreach (RecursiveAI.OneHtml.Node child in node.Children)
    {
        pending.Push(child);
    }
}

RecursiveAI.OneHtml.Document fragment = RecursiveAI.OneHtml.Document.ParseFragment("<li>a<li>b", new RecursiveAI.OneHtml.FragmentContext(RecursiveAI.OneHtml.ElementNamespace.Html, "ul"));
bool lossless = doc.ToLosslessBytes().AsSpan().SequenceEqual(source);
Console.WriteLine($"nodes={doc.NodeCount} elements={elements} lossless={lossless} mode={doc.Mode} fragment={fragment.SerializeChildren(fragment.Root)}");
return htmlOk && cssOk && lossless && elements > 15 ? 0 : 1;
