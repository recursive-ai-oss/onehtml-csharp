using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Locators;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// The rules of one stylesheet, grouped by the durable description saved edits use: the enclosing at-rules and the
/// normalized selector, with the rule's position in sheet order among those that share both.
/// </summary>
internal sealed class RuleCatalog
{
    private readonly Dictionary<string, List<int>> _byKey = [];
    private readonly Dictionary<int, RuleInfo> _info = [];

    internal sealed record RuleInfo(string Selector, string[] AtRules, int Index);

    public RuleCatalog(Stylesheet sheet)
    {
        Sheet = sheet;
        var stack = new Stack<(CssNode Node, string[] At)>();
        var top = new List<CssNode>();
        foreach (CssNode n in sheet.Tree.Nodes)
        {
            top.Add(n);
        }

        for (int i = top.Count - 1; i >= 0; i--)
        {
            stack.Push((top[i], []));
        }

        while (stack.Count > 0)
        {
            (CssNode node, string[] at) = stack.Pop();
            if (!SheetLayout.IsRule(node))
            {
                continue;
            }

            string selector = SelectorOf(node);
            string key = Key(at, selector);
            if (!_byKey.TryGetValue(key, out List<int>? list))
            {
                list = [];
                _byKey[key] = list;
            }

            _info[node.Index] = new RuleInfo(selector, at, list.Count);
            list.Add(node.Index);
            if (node.HasBlock)
            {
                string[] inner = [.. at, selector];
                var kids = new List<CssNode>();
                foreach (CssNode k in node.Children)
                {
                    kids.Add(k);
                }

                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push((kids[i], inner));
                }
            }
        }
    }

    public Stylesheet Sheet { get; }

    public static string Key(string[] at, string selector) =>
        at.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0001" + string.Join('\u0002', at) + "\u0001" + selector;

    public RuleInfo? Describe(CssNode rule) => _info.TryGetValue(rule.Index, out RuleInfo? i) ? i : null;

    /// <summary>The rules with exactly this selector inside exactly these at-rules, in sheet order.</summary>
    public List<CssNode> Candidates(string[] at, string selector)
    {
        var result = new List<CssNode>();
        if (_byKey.TryGetValue(Key(at, selector), out List<int>? list))
        {
            foreach (int i in list)
            {
                RuleInfo info = _info[i];
                if (string.Equals(info.Selector, selector, StringComparison.Ordinal) && info.AtRules.AsSpan().SequenceEqual(at))
                {
                    result.Add(new CssNode(Sheet.Tree, i));
                }
            }
        }

        return result;
    }

    public static string SelectorOf(CssNode rule)
    {
        string prelude = SheetLayout.Norm(rule.PreludeText);
        if (rule.Kind != CssNodeKind.AtRule)
        {
            return prelude;
        }

        string name = rule.Name;
        var sb = new StringBuilder(name.Length + prelude.Length + 2).Append('@');
        foreach (char c in name)
        {
            sb.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        }

        return prelude.Length == 0 ? sb.ToString() : sb.Append(' ').Append(prelude).ToString();
    }

    /// <summary><c>sha256:</c> hash of the rule's own declarations: lower-cased names (custom properties as written), normalized values, importance.</summary>
    public static string DeclarationsHash(CssNode rule)
    {
        var sb = new StringBuilder();
        foreach (CssNode d in rule.Declarations)
        {
            string name = d.Name;
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                var lower = new StringBuilder(name.Length);
                foreach (char c in name)
                {
                    lower.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
                }

                name = lower.ToString();
            }

            sb.Append(name).Append(':').Append(SheetLayout.Norm(d.ValueText)).Append(d.IsImportant ? "!important" : string.Empty).Append(";\n");
        }

        return "sha256:" + Sha256.HashHex(Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
