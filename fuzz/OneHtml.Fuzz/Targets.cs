using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>The registry of fuzz targets, keyed by name. A target must not throw for any input.</summary>
internal static class Targets
{
    public static readonly SortedDictionary<string, Action<ReadOnlySpan<byte>>> All = new(StringComparer.Ordinal)
    {
        ["noop"] = static _ => { },
    };
}
