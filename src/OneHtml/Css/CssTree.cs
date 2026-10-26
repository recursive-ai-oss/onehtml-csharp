using System;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>What a <see cref="CssNode"/> is.</summary>
public enum CssNodeKind : byte
{
    /// <summary>The synthetic parent of the top-level nodes of a parse.</summary>
    Root = 0,

    /// <summary>A qualified rule: a prelude and a <c>{}</c> block. Style rules are qualified rules.</summary>
    QualifiedRule = 1,

    /// <summary>An at-rule: an at-keyword, a prelude, and either a <c>;</c> or a <c>{}</c> block.</summary>
    AtRule = 2,

    /// <summary>A declaration: <c>name: value</c> with an optional <c>!important</c>.</summary>
    Declaration = 3,

    /// <summary>
    /// A stretch of source the parser discarded as an invalid construct (CSS Syntax: "return nothing"). It is kept so
    /// that every byte of the source belongs to a node, a token or a comment.
    /// </summary>
    Invalid = 4,
}

/// <summary>Why a single-construct entry point (rule, declaration, component value) returned no result.</summary>
public enum CssParseError : byte
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The input was empty (only whitespace and comments).</summary>
    Empty = 1,

    /// <summary>The input did not parse as the requested construct.</summary>
    Invalid = 2,

    /// <summary>The construct parsed but was followed by more input.</summary>
    ExtraInput = 3,
}

/// <summary>Node table row. Fields are token indexes unless noted.</summary>
internal struct Item
{
    public const byte Important = 1;
    public const byte CustomProperty = 2;
    public const byte HasBlock = 4;
    public const byte BlockClosed = 8;

    public CssNodeKind Kind;
    public byte Flags;
    public int Parent;
    public int FirstChild;
    public int NextSibling;

    /// <summary>Source offsets of the whole node: first character to last character, terminating ";" or "}" included.</summary>
    public int Start;

    public int End;

    /// <summary>The at-keyword or the declaration's name token; -1 for others.</summary>
    public int Name;

    /// <summary>A declaration's colon token; otherwise -1.</summary>
    public int Colon;

    /// <summary>Prelude (rules) or value (declarations) as the token range [V0, V1).</summary>
    public int V0;

    public int V1;

    /// <summary>A rule's "{" and its matching "}" (-1 when the block runs to the end of input); for a declaration, the "!" and "important" tokens (-1 when absent).</summary>
    public int Open;

    public int Close;

    /// <summary>The terminating ";" token of a statement at-rule or declaration, or -1.</summary>
    public int Semi;
}

/// <summary>
/// An immutable parse of CSS text: the tokens, comments, bracket matching and the node tree, as flat arrays. All
/// public views (<see cref="CssNode"/>, <see cref="ComponentValue"/>, <see cref="ComponentValueList"/>) are
/// small structs over these arrays. Build one with the <c>Parse*</c> methods, which implement the entry points of
/// CSS Syntax §5.4.
/// </summary>
public sealed class CssTree
{
    internal readonly Token[] Tokens;
    internal readonly int MainCount;
    internal readonly int[] Match;
    internal readonly int[] CommentOffsets;
    internal readonly int CommentInts;
    internal readonly Item[] Items;
    internal readonly int ItemCount;
    internal readonly int[]? GroupBounds;

    internal CssTree(string text, Token[] tokens, int mainCount, int[] match, int[] comments, int commentInts,
        Item[] items, int itemCount, CssParseError error, int valuesFrom, int valuesTo, int[]? groups)
    {
        Text = text;
        Tokens = tokens;
        MainCount = mainCount;
        Match = match;
        CommentOffsets = comments;
        CommentInts = commentInts;
        Items = items;
        ItemCount = itemCount;
        Error = error;
        ValuesFrom = valuesFrom;
        ValuesTo = valuesTo;
        GroupBounds = groups;
    }

    /// <summary>
    /// True when blocks and functions nested deeper than <see cref="CssParseOptions.MaxNestingDepth"/>. Matching then
    /// stops at the limit: the block that would exceed it and everything after it are read as unclosed blocks that run
    /// to the end of their range, as CSS Syntax treats a block with no closing token at the end of the input.
    /// </summary>
    public bool NestingLimitReached { get; internal init; }

    /// <summary>The token index of the first opener past the nesting limit, or -1.</summary>
    internal int NestingOverflowAt { get; init; } = -1;

    internal int ValuesFrom { get; }

    internal int ValuesTo { get; }

    /// <summary>The text that was parsed. All source ranges are UTF-16 offsets into it.</summary>
    public string Text { get; }

    /// <summary>Set by the single-construct entry points when they found no result; <see cref="CssParseError.None"/> otherwise.</summary>
    public CssParseError Error { get; }

    /// <summary>The number of tokens (whitespace included, comments excluded).</summary>
    public int TokenCount => MainCount;

    /// <summary>The number of comments.</summary>
    public int CommentCount => CommentInts / 2;

    /// <summary>The top-level nodes in source order.</summary>
    public CssNodeList Nodes => new(this, Items[0].FirstChild);

    /// <summary>The root node; its children are <see cref="Nodes"/>.</summary>
    public CssNode Root => new(this, 0);

    /// <summary>
    /// For the component-value entry points, the parsed values: one value for <see cref="ParseComponentValue"/>, all
    /// top-level values for <see cref="ParseComponentValueList"/>.
    /// </summary>
    public ComponentValueList Values => new(this, ValuesFrom, ValuesTo);

    /// <summary>For <see cref="ParseCommaSeparatedList"/>, the groups split at top-level commas.</summary>
    public int GroupCount => GroupBounds is null ? 0 : GroupBounds.Length / 2;

    /// <summary>Gets one group of a comma-separated parse.</summary>
    /// <param name="index">The zero-based group index.</param>
    public ComponentValueList GetGroup(int index)
    {
        if (GroupBounds is null || (uint)index >= (uint)(GroupBounds.Length / 2))
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new ComponentValueList(this, GroupBounds[2 * index], GroupBounds[(2 * index) + 1]);
    }

    /// <summary>Gets a token by index (0 to <see cref="TokenCount"/> - 1).</summary>
    /// <param name="index">The token index.</param>
    public CssToken GetToken(int index)
    {
        if ((uint)index >= (uint)MainCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new CssToken(this, index);
    }

    /// <summary>Gets the source range of a comment.</summary>
    /// <param name="index">The comment index, in source order.</param>
    public CssRange GetComment(int index)
    {
        if ((uint)index >= (uint)CommentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new CssRange(CommentOffsets[2 * index], CommentOffsets[(2 * index) + 1]);
    }

    /// <summary>Parses a stylesheet (CSS Syntax §5.4.3). Never throws for malformed CSS.</summary>
    /// <param name="text">Decoded stylesheet text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <exception cref="CssLimitExceededException">A limit in <paramref name="options"/> was exceeded.</exception>
    public static CssTree ParseStylesheet(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.Stylesheet, options, cancellationToken);

    /// <summary>Parses a list of declarations, as for a <c>style</c> attribute (CSS Syntax §5.4.5).</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <remarks>
    /// At-rules in the list are kept as <see cref="CssNodeKind.AtRule"/> nodes (the specification discards them), and
    /// discarded constructs as <see cref="CssNodeKind.Invalid"/> nodes. Use <see cref="CssNode.Declarations"/> on
    /// <see cref="Root"/> for just the declarations.
    /// </remarks>
    public static CssTree ParseDeclarationList(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.DeclarationList, options, cancellationToken);

    /// <summary>Parses the contents of a block: declarations, at-rules and qualified rules mixed (CSS Syntax §5.5.5).</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static CssTree ParseBlockContents(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.BlockContents, options, cancellationToken);

    /// <summary>Parses exactly one rule (CSS Syntax §5.4.6). See <see cref="Error"/>.</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static CssTree ParseRule(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.Rule, options, cancellationToken);

    /// <summary>Parses exactly one declaration (CSS Syntax §5.4.7). See <see cref="Error"/>.</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static CssTree ParseDeclaration(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.Declaration, options, cancellationToken);

    /// <summary>Parses exactly one component value (CSS Syntax §5.4.8). See <see cref="Error"/> and <see cref="Values"/>.</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static CssTree ParseComponentValue(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.ComponentValue, options, cancellationToken);

    /// <summary>Parses a list of component values (CSS Syntax §5.4.9). See <see cref="Values"/>.</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static CssTree ParseComponentValueList(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.ComponentValueList, options, cancellationToken);

    /// <summary>Parses a comma-separated list of component values (CSS Syntax §5.4.10). See <see cref="GroupCount"/>.</summary>
    /// <param name="text">Decoded text.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static CssTree ParseCommaSeparatedList(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => CssTreeBuilder.Build(text, CssEntry.CommaSeparatedList, options, cancellationToken);
}

internal enum CssEntry
{
    Stylesheet,
    DeclarationList,
    BlockContents,
    Rule,
    Declaration,
    ComponentValue,
    ComponentValueList,
    CommaSeparatedList,
}
