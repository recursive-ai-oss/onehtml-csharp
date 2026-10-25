using System;
using System.Collections.Generic;

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

/// <summary>
/// Everything a style computation depends on that is not the document: the media features today, and (reserved for the
/// cascade) the interaction state. Immutable; make a variant with <c>with</c>. New members get defaults, so callers that
/// build a context from <see cref="Default"/> keep working when they are added.
/// </summary>
public sealed record StyleContext
{
    /// <summary>A 1280×720 light-scheme screen with no preferences.</summary>
    public static StyleContext Default { get; } = new();

    /// <summary>The media features.</summary>
    public MediaFeatures Media { get; init; } = MediaFeatures.Default;

    /// <summary>A length context for the viewport of <see cref="Media"/> with the usual 16px initial font size.</summary>
    public LengthContext CreateLengthContext() => LengthContext.Default with { ViewportWidth = Media.Width, ViewportHeight = Media.Height };

    /// <summary>The same context with other media features.</summary>
    /// <param name="media">The features.</param>
    public StyleContext WithMedia(MediaFeatures media) => this with { Media = media };
}
