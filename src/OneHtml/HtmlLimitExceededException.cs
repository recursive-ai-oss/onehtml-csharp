using System;

namespace RecursiveAI.OneHtml;

/// <summary>Thrown when an input exceeds one of the configured <see cref="ParseLimits"/>.</summary>
public sealed class HtmlLimitExceededException : Exception
{
    /// <summary>Creates the exception for the named limit.</summary>
    /// <param name="limitName">The name of the <see cref="ParseLimits"/> property that was exceeded.</param>
    /// <param name="limit">The configured bound.</param>
    /// <param name="actual">The value that exceeded it, or the first value over the bound that was seen.</param>
    public HtmlLimitExceededException(string limitName, long limit, long actual)
        : base($"The input exceeds the {limitName} limit of {limit} (saw {actual}).")
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>The name of the <see cref="ParseLimits"/> property that was exceeded.</summary>
    public string LimitName { get; }

    /// <summary>The configured bound.</summary>
    public long Limit { get; }

    /// <summary>The value that exceeded the bound.</summary>
    public long Actual { get; }
}
