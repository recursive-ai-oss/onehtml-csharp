using System;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>
/// Bounds on the size of a <c>DOMSnapshot.captureSnapshot</c> result. Each is checked before the work it guards, so a
/// hostile input fails with a <see cref="DomSnapshotFormatException"/> instead of exhausting memory.
/// </summary>
public sealed record DomSnapshotLimits
{
    /// <summary>The defaults.</summary>
    public static DomSnapshotLimits Default { get; } = new();

    /// <summary>Largest accepted JSON input in bytes. Default 256 MiB.</summary>
    public int MaxInputBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>Largest accepted string table, in entries. Default 16 Mi.</summary>
    public int MaxStrings { get; init; } = 16 * 1024 * 1024;

    /// <summary>Largest accepted total length of all strings, in UTF-16 code units. Default 256 Mi.</summary>
    public long MaxStringChars { get; init; } = 256L * 1024 * 1024;

    /// <summary>Most documents (the page and its frames). Default 4096.</summary>
    public int MaxDocuments { get; init; } = 4096;

    /// <summary>Most DOM nodes over all documents. Default 8 Mi.</summary>
    public int MaxNodes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Most layout items over all documents. Default 8 Mi.</summary>
    public int MaxLayoutNodes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Most text boxes over all documents. Default 16 Mi.</summary>
    public int MaxTextBoxes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Most elements of any one integer array, such as the flattened attribute or style tables. Default 128 Mi.</summary>
    public int MaxArrayElements { get; init; } = 128 * 1024 * 1024;

    /// <summary>Deepest DOM nesting. Default 10 000.</summary>
    public int MaxNodeDepth { get; init; } = 10_000;

    /// <summary>Most entries in the computed-style name list. Default 1024.</summary>
    public int MaxComputedStyleNames { get; init; } = 1024;

    /// <summary>Deepest JSON nesting. Default 64; Chrome's output nests four levels.</summary>
    public int MaxJsonDepth { get; init; } = 64;
}
