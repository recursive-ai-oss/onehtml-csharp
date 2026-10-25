namespace RecursiveAI.OneHtml.Css;

/// <summary>The kind of a CSS token (CSS Syntax §4). Comments are not tokens; they are kept in a separate list.</summary>
public enum CssTokenKind : byte
{
    /// <summary>An identifier, for example <c>color</c>.</summary>
    Ident,

    /// <summary>A function name and its opening parenthesis, for example <c>rgb(</c>.</summary>
    Function,

    /// <summary>An at-keyword, for example <c>@media</c>.</summary>
    AtKeyword,

    /// <summary>A hash, for example <c>#fff</c>.</summary>
    Hash,

    /// <summary>A quoted string.</summary>
    QuotedString,

    /// <summary>A string cut short by an unescaped newline.</summary>
    BadString,

    /// <summary>An unquoted <c>url(...)</c>.</summary>
    Url,

    /// <summary>A malformed unquoted <c>url(...)</c>.</summary>
    BadUrl,

    /// <summary>A single code point that is not part of any other token.</summary>
    Delim,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A number followed by <c>%</c>.</summary>
    Percentage,

    /// <summary>A number followed by a unit.</summary>
    Dimension,

    /// <summary>A <c>U+xxxx</c> range; only produced inside a <c>unicode-range</c> declaration.</summary>
    UnicodeRange,

    /// <summary>A run of whitespace.</summary>
    Whitespace,

    /// <summary><c>&lt;!--</c>.</summary>
    Cdo,

    /// <summary><c>--&gt;</c>.</summary>
    Cdc,

    /// <summary><c>:</c>.</summary>
    Colon,

    /// <summary><c>;</c>.</summary>
    Semicolon,

    /// <summary><c>,</c>.</summary>
    Comma,

    /// <summary><c>[</c>.</summary>
    LeftBracket,

    /// <summary><c>]</c>.</summary>
    RightBracket,

    /// <summary><c>(</c>.</summary>
    LeftParen,

    /// <summary><c>)</c>.</summary>
    RightParen,

    /// <summary><c>{</c>.</summary>
    LeftBrace,

    /// <summary><c>}</c>.</summary>
    RightBrace,
}
