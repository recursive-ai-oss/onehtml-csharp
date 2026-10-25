using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz target for the CSS parser: parse the input through every entry point, require that the tokens and comments
/// rebuild the input exactly and that node ranges are ordered and nested, and serialize. Only the typed limit
/// exception may escape the library; anything else is a finding. The first input byte picks the text encoding
/// (odd: UTF-16, so lone surrogates are reachable; even: UTF-8).
/// </summary>
internal static class CssTarget
{
    private static readonly CssParseOptions Options = new() { MaxInputLength = 1 << 20, MaxTokens = 1 << 20, MaxNestingDepth = 64 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        string text = Decode(data);
        try
        {
            Check(CssTree.ParseStylesheet(text, Options));
            Check(CssTree.ParseDeclarationList(text, Options));
            Check(CssTree.ParseBlockContents(text, Options));
            Check(CssTree.ParseRule(text, Options));
            Check(CssTree.ParseDeclaration(text, Options));
            Check(CssTree.ParseComponentValue(text, Options));
            Check(CssTree.ParseComponentValueList(text, Options));
            Check(CssTree.ParseCommaSeparatedList(text, Options));
        }
        catch (CssLimitExceededException)
        {
        }
    }

    private static string Decode(ReadOnlySpan<byte> data)
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

    private static void Check(CssTree tree)
    {
        if (CssSourceWriter.Write(tree) != tree.Text)
        {
            throw new InvalidOperationException("source round trip differs");
        }

        var stack = new Stack<(CssNode Parent, int Start, int End)>();
        stack.Push((tree.Root, 0, tree.Text.Length));
        while (stack.Count > 0)
        {
            (CssNode parent, int start, int end) = stack.Pop();
            int last = start;
            foreach (CssNode n in parent.Children)
            {
                CssRange r = n.Range;
                if (r.Start < last || r.End < r.Start || r.End > end)
                {
                    throw new InvalidOperationException("node ranges are not ordered and nested");
                }

                last = r.End;
                _ = n.Name;
                _ = n.PreludeText.Length + n.ValueText.Length;
                _ = n.AtRuleKind;
                foreach (ComponentValue v in n.Prelude)
                {
                    _ = v.Range;
                    _ = v.Token.Value;
                }

                if (n.HasBlock)
                {
                    stack.Push((n, n.ContentRange.Start, n.ContentRange.End));
                }
            }
        }

        _ = CssSerializer.Serialize(tree);
    }
}
