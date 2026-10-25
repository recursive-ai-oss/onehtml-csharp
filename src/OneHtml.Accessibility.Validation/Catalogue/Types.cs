using System;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>A version of the Web Content Accessibility Guidelines. Values are ordered oldest to newest.</summary>
public enum WcagVersion
{
    /// <summary>WCAG 2.0 (2008).</summary>
    Wcag20 = 20,

    /// <summary>WCAG 2.1 (2018).</summary>
    Wcag21 = 21,

    /// <summary>WCAG 2.2 (2023).</summary>
    Wcag22 = 22,
}

/// <summary>A WCAG conformance level. Values are ordered from least to most demanding.</summary>
public enum WcagLevel
{
    /// <summary>Level A.</summary>
    A = 1,

    /// <summary>Level AA.</summary>
    AA = 2,

    /// <summary>Level AAA.</summary>
    AAA = 3,
}

/// <summary>Where a rule in the catalogue comes from.</summary>
public enum RuleSourceKind
{
    /// <summary>A W3C ACT rule; the id is its six-character identifier.</summary>
    Act,

    /// <summary>A rule published in axe-core; the id is its rule identifier.</summary>
    Axe,

    /// <summary>A WCAG failure technique; the id is its number, such as <c>F65</c>.</summary>
    FailureTechnique,

    /// <summary>A check of our own; the id is the rule's own identifier.</summary>
    Own,
}

/// <summary>One source of a rule: a kind and the identifier inside that source.</summary>
/// <param name="Kind">Which source it is.</param>
/// <param name="Id">The identifier within that source.</param>
public readonly record struct RuleSource(RuleSourceKind Kind, string Id)
{
    /// <summary>Creates an ACT rule source.</summary>
    public static RuleSource Act(string id) => new(RuleSourceKind.Act, id);

    /// <summary>Creates an axe-core rule source.</summary>
    public static RuleSource Axe(string id) => new(RuleSourceKind.Axe, id);

    /// <summary>Creates a WCAG failure technique source.</summary>
    public static RuleSource FailureTechnique(string id) => new(RuleSourceKind.FailureTechnique, id);

    /// <summary>Creates a source for a check of our own.</summary>
    public static RuleSource Own(string id) => new(RuleSourceKind.Own, id);
}

/// <summary>What a rule needs to reach a verdict.</summary>
public enum RuleNeeds
{
    /// <summary>The document, its styles and the accessibility tree are enough.</summary>
    Static,

    /// <summary>Layout, painting or other facts from a rendered page are needed.</summary>
    RenderedFacts,
}

/// <summary>A WCAG success criterion in one row of the published criteria table.</summary>
/// <param name="Number">The number, such as <c>1.4.3</c>.</param>
/// <param name="Id">The anchor id used by the W3C documents, such as <c>contrast-minimum</c>.</param>
/// <param name="Handle">The short name as published, such as <c>Contrast (Minimum)</c>.</param>
/// <param name="Level">The conformance level.</param>
/// <param name="IntroducedIn">The first WCAG version that has the criterion.</param>
/// <param name="RemovedIn">The first version that no longer has it, or <see langword="null"/> if it is still in the newest version.</param>
public sealed record SuccessCriterion(
    string Number,
    string Id,
    string Handle,
    WcagLevel Level,
    WcagVersion IntroducedIn,
    WcagVersion? RemovedIn)
{
    /// <summary>True when the criterion is part of the given WCAG version.</summary>
    public bool IsInVersion(WcagVersion version) =>
        version >= IntroducedIn && (RemovedIn is null || version < RemovedIn.Value);

    /// <summary>The guideline number, such as <c>1.4</c>.</summary>
    public string Guideline => Number[..Number.LastIndexOf('.')];

    /// <inheritdoc/>
    public override string ToString() => Number + " " + Handle;
}
