using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>Why a node is hidden from the accessibility tree or ignored by it. Several can apply at once.</summary>
[Flags]
public enum HiddenReasons : ushort
{
    /// <summary>Not hidden.</summary>
    None = 0,

    /// <summary><c>display: none</c>, computed on the element itself (CSS Display 3 section 2.5).</summary>
    DisplayNone = 1,

    /// <summary>The <c>hidden</c> attribute, which the HTML Standard's rendering section turns into <c>display: none</c> (HTML section 15.3.1).</summary>
    HiddenAttribute = 2,

    /// <summary>A <c>template</c> element or its contents: never rendered.</summary>
    Template = 4,

    /// <summary>Content of a <c>details</c> element that is not open, other than its first <c>summary</c> (HTML section 4.11.1).</summary>
    ClosedDetails = 8,

    /// <summary>Inside an element whose <c>content-visibility</c> is <c>hidden</c> (CSS Containment 2 section 4).</summary>
    ContentVisibilityHidden = 16,

    /// <summary><c>visibility: hidden</c> or <c>collapse</c> computed on the element (CSS Display 3 section 3.4). A descendant can show again.</summary>
    VisibilityHidden = 32,

    /// <summary><c>aria-hidden="true"</c> on the element or an ancestor (WAI-ARIA 1.2 section 6.6.1).</summary>
    AriaHidden = 64,

    /// <summary>The <c>inert</c> attribute on the element or an ancestor (HTML section 6.6.3).</summary>
    Inert = 128,
}

/// <summary>What is known about hiding that depends on layout: zero size, clipping, off-screen positions.</summary>
public enum LayoutHiding : byte
{
    /// <summary>Not known: it needs layout, which this library does not do.</summary>
    Unknown = 0,
}

/// <summary>Hidden status of a node and where it comes from.</summary>
/// <param name="Reasons">Every reason that applies.</param>
/// <param name="Origin">The element the strongest reason is set on (the node itself or an ancestor); null node when nothing hides it.</param>
/// <param name="IsInherited">True when <paramref name="Origin"/> is an ancestor and not the node itself.</param>
public readonly record struct HiddenInfo(HiddenReasons Reasons, Node Origin, bool IsInherited)
{
    /// <summary>True when any reason applies.</summary>
    public bool IsHidden => Reasons != HiddenReasons.None;

    /// <summary>True when the node generates no accessibility object at all (not rendered, or its contents are skipped).</summary>
    public bool IsNotRendered => (Reasons & (HiddenReasons.DisplayNone | HiddenReasons.HiddenAttribute | HiddenReasons.Template | HiddenReasons.ClosedDetails | HiddenReasons.ContentVisibilityHidden)) != 0;

    /// <summary>The reason reported first: not rendered kinds, then <c>aria-hidden</c>, then <c>inert</c>, then <c>visibility</c>.</summary>
    public HiddenReasons Primary
    {
        get
        {
            ReadOnlySpan<HiddenReasons> order =
            [
                HiddenReasons.DisplayNone, HiddenReasons.HiddenAttribute, HiddenReasons.Template, HiddenReasons.ClosedDetails,
                HiddenReasons.ContentVisibilityHidden, HiddenReasons.AriaHidden, HiddenReasons.Inert, HiddenReasons.VisibilityHidden,
            ];
            foreach (HiddenReasons r in order)
            {
                if ((Reasons & r) != 0)
                {
                    return r;
                }
            }

            return HiddenReasons.None;
        }
    }

    /// <summary>Hiding that depends on layout is not known without rendered facts.</summary>
    public static LayoutHiding LayoutDependent => LayoutHiding.Unknown;
}

/// <summary>Which step of the accessible name computation produced a text (AccName 1.2 section 4.3.2).</summary>
public enum AxTextSource : byte
{
    /// <summary>No text.</summary>
    None = 0,

    /// <summary><c>aria-labelledby</c> (step 2B).</summary>
    LabelledBy,

    /// <summary><c>aria-label</c> (step 2C).</summary>
    AriaLabel,

    /// <summary>A native label element: <c>label</c>, <c>legend</c>, <c>caption</c>, <c>figcaption</c>, SVG <c>title</c> (step 2D).</summary>
    NativeLabelElement,

    /// <summary>A native attribute: <c>alt</c>, button <c>value</c>, <c>optgroup</c> or <c>track</c> <c>label</c> (step 2D).</summary>
    NativeAttribute,

    /// <summary>The element's contents (step 2F).</summary>
    Contents,

    /// <summary>The <c>title</c> attribute (step 2I).</summary>
    Title,

    /// <summary>The <c>placeholder</c> attribute (HTML-AAM section 4.1.2 fallback).</summary>
    Placeholder,

    /// <summary>The text of a text node (step 2G).</summary>
    TextNode,

    /// <summary>The document's <c>title</c> element.</summary>
    DocumentTitle,

    /// <summary><c>aria-describedby</c>.</summary>
    DescribedBy,

    /// <summary><c>aria-description</c>.</summary>
    AriaDescription,

    /// <summary>The <c>summary</c> element of a <c>details</c> element.</summary>
    Summary,
}

/// <summary>A name or description and where it came from.</summary>
/// <param name="Value">The text, with whitespace collapsed and trimmed; empty when there is none.</param>
/// <param name="Source">The step that produced it, <see cref="AxTextSource.None"/> when <paramref name="Value"/> is empty.</param>
/// <param name="Attribute">For attribute sources, the attribute name.</param>
/// <param name="RelatedElements">For <c>aria-labelledby</c>, <c>aria-describedby</c> and native label elements, the elements read.</param>
public readonly record struct AxText(string Value, AxTextSource Source, string? Attribute, IReadOnlyList<Node> RelatedElements)
{
    /// <summary>The empty text.</summary>
    public static AxText Empty { get; } = new(string.Empty, AxTextSource.None, null, []);

    /// <summary>True when there is no text.</summary>
    public bool IsEmpty => Value.Length == 0;

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>The kinds of tree diagnostics.</summary>
public enum AxDiagnosticKind : byte
{
    /// <summary>A diagnostic of the role layer; see <see cref="AxDiagnostic.RoleKind"/>.</summary>
    Role,

    /// <summary>An ID reference in a relation attribute names no element, or only an element outside the tree.</summary>
    BrokenIdReference,

    /// <summary>A role requires a parent role the final tree does not provide (WAI-ARIA 1.2 section 5.2.3).</summary>
    MissingRequiredContext,

    /// <summary>A role requires owned elements the final tree does not provide (WAI-ARIA 1.2 section 5.2.4).</summary>
    MissingRequiredOwnedElements,

    /// <summary>A focusable element is hidden by <c>aria-hidden</c>: it can be reached but is not exposed (WAI-ARIA 1.2 section 6.6.1).</summary>
    FocusableUnderAriaHidden,

    /// <summary>An interactive element has no accessible name.</summary>
    NamelessInteractive,

    /// <summary>A role that requires a name has an empty one.</summary>
    EmptyNameRequired,

    /// <summary>An <c>aria-owns</c> reference was not honoured; <see cref="AxDiagnostic.Message"/> says why.</summary>
    AriaOwnsIgnored,

    /// <summary>A limit was reached and the result is partial for this node.</summary>
    LimitExceeded,
}

/// <summary>One thing found while computing the tree.</summary>
/// <param name="Kind">What kind of finding.</param>
/// <param name="Node">The element it concerns.</param>
/// <param name="Attribute">The attribute involved, if any.</param>
/// <param name="Value">The offending value, if any.</param>
/// <param name="Message">A short English description.</param>
/// <param name="RoleKind">For <see cref="AxDiagnosticKind.Role"/>, the role layer's kind.</param>
public readonly record struct AxDiagnostic(AxDiagnosticKind Kind, Node Node, string? Attribute, string? Value, string Message, Aria.RoleDiagnosticKind? RoleKind = null);

/// <summary>The relations a node has to other nodes.</summary>
public enum AxRelationKind : byte
{
    /// <summary><c>aria-owns</c>, after re-parenting.</summary>
    Owns,

    /// <summary><c>aria-controls</c> and the native <c>list</c> attribute.</summary>
    Controls,

    /// <summary><c>aria-labelledby</c>.</summary>
    LabelledBy,

    /// <summary><c>aria-describedby</c>.</summary>
    DescribedBy,

    /// <summary><c>aria-details</c>.</summary>
    Details,

    /// <summary><c>aria-errormessage</c>.</summary>
    ErrorMessage,

    /// <summary><c>aria-flowto</c>.</summary>
    FlowTo,

    /// <summary><c>aria-activedescendant</c>.</summary>
    ActiveDescendant,
}

/// <summary>An ID-reference relation of a node, resolved.</summary>
/// <param name="Kind">Which relation.</param>
/// <param name="Attribute">The attribute that states it.</param>
/// <param name="Elements">The elements the valid IDs name, in attribute order, without repeats. For <see cref="AxRelationKind.Owns"/> only the elements that were adopted.</param>
/// <param name="Targets">The tree nodes of <paramref name="Elements"/> that have one (an element that is not rendered has none).</param>
/// <param name="BrokenIds">The IDs that name no element.</param>
public readonly record struct AxRelation(AxRelationKind Kind, string Attribute, IReadOnlyList<Node> Elements, IReadOnlyList<AxNode> Targets, IReadOnlyList<string> BrokenIds);

/// <summary>The tristate of <c>aria-checked</c> and <c>aria-pressed</c>.</summary>
public enum AxTristate : byte
{
    /// <summary>false</summary>
    False = 0,

    /// <summary>true</summary>
    True,

    /// <summary>mixed</summary>
    Mixed,
}

/// <summary>States and properties of a node (WAI-ARIA 1.2 sections 6.2 and 6.3), from ARIA attributes and the HTML-AAM native mappings. A null member means the node does not have it.</summary>
public sealed class AxStates
{
    internal AxStates()
    {
    }

    /// <summary>Expanded or collapsed.</summary>
    public bool? Expanded { get; internal set; }

    /// <summary>Checked, unchecked or mixed.</summary>
    public AxTristate? Checked { get; internal set; }

    /// <summary>Selected.</summary>
    public bool? Selected { get; internal set; }

    /// <summary>Pressed, not pressed or mixed.</summary>
    public AxTristate? Pressed { get; internal set; }

    /// <summary>Disabled, by <c>aria-disabled</c>, the native attribute, a disabled fieldset or an ancestor's <c>aria-disabled</c>.</summary>
    public bool Disabled { get; internal set; }

    /// <summary>Required.</summary>
    public bool Required { get; internal set; }

    /// <summary><c>aria-invalid</c>: <c>true</c>, <c>grammar</c> or <c>spelling</c>; null when absent or false.</summary>
    public string? Invalid { get; internal set; }

    /// <summary>Read-only.</summary>
    public bool ReadOnly { get; internal set; }

    /// <summary>Hierarchical level (headings, <c>aria-level</c>).</summary>
    public int? Level { get; internal set; }

    /// <summary>Position in the set, given or computed (Core-AAM section 5.4).</summary>
    public int? PosInSet { get; internal set; }

    /// <summary>Size of the set, given or computed.</summary>
    public int? SetSize { get; internal set; }

    /// <summary>The current value of a range or the text value (<c>aria-valuenow</c> or native).</summary>
    public double? ValueNow { get; internal set; }

    /// <summary>Minimum of a range.</summary>
    public double? ValueMin { get; internal set; }

    /// <summary>Maximum of a range.</summary>
    public double? ValueMax { get; internal set; }

    /// <summary><c>aria-valuetext</c>.</summary>
    public string? ValueText { get; internal set; }

    /// <summary>The value a widget shows: the text of a text field, the chosen option of a combobox, the number of a range.</summary>
    public string? Value { get; internal set; }

    /// <summary><c>aria-live</c>: <c>off</c>, <c>polite</c> or <c>assertive</c>; includes the role's implicit value.</summary>
    public string? Live { get; internal set; }

    /// <summary><c>aria-atomic</c>.</summary>
    public bool? Atomic { get; internal set; }

    /// <summary><c>aria-relevant</c>.</summary>
    public string? Relevant { get; internal set; }

    /// <summary><c>aria-busy</c>.</summary>
    public bool Busy { get; internal set; }

    /// <summary><c>aria-modal</c>.</summary>
    public bool Modal { get; internal set; }

    /// <summary><c>aria-current</c>: <c>true</c>, <c>page</c>, <c>step</c>, <c>location</c>, <c>date</c> or <c>time</c>; null when absent or false.</summary>
    public string? Current { get; internal set; }

    /// <summary><c>aria-haspopup</c>: <c>true</c>, <c>menu</c>, <c>listbox</c>, <c>tree</c>, <c>grid</c> or <c>dialog</c>; null when absent or false.</summary>
    public string? HasPopup { get; internal set; }

    /// <summary><c>aria-orientation</c>, or the role's default.</summary>
    public string? Orientation { get; internal set; }

    /// <summary><c>aria-multiselectable</c> or a <c>select</c> with <c>multiple</c>.</summary>
    public bool MultiSelectable { get; internal set; }

    /// <summary><c>aria-multiline</c> or a <c>textarea</c>.</summary>
    public bool Multiline { get; internal set; }

    /// <summary><c>aria-autocomplete</c>, when not <c>none</c>.</summary>
    public string? AutoComplete { get; internal set; }

    /// <summary><c>aria-keyshortcuts</c>.</summary>
    public string? KeyShortcuts { get; internal set; }

    /// <summary><c>aria-roledescription</c>.</summary>
    public string? RoleDescription { get; internal set; }

    /// <summary>The target of a link, as written in <c>href</c>.</summary>
    public string? Url { get; internal set; }

    /// <summary>Editable text: <c>plaintext</c> for text controls, <c>richtext</c> for <c>contenteditable</c>.</summary>
    public string? Editable { get; internal set; }
}

/// <summary>Keyboard focus facts of a node (HTML section 6.6).</summary>
/// <param name="IsFocusable">The node can take focus now: focusable by markup, rendered, visible, enabled and not inert.</param>
/// <param name="TabIndex">The parsed <c>tabindex</c> attribute, if valid.</param>
/// <param name="SequentialIndex">Position in the sequential focus navigation order, or -1 when the node is not in it (not focusable, or <c>tabindex</c> negative).</param>
public readonly record struct AxFocus(bool IsFocusable, int? TabIndex, int SequentialIndex)
{
    /// <summary>True when sequential navigation reaches the node.</summary>
    public bool IsTabbable => SequentialIndex >= 0;
}

/// <summary>Limits and switches for <see cref="AccessibilityTree.Build"/>.</summary>
public sealed class AccessibilityOptions
{
    /// <summary>The most nodes all name computations together may visit; past it, names fall back to attributes only and a limit diagnostic is recorded. Default 32 million.</summary>
    public int MaxNameWork { get; init; } = 32_000_000;

    /// <summary>The longest name or description kept, in UTF-16 units; longer text is cut. Default 65536.</summary>
    public int MaxNameLength { get; init; } = 65536;

    /// <summary>The most ID references read from one attribute. Default 4096.</summary>
    public int MaxIdReferences { get; init; } = 4096;

    /// <summary>The most steps all <c>aria-owns</c> ancestor checks together may take; past it, further <c>aria-owns</c> references are ignored. Default 16 million.</summary>
    public int MaxOwnsWork { get; init; } = 16_000_000;
}
