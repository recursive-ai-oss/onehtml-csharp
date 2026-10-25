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
return tags == 5 && lastByte == page.Length ? 0 : 1;
