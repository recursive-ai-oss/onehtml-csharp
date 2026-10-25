namespace RecursiveAI.OneHtml;

/// <summary>
/// Upper bounds on the work and memory the decoder and tokenizer will spend on one input. Every bound is checked
/// before the allocation or loop it guards, and exceeding one throws <see cref="HtmlLimitExceededException"/>.
/// The defaults are generous: real pages are far below them.
/// </summary>
public sealed record ParseLimits
{
    /// <summary>The limits used when none are given.</summary>
    public static ParseLimits Default { get; } = new();

    /// <summary>
    /// Largest input in bytes (or UTF-16 code units for text given as a string). Default 64 MiB.
    /// Checked before any buffer is allocated.
    /// </summary>
    public int MaxInputLength { get; init; } = 64 * 1024 * 1024;

    /// <summary>
    /// Most attributes kept on a single tag, duplicates included. Default 1024. Checked as each attribute starts,
    /// so a tag with a million attributes is rejected after the 1025th rather than after it is read.
    /// </summary>
    public int MaxAttributesPerTag { get; init; } = 1024;

    /// <summary>
    /// Most runs the byte-offset map may hold. Default 16,777,216 (256 MiB of map). Ordinary pages need a few
    /// hundred to a few thousand runs; only input that alternates between 1-byte and multi-byte characters on
    /// nearly every character comes anywhere near the bound.
    /// </summary>
    public int MaxOffsetMapRuns { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// Most nodes (elements, text, comments, DOCTYPE and processing instructions, and template content roots) one
    /// document may hold. Default 4,194,304 (about 250 MiB of columns). Checked before each node is added.
    /// </summary>
    public int MaxNodes { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Most elements on the stack of open elements at once, which is the nesting depth of the tree being built.
    /// Default 4096. Nothing recurses on depth, but the standard's scope checks walk the stack, so this also bounds
    /// their cost. Checked before each push.
    /// </summary>
    public int MaxOpenElements { get; init; } = 4096;

    /// <summary>
    /// Most entries on the list of active formatting elements. Default 1024. The standard's "Noah's Ark" check scans
    /// the list on every push, so this bounds its cost. Checked before each push.
    /// </summary>
    public int MaxActiveFormattingElements { get; init; } = 1024;

    /// <summary>
    /// Most attributes stored in one document, counting the copies made when formatting elements are reopened.
    /// Default 4,194,304. Checked before each attribute is stored.
    /// </summary>
    public int MaxTotalAttributes { get; init; } = 4 * 1024 * 1024;
}
