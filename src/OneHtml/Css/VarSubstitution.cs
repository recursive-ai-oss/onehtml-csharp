using System;
using System.Collections.Generic;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The CSS-wide keywords every property accepts (CSS Cascade 5 §7.3 and §7.4, CSS Values 4 §3.1).</summary>
public enum CssWideKeyword : byte
{
    /// <summary>Not a CSS-wide keyword.</summary>
    None,

    /// <summary><c>initial</c>.</summary>
    Initial,

    /// <summary><c>inherit</c>.</summary>
    Inherit,

    /// <summary><c>unset</c>.</summary>
    Unset,

    /// <summary><c>revert</c>.</summary>
    Revert,

    /// <summary><c>revert-layer</c>.</summary>
    RevertLayer,

    /// <summary><c>revert-rule</c>.</summary>
    RevertRule,
}

/// <summary>Recognizes the CSS-wide keywords.</summary>
public static class CssWideKeywords
{
    /// <summary>
    /// Returns the keyword when <paramref name="value"/> is exactly one CSS-wide keyword (ASCII case-insensitive,
    /// surrounding whitespace ignored), otherwise <see cref="CssWideKeyword.None"/>.
    /// </summary>
    /// <param name="value">A declaration's value.</param>
    public static CssWideKeyword Detect(ComponentValueList value)
    {
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To || r.Kind(r.From) != CssTokenKind.Ident)
        {
            return CssWideKeyword.None;
        }

        return FromName(r.Name(r.From, out _));
    }

    /// <summary>Maps a keyword name to the enum; <see cref="CssWideKeyword.None"/> when it is not one.</summary>
    /// <param name="name">An identifier.</param>
    public static CssWideKeyword FromName(ReadOnlySpan<char> name)
    {
        if (name.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            return CssWideKeyword.Initial;
        }

        if (name.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            return CssWideKeyword.Inherit;
        }

        if (name.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            return CssWideKeyword.Unset;
        }

        if (name.Equals("revert", StringComparison.OrdinalIgnoreCase))
        {
            return CssWideKeyword.Revert;
        }

        if (name.Equals("revert-layer", StringComparison.OrdinalIgnoreCase))
        {
            return CssWideKeyword.RevertLayer;
        }

        return name.Equals("revert-rule", StringComparison.OrdinalIgnoreCase) ? CssWideKeyword.RevertRule : CssWideKeyword.None;
    }

    /// <summary>The keyword as CSS text.</summary>
    /// <param name="keyword">A keyword.</param>
    public static string ToCss(CssWideKeyword keyword) => keyword switch
    {
        CssWideKeyword.Initial => "initial",
        CssWideKeyword.Inherit => "inherit",
        CssWideKeyword.Unset => "unset",
        CssWideKeyword.Revert => "revert",
        CssWideKeyword.RevertLayer => "revert-layer",
        CssWideKeyword.RevertRule => "revert-rule",
        _ => string.Empty,
    };
}

/// <summary>Own values first; a name that is specified here but not valid is absent; any other name reads the parent's.</summary>
internal sealed class ScopedLookup(Dictionary<string, string> own, IReadOnlyDictionary<string, ComponentValueList> specified, IReadOnlyDictionary<string, string> inherited) : IReadOnlyDictionary<string, string>
{
    public string this[string key] => TryGetValue(key, out string? v) ? v : throw new KeyNotFoundException(key);

    public IEnumerable<string> Keys => own.Keys;

    public IEnumerable<string> Values => own.Values;

    public int Count => own.Count;

    public bool ContainsKey(string key) => TryGetValue(key, out _);

    public bool TryGetValue(string key, out string value)
    {
        if (own.TryGetValue(key, out string? v))
        {
            value = v;
            return true;
        }

        if (specified.ContainsKey(key))
        {
            value = string.Empty;
            return false;
        }

        return inherited.TryGetValue(key, out value!);
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => own.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Bounds for <c>var()</c> substitution, checked before the work they guard.</summary>
public sealed class VarLimits
{
    /// <summary>The defaults: 1,048,576 characters per substituted value, 2,097,152 token visits per value, 16,777,216 characters per <see cref="VarSubstitution.ResolveCustomProperties(IReadOnlyDictionary{string, ComponentValueList}, VarLimits?)"/> call.</summary>
    public static VarLimits Default { get; } = new();

    /// <summary>The longest text one substituted value may have. CSS Variables 1 §3 lets implementations bound the expansion; exceeding it makes the value invalid at computed-value time.</summary>
    public int MaxOutputLength { get; init; } = 1 << 20;

    /// <summary>The most tokens one substitution may visit, counting every token of every expanded fallback. This bounds work even when the expansion is empty.</summary>
    public int MaxSteps { get; init; } = 1 << 21;

    /// <summary>The most characters all custom properties of one <see cref="VarSubstitution.ResolveCustomProperties(IReadOnlyDictionary{string, ComponentValueList}, VarLimits?)"/> call may produce together.</summary>
    public int MaxTotalLength { get; init; } = 1 << 24;
}

/// <summary>Why a substitution produced no value.</summary>
public enum SubstitutionStatus : byte
{
    /// <summary>The value was substituted; <see cref="SubstitutionResult.Text"/> holds it.</summary>
    Ok,

    /// <summary>A <c>var()</c> referred to a missing, cyclic or invalid custom property and had no fallback: the declaration is "invalid at computed-value time" (CSS Variables 1 §3.2).</summary>
    InvalidAtComputedValueTime,

    /// <summary>A <c>var()</c> function was malformed (no custom property name).</summary>
    Syntax,

    /// <summary>A limit of <see cref="VarLimits"/> was reached.</summary>
    LimitExceeded,
}

/// <summary>The outcome of substituting <c>var()</c> into a value.</summary>
public readonly struct SubstitutionResult
{
    internal SubstitutionResult(SubstitutionStatus status, string text, CssTree? tree)
    {
        Status = status;
        Text = text;
        Tree = tree;
    }

    /// <summary>Whether and why substitution produced a value.</summary>
    public SubstitutionStatus Status { get; }

    /// <summary>True when <see cref="Status"/> is <see cref="SubstitutionStatus.Ok"/>.</summary>
    public bool IsValid => Status == SubstitutionStatus.Ok;

    /// <summary>The substituted value as CSS text; empty when not valid.</summary>
    public string Text { get; }

    internal CssTree? Tree { get; }

    /// <summary>The substituted value as component values (empty when not valid). Typed parsers read it like any other value.</summary>
    public ComponentValueList Value => Tree is null ? default : Tree.Values.Trimmed();
}

/// <summary>
/// <c>var()</c> substitution (CSS Variables Module Level 1 §3, Level 2): replaces each <c>var(--name, fallback)</c>
/// by the custom property's value or the fallback, detects reference cycles, and enforces <see cref="VarLimits"/>.
/// Iterative throughout; nothing recurses on input depth.
/// </summary>
public static class VarSubstitution
{
    /// <summary>True when the value contains a <c>var()</c> function at any depth.</summary>
    /// <param name="value">A declaration's value.</param>
    public static bool ContainsVar(ComponentValueList value)
    {
        TokenRange r = TokenRange.Of(value);
        for (int i = r.From; i < r.To; i++)
        {
            if (r.IsFunction(i, "var"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when every <c>var()</c> in the value, at any depth, is well formed at parse time (CSS Variables 2 <c>var()</c>
    /// grammar). The first argument is a free-form production: one or more tokens, or a single <c>{}</c> block with
    /// content, and a <c>{}</c> block may not be mixed with other tokens there. Whether the argument names a custom
    /// property is only checked when it is substituted, so <c>var(--x ())</c> is valid here. What follows the first
    /// top-level comma is the fallback, which may be empty. A value without <c>var()</c> is valid.
    /// </summary>
    /// <param name="value">A declaration's value.</param>
    public static bool IsValidSyntax(ComponentValueList value)
    {
        TokenRange r = TokenRange.Of(value);
        for (int i = r.From; i < r.To; i++)
        {
            if (r.IsFunction(i, "var") && !IsValidVarFunction(r.Inner(i)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidVarFunction(TokenRange args)
    {
        // Split the name argument from the fallback at the first top-level comma.
        int i = args.SkipWs(args.From);
        int nameStart = i;
        int tokens = 0;
        bool block = false;
        bool bareToken = false;
        while (i < args.To && args.Kind(i) != CssTokenKind.Comma)
        {
            CssTokenKind k = args.Kind(i);
            if (k is CssTokenKind.BadString or CssTokenKind.BadUrl or CssTokenKind.Semicolon)
            {
                return false;
            }

            if (k != CssTokenKind.Whitespace)
            {
                tokens++;
                if (k == CssTokenKind.LeftBrace)
                {
                    block = true;
                    if (i != nameStart || args.Close(i) <= i + 1 || args.SkipWs(i + 1) >= args.Close(i))
                    {
                        return false;
                    }
                }
                else
                {
                    bareToken = true;
                }
            }

            i = args.Next(i);
        }

        if (tokens == 0 || (block && bareToken))
        {
            return false;
        }

        for (int j = i; j < args.To; j++)
        {
            if (args.Kind(j) is CssTokenKind.BadString or CssTokenKind.BadUrl)
            {
                return false;
            }
        }

        // The fallback is a <declaration-value>: no top-level "!" or ";" (CSS Syntax 3 §5.4.7).
        for (int j = i < args.To ? args.Next(i) : i; j < args.To; j = args.Next(j))
        {
            if (args.Kind(j) == CssTokenKind.Semicolon || args.IsDelim(j, '!'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the computed values of custom properties from their specified values: <c>var()</c> inside them is
    /// substituted, and every property that takes part in a reference cycle, or depends on an invalid property
    /// without a fallback, is left out (it is "guaranteed-invalid", CSS Variables 1 §2.1 and §3.2).
    /// </summary>
    /// <param name="specified">Specified values by name (including the leading <c>--</c>; names are case-sensitive).</param>
    /// <param name="limits">Bounds; <see cref="VarLimits.Default"/> when null.</param>
    /// <returns>The valid computed values as text, by name.</returns>
    public static Dictionary<string, string> ResolveCustomProperties(IReadOnlyDictionary<string, ComponentValueList> specified, VarLimits? limits = null) =>
        ResolveCustomProperties(specified, null, limits);

    /// <summary>
    /// As <see cref="ResolveCustomProperties(IReadOnlyDictionary{string, ComponentValueList}, VarLimits?)"/>, for an element
    /// whose parent already has computed custom properties: a <c>var()</c> naming a property that is not in
    /// <paramref name="specified"/> reads <paramref name="inherited"/> (custom properties inherit, CSS Variables 1 §2).
    /// The result holds only the properties of <paramref name="specified"/>; one that is cyclic or invalid is absent and
    /// does not fall back to the inherited value.
    /// </summary>
    /// <param name="specified">Specified values by name (including the leading <c>--</c>; names are case-sensitive).</param>
    /// <param name="inherited">The parent's computed custom properties, or null.</param>
    /// <param name="limits">Bounds; <see cref="VarLimits.Default"/> when null.</param>
    public static Dictionary<string, string> ResolveCustomProperties(IReadOnlyDictionary<string, ComponentValueList> specified, IReadOnlyDictionary<string, string>? inherited, VarLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(specified);
        limits ??= VarLimits.Default;
        var names = new List<string>(specified.Keys);
        names.Sort(StringComparer.Ordinal);
        int n = names.Count;
        var index = new Dictionary<string, int>(n, StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            index[names[i]] = i;
        }

        // Edges: custom properties referenced by var() anywhere in the value, fallbacks included.
        var edges = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            edges[i] = [];
            TokenRange r = TokenRange.Of(specified[names[i]]);
            for (int k = r.From; k < r.To; k++)
            {
                if (r.IsFunction(k, "var") && TryReferenceName(r, k, out string? target, out _, out _) && index.TryGetValue(target!, out int t))
                {
                    edges[i].Add(t);
                }
            }
        }

        bool[] inCycle = FindCycles(edges);

        // Resolve in dependency order (post-order of an iterative DFS), skipping cyclic properties.
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> lookup = inherited is null ? resolved : new ScopedLookup(resolved, specified, inherited);
        int[] state = new int[n]; // 0 new, 1 on stack, 2 done
        var stack = new Stack<(int Node, int Next)>();
        int total = 0;
        for (int root = 0; root < n; root++)
        {
            if (state[root] != 0)
            {
                continue;
            }

            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                (int node, int next) = stack.Pop();
                if (next < edges[node].Count)
                {
                    stack.Push((node, next + 1));
                    int t = edges[node][next];
                    if (state[t] == 0)
                    {
                        state[t] = 1;
                        stack.Push((t, 0));
                    }

                    continue;
                }

                state[node] = 2;
                if (inCycle[node] || total > limits.MaxTotalLength)
                {
                    continue;
                }

                SubstitutionResult res = Substitute(specified[names[node]], lookup, limits);
                if (res.IsValid)
                {
                    total += res.Text.Length;
                    resolved[names[node]] = res.Text;
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Substitutes every <c>var()</c> in <paramref name="value"/> using already-computed custom property values (see
    /// <see cref="ResolveCustomProperties(IReadOnlyDictionary{string, ComponentValueList}, VarLimits?)"/>). A <c>var()</c> whose property is missing uses its fallback, or makes
    /// the result <see cref="SubstitutionStatus.InvalidAtComputedValueTime"/>.
    /// </summary>
    /// <param name="value">A declaration's value (any property, or a custom property).</param>
    /// <param name="customProperties">Computed custom property values by name; absent means guaranteed-invalid.</param>
    /// <param name="limits">Bounds; <see cref="VarLimits.Default"/> when null.</param>
    public static SubstitutionResult Substitute(ComponentValueList value, IReadOnlyDictionary<string, string> customProperties, VarLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(customProperties);
        limits ??= VarLimits.Default;
        TokenRange top = TokenRange.Of(value);
        if (!ContainsVar(value))
        {
            string raw = top.RawText();
            return new SubstitutionResult(SubstitutionStatus.Ok, raw, null);
        }

        var sb = new Emitter(limits);
        var frames = new Stack<Frame>();
        frames.Push(new Frame(top, top.From, top.To, top.IsEmpty ? 0 : top.Tree.Tokens[top.From].Start));
        long steps = 0;
        while (frames.Count > 0)
        {
            Frame f = frames.Pop();
            TokenRange r = f.Range;
            int copyFrom = f.CopyFrom;
            int i = f.Pos;
            bool pushed = false;
            while (i < f.End)
            {
                if (++steps > limits.MaxSteps)
                {
                    return Fail(SubstitutionStatus.LimitExceeded);
                }

                if (!r.IsFunction(i, "var"))
                {
                    i++;
                    continue;
                }

                int close = r.Close(i);
                int segEnd = r.Tree.Tokens[i].Start;
                if (!sb.Text(r.Tree.Text, copyFrom, segEnd))
                {
                    return Fail(SubstitutionStatus.LimitExceeded);
                }

                if (!TryReferenceName(r, i, out string? name, out int fbFrom, out int fbTo))
                {
                    return Fail(SubstitutionStatus.Syntax);
                }

                int after = close < f.End ? close + 1 : f.End;
                int afterText = close < f.End ? r.Tree.Tokens[close].End : (f.End > f.Range.From ? r.Tree.Tokens[f.End - 1].End : 0);
                if (customProperties.TryGetValue(name!, out string? text))
                {
                    if (!sb.Substituted(text))
                    {
                        return Fail(SubstitutionStatus.LimitExceeded);
                    }

                    copyFrom = afterText;
                    i = after;
                    continue;
                }

                if (fbFrom < 0)
                {
                    return Fail(SubstitutionStatus.InvalidAtComputedValueTime);
                }

                // Continue after the var() once the fallback has been expanded.
                frames.Push(new Frame(r, after, f.End, afterText));
                int fbText = fbFrom < fbTo ? r.Tree.Tokens[fbFrom].Start : afterText;
                frames.Push(new Frame(r, fbFrom, fbTo, fbText));
                pushed = true;
                break;
            }

            if (pushed)
            {
                continue;
            }

            int endText = f.End > r.From && f.End <= r.Tree.TokenCount ? r.Tree.Tokens[f.End - 1].End : copyFrom;
            if (!sb.Text(r.Tree.Text, copyFrom, endText))
            {
                return Fail(SubstitutionStatus.LimitExceeded);
            }
        }

        string result = sb.ToString();
        CssTree tree;
        try
        {
            tree = CssTree.ParseComponentValueList(result, new CssParseOptions { MaxInputLength = Math.Max(limits.MaxOutputLength, 1), MaxTokens = limits.MaxOutputLength + 1 });
        }
        catch (CssLimitExceededException)
        {
            return Fail(SubstitutionStatus.LimitExceeded);
        }

        return new SubstitutionResult(SubstitutionStatus.Ok, result.Trim(' ', '\t', '\n', '\r', '\f'), tree);

        static SubstitutionResult Fail(SubstitutionStatus s) => new(s, string.Empty, null);
    }

    private readonly record struct Frame(TokenRange Range, int Pos, int End, int CopyFrom);

    // Collects the output, enforcing the length limit before each append.
    private sealed class Emitter(VarLimits limits)
    {
        private readonly StringBuilder _sb = new();
        private bool _afterSubstitution;

        public bool Text(string text, int from, int to)
        {
            if (to <= from)
            {
                return true;
            }

            if ((long)_sb.Length + (to - from) + 4 > limits.MaxOutputLength)
            {
                return false;
            }

            // Substitution happens between tokens (CSS Variables 1 §3.1): an empty comment keeps a substituted value from fusing with the text after it.
            if (_afterSubstitution && _sb.Length > 0 && IsNameChar(_sb[^1]) && IsNameChar(text[from]))
            {
                _sb.Append("/**/");
            }

            _afterSubstitution = false;
            _sb.Append(text, from, to - from);
            return true;
        }

        public bool Substituted(string text)
        {
            if ((long)_sb.Length + text.Length + 4 > limits.MaxOutputLength)
            {
                return false;
            }

            if (_sb.Length > 0 && text.Length > 0 && IsNameChar(_sb[^1]) && IsNameChar(text[0]))
            {
                _sb.Append("/**/");
            }

            _sb.Append(text);
            _afterSubstitution = text.Length > 0;
            return true;
        }

        public override string ToString() => _sb.ToString();
    }

    private static bool IsNameChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or '%' or '#' or '\\' or >= '\u0080';

    // var( --name [, fallback] ): name token and the token range of the fallback (-1 when there is no comma).
    private static bool TryReferenceName(TokenRange r, int fn, out string? name, out int fallbackFrom, out int fallbackTo)
    {
        name = null;
        fallbackFrom = -1;
        fallbackTo = -1;
        int close = r.Close(fn);
        int j = r.SkipWs(fn + 1);
        if (j >= close || r.Kind(j) != CssTokenKind.Ident)
        {
            return false;
        }

        string id = r.IdentString(j);
        if (!id.StartsWith("--", StringComparison.Ordinal) || id.Length < 3)
        {
            return false;
        }

        name = id;
        j = r.SkipWs(j + 1);
        if (j >= close)
        {
            return true;
        }

        if (r.Kind(j) != CssTokenKind.Comma)
        {
            return false;
        }

        fallbackFrom = Math.Min(r.SkipWs(j + 1), close);
        fallbackTo = close;
        return true;
    }

    // Iterative Tarjan strongly connected components; a node is cyclic when its component has more than one node or it refers to itself.
    private static bool[] FindCycles(List<int>[] edges)
    {
        int n = edges.Length;
        bool[] cyclic = new bool[n];
        int[] low = new int[n];
        int[] num = new int[n];
        bool[] onStack = new bool[n];
        int counter = 0;
        var tarjan = new Stack<int>();
        var work = new Stack<(int Node, int Edge)>();
        for (int s = 0; s < n; s++)
        {
            if (num[s] != 0)
            {
                continue;
            }

            work.Push((s, 0));
            while (work.Count > 0)
            {
                (int v, int e) = work.Pop();
                if (e == 0)
                {
                    num[v] = low[v] = ++counter;
                    tarjan.Push(v);
                    onStack[v] = true;
                }

                if (e < edges[v].Count)
                {
                    work.Push((v, e + 1));
                    int w = edges[v][e];
                    if (w == v)
                    {
                        cyclic[v] = true;
                    }

                    if (num[w] == 0)
                    {
                        work.Push((w, 0));
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], num[w]);
                    }

                    continue;
                }

                if (low[v] == num[v])
                {
                    int size = 0;
                    var members = new List<int>();
                    int w;
                    do
                    {
                        w = tarjan.Pop();
                        onStack[w] = false;
                        members.Add(w);
                        size++;
                    }
                    while (w != v);
                    if (size > 1)
                    {
                        foreach (int m in members)
                        {
                            cyclic[m] = true;
                        }
                    }
                }

                if (work.Count > 0)
                {
                    (int parent, _) = work.Peek();
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }

        return cyclic;
    }
}
