using System;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Which limit a parse exceeded.</summary>
public enum CssLimit
{
    /// <summary>The input has more UTF-16 code units than <see cref="CssParseOptions.MaxInputLength"/>.</summary>
    InputLength,

    /// <summary>The input has more tokens than <see cref="CssParseOptions.MaxTokens"/>.</summary>
    TokenCount,

    /// <summary>
    /// Reserved. Nesting deeper than <see cref="CssParseOptions.MaxNestingDepth"/> no longer throws; see
    /// <see cref="CssTree.NestingLimitReached"/>.
    /// </summary>
    NestingDepth,
}

/// <summary>
/// Thrown when CSS input exceeds a <see cref="CssParseOptions"/> limit. The limits are checked before the memory
/// they guard is allocated, so hostile input fails cleanly instead of exhausting memory.
/// </summary>
public sealed class CssLimitExceededException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="limit">The limit that was exceeded.</param>
    /// <param name="maximum">The configured maximum.</param>
    public CssLimitExceededException(CssLimit limit, long maximum)
        : base($"CSS input exceeds the {limit} limit of {maximum}.")
    {
        Limit = limit;
        Maximum = maximum;
    }

    /// <summary>The limit that was exceeded.</summary>
    public CssLimit Limit { get; }

    /// <summary>The configured maximum.</summary>
    public long Maximum { get; }
}

/// <summary>
/// Limits for parsing untrusted CSS. Every limit is checked before the work it guards. Exceeding the input length or
/// token count throws <see cref="CssLimitExceededException"/>. Nesting depth degrades instead (see
/// <see cref="MaxNestingDepth"/>). Ordinary malformed CSS never throws, it is recovered from as the specification says.
/// </summary>
public sealed class CssParseOptions
{
    /// <summary>The options used when none are given.</summary>
    public static CssParseOptions Default { get; } = new();

    /// <summary>Maximum input length in UTF-16 code units. Default 16,777,216.</summary>
    public int MaxInputLength { get; init; } = 1 << 24;

    /// <summary>Maximum number of tokens, whitespace included. Default 8,388,608.</summary>
    public int MaxTokens { get; init; } = 1 << 23;

    /// <summary>
    /// Maximum nesting of blocks and functions that is matched. Default 256. Deeper nesting does not fail: the block that
    /// would exceed the limit, and everything after it, is read as unclosed blocks running to the end of their range,
    /// exactly as the specification treats a block still open at the end of the input, and
    /// <see cref="CssTree.NestingLimitReached"/> is set.
    /// </summary>
    public int MaxNestingDepth { get; init; } = 256;
}
