using System;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>
/// Thrown when a <c>DOMSnapshot.captureSnapshot</c> result cannot be read: the JSON is malformed, a table is
/// inconsistent (lengths differ, an index is out of range), the format is not the pinned one, or a
/// <see cref="DomSnapshotLimits"/> bound was exceeded.
/// </summary>
public sealed class DomSnapshotFormatException : Exception
{
    /// <summary>Creates the exception for a malformed or unsupported snapshot.</summary>
    /// <param name="message">What is wrong and where.</param>
    public DomSnapshotFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception for a malformed snapshot caused by another exception.</summary>
    /// <param name="message">What is wrong and where.</param>
    /// <param name="inner">The underlying JSON error.</param>
    public DomSnapshotFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Creates the exception for an exceeded limit.</summary>
    /// <param name="limitName">The <see cref="DomSnapshotLimits"/> property.</param>
    /// <param name="limit">The bound.</param>
    /// <param name="actual">The first value over it that was seen.</param>
    public DomSnapshotFormatException(string limitName, long limit, long actual)
        : base($"The snapshot exceeds the {limitName} limit of {limit} (saw {actual}).")
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>The name of the exceeded limit, or <see langword="null"/> when the failure is not a limit.</summary>
    public string? LimitName { get; }

    /// <summary>The bound that was exceeded; 0 when <see cref="LimitName"/> is <see langword="null"/>.</summary>
    public long Limit { get; }

    /// <summary>The value that exceeded the bound; 0 when <see cref="LimitName"/> is <see langword="null"/>.</summary>
    public long Actual { get; }
}
