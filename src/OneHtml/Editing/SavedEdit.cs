using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>What a saved edit's target points at.</summary>
public enum SavedTargetKind
{
    /// <summary>An element, described by locator, fingerprint and source anchor.</summary>
    Element = 0,

    /// <summary>A text node: its parent element and its position among the parent's child nodes.</summary>
    Text = 1,

    /// <summary>A comment node, described like a text node.</summary>
    Comment = 2,

    /// <summary>A CSS rule: sheet, selector, position among identical selectors, enclosing at-rules and a declarations fingerprint.</summary>
    Rule = 3,
}

/// <summary>
/// A durable description of what an edit applies to. Which members are filled depends on <see cref="Kind"/>; absent
/// members are <see langword="null"/> (or -1 for numbers). The members mirror the saved-edit JSON.
/// </summary>
public sealed class SavedTarget
{
    /// <summary>What kind of target this is.</summary>
    public SavedTargetKind Kind { get; init; }

    /// <summary>Element: a selector that matched exactly this element in the document the edit was made on.</summary>
    public string? Locator { get; init; }

    /// <summary>Element: the fingerprint's tag name.</summary>
    public string? Tag { get; init; }

    /// <summary>Element: the fingerprint's key attributes, sorted by name.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; init; } = [];

    /// <summary>Element, text and comment: <c>sha256:</c> hash of the whitespace-normalized text content.</summary>
    public string? TextHash { get; init; }

    /// <summary>Element: <c>sha256:</c> hash of the ancestor path with positions.</summary>
    public string? PathHash { get; init; }

    /// <summary>Whether the JSON had a <c>fingerprint</c> object (an element target without one cannot be verified).</summary>
    public bool HasFingerprint { get; init; }

    /// <summary>Node targets: the first byte of the node's source in the document the edit was made on, or -1.</summary>
    public long AnchorStart { get; init; } = -1;

    /// <summary>Node targets: the byte after the node's source, or -1.</summary>
    public long AnchorEnd { get; init; } = -1;

    /// <summary>Node targets: <c>sha256:</c> hash of the source bytes of [<see cref="AnchorStart"/>, <see cref="AnchorEnd"/>).</summary>
    public string? AnchorBytes { get; init; }

    /// <summary>Text and comment: the element that contains the node.</summary>
    public SavedTarget? Parent { get; init; }

    /// <summary>Text and comment: the node's position among its parent's child nodes, from 0, or -1.</summary>
    public int ChildIndex { get; init; } = -1;

    /// <summary>Rule: <c>inline:n</c> or the URL of a supplied linked sheet.</summary>
    public string? Sheet { get; init; }

    /// <summary>Rule: the whitespace-normalized selector (style rule) or <c>@name prelude</c> (at-rule).</summary>
    public string? Selector { get; init; }

    /// <summary>Rule: position among the rules with the same selector inside the same at-rules, in sheet order, from 0; -1 when absent.</summary>
    public int Index { get; init; } = -1;

    /// <summary>Rule: the selectors or at-rule heads of the enclosing rules, outermost first.</summary>
    public IReadOnlyList<string> AtRules { get; init; } = [];

    /// <summary>Rule: <c>sha256:</c> hash of the rule's own declarations.</summary>
    public string? DeclarationsHash { get; init; }
}

/// <summary>
/// One saved edit: an operation name, its target and the operation's fields. Fields an operation does not use are
/// <see langword="null"/>. The operation names are those of the saved-edit format (<c>setAttribute</c>,
/// <c>insertRule</c>, ...); the reader keeps unknown names so a replay can report them.
/// </summary>
public sealed class SavedEdit
{
    /// <summary>The operation name.</summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>What the edit applies to. For <c>insertRule</c>, the reference rule of <c>before</c>, <c>after</c> or a block <c>end</c>.</summary>
    public SavedTarget? Target { get; init; }

    /// <summary><c>insertRule</c>: <c>before</c>, <c>after</c> or <c>end</c>.</summary>
    public string? Placement { get; init; }

    /// <summary>The attribute name of the attribute operations.</summary>
    public string? Name { get; init; }

    /// <summary>An attribute or declaration value.</summary>
    public string? Value { get; init; }

    /// <summary>The text of <c>replaceText</c>.</summary>
    public string? Text { get; init; }

    /// <summary>The markup of the insertion, wrapping and replacement operations.</summary>
    public string? Html { get; init; }

    /// <summary>The CSS rule text of <c>insertRule</c> and <c>replaceRule</c>.</summary>
    public string? Css { get; init; }

    /// <summary>The property of the style and declaration operations.</summary>
    public string? Property { get; init; }

    /// <summary>Whether the declaration is <c>!important</c>; false when absent.</summary>
    public bool Important { get; init; }

    /// <summary><c>wrap</c>: how many contiguous siblings go inside the wrapper; 1 when absent.</summary>
    public int Count { get; init; } = 1;

    /// <summary><c>insertRule</c> at the end of a sheet: the sheet's name.</summary>
    public string? Sheet { get; init; }

    /// <summary>The edit's opaque metadata as compact JSON text, or <see langword="null"/> when there is none. The library never reads it.</summary>
    public string? MetaJson { get; init; }
}

/// <summary>A saved list of edits: the unit of the edit JSON format, version 1.</summary>
public sealed class EditEnvelope
{
    /// <summary>The value of the <c>format</c> member.</summary>
    public const string FormatName = "onehtml-edits";

    /// <summary>The version this library reads and writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Gathers saved edits.</summary>
    /// <param name="edits">The edits in order.</param>
    /// <param name="documentUrl">Informative: the URL of the document the edits were made against.</param>
    /// <param name="documentSha256">Informative: the SHA-256 of that document's bytes, as lower-case hex.</param>
    public EditEnvelope(IEnumerable<SavedEdit> edits, string? documentUrl = null, string? documentSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(edits);
        Edits = new List<SavedEdit>(edits);
        DocumentUrl = documentUrl;
        DocumentSha256 = documentSha256;
    }

    /// <summary>The edits in order.</summary>
    public IReadOnlyList<SavedEdit> Edits { get; }

    /// <summary>Informative: the URL of the document the edits were made against.</summary>
    public string? DocumentUrl { get; }

    /// <summary>Informative: the SHA-256 of that document's bytes.</summary>
    public string? DocumentSha256 { get; }
}

/// <summary>The edit JSON could not be read or written.</summary>
public sealed class EditJsonException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="position">The byte offset in the input, or -1.</param>
    public EditJsonException(string message, long position = -1)
        : base(position >= 0 ? message + " (at byte " + position.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : message)
    {
        Position = position;
    }

    /// <summary>The byte offset in the input where the problem was found, or -1.</summary>
    public long Position { get; }
}

/// <summary>Bounds on the edit JSON reader. Each is checked before the work it guards.</summary>
public sealed class EditJsonLimits
{
    /// <summary>The default limits.</summary>
    public static EditJsonLimits Default { get; } = new();

    /// <summary>The largest accepted input in bytes. Default 32 MiB.</summary>
    public int MaxBytes { get; init; } = 32 << 20;

    /// <summary>The deepest JSON nesting accepted anywhere, unknown members and metadata included. Default 32.</summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>The most edits in one envelope. Default 100,000.</summary>
    public int MaxEdits { get; init; } = 100_000;

    /// <summary>The longest string value in bytes of JSON text. Default 16 MiB.</summary>
    public int MaxStringBytes { get; init; } = 16 << 20;

    /// <summary>The largest <c>meta</c> value of one edit in bytes. Default 64 KiB.</summary>
    public int MaxMetaBytes { get; init; } = 64 << 10;

    /// <summary>The most key attributes in one fingerprint. Default 64.</summary>
    public int MaxAttributes { get; init; } = 64;

    /// <summary>The most enclosing at-rules in one rule target. Default 64.</summary>
    public int MaxAtRules { get; init; } = 64;
}
