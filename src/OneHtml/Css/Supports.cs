using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Hooks and choices for <c>@supports</c> evaluation.</summary>
public sealed class SupportsOptions
{
    /// <summary>The defaults: <c>selector()</c> is checked for syntax only.</summary>
    public static SupportsOptions Default { get; } = new();

    /// <summary>
    /// Decides <c>selector(...)</c> conditions from the selector's text (without the function wrapper). The selectors
    /// component supplies a real check; when null, the text only has to look like one complex selector (no top-level
    /// comma, no braces, strings or semicolons outside attribute brackets, not empty).
    /// </summary>
    public Func<string, bool>? SelectorSupported { get; init; }
}

/// <summary>A parsed <c>@supports</c> condition (CSS Conditional 3 §6).</summary>
public sealed class SupportsCondition
{
    private readonly MqNode? _root;

    private SupportsCondition(MqNode? root, string text)
    {
        _root = root;
        Text = text;
    }

    /// <summary>False when the text is not a valid <c>&lt;supports-condition&gt;</c>; such a condition evaluates to false.</summary>
    public bool IsValid => _root is not null;

    /// <summary>The condition text, trimmed of whitespace. A condition that ends in a string broken by a newline keeps that one newline, so that the text tokenizes the same way when parsed again.</summary>
    public string Text { get; }

    /// <summary>Parses a condition. A bare declaration (<c>display: grid</c>) is accepted too, as <c>CSS.supports()</c> does.</summary>
    /// <param name="text">The condition.</param>
    /// <param name="options">Evaluation options; null for the defaults.</param>
    public static SupportsCondition Parse(string text, SupportsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(CssTree.ParseComponentValueList(text).Values, options);
    }

    /// <summary>Parses a condition from component values.</summary>
    /// <param name="values">The prelude of <c>@supports</c>.</param>
    /// <param name="options">Evaluation options; null for the defaults.</param>
    public static SupportsCondition Parse(ComponentValueList values, SupportsOptions? options = null)
    {
        options ??= SupportsOptions.Default;
        TokenRange r = TokenRange.Of(values).Trim();
        string text = r.RawText();
        if (!r.IsEmpty && r.Kind(r.To - 1) == CssTokenKind.BadString && r.Tree.Tokens[r.To - 1].End < r.Tree.Text.Length)
        {
            // CSS Syntax 4.3.5: a string ends at a newline, which is not part of it and makes it a bad-string. The newline
            // is the whitespace the trim just dropped; without it the text ends in an ordinary string cut off by the end
            // of the input, a different token, and the condition would read back differently.
            text += r.Tree.Text[r.Tree.Tokens[r.To - 1].End];
        }

        if (r.IsEmpty)
        {
            return new SupportsCondition(null, text);
        }

        MqNode? root = MediaQueryParser.ParseCondition(r, allowOr: true, (inner, raw) => Leaf(inner, raw, options), (rr, i, raw) => FunctionLeaf(rr, i, raw, options));
        if (root is null && r.Kind(r.From) is not (CssTokenKind.LeftParen or CssTokenKind.Function) && !r.IsIdent(r.From, "not"))
        {
            MqNode leaf = Leaf(r, text, options);
            root = leaf.Kind == MqKind.General ? null : leaf;
        }

        return new SupportsCondition(root, text);
    }

    /// <summary>Evaluates the condition. Unknown terms (<c>&lt;general-enclosed&gt;</c>) count as false, as does a condition that does not parse.</summary>
    public bool Evaluate() => _root is not null && MediaQueryParser.Evaluate(_root, MediaFeatures.Default) == Tri.True;

    private static MqNode Leaf(TokenRange inner, string raw, SupportsOptions options)
    {
        // ( <declaration> ): name ':' value ['!important']
        MqNode General() => new() { Kind = MqKind.General, Text = raw };
        int i = inner.SkipWs(inner.From);
        if (i >= inner.To || inner.Kind(i) != CssTokenKind.Ident)
        {
            return General();
        }

        string name = inner.IdentString(i);
        int colon = inner.SkipWs(i + 1);
        if (colon >= inner.To || inner.Kind(colon) != CssTokenKind.Colon)
        {
            return General();
        }

        TokenRange value = inner.Sub(colon + 1, inner.To).Trim();
        // Trailing !important is allowed and ignored.
        int end = value.To;
        int j = value.From;
        int bang = -1;
        while (j < value.To)
        {
            if (value.IsDelim(j, '!'))
            {
                bang = j;
            }

            j = value.Next(j);
        }

        if (bang >= 0)
        {
            int after = value.SkipWs(bang + 1);
            if (after >= value.To || !value.IsIdent(after, "important") || value.SkipWs(after + 1) < value.To)
            {
                return General();
            }

            end = bang;
        }

        TokenRange v = value.Sub(value.From, end).Trim();
        string css = raw;
        bool supported = DeclarationSupport.IsSupported(name, new ComponentValueList(v.Tree, v.From, v.To));
        return new MqNode { Kind = MqKind.Feature, Text = css, Leaf = () => supported };
    }

    private static MqNode? FunctionLeaf(TokenRange r, int i, string raw, SupportsOptions options)
    {
        string name = r.IdentString(i).AsciiLower();
        TokenRange inner = r.Inner(i);
        switch (name)
        {
            case "selector":
            {
                string selector = inner.Trim().RawText();
                bool ok = options.SelectorSupported is { } check ? check(selector) : LooksLikeComplexSelector(inner.Trim());
                return new MqNode { Kind = MqKind.Feature, Text = raw, Leaf = () => ok };
            }

            case "font-format":
            {
                bool ok = OneIdentIn(inner, FontFormats);
                return new MqNode { Kind = MqKind.Feature, Text = raw, Leaf = () => ok };
            }

            case "font-tech":
            {
                bool ok = OneIdentIn(inner, FontTechs);
                return new MqNode { Kind = MqKind.Feature, Text = raw, Leaf = () => ok };
            }

            case "at-rule":
            {
                TokenRange t = inner.Trim();
                bool ok = !t.IsEmpty && t.Kind(t.From) == CssTokenKind.AtKeyword && t.Next(t.From) == t.To && ValueKinds.In(AtRules, new CssToken(t.Tree, t.From).Value);
                return new MqNode { Kind = MqKind.Feature, Text = raw, Leaf = () => ok };
            }

            default:
                return null;
        }
    }

    // The font formats and technologies this library reports as supported: those a user agent that renders web fonts handles.
    private static readonly FrozenSet<string> FontFormats = ValueKinds.Set("collection", "opentype", "truetype", "woff", "woff2");
    private static readonly FrozenSet<string> FontTechs = ValueKinds.Set("features-opentype", "features-aat", "features-graphite", "variations", "palettes", "incremental-patch", "incremental-range", "incremental", "color-colrv0", "color-colrv1", "color-svg", "color-sbix", "color-cbdt");
    private static readonly FrozenSet<string> AtRules = ValueKinds.Set("media", "supports", "import", "namespace", "layer", "font-face", "keyframes", "page", "property", "container", "counter-style", "scope", "starting-style", "font-feature-values", "font-palette-values", "charset");

    private static bool OneIdentIn(TokenRange inner, FrozenSet<string> set)
    {
        TokenRange t = inner.Trim();
        return !t.IsEmpty && t.Kind(t.From) == CssTokenKind.Ident && t.Next(t.From) == t.To && ValueKinds.In(set, t.Name(t.From, out _));
    }

    // Syntax-only stand-in for selector(): one non-empty complex selector made of selector-looking tokens.
    private static bool LooksLikeComplexSelector(TokenRange t)
    {
        if (t.IsEmpty)
        {
            return false;
        }

        int i = t.From;
        while (i < t.To)
        {
            switch (t.Kind(i))
            {
                case CssTokenKind.Comma:
                case CssTokenKind.Semicolon:
                case CssTokenKind.QuotedString:
                case CssTokenKind.BadString:
                case CssTokenKind.BadUrl:
                case CssTokenKind.LeftBrace:
                case CssTokenKind.RightBrace:
                case CssTokenKind.RightParen:
                case CssTokenKind.RightBracket:
                case CssTokenKind.Number:
                case CssTokenKind.Percentage:
                case CssTokenKind.AtKeyword:
                    return false;
            }

            i = t.Next(i);
        }

        return true;
    }
}

/// <summary>Whether a declaration is supported (<c>@supports (property: value)</c>, <c>CSS.supports(property, value)</c>).</summary>
public static class DeclarationSupport
{
    /// <summary>
    /// The rule (CSS Conditional 3 §6.1, narrowed to what this library can check): custom properties accept any value; a
    /// property must be in the property table; CSS-wide keywords and values containing <c>var()</c> are accepted; a typed
    /// property (see <see cref="TypedProperties.IsTyped"/>) must parse with its typed parser; a shorthand with a grammar
    /// here must expand; a property whose value syntax is only keywords and simple types (<c>&lt;length&gt;</c>,
    /// <c>&lt;color&gt;</c>, ...) must match it; every other property is supported when its value is syntactically
    /// acceptable: non-empty, no bad strings or URLs, no stray closing brackets, no braces or semicolons at the top level.
    /// </summary>
    /// <param name="property">The property name.</param>
    /// <param name="value">The value, without <c>!important</c>.</param>
    public static bool IsSupported(string property, ComponentValueList value)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
        {
            TokenRange cv = TokenRange.Of(value).Trim();
            return property.Length > 2 && (cv.IsEmpty || SyntacticallyAcceptable(cv, allowBlocks: true)) && VarSubstitution.IsValidSyntax(value);
        }

        if (!PropertyTable.TryGet(property, out PropertyInfo? info))
        {
            return false;
        }

        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return false;
        }

        bool hasVar = VarSubstitution.ContainsVar(value);
        if (!SyntacticallyAcceptable(r, allowBlocks: hasVar))
        {
            return false;
        }

        if (CssWideKeywords.Detect(value) != CssWideKeyword.None)
        {
            return true;
        }

        if (hasVar)
        {
            return VarSubstitution.IsValidSyntax(value);
        }

        if (TypedProperties.IsTyped(info.Name) || info.Name is "content" or "clip-path" or "background-image" or "speak-as")
        {
            return TypedProperties.IsValid(info.Name, value);
        }

        PropertyInfo target = PropertyTable.ResolveAlias(info);
        if (AnimationGrammar.Handles(target.Name))
        {
            return AnimationGrammar.IsValid(target.Name, r);
        }

        if (info.IsShorthand && Shorthands.HasGrammar(info.Name))
        {
            return Shorthands.Expand(info.Name, value).Status == ShorthandStatus.Expanded;
        }

        if (target.Syntax is { } syntax)
        {
            SimpleSyntax.Result result = SimpleSyntax.Validate(syntax, r);
            if (result != SimpleSyntax.Result.Unhandled)
            {
                return result == SimpleSyntax.Result.Valid;
            }
        }

        return true;
    }

    internal static bool SyntacticallyAcceptable(TokenRange r, bool allowBlocks = false)
    {
        int i = r.From;
        while (i < r.To)
        {
            switch (r.Kind(i))
            {
                case CssTokenKind.BadString:
                case CssTokenKind.BadUrl:
                case CssTokenKind.Semicolon:
                case CssTokenKind.RightBrace:
                case CssTokenKind.RightParen:
                case CssTokenKind.RightBracket:
                    return false;
                case CssTokenKind.LeftBrace when !allowBlocks:
                    return false;
                case CssTokenKind.Delim when r.IsDelim(i, '!'):
                    return false;
            }

            i = r.Next(i);
        }

        return true;
    }
}

/// <summary>
/// Checks a value against a value-definition syntax that is only a list of keywords and simple types joined by
/// <c>|</c> (for example <c>auto | &lt;length-percentage [0,∞]&gt;</c>). Anything more complex is reported as unhandled.
/// </summary>
internal static partial class SimpleSyntax
{
    public enum Result : byte
    {
        Unhandled,
        Valid,
        Invalid,
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeywordRegex();
    [GeneratedRegex(@"^<([a-z-]+)(?:\s*\[(-?[0-9.]+|-∞),\s*(-?[0-9.]+|∞)\])?>$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeRefRegex();

    public static Result Validate(string syntax, TokenRange r)
    {
        if (syntax.Length > 400)
        {
            return Result.Unhandled;
        }

        string[] alternatives = syntax.Split('|', StringSplitOptions.TrimEntries);
        var types = new List<(string Name, bool NonNegative)>();
        var keywords = new List<string>();
        foreach (string alt in alternatives)
        {
            if (KeywordRegex().IsMatch(alt))
            {
                keywords.Add(alt);
                continue;
            }

            Match m = TypeRefRegex().Match(alt);
            if (!m.Success)
            {
                return Result.Unhandled;
            }

            string name = m.Groups[1].Value;
            if (!Supported(name))
            {
                return Result.Unhandled;
            }

            types.Add((name, m.Groups[2].Success && m.Groups[2].Value == "0"));
        }

        if (r.Next(r.From) != r.To)
        {
            return Result.Invalid; // all alternatives here are single component values
        }

        int i = r.From;
        if (r.Kind(i) == CssTokenKind.Ident)
        {
            ReadOnlySpan<char> w = r.Name(i, out _);
            foreach (string k in keywords)
            {
                if (w.Equals(k, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Valid;
                }
            }

            if (types.Exists(t => t.Name is "custom-ident" or "dashed-ident" or "ident"))
            {
                return CssWideKeywords.FromName(w) == CssWideKeyword.None ? Result.Valid : Result.Invalid;
            }
        }

        foreach ((string name, bool nonNegative) in types)
        {
            if (Matches(name, nonNegative, r, i))
            {
                return Result.Valid;
            }
        }

        return Result.Invalid;
    }

    private static bool Supported(string name) => name is "length" or "length-percentage" or "percentage" or "number" or "integer" or "color"
        or "time" or "angle" or "resolution" or "string" or "url" or "custom-ident" or "dashed-ident" or "ident" or "flex" or "frequency"
        or "zero" or "alpha-value" or "image";

    private static bool Matches(string type, bool nonNegative, TokenRange r, int i)
    {
        QuantityKinds nn = nonNegative ? QuantityKinds.NonNegative : 0;
        switch (type)
        {
            case "length": return CssQuantity.TryParse(r, i, QuantityKinds.Length | QuantityKinds.UnitlessZeroLength | nn, null, out _, out _);
            case "length-percentage": return CssQuantity.TryParse(r, i, QuantityKinds.LengthPercentage | QuantityKinds.UnitlessZeroLength | nn, null, out _, out _);
            case "percentage": return CssQuantity.TryParse(r, i, QuantityKinds.Percentage | nn, null, out _, out _);
            case "number": return CssQuantity.TryParse(r, i, QuantityKinds.Number | nn, null, out _, out _);
            case "alpha-value": return CssQuantity.TryParse(r, i, QuantityKinds.Number | QuantityKinds.Percentage, null, out _, out _);
            case "integer": return CssQuantity.TryParse(r, i, QuantityKinds.Number | QuantityKinds.IntegerOnly | nn, null, out _, out _);
            case "time": return CssQuantity.TryParse(r, i, QuantityKinds.Time | nn, null, out _, out _);
            case "angle": return CssQuantity.TryParse(r, i, QuantityKinds.Angle | QuantityKinds.UnitlessZeroLength, null, out _, out _) || (r.Kind(i) == CssTokenKind.Number && r.Number(i) == 0);
            case "frequency": return CssQuantity.TryParse(r, i, QuantityKinds.Frequency | nn, null, out _, out _);
            case "resolution": return CssQuantity.TryParse(r, i, QuantityKinds.Resolution | nn, null, out _, out _);
            case "flex": return CssQuantity.TryParse(r, i, QuantityKinds.Flex | nn, null, out _, out _);
            case "zero": return r.Kind(i) == CssTokenKind.Number && r.Number(i) == 0;
            case "color": return ValueKinds.Color(r, i) > 0;
            case "string": return r.Kind(i) == CssTokenKind.QuotedString;
            case "url": return r.Kind(i) == CssTokenKind.Url || r.IsFunction(i, "url");
            case "image": return ValueKinds.Image(r, i) > 0;
            default: return false;
        }
    }
}
