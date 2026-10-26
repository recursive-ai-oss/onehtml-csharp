using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>A media query list (Media Queries 5 §2.3): parsed once, evaluated against any <see cref="MediaFeatures"/>.</summary>
public sealed class MediaQueryList
{
    internal MediaQueryList(List<MediaQuery> queries) => Queries = queries;

    /// <summary>The queries in order; a query that failed to parse is present as <c>not all</c> (§2.3 error handling).</summary>
    public IReadOnlyList<MediaQuery> Queries { get; }

    /// <summary>True for the empty list (or only whitespace), which matches everything.</summary>
    public bool MatchesEverything => Queries.Count == 0;

    /// <summary>Parses a media query list from text (for example the <c>media</c> attribute or the prelude of <c>@media</c>). Never throws; errors become <c>not all</c> queries.</summary>
    /// <param name="text">The text.</param>
    public static MediaQueryList Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(CssTree.ParseComponentValueList(text).Values);
    }

    /// <summary>Parses a media query list from component values.</summary>
    /// <param name="values">The prelude's values.</param>
    public static MediaQueryList Parse(ComponentValueList values)
    {
        TokenRange all = TokenRange.Of(values).Trim();
        var queries = new List<MediaQuery>();
        if (all.IsEmpty)
        {
            return new MediaQueryList(queries);
        }

        int start = all.From;
        int i = all.From;
        while (true)
        {
            if (i >= all.To || all.Kind(i) == CssTokenKind.Comma)
            {
                queries.Add(MediaQueryParser.ParseQuery(all.Sub(start, i).Trim()));
                if (i >= all.To)
                {
                    break;
                }

                start = i + 1;
                i++;
                continue;
            }

            i = all.Next(i);
        }

        return new MediaQueryList(queries);
    }

    /// <summary>True when the list matches: it is empty, or at least one query is true.</summary>
    /// <param name="features">The environment.</param>
    public bool Matches(MediaFeatures features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (Queries.Count == 0)
        {
            return true;
        }

        foreach (MediaQuery q in Queries)
        {
            if (q.Matches(features))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the list matches under the media features of <paramref name="context"/>.</summary>
    /// <param name="context">The style context.</param>
    public bool Matches(StyleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Matches(context.Media);
    }

    /// <summary>The list in canonical form (CSSOM "serialize a media query list"): queries joined by <c>, </c>, features lower case, <c>not all</c> for errors.</summary>
    public string ToCss() => string.Join(", ", Queries);

    /// <inheritdoc />
    public override string ToString() => ToCss();
}

/// <summary>One media query.</summary>
public sealed class MediaQuery
{
    internal MediaQuery(bool valid, bool negated, bool only, string? type, MqNode? condition, string text)
    {
        IsValid = valid;
        IsNegated = negated;
        IsOnly = only;
        MediaTypeName = type;
        Condition = condition;
        Text = text;
    }

    internal MqNode? Condition { get; }

    /// <summary>False when the text did not parse; such a query is serialized and evaluated as <c>not all</c>.</summary>
    public bool IsValid { get; }

    /// <summary>True when written with a leading <c>not</c> before a media type.</summary>
    public bool IsNegated { get; }

    /// <summary>True when written with <c>only</c>.</summary>
    public bool IsOnly { get; }

    /// <summary>The media type, lower case (<c>all</c>, <c>screen</c>, <c>print</c> or an unknown type), or null for a bare condition.</summary>
    public string? MediaTypeName { get; }

    /// <summary>The canonical text.</summary>
    public string Text { get; }

    /// <summary>True when the query is true in <paramref name="features"/>. An unknown feature or general-enclosed term makes a condition unknown, which counts as false (§3.2).</summary>
    /// <param name="features">The environment.</param>
    public bool Matches(MediaFeatures features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (!IsValid)
        {
            return false;
        }

        bool typeOk = true;
        if (MediaTypeName is not null)
        {
            typeOk = MediaTypeName switch
            {
                "all" => true,
                "screen" => features.Type == MediaType.Screen,
                "print" => features.Type == MediaType.Print,
                _ => false,
            };
        }

        // Media Queries 5 §3.1: `not` negates the query's result, and the negation of unknown is unknown, which counts
        // as false. So `not all and (unknown-feature)` does not match.
        Tri result = !typeOk ? Tri.False : Condition is null ? Tri.True : MediaQueryParser.Evaluate(Condition, features);
        if (IsNegated)
        {
            result = result switch { Tri.True => Tri.False, Tri.False => Tri.True, _ => Tri.Unknown };
        }

        return result == Tri.True;
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}

internal enum Tri : byte
{
    False,
    True,
    Unknown,
}

internal enum MqKind : byte
{
    Feature,
    Not,
    And,
    Or,
    General,
}

internal sealed class MqNode
{
    public MqKind Kind;
    public bool InParens;
    public string Text = string.Empty;       // General: the source; Feature: the canonical text
    public MqFeature? Feature;
    public Func<bool>? Leaf;                 // a leaf evaluated by a callback (@supports)
    public List<MqNode> Children = [];
}

internal enum MqValueKind : byte
{
    Length,
    Number,
    Integer,
    Ratio,
    Resolution,
    Keyword,
}

internal sealed class MqFeature
{
    public string Name = string.Empty;
    public MqDescriptor Descriptor = null!;
    public bool IsBoolean;
    public List<(string Op, double Value, string? Word)> Comparisons = [];
    public double PlainValue;
    public string? PlainWord;
    public int PrefixKind;        // 0 none, -1 min, +1 max
    public bool IsPlain;
}

internal sealed class MqDescriptor
{
    public MqValueKind Kind;
    public bool Range;
    public double Min = double.NegativeInfinity;   // <mq-boolean> features take only 0 or 1
    public double Max = double.PositiveInfinity;
    public string[] Keywords = [];
    public Func<MediaFeatures, double> Number = _ => 0;
    public Func<MediaFeatures, string> Word = _ => string.Empty;
}

internal static class MediaQueryParser
{
    private static readonly FrozenDictionary<string, MqDescriptor> Features = BuildFeatures();

    private static readonly FrozenSet<string> ReservedTypeWords = ValueKinds.Set("not", "and", "or", "only", "layer");

    private static MqDescriptor Num(MqValueKind k, bool range, Func<MediaFeatures, double> f) => new() { Kind = k, Range = range, Number = f };

    private static MqDescriptor Kw(Func<MediaFeatures, string> f, params string[] words) => new() { Kind = MqValueKind.Keyword, Keywords = words, Word = f };

    private static FrozenDictionary<string, MqDescriptor> BuildFeatures()
    {
        var d = new Dictionary<string, MqDescriptor>(StringComparer.Ordinal)
        {
            ["width"] = Num(MqValueKind.Length, true, f => f.Width),
            ["height"] = Num(MqValueKind.Length, true, f => f.Height),
            ["device-width"] = Num(MqValueKind.Length, true, f => f.Width),
            ["device-height"] = Num(MqValueKind.Length, true, f => f.Height),
            ["aspect-ratio"] = Num(MqValueKind.Ratio, true, f => f.Width / f.Height),
            ["device-aspect-ratio"] = Num(MqValueKind.Ratio, true, f => f.Width / f.Height),
            ["resolution"] = Num(MqValueKind.Resolution, true, f => f.DevicePixelRatio),
            ["-webkit-device-pixel-ratio"] = Num(MqValueKind.Number, true, f => f.DevicePixelRatio),
            ["color"] = Num(MqValueKind.Integer, true, f => f.ColorBits),
            ["color-index"] = Num(MqValueKind.Integer, true, _ => 0),
            ["monochrome"] = Num(MqValueKind.Integer, true, f => f.ColorBits == 0 ? 8 : 0),
            ["grid"] = new() { Kind = MqValueKind.Integer, Range = false, Number = _ => 0, Min = 0, Max = 1 },
            ["orientation"] = Kw(f => f.Height >= f.Width ? "portrait" : "landscape", "portrait", "landscape"),
            ["hover"] = Kw(f => f.Hover == HoverCapability.Hover ? "hover" : "none", "none", "hover"),
            ["any-hover"] = Kw(f => f.AnyHover == HoverCapability.Hover ? "hover" : "none", "none", "hover"),
            ["pointer"] = Kw(f => f.PrimaryPointer.ToString().AsciiLower(), "none", "coarse", "fine"),
            ["any-pointer"] = Kw(f => f.AnyPointer.ToString().AsciiLower(), "none", "coarse", "fine"),
            ["update"] = Kw(f => f.Type == MediaType.Print ? "none" : "fast", "none", "slow", "fast"),
            ["overflow-block"] = Kw(f => f.Type == MediaType.Print ? "paged" : "scroll", "none", "scroll", "paged"),
            ["overflow-inline"] = Kw(_ => "scroll", "none", "scroll"),
            ["scan"] = Kw(_ => "progressive", "interlace", "progressive"),
            ["color-gamut"] = Kw(_ => "srgb", "srgb", "p3", "rec2020"),
            ["prefers-color-scheme"] = Kw(f => f.ColorScheme == PrefersColorScheme.Dark ? "dark" : "light", "light", "dark"),
            ["prefers-reduced-motion"] = Kw(f => f.ReducedMotion == PrefersReduced.Reduce ? "reduce" : "no-preference", "no-preference", "reduce"),
            ["prefers-reduced-transparency"] = Kw(f => f.ReducedTransparency == PrefersReduced.Reduce ? "reduce" : "no-preference", "no-preference", "reduce"),
            ["prefers-reduced-data"] = Kw(f => f.ReducedData == PrefersReduced.Reduce ? "reduce" : "no-preference", "no-preference", "reduce"),
            ["prefers-contrast"] = Kw(f => f.Contrast switch { PrefersContrast.More => "more", PrefersContrast.Less => "less", PrefersContrast.Custom => "custom", _ => "no-preference" }, "no-preference", "more", "less", "custom"),
            ["forced-colors"] = Kw(f => f.ForcedColors ? "active" : "none", "none", "active"),
            ["scripting"] = Kw(f => f.Scripting, "none", "initial-only", "enabled"),
            ["display-mode"] = Kw(_ => "browser", "fullscreen", "standalone", "minimal-ui", "browser", "picture-in-picture", "window-controls-overlay"),
            ["dynamic-range"] = Kw(_ => "standard", "standard", "high"),
            ["video-dynamic-range"] = Kw(_ => "standard", "standard", "high"),
            ["inverted-colors"] = Kw(_ => "none", "none", "inverted"),
            ["nav-controls"] = Kw(_ => "none", "none", "back"),
        };
        return d.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // ---- queries ----------------------------------------------------------------------------------------------------------------

    public static MediaQuery ParseQuery(TokenRange r)
    {
        MediaQuery Invalid() => new(false, true, false, "all", null, "not all");
        if (r.IsEmpty)
        {
            return Invalid();
        }

        int i = r.From;
        bool negated = false, only = false;
        string? type = null;
        if (r.Kind(i) == CssTokenKind.Ident)
        {
            string w = r.IdentString(i).AsciiLower();
            int after = r.SkipWs(i + 1);
            bool nextIsCondition = after < r.To && (r.Kind(after) is CssTokenKind.LeftParen or CssTokenKind.Function);
            if (w == "not" && nextIsCondition)
            {
                MqNode? cond = ParseCondition(r, allowOr: true);
                return cond is null ? Invalid() : new MediaQuery(true, false, false, null, cond, Serialize(cond));
            }

            if (w is "not" or "only")
            {
                negated = w == "not";
                only = w == "only";
                i = after;
                if (i >= r.To || r.Kind(i) != CssTokenKind.Ident)
                {
                    return Invalid();
                }

                w = r.IdentString(i).AsciiLower();
                after = r.SkipWs(i + 1);
            }

            if (ValueKinds.In(ReservedTypeWords, w))
            {
                return Invalid();
            }

            type = w;
            MqNode? condition = null;
            if (after < r.To)
            {
                if (!r.IsIdent(after, "and"))
                {
                    return Invalid();
                }

                int c = r.SkipWs(after + 1);
                if (c >= r.To)
                {
                    return Invalid();
                }

                condition = ParseCondition(r.Sub(c, r.To), allowOr: false);
                if (condition is null)
                {
                    return Invalid();
                }
            }

            var sb = new StringBuilder();
            if (negated)
            {
                sb.Append("not ");
            }
            else if (only)
            {
                sb.Append("only ");
            }

            sb.Append(CssSerializer.SerializeIdentifier(type));
            if (condition is not null)
            {
                sb.Append(" and ").Append(Serialize(condition));
            }

            return new MediaQuery(true, negated, only, type, condition, sb.ToString());
        }

        MqNode? top = ParseCondition(r, allowOr: true);
        return top is null ? Invalid() : new MediaQuery(true, false, false, null, top, Serialize(top));
    }

    private sealed class Frame
    {
        public TokenRange R;
        public int Pos;
        public readonly List<MqNode> Items = [];
        public string? Combinator;
        public bool Not;
        public bool ExpectItem = true;
        public bool Failed;
        public bool Nested;
        public string Raw = string.Empty;
        public bool AllowOr = true;
    }

    // <media-condition>: not X | X [and X]* | X [or X]*. Parenthesized conditions nest through an explicit stack.
    internal static MqNode? ParseCondition(TokenRange range, bool allowOr, Func<TokenRange, string, MqNode>? leaf = null, Func<TokenRange, int, string, MqNode?>? function = null)
    {
        var stack = new Stack<Frame>();
        stack.Push(new Frame { R = range, Pos = range.From, AllowOr = allowOr });
        MqNode? last = null;
        while (stack.Count > 0)
        {
            Frame f = stack.Peek();
            TokenRange r = f.R;
            f.Pos = r.SkipWs(f.Pos);
            if (f.Pos >= r.To || f.Failed)
            {
                MqNode? node = Finish(f);
                stack.Pop();
                if (stack.Count == 0)
                {
                    return node;
                }

                Frame parent = stack.Peek();
                if (node is null)
                {
                    node = new MqNode { Kind = MqKind.General, Text = f.Raw };
                }

                node.InParens = true;
                parent.Items.Add(node);
                parent.ExpectItem = false;
                last = node;
                continue;
            }

            int i = f.Pos;
            if (f.ExpectItem)
            {
                if (r.IsIdent(i, "not") && f.Items.Count == 0 && !f.Not)
                {
                    f.Not = true;
                    f.Pos = i + 1;
                    continue;
                }

                CssTokenKind k = r.Kind(i);
                if (k == CssTokenKind.LeftParen)
                {
                    int close = r.Close(i);
                    f.Pos = Math.Min(close + 1, r.To);
                    TokenRange inner = r.Sub(i + 1, close);
                    string raw = RawOf(r, i, f.Pos);
                    int first = inner.SkipWs(inner.From);
                    if (first < inner.To && (inner.Kind(first) is CssTokenKind.LeftParen or CssTokenKind.Function || inner.IsIdent(first, "not")))
                    {
                        stack.Push(new Frame { R = inner, Pos = inner.From, Nested = true, Raw = raw });
                        continue;
                    }

                    f.Items.Add(leaf is null ? ParseFeature(inner, raw) : leaf(inner, raw));
                    f.ExpectItem = false;
                    continue;
                }

                if (k == CssTokenKind.Function)
                {
                    int close = r.Close(i);
                    f.Pos = Math.Min(close + 1, r.To);
                    f.Items.Add((function is null ? null : function(r, i, RawOf(r, i, f.Pos))) ?? new MqNode { Kind = MqKind.General, Text = RawOf(r, i, f.Pos) });
                    f.ExpectItem = false;
                    continue;
                }

                f.Failed = true;
                continue;
            }

            // after an item: a combinator
            string? word = r.Kind(i) == CssTokenKind.Ident ? r.IdentString(i).AsciiLower() : null;
            if (f.Not || word is not ("and" or "or") || (f.Combinator is not null && f.Combinator != word) || (word == "or" && !f.AllowOr && !f.Nested))
            {
                f.Failed = true;
                continue;
            }

            f.Combinator = word;
            f.ExpectItem = true;
            f.Pos = i + 1;
        }

        return last;
    }

    private static MqNode? Finish(Frame f)
    {
        if (f.Failed || f.ExpectItem || f.Items.Count == 0)
        {
            if (f.Nested)
            {
                return null;
            }

            return null;
        }

        if (f.Not)
        {
            if (f.Items.Count != 1)
            {
                return null;
            }

            return new MqNode { Kind = MqKind.Not, Children = [f.Items[0]] };
        }

        if (f.Items.Count == 1)
        {
            return f.Items[0];
        }

        return new MqNode { Kind = f.Combinator == "or" ? MqKind.Or : MqKind.And, Children = f.Items };
    }

    internal static string RawOf(TokenRange r, int from, int to)
    {
        // Only whitespace tokens are dropped from the end: an escaped space ("\ ") belongs to the token before it.
        int lastIndex = to - 1;
        while (lastIndex > from && r.Kind(lastIndex) == CssTokenKind.Whitespace)
        {
            lastIndex--;
        }

        string raw = r.Tree.Text.Substring(r.Tree.Tokens[from].Start, r.Tree.Tokens[lastIndex].End - r.Tree.Tokens[from].Start);

        // CSS Syntax 5: blocks and functions still open at the end of the input are closed there. The serialization writes
        // those closers out, so that it parses back to the same query.
        var open = new Stack<char>();
        for (int i = from; i < to; i++)
        {
            switch (r.Kind(i))
            {
                case CssTokenKind.Function or CssTokenKind.LeftParen:
                    open.Push(')');
                    break;
                case CssTokenKind.LeftBracket:
                    open.Push(']');
                    break;
                case CssTokenKind.LeftBrace:
                    open.Push('}');
                    break;
                case CssTokenKind.RightParen when open.Count > 0 && open.Peek() == ')':
                case CssTokenKind.RightBracket when open.Count > 0 && open.Peek() == ']':
                case CssTokenKind.RightBrace when open.Count > 0 && open.Peek() == '}':
                    open.Pop(); // a closer that does not match the innermost open block is an ordinary token (CSS Syntax 5.4.8)
                    break;
            }
        }

        ref readonly Token last = ref r.Tree.Tokens[lastIndex];
        if (last.Kind == CssTokenKind.BadString && open.Count > 0)
        {
            // A bad string runs up to a newline that the trimmed text no longer has; a closer would end up inside it.
            raw += "\n";
        }
        else if (last.Kind is CssTokenKind.Url or CssTokenKind.BadUrl && (last.Flags & Token.Terminated) == 0)
        {
            // CSS Syntax 4.3.6 and 4.3.15: a url or bad-url token cut off by the end of the input is a token like any other.
            // Written with the closers after it, the first closer would end the url and leave its block open, so it is closed here.
            // A backslash at the end would escape the closer ("\)" is an escape), so it gets a line continuation first.
            raw += (OddBackslashes(raw) ? "\n" : string.Empty) + ")";
        }
        else if (last.Kind == CssTokenKind.QuotedString && (last.Flags & Token.Terminated) == 0)
        {
            // A string cut off by the end of the input is closed there too; a closer written after it would be part of the string.
            // A backslash at the end of the string would escape the quote, so it gets a line continuation first.
            raw += (OddBackslashes(raw) ? "\n" : string.Empty) + r.Tree.Text[last.Start];
        }

        if (open.Count == 0)
        {
            return raw;
        }

        // A lone backslash at the end of the input is a delimiter; a closer right after it would be escaped by it.
        return raw + (raw.EndsWith('\\') ? "\n" : string.Empty) + new string([.. open]);
    }

    private static bool OddBackslashes(string s)
    {
        int n = 0;
        while (n < s.Length && s[s.Length - 1 - n] == '\\')
        {
            n++;
        }

        return (n & 1) == 1;
    }

    // ---- features ---------------------------------------------------------------------------------------------------------------

    private static MqNode ParseFeature(TokenRange inner, string raw)
    {
        MqNode General() => new() { Kind = MqKind.General, Text = raw };
        var parts = new List<(bool IsOp, string Op, int From, int To)>();
        int i = inner.SkipWs(inner.From);
        int valueStart = -1;
        int valueEnd = -1;
        void FlushValue()
        {
            if (valueStart >= 0)
            {
                parts.Add((false, string.Empty, valueStart, valueEnd));
                valueStart = -1;
            }
        }

        while (i < inner.To)
        {
            CssTokenKind k = inner.Kind(i);
            string? op = null;
            int len = 1;
            if (k == CssTokenKind.Colon)
            {
                op = ":";
            }
            else if (inner.IsDelim(i, '<') || inner.IsDelim(i, '>'))
            {
                op = inner.IsDelim(i, '<') ? "<" : ">";
                if (i + 1 < inner.To && inner.IsDelim(i + 1, '=') && inner.Tree.Tokens[i].End == inner.Tree.Tokens[i + 1].Start)
                {
                    op += "=";
                    len = 2;
                }
            }
            else if (inner.IsDelim(i, '='))
            {
                op = "=";
            }

            if (op is not null)
            {
                FlushValue();
                parts.Add((true, op, i, i + len));
                i = inner.SkipWs(i + len);
                continue;
            }

            if (k == CssTokenKind.Whitespace)
            {
                i++;
                continue;
            }

            if (valueStart < 0)
            {
                valueStart = i;
            }

            i = inner.Next(i);
            valueEnd = i;
        }

        FlushValue();
        if (parts.Count == 0)
        {
            return General();
        }

        // Shapes: [V] | [V : V] | [V op V] | [V op V op V]
        bool Ident(int p, out string name)
        {
            name = string.Empty;
            (bool isOp, _, int from, int to) = parts[p];
            if (isOp || to - from < 1 || inner.Kind(from) != CssTokenKind.Ident || inner.SkipWs(from + 1) < to)
            {
                return false;
            }

            name = inner.IdentString(from).AsciiLower();
            return true;
        }

        var f = new MqFeature();
        if (parts.Count == 1)
        {
            if (!Ident(0, out string n) || !Features.TryGetValue(n, out MqDescriptor? desc))
            {
                return General();
            }

            f.Name = n;
            f.Descriptor = desc;
            f.IsBoolean = true;
            return new MqNode { Kind = MqKind.Feature, Feature = f, Text = "(" + n + ")" };
        }

        if (parts.Count == 3 && parts[1].IsOp && parts[1].Op == ":")
        {
            if (!Ident(0, out string n))
            {
                return General();
            }

            int prefix = 0;
            string baseName = n;
            if (n.StartsWith("min-", StringComparison.Ordinal))
            {
                prefix = -1;
                baseName = n[4..];
            }
            else if (n.StartsWith("max-", StringComparison.Ordinal))
            {
                prefix = 1;
                baseName = n[4..];
            }

            if (!Features.TryGetValue(baseName, out MqDescriptor? desc) || (prefix != 0 && !desc.Range))
            {
                if (prefix != 0 && Features.TryGetValue(n, out MqDescriptor? whole))
                {
                    desc = whole;
                    baseName = n;
                    prefix = 0;
                }
                else
                {
                    return General();
                }
            }

            if (!ParseValue(inner, parts[2].From, parts[2].To, desc, out double num, out string? word, out string text))
            {
                return General();
            }

            f.Name = baseName;
            f.Descriptor = desc;
            f.IsPlain = true;
            f.PrefixKind = prefix;
            f.PlainValue = num;
            f.PlainWord = word;
            string shown = (prefix < 0 ? "min-" : prefix > 0 ? "max-" : string.Empty) + baseName;
            return new MqNode { Kind = MqKind.Feature, Feature = f, Text = "(" + shown + ": " + text + ")" };
        }

        if (parts.Count == 3 && parts[1].IsOp && parts[1].Op != ":" && !parts[0].IsOp && !parts[2].IsOp)
        {
            bool nameLeft = Ident(0, out string left) && Features.TryGetValue(left, out MqDescriptor? dl) && dl.Range;
            bool nameRight = Ident(2, out string right) && Features.TryGetValue(right, out MqDescriptor? dr) && dr.Range;
            if (nameLeft == nameRight)
            {
                return General();
            }

            string name = nameLeft ? left : right;
            MqDescriptor desc = Features[name];
            int vp = nameLeft ? 2 : 0;
            if (!ParseValue(inner, parts[vp].From, parts[vp].To, desc, out double num, out _, out string text))
            {
                return General();
            }

            // Normalize to "name op value".
            string op = parts[1].Op;
            string flipped = nameLeft ? op : op switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => op };
            f.Name = name;
            f.Descriptor = desc;
            f.Comparisons.Add((flipped, num, null));
            string textOut = nameLeft ? "(" + name + " " + op + " " + text + ")" : "(" + text + " " + op + " " + name + ")";
            return new MqNode { Kind = MqKind.Feature, Feature = f, Text = textOut };
        }

        if (parts.Count == 5 && !parts[0].IsOp && parts[1].IsOp && !parts[2].IsOp && parts[3].IsOp && !parts[4].IsOp
            && Ident(2, out string mid) && Features.TryGetValue(mid, out MqDescriptor? dm) && dm.Range)
        {
            string o1 = parts[1].Op;
            string o2 = parts[3].Op;
            bool lessDir = o1.StartsWith('<') && o2.StartsWith('<');
            bool greaterDir = o1.StartsWith('>') && o2.StartsWith('>');
            if (!lessDir && !greaterDir)
            {
                return General();
            }

            if (!ParseValue(inner, parts[0].From, parts[0].To, dm, out double a, out _, out string ta) || !ParseValue(inner, parts[4].From, parts[4].To, dm, out double b, out _, out string tb))
            {
                return General();
            }

            string Flip(string op) => op switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => op };
            f.Name = mid;
            f.Descriptor = dm;
            f.Comparisons.Add((Flip(o1), a, null));
            f.Comparisons.Add((o2, b, null));
            return new MqNode { Kind = MqKind.Feature, Feature = f, Text = "(" + ta + " " + o1 + " " + mid + " " + o2 + " " + tb + ")" };
        }

        return General();
    }

    private static bool ParseValue(TokenRange inner, int from, int to, MqDescriptor d, out double number, out string? word, out string text)
    {
        number = 0;
        word = null;
        text = string.Empty;
        TokenRange v = inner.Sub(from, to).Trim();
        if (v.IsEmpty)
        {
            return false;
        }

        switch (d.Kind)
        {
            case MqValueKind.Keyword:
            {
                if (v.Kind(v.From) != CssTokenKind.Ident || v.Next(v.From) != v.To)
                {
                    return false;
                }

                string w = v.IdentString(v.From).AsciiLower();
                if (Array.IndexOf(d.Keywords, w) < 0)
                {
                    return false;
                }

                word = w;
                text = w;
                return true;
            }

            case MqValueKind.Ratio:
            {
                int i = v.From;
                if (v.Kind(i) != CssTokenKind.Number || v.Number(i) < 0)
                {
                    return false;
                }

                double num = v.Number(i);
                double den = 1;
                string t = CssUnits.FormatNumber(num);
                i = v.SkipWs(i + 1);
                if (i < v.To)
                {
                    if (!v.IsDelim(i, '/'))
                    {
                        return false;
                    }

                    i = v.SkipWs(i + 1);
                    if (i >= v.To || v.Kind(i) != CssTokenKind.Number || v.Number(i) < 0 || v.SkipWs(i + 1) < v.To)
                    {
                        return false;
                    }

                    den = v.Number(i);
                    t += " / " + CssUnits.FormatNumber(den);
                }

                number = den == 0 ? (num == 0 ? double.NaN : double.PositiveInfinity) : num / den;
                text = t;
                return true;
            }

            case MqValueKind.Resolution:
            {
                if (v.IsIdent(v.From, "infinite") && v.Next(v.From) == v.To)
                {
                    number = double.PositiveInfinity;
                    text = "infinite";
                    return true;
                }

                if (v.Next(v.From) != v.To || !CssQuantity.TryParse(v, v.From, QuantityKinds.Resolution | QuantityKinds.NonNegative, null, out CssQuantity q, out _))
                {
                    return false;
                }

                number = q.Resolve(LengthContext.Default);
                text = q.ToCss();
                return true;
            }

            default:
            {
                QuantityKinds kinds = d.Kind switch
                {
                    MqValueKind.Length => QuantityKinds.Length | QuantityKinds.UnitlessZeroLength,
                    MqValueKind.Integer => QuantityKinds.Number | QuantityKinds.IntegerOnly,
                    _ => QuantityKinds.Number,
                };
                if (v.Next(v.From) != v.To || !CssQuantity.TryParse(v, v.From, kinds, null, out CssQuantity q, out _))
                {
                    return false;
                }

                // Relative units in media queries use the initial values (16px font, the viewport of the context); here the default context.
                number = q.Resolve(LengthContext.Default);
                text = q.ToCss();
                return number >= d.Min && number <= d.Max;
            }
        }
    }

    // ---- evaluation (iterative post-order, three-valued) ----------------------------------------------------------------------

    public static Tri Evaluate(MqNode root, MediaFeatures features)
    {
        var work = new Stack<(MqNode Node, int Next)>();
        var results = new Stack<Tri>();
        work.Push((root, 0));
        while (work.Count > 0)
        {
            (MqNode node, int next) = work.Pop();
            if (node.Kind is MqKind.Feature)
            {
                results.Push(node.Leaf is not null ? (node.Leaf() ? Tri.True : Tri.False) : EvalFeature(node.Feature!, features));
                continue;
            }

            if (node.Kind == MqKind.General)
            {
                results.Push(Tri.Unknown);
                continue;
            }

            if (next < node.Children.Count)
            {
                work.Push((node, next + 1));
                work.Push((node.Children[next], 0));
                continue;
            }

            int n = node.Children.Count;
            var vals = new Tri[n];
            for (int k = n - 1; k >= 0; k--)
            {
                vals[k] = results.Pop();
            }

            results.Push(node.Kind switch
            {
                MqKind.Not => vals[0] switch { Tri.True => Tri.False, Tri.False => Tri.True, _ => Tri.Unknown },
                MqKind.And => Array.IndexOf(vals, Tri.False) >= 0 ? Tri.False : Array.IndexOf(vals, Tri.Unknown) >= 0 ? Tri.Unknown : Tri.True,
                _ => Array.IndexOf(vals, Tri.True) >= 0 ? Tri.True : Array.IndexOf(vals, Tri.Unknown) >= 0 ? Tri.Unknown : Tri.False,
            });
        }

        return results.Pop();
    }

    private static Tri EvalFeature(MqFeature f, MediaFeatures m)
    {
        MqDescriptor d = f.Descriptor;
        if (d.Kind == MqValueKind.Keyword)
        {
            string cur = d.Word(m);
            if (f.IsBoolean)
            {
                return cur is not ("none" or "no-preference") ? Tri.True : Tri.False;
            }

            if (f.Name == "color-gamut")
            {
                int rank(string s) => s switch { "srgb" => 0, "p3" => 1, _ => 2 };
                return rank(cur) >= rank(f.PlainWord!) ? Tri.True : Tri.False;
            }

            return cur == f.PlainWord ? Tri.True : Tri.False;
        }

        double v = d.Number(m);
        if (f.IsBoolean)
        {
            return v != 0 ? Tri.True : Tri.False;
        }

        if (f.IsPlain)
        {
            bool ok = f.PrefixKind switch
            {
                < 0 => v >= f.PlainValue,
                > 0 => v <= f.PlainValue,
                _ => v == f.PlainValue || (double.IsPositiveInfinity(v) && double.IsPositiveInfinity(f.PlainValue)),
            };
            return ok ? Tri.True : Tri.False;
        }

        foreach ((string op, double val, _) in f.Comparisons)
        {
            bool ok = op switch
            {
                "<" => v < val,
                "<=" => v <= val,
                ">" => v > val,
                ">=" => v >= val,
                _ => v == val,
            };
            if (!ok)
            {
                return Tri.False;
            }
        }

        return Tri.True;
    }

    // ---- serialization ------------------------------------------------------------------------------------------------------------

    public static string Serialize(MqNode root)
    {
        var text = new Dictionary<MqNode, string>(ReferenceEqualityComparer.Instance);
        var work = new Stack<(MqNode Node, bool Visited)>();
        work.Push((root, false));
        while (work.Count > 0)
        {
            (MqNode node, bool visited) = work.Pop();
            if (!visited && node.Children.Count > 0)
            {
                work.Push((node, true));
                foreach (MqNode c in node.Children)
                {
                    work.Push((c, false));
                }

                continue;
            }

            string s;
            switch (node.Kind)
            {
                case MqKind.Feature:
                case MqKind.General:
                    s = node.Text;
                    break;
                case MqKind.Not:
                    s = "not " + text[node.Children[0]];
                    break;
                default:
                {
                    string join = node.Kind == MqKind.And ? " and " : " or ";
                    s = string.Join(join, node.Children.ConvertAll(c => text[c]));
                    break;
                }
            }

            if (node.InParens && node.Kind is not (MqKind.Feature or MqKind.General))
            {
                s = "(" + s + ")";
            }

            text[node] = s;
        }

        return text[root];
    }
}
