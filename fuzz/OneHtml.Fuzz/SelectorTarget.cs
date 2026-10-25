using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Locators;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz target for selectors. The input is a selector, a zero byte, then a small page (a built-in page when there is none).
/// Checks: parsing either succeeds or throws the typed exception; the serialization parses and serializes to itself;
/// <c>querySelectorAll</c> (the bulk path with its caches and marks) returns exactly the elements for which
/// <c>matches</c> (the single-element path) is true, in document order; <c>querySelector</c> returns the first of them; and
/// every element of the page gets a locator that finds exactly it. The first selector byte picks the text encoding (odd:
/// UTF-16, so lone surrogates are reachable).
/// </summary>
internal static class SelectorTarget
{
    private const string DefaultPage =
        "<!DOCTYPE html><html lang=en><body><form id=f><input name=a required><input type=checkbox checked><select><option>1<option selected>2</select>"
        + "<fieldset disabled><input id=d></fieldset><textarea placeholder=p></textarea></form><div class='a b' id=x><p>one<p dir=rtl>two<ul><li>1<li>2<li>3</ul>"
        + "<a href=u>l</a><svg><a id=s></a></svg><template><b></b></template></div><h1>t</h1>";

    private static readonly SelectorParseOptions Options = new()
    {
        Limits = new SelectorLimits { MaxLength = 4096, MaxNesting = 8, MaxCompounds = 16, MaxComplexSelectors = 64, MaxSimpleSelectors = 256 },
    };

    private static readonly ParseLimits PageLimits = new() { MaxInputLength = 1 << 16, MaxNodes = 1 << 12, MaxOpenElements = 128 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        // The first byte is the encoding flag, so the separator is the first zero after it.
        int split = data.Length > 1 ? data[1..].IndexOf((byte)0) : -1;
        split = split < 0 ? -1 : split + 1;
        ReadOnlySpan<byte> selectorBytes = split < 0 ? data : data[..split];
        ReadOnlySpan<byte> pageBytes = split < 0 ? default : data[(split + 1)..];
        string selector = DecodeSelector(selectorBytes);

        if (!SelectorList.TryParse(selector, out SelectorList? list, Options))
        {
            return;
        }

        string s1 = list!.ToString();
        SelectorList reparsed = SelectorList.Parse(s1);
        if (reparsed.ToString() != s1)
        {
            throw new InvalidOperationException("serialization is not stable: " + s1);
        }

        _ = list.MaxSpecificity;
        _ = list.Dependencies;
        _ = SelectorList.IsValidComplexSelector(selector);

        Document doc;
        try
        {
            string page = pageBytes.Length == 0 ? DefaultPage : "<!DOCTYPE html>" + Encoding.UTF8.GetString(pageBytes);
            doc = Document.Parse(page, new ParseOptions { Limits = PageLimits });
        }
        catch (HtmlLimitExceededException)
        {
            return;
        }

        Node target = doc.Root.QuerySelector("[id=x], #x, body");
        var state = new InteractionState { Hover = target, Focus = target, FocusVisible = true, Target = target, Active = target };
        var options = new SelectorMatchOptions { State = state };
        List<Node> all = doc.Root.QuerySelectorAll(list, options);
        var expected = new List<Node>();
        foreach (Node e in doc.Root.DescendantElements)
        {
            if (e.Matches(list, new SelectorMatchOptions { Scope = doc.Root, State = state }))
            {
                expected.Add(e);
            }
        }

        if (all.Count != expected.Count)
        {
            throw new InvalidOperationException($"querySelectorAll found {all.Count} but matches says {expected.Count} for {s1}");
        }

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != expected[i])
            {
                throw new InvalidOperationException("result order or identity differs for " + s1);
            }
        }

        Node first = doc.Root.QuerySelector(list, options);
        if ((all.Count == 0) != first.IsNull || (all.Count > 0 && first != all[0]))
        {
            throw new InvalidOperationException("querySelector differs from querySelectorAll for " + s1);
        }

        var generator = new LocatorGenerator(doc);
        foreach (Node e in doc.Root.DescendantElements)
        {
            if (generator.TryGenerate(e, out string? loc) && !generator.Verify(e, loc!))
            {
                throw new InvalidOperationException($"locator {loc} does not find exactly its element");
            }
        }
    }

    private static string DecodeSelector(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        if ((data[0] & 1) == 1)
        {
            ReadOnlySpan<byte> body = data[1..];
            var chars = new char[body.Length / 2];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)(body[2 * i] | (body[(2 * i) + 1] << 8));
            }

            return new string(chars);
        }

        return Encoding.UTF8.GetString(data[1..]);
    }
}
