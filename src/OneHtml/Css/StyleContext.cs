using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The media type of the output being styled (Media Queries 5 §2.1). Deprecated types never match.</summary>
public enum MediaType : byte
{
    /// <summary>Screen output.</summary>
    Screen,

    /// <summary>Print output.</summary>
    Print,
}

/// <summary><c>prefers-color-scheme</c>.</summary>
public enum PrefersColorScheme : byte
{
    /// <summary><c>light</c>.</summary>
    Light,

    /// <summary><c>dark</c>.</summary>
    Dark,
}

/// <summary><c>prefers-reduced-motion</c>, <c>prefers-reduced-transparency</c> and <c>prefers-reduced-data</c>.</summary>
public enum PrefersReduced : byte
{
    /// <summary><c>no-preference</c>.</summary>
    NoPreference,

    /// <summary><c>reduce</c>.</summary>
    Reduce,
}

/// <summary><c>prefers-contrast</c>.</summary>
public enum PrefersContrast : byte
{
    /// <summary><c>no-preference</c>.</summary>
    NoPreference,

    /// <summary><c>more</c>.</summary>
    More,

    /// <summary><c>less</c>.</summary>
    Less,

    /// <summary><c>custom</c>.</summary>
    Custom,
}

/// <summary>Whether the primary input can hover (<c>hover</c>, <c>any-hover</c>).</summary>
public enum HoverCapability : byte
{
    /// <summary><c>none</c>.</summary>
    None,

    /// <summary><c>hover</c>.</summary>
    Hover,
}

/// <summary>The pointing device accuracy (<c>pointer</c>, <c>any-pointer</c>).</summary>
public enum PointerCapability : byte
{
    /// <summary><c>none</c>.</summary>
    None,

    /// <summary><c>coarse</c>.</summary>
    Coarse,

    /// <summary><c>fine</c>.</summary>
    Fine,
}

/// <summary>The media-feature values a media query is evaluated against: the media half of the style context (CSS Media Queries 5 §4 to §7). Immutable; change a value with <c>with</c>.</summary>
public sealed record MediaFeatures
{
    /// <summary>1280×720 px, 1 device pixel per CSS pixel, screen, light scheme, no preferences, a hover-capable fine pointer, no script.</summary>
    public static MediaFeatures Default { get; } = new();

    /// <summary>Screen or print.</summary>
    public MediaType Type { get; init; } = MediaType.Screen;

    /// <summary>Viewport width in CSS px (<c>width</c>, and <c>device-width</c>).</summary>
    public double Width { get; init; } = 1280;

    /// <summary>Viewport height in CSS px (<c>height</c>, and <c>device-height</c>).</summary>
    public double Height { get; init; } = 720;

    /// <summary>Device pixels per CSS pixel (<c>resolution</c>).</summary>
    public double DevicePixelRatio { get; init; } = 1;

    /// <summary><c>prefers-color-scheme</c>.</summary>
    public PrefersColorScheme ColorScheme { get; init; } = PrefersColorScheme.Light;

    /// <summary><c>prefers-reduced-motion</c>.</summary>
    public PrefersReduced ReducedMotion { get; init; } = PrefersReduced.NoPreference;

    /// <summary><c>prefers-reduced-transparency</c>.</summary>
    public PrefersReduced ReducedTransparency { get; init; } = PrefersReduced.NoPreference;

    /// <summary><c>prefers-reduced-data</c>.</summary>
    public PrefersReduced ReducedData { get; init; } = PrefersReduced.NoPreference;

    /// <summary><c>prefers-contrast</c>.</summary>
    public PrefersContrast Contrast { get; init; } = PrefersContrast.NoPreference;

    /// <summary><c>forced-colors: active</c> when true, <c>none</c> otherwise.</summary>
    public bool ForcedColors { get; init; }

    /// <summary><c>hover</c>.</summary>
    public HoverCapability Hover { get; init; } = HoverCapability.Hover;

    /// <summary><c>any-hover</c>.</summary>
    public HoverCapability AnyHover { get; init; } = HoverCapability.Hover;

    /// <summary><c>pointer</c>.</summary>
    public PointerCapability PrimaryPointer { get; init; } = PointerCapability.Fine;

    /// <summary><c>any-pointer</c>.</summary>
    public PointerCapability AnyPointer { get; init; } = PointerCapability.Fine;

    /// <summary>Bits per color component (<c>color</c>); 8 by default, 0 for a monochrome device.</summary>
    public int ColorBits { get; init; } = 8;

    /// <summary><c>scripting</c>: always <c>none</c>, because the library never runs script (it says so in <see cref="Scripting"/>).</summary>
    public string Scripting { get; } = "none";
}

/// <summary>How the focused element received focus; it decides <c>:focus-visible</c> (HTML 4.16.3 and Selectors 4 §10.3).</summary>
public enum FocusSource : byte
{
    /// <summary>Keyboard (or script-driven) focus: <c>:focus-visible</c> matches the focused element.</summary>
    Keyboard,

    /// <summary>Focus from a pointing device: <c>:focus-visible</c> matches only text-entry controls and editing hosts, as Chrome documents it.</summary>
    PointingDevice,
}

/// <summary>
/// Everything a style computation depends on that is not the document: the media features and one browser-realistic
/// interaction state (at most one focused element, one hovered element, optionally an active element and a
/// <c>:target</c>). Immutable; make a variant with <c>with</c>. New members get defaults, so callers that build a context
/// from <see cref="Default"/> keep working when they are added. Node handles must belong to the document the context is
/// used with. Script-driven states are invisible, so state results are "CSS only".
/// </summary>
public sealed record StyleContext
{
    /// <summary>A 1280×720 light-scheme screen with no preferences and nothing focused, hovered, active or targeted.</summary>
    public static StyleContext Default { get; } = new();

    /// <summary>The media features.</summary>
    public MediaFeatures Media { get; init; } = MediaFeatures.Default;

    /// <summary>The focused element (<c>:focus</c>, and <c>:focus-within</c> on it and its ancestors); the null node for none.</summary>
    public Node Focus { get; init; }

    /// <summary>How <see cref="Focus"/> was given; <see cref="FocusSource.Keyboard"/> by default.</summary>
    public FocusSource FocusSource { get; init; }

    /// <summary>The hovered element: it and its ancestors match <c>:hover</c>, and so does the control of a hovered <c>label</c>.</summary>
    public Node Hover { get; init; }

    /// <summary>The element being activated (<c>:active</c>); it and its ancestors match, and so does the control of an activated <c>label</c>.</summary>
    public Node Active { get; init; }

    /// <summary>The element the URL fragment names (<c>:target</c>; <c>:target-within</c> on its ancestors).</summary>
    public Node Target { get; init; }

    private FormStateOverrides _formState = FormStateOverrides.Empty;

    /// <summary>
    /// Form state that differs from the document's attributes: checkboxes and radio buttons the user toggled, controls a script disabled,
    /// tri-state checkboxes. Any number of elements. <see cref="FormStateOverrides.Empty"/> by default (a null value reads as empty); its elements
    /// must belong to the document the context is used with. See <see cref="FormStateOverrides"/> for what follows from it. The accessibility tree reads attributes and ignores it.
    /// </summary>
    public FormStateOverrides FormState { get => _formState; init => _formState = value ?? FormStateOverrides.Empty; }

    /// <summary>True when any element has a form-state override.</summary>
    public bool HasFormState => !_formState.IsEmpty;

    /// <summary>True when any of focus, hover, active or target is set.</summary>
    public bool HasInteractionState => !Focus.IsNull || !Hover.IsNull || !Active.IsNull || !Target.IsNull;

    /// <summary>A length context for the viewport of <see cref="Media"/> with the usual 16px initial font size.</summary>
    public LengthContext CreateLengthContext() => LengthContext.Default with { ViewportWidth = Media.Width, ViewportHeight = Media.Height };

    /// <summary>The same context with other media features.</summary>
    /// <param name="media">The features.</param>
    public StyleContext WithMedia(MediaFeatures media) => this with { Media = media };

    /// <summary>The same context with this element focused.</summary>
    /// <param name="element">The focused element, or the null node to clear focus.</param>
    /// <param name="source">How it got focus.</param>
    public StyleContext WithFocus(Node element, FocusSource source = FocusSource.Keyboard) => this with { Focus = element, FocusSource = source };

    /// <summary>The same context with other form-state overrides.</summary>
    /// <param name="formState">The overrides; they replace the current ones.</param>
    public StyleContext WithFormState(FormStateOverrides formState) => this with { FormState = formState };

    /// <summary>The same context with this element hovered (the null node clears it).</summary>
    /// <param name="element">The hovered element.</param>
    public StyleContext WithHover(Node element) => this with { Hover = element };

    /// <summary>The same context without any focus, hover, active or target state (form-state overrides stay).</summary>
    public StyleContext WithoutInteraction() => this with { Focus = default, Hover = default, Active = default, Target = default, FocusSource = FocusSource.Keyboard };

    /// <summary>
    /// The interaction state for the selector matcher, or null when there is none. <c>:focus-visible</c> matches after
    /// keyboard focus, and after pointer focus only on text-entry controls and editing hosts (Chrome's documented rule).
    /// The control of a hovered or activated <c>label</c> is added so that it matches too (HTML 4.16.3).
    /// </summary>
    internal InteractionState? ToInteractionState()
    {
        if (!HasInteractionState && !HasFormState)
        {
            return null;
        }

        return new InteractionState
        {
            Focus = Focus,
            FocusVisible = !Focus.IsNull && (FocusSource == FocusSource.Keyboard || ElementState.IsTextEntryOrEditingHost(Focus)),
            Hover = Hover,
            HoverControl = LabelControl.Of(Hover),
            Active = Active,
            ActiveControl = LabelControl.Of(Active),
            Target = Target,
            FormState = HasFormState ? _formState : null,
        };
    }
}
