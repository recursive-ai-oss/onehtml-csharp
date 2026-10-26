using System;
using RecursiveAI.OneHtml.Selectors;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The origin of a style sheet (CSS Cascade 5 §6.2).</summary>
public enum StyleOrigin : byte
{
    /// <summary>The user-agent sheet: the CSS of the HTML Standard's Rendering section.</summary>
    UserAgent = 0,

    /// <summary>Sheets the caller supplies as the user's own. None by default.</summary>
    User = 1,

    /// <summary>The page's own sheets: <c>style</c> and <c>link</c> elements, <c>style=""</c> attributes and presentational hints.</summary>
    Author = 2,
}

/// <summary>The bytes or text of a style sheet the caller fetched.</summary>
public sealed class StylesheetResource
{
    /// <summary>A sheet as bytes. The encoding is decided by the CSS Syntax rules: the label, an <c>@charset</c>, then UTF-8.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="protocolEncodingLabel">The <c>charset</c> of the transport's <c>Content-Type</c>, if any.</param>
    public StylesheetResource(byte[] bytes, string? protocolEncodingLabel = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Bytes = bytes;
        ProtocolEncodingLabel = protocolEncodingLabel;
    }

    /// <summary>A sheet that is already text.</summary>
    /// <param name="text">The text.</param>
    public StylesheetResource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The bytes, or null when the resource is text.</summary>
    public byte[]? Bytes { get; }

    /// <summary>The text, or null when the resource is bytes.</summary>
    public string? Text { get; }

    /// <summary>The transport's encoding label, or null.</summary>
    public string? ProtocolEncodingLabel { get; }
}

/// <summary>Returns the sheet for an absolute URL, or null when the caller does not have it. The library never fetches.</summary>
/// <param name="absoluteUrl">The URL of a <c>link</c> or <c>@import</c>, resolved against the document's base URL, without its fragment.</param>
public delegate StylesheetResource? StylesheetResolver(string absoluteUrl);

/// <summary>Where the style sheets of a document come from, and bounds on them.</summary>
public sealed class StyleSourceOptions
{
    /// <summary>The document's URL, used as the base for relative <c>href</c>s unless a <c>base</c> element says otherwise. Without it relative URLs cannot be resolved and are reported missing.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Supplies linked and imported sheets by absolute URL. Without it linked sheets are reported missing.</summary>
    public StylesheetResolver? Resolver { get; init; }

    /// <summary>Sheets of the user origin, in order. Empty by default.</summary>
    public IReadOnlyList<Stylesheet>? UserSheets { get; init; }

    /// <summary>Whether the user-agent sheet applies; true by default.</summary>
    public bool IncludeUserAgentSheet { get; init; } = true;

    /// <summary>Limits for sheets the library parses (<c>style</c> text, resolved sheets, <c>style=""</c>); the defaults of <see cref="CssParseOptions"/> when null.</summary>
    public CssParseOptions? ParseOptions { get; init; }

    /// <summary>The deepest <c>@import</c> chain followed. Deeper imports are ignored and reported. Default 16.</summary>
    public int MaxImportDepth { get; init; } = 16;

    /// <summary>The most sheets (including imports) taken into one document. Default 4,096.</summary>
    public int MaxSheets { get; init; } = 4096;

    /// <summary>The most style rules (nested rules and declaration runs count) taken into one document. Default 2,000,000.</summary>
    public int MaxRules { get; init; } = 2_000_000;

    /// <summary>The most cascade layers per origin. Default 65,536.</summary>
    public int MaxLayers { get; init; } = 1 << 16;
}

/// <summary>What the style-source gatherer could not do.</summary>
public enum StyleDiagnosticKind : byte
{
    /// <summary>A linked or imported sheet was not supplied by the resolver (or its URL could not be resolved).</summary>
    MissingStylesheet,

    /// <summary>An <c>@import</c> would load a sheet that is already being imported.</summary>
    ImportCycle,

    /// <summary>An <c>@import</c> chain is deeper than <see cref="StyleSourceOptions.MaxImportDepth"/>.</summary>
    ImportDepthExceeded,

    /// <summary>A bound of <see cref="StyleSourceOptions"/> was reached; the rest of the input is ignored.</summary>
    LimitExceeded,

    /// <summary>A rule this library does not evaluate (<c>@scope</c>, <c>@container</c>) was skipped.</summary>
    UnsupportedRule,

    /// <summary>A style rule's selector could not be parsed (or nesting made it too long), so the rule is dropped.</summary>
    InvalidSelector,
}

/// <summary>One thing reported while gathering style sources.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Message">A sentence about it.</param>
/// <param name="Url">The URL involved, if any.</param>
public sealed record StyleDiagnostic(StyleDiagnosticKind Kind, string Message, string? Url = null);

/// <summary>A style sheet taken into a document's cascade.</summary>
public sealed class StyleSheetInfo
{
    internal StyleSheetInfo(Stylesheet sheet, StyleOrigin origin, Node owner, string? url, bool isImport)
    {
        Sheet = sheet;
        Origin = origin;
        Owner = owner;
        Url = url;
        IsImport = isImport;
    }

    /// <summary>The parsed sheet.</summary>
    public Stylesheet Sheet { get; }

    /// <summary>Its origin.</summary>
    public StyleOrigin Origin { get; }

    /// <summary>The <c>style</c> or <c>link</c> element it came from; the null node for the user-agent sheet, user sheets and imports.</summary>
    public Node Owner { get; }

    /// <summary>The absolute URL it was loaded from, or null for <c>style</c> text and built-in sheets.</summary>
    public string? Url { get; }

    /// <summary>True when an <c>@import</c> loaded it.</summary>
    public bool IsImport { get; }
}

/// <summary>What kind of declaration a value came from.</summary>
public enum DeclarationKind : byte
{
    /// <summary>A rule of a style sheet.</summary>
    Sheet,

    /// <summary>A <c>style=""</c> attribute.</summary>
    InlineStyle,

    /// <summary>An HTML presentational attribute (<c>bgcolor</c>, <c>font color</c>, …), treated as an author rule of zero specificity.</summary>
    PresentationalHint,
}

/// <summary>Where a winning declaration is: the sheet, the rule and the source range, for "where did this value come from".</summary>
public sealed class DeclarationSource
{
    internal DeclarationSource(DeclarationKind kind, StyleOrigin origin, CssNode declaration, CssNode rule, Stylesheet? sheet, Node owner, bool important, string layerName, string? selectorText, string? sheetUrl)
    {
        Kind = kind;
        Origin = origin;
        Declaration = declaration;
        Rule = rule;
        Sheet = sheet;
        Owner = owner;
        IsImportant = important;
        LayerName = layerName;
        SelectorText = selectorText;
        SheetUrl = sheetUrl;
    }

    /// <summary>Sheet rule, inline style or presentational hint.</summary>
    public DeclarationKind Kind { get; }

    /// <summary>The origin of the sheet.</summary>
    public StyleOrigin Origin { get; }

    /// <summary>The declaration node. Its <see cref="CssNode.Range"/> is a range of the sheet's text, or of the attribute value for <see cref="DeclarationKind.InlineStyle"/>.</summary>
    public CssNode Declaration { get; }

    /// <summary>The style rule that holds the declaration (the default node for inline style and hints). For a nested rule this is the nested rule.</summary>
    public CssNode Rule { get; }

    /// <summary>The sheet, or null for inline style and hints.</summary>
    public Stylesheet? Sheet { get; }

    /// <summary>The <c>style</c>/<c>link</c> element of the sheet, the element carrying the <c>style=""</c> attribute or hint, or the null node for built-in and user sheets.</summary>
    public Node Owner { get; }

    /// <summary>Whether the declaration is <c>!important</c>.</summary>
    public bool IsImportant { get; }

    /// <summary>The cascade layer path (<c>a.b</c>), empty when unlayered; anonymous layers print as <c>(anonymous)</c> segments.</summary>
    public string LayerName { get; }

    /// <summary>The (nesting-resolved) selector text of the rule, or null.</summary>
    public string? SelectorText { get; }

    /// <summary>The URL of the sheet, if it has one.</summary>
    public string? SheetUrl { get; }
}

/// <summary>
/// A cascaded value: the winning declaration's value for a property, after CSS-wide keywords, <c>var()</c> and inheritance
/// are resolved. Values stay token lists (<see cref="Tokens"/>); typed access is on <see cref="ComputedStyle"/>.
/// </summary>
public sealed class StyleValue
{
    internal StyleValue(int propertyId, string? customName, ComponentValueList tokens, CssWideKeyword keyword, ParsedDeclaration? declaration, bool pending, string? text)
    {
        PropertyId = propertyId;
        CustomName = customName;
        Tokens = tokens;
        Keyword = keyword;
        Declaration = declaration;
        Pending = pending;
        _text = text;
    }

    private string? _text;

    internal int PropertyId { get; }

    internal string? CustomName { get; }

    internal CssWideKeyword Keyword { get; }

    internal ParsedDeclaration? Declaration { get; }

    internal bool Pending { get; }

    /// <summary>The value as component values. Empty for an initial value the property table has no CSS text for.</summary>
    public ComponentValueList Tokens { get; }

    /// <summary>The value as CSS text, trimmed (after <c>var()</c> substitution).</summary>
    public string Text => _text ??= Tokens.Trimmed().Raw.ToString();

    /// <summary>Where the value was declared, or null for an initial value.</summary>
    public DeclarationSource? Source => Declaration?.Source;

    /// <summary>True when the value is the property's initial value (no declaration won, or <c>initial</c> was used).</summary>
    public bool IsInitial { get; internal init; }

    /// <summary>True when the value was taken from the parent (inherited, or by <c>inherit</c>).</summary>
    public bool IsInherited { get; internal init; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>One element reported changed to <see cref="DocumentStyles.Restyle"/>.</summary>
/// <param name="NodeIndex">The element's <see cref="Node.Index"/> in the new document (the same as in the old one when <see cref="StyleChange.OldToNew"/> is null).</param>
/// <param name="AttributeNames">The names of the attributes that changed (added, removed or given another value), lower case; null means "any attribute may have changed".</param>
public readonly record struct ElementChange(int NodeIndex, IReadOnlyList<string>? AttributeNames = null);

/// <summary>What changed between the document a <see cref="DocumentStyles"/> was built for and the next one.</summary>
public sealed class StyleChange
{
    /// <summary>
    /// Elements to restyle, by index in the new document: elements whose attributes changed (name them in <see cref="ElementChange.AttributeNames"/>
    /// when known), and elements whose children or text changed. Without <see cref="OldToNew"/> the new document must have the same
    /// tree shape (same node indices, parents and kinds). With it, added, removed and moved elements are found by comparing the
    /// two trees, so they need not be listed.
    /// </summary>
    public IReadOnlyList<ElementChange> Elements { get; init; } = [];

    /// <summary>
    /// For a new document whose node indices differ from the old one (an edited document is parsed again): for each old node index,
    /// the index of the same node in the new document, or -1 when it was removed. Length must be the old node count. Null means
    /// the node indices are the same in both.
    /// </summary>
    public IReadOnlyList<int>? OldToNew { get; init; }

    /// <summary>True when any style sheet changed (a <c>style</c> element's text, a linked sheet's bytes, a <c>link</c> element added, removed or changed): the sheets are gathered again and only elements the changed rules reach are restyled.</summary>
    public bool SheetsChanged { get; init; }
}
