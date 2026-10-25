using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>The registry of fuzz targets, keyed by name. A target must not throw for any input.</summary>
internal static class Targets
{
    public static readonly SortedDictionary<string, Action<ReadOnlySpan<byte>>> All = new(StringComparer.Ordinal)
    {
        ["css"] = CssTarget.Run,
        ["noop"] = static _ => { },
        ["tokenize"] = Tokenize,
        ["treebuilder"] = TreeBuilder,
        ["values"] = ValueTarget.Run,
    };

    // Small limits so hostile inputs reach the limit paths quickly.
    private static readonly ParseLimits s_limits = new() { MaxInputLength = 1 << 20, MaxAttributesPerTag = 64, MaxOffsetMapRuns = 1 << 16, MaxNodes = 1 << 16, MaxOpenElements = 256, MaxActiveFormattingElements = 64 };

    /// <summary>
    /// Sniff, decode and tokenize. The first byte picks the options: bit 0 records errors, bit 1 forces an encoding
    /// chosen by the next byte, bit 2 sets foreign content, bits 3-5 pick the initial tokenizer state.
    /// </summary>
    private static void Tokenize(ReadOnlySpan<byte> input)
    {
        if (input.Length < 2)
        {
            return;
        }

        int flags = input[0];
        int pick = input[1];
        byte[] bytes = input[2..].ToArray();

        WhatwgEncoding? forced = null;
        if ((flags & 2) != 0)
        {
            IReadOnlyList<WhatwgEncoding> all = WhatwgEncoding.All;
            forced = all[pick % all.Count];
        }

        try
        {
            SourceText text = SourceText.Decode(bytes, new DecodeOptions { OverrideEncoding = forced, Limits = s_limits });
            Check(text.Map.ToByteOffset(0) >= 0, "map start");
            Check(text.Map.ToByteOffset(text.Text.Length) <= bytes.Length, "map end");

            var tokenizer = new HtmlTokenizer(text, new HtmlTokenizerOptions { RecordErrors = (flags & 1) != 0, Limits = s_limits });
            tokenizer.InForeignContent = (flags & 4) != 0;
            tokenizer.SetState((TokenizerState)(((flags >> 3) & 7) % 6));
            int last = 0;
            HtmlTokenKind kind;
            while ((kind = tokenizer.Next()) != HtmlTokenKind.EndOfFile)
            {
                Check(tokenizer.Start >= last && tokenizer.End >= tokenizer.Start && tokenizer.End <= text.Text.Length, "token range");
                last = tokenizer.Start;
                _ = tokenizer.Data.Length + tokenizer.Name.Length;
                if (kind == HtmlTokenKind.StartTag)
                {
                    AttributeViewList list = tokenizer.Attributes;
                    for (int i = 0; i < list.Count; i++)
                    {
                        AttributeView a = list[i];
                        Check(a.Start <= a.NameStart && a.NameEnd <= a.End && a.End <= text.Text.Length, "attribute range");
                        _ = a.Value.Length;
                    }
                }

                _ = text.ToByteOffset(tokenizer.End);
                if (kind == HtmlTokenKind.StartTag && tokenizer.Name.SequenceEqual("textarea"))
                {
                    tokenizer.SetState(TokenizerState.Rcdata);
                }
            }

            // Re-decode as a later <meta> would ask for.
            _ = text.ChangeEncoding(WhatwgEncoding.All[pick % WhatwgEncoding.All.Count]);
        }
        catch (HtmlLimitExceededException)
        {
            // A configured limit is the designed failure mode.
        }
    }

    private static readonly FragmentContext[] s_contexts =
    [
        new(ElementNamespace.Html, "div"), new(ElementNamespace.Html, "td"), new(ElementNamespace.Html, "tr"),
        new(ElementNamespace.Html, "table"), new(ElementNamespace.Html, "select"), new(ElementNamespace.Html, "template"),
        new(ElementNamespace.Html, "title"), new(ElementNamespace.Html, "textarea"), new(ElementNamespace.Html, "script"),
        new(ElementNamespace.Html, "plaintext"), new(ElementNamespace.Html, "html"), new(ElementNamespace.Html, "head"),
        new(ElementNamespace.Html, "caption"), new(ElementNamespace.Html, "colgroup"), new(ElementNamespace.Html, "frameset"),
        new(ElementNamespace.Html, "noscript"), new(ElementNamespace.Html, "div", insideForm: true),
        new(ElementNamespace.Svg, "svg"), new(ElementNamespace.Svg, "foreignObject"), new(ElementNamespace.MathMl, "math"),
        new(ElementNamespace.MathMl, "annotation-xml", isHtmlIntegrationPoint: true),
    ];

    /// <summary>
    /// Parse as a document or as a fragment and check the tree. The first byte picks the options: bit 0 scripting,
    /// bit 1 record errors, bit 2 fragment, bits 3-7 the fragment context. Checks: structure and source ranges
    /// (<see cref="RecursiveAI.OneHtml.Tests.DocumentInvariants"/>), the lossless writer returns the input bytes, and
    /// serializing, re-parsing and serializing again is stable where the standard says it is.
    /// </summary>
    private static void TreeBuilder(ReadOnlySpan<byte> input)
    {
        if (input.Length < 1)
        {
            return;
        }

        int flags = input[0];
        byte[] bytes = input[1..].ToArray();
        var options = new ParseOptions { ScriptingEnabled = (flags & 1) != 0, RecordErrors = (flags & 2) != 0, Limits = s_limits };
        try
        {
            if ((flags & 4) != 0)
            {
                FragmentContext context = s_contexts[(flags >> 3) % s_contexts.Length];
                string html = SourceText.Decode(bytes, new DecodeOptions { Limits = s_limits }).Text;
                Document fragment = Document.ParseFragment(html, context, options);
                Verify(fragment);
                _ = fragment.SerializeChildren(fragment.Root, options.ScriptingEnabled);
                return;
            }

            Document doc = Document.Parse(bytes, options);
            Verify(doc);
            Check(doc.ToLosslessBytes().AsSpan().SequenceEqual(bytes), "lossless round trip");
            if (RecursiveAI.OneHtml.Tests.DocumentInvariants.ExpectsStableRoundTrip(doc))
            {
                string first = doc.SerializeChildren(doc.Root, options.ScriptingEnabled);
                Document again = Document.Parse(first, options);
                Check(again.SerializeChildren(again.Root, options.ScriptingEnabled) == first, "serialize, parse, serialize");
            }
        }
        catch (HtmlLimitExceededException)
        {
            // A configured limit is the designed failure mode.
        }
    }

    private static void Verify(Document doc)
    {
        string? problem = RecursiveAI.OneHtml.Tests.DocumentInvariants.Check(doc);
        Check(problem is null, problem ?? string.Empty);
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("invariant broken: " + what);
        }
    }
}
