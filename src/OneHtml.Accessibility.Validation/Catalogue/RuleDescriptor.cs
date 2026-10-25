using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>
/// What the catalogue records about one rule: where it comes from, which criteria it checks, what it needs, and its
/// fix or the reason it has none. The rule implementation is a separate type that points back at this descriptor.
/// </summary>
public sealed record RuleDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="id">The rule's identifier in the catalogue; axe's identifier where an axe rule is the counterpart.</param>
    /// <param name="sources">Where the rule comes from. At least one.</param>
    /// <param name="criteria">Numbers of the WCAG success criteria it checks, such as <c>4.1.2</c>. May be empty for best practices.</param>
    /// <param name="bestPractice">True when the rule is a best practice rather than a requirement of a criterion.</param>
    /// <param name="needs">Whether the document and tree suffice, or rendered facts are required.</param>
    /// <param name="fix">The single judgement-free fix the rule can suggest, in words, or <see langword="null"/>.</param>
    /// <param name="noFixReason">Why the rule has no suggested fix, or <see langword="null"/> when <paramref name="fix"/> is given.</param>
    /// <param name="tags">Free-form tags for selecting rules, such as <c>cat.forms</c> or <c>experimental</c>.</param>
    /// <exception cref="ArgumentException">The id is empty, there is no source, a criterion number is unknown, or both a fix and a no-fix reason are given.</exception>
    public RuleDescriptor(
        string id,
        IEnumerable<RuleSource> sources,
        IEnumerable<string> criteria,
        bool bestPractice,
        RuleNeeds needs,
        string? fix,
        string? noFixReason,
        IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(criteria);
        Id = id;
        Sources = [.. sources];
        if (Sources.Count == 0)
        {
            throw new ArgumentException("A rule needs at least one source.", nameof(sources));
        }

        var resolved = new List<SuccessCriterion>();
        foreach (string number in criteria)
        {
            resolved.Add(WcagCriteria.Get(number));
        }

        Criteria = resolved;
        BestPractice = bestPractice;
        Needs = needs;
        if (fix is not null && noFixReason is not null)
        {
            throw new ArgumentException("A rule has a fix or a reason for having none, not both.", nameof(noFixReason));
        }

        Fix = fix;
        NoFixReason = noFixReason;
        Tags = tags is null ? [] : [.. tags];
    }

    /// <summary>The rule's identifier in the catalogue.</summary>
    public string Id { get; }

    /// <summary>Where the rule comes from.</summary>
    public IReadOnlyList<RuleSource> Sources { get; }

    /// <summary>The success criteria the rule checks, in the order given.</summary>
    public IReadOnlyList<SuccessCriterion> Criteria { get; }

    /// <summary>True when the rule is a best practice.</summary>
    public bool BestPractice { get; }

    /// <summary>What the rule needs to reach a verdict.</summary>
    public RuleNeeds Needs { get; }

    /// <summary>The one judgement-free fix the rule can suggest, or <see langword="null"/>.</summary>
    public string? Fix { get; }

    /// <summary>Why the rule has no suggested fix, or <see langword="null"/>.</summary>
    public string? NoFixReason { get; }

    /// <summary>Free-form tags.</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// A selection of rules by their labels (criteria with version and level, or best practice). A profile for a WCAG
/// version and level includes the criteria of that level and below, in that version and every earlier version, minus
/// criteria the version no longer has. The names refer to the standards that cite them (Section 508 cites WCAG 2.0 AA;
/// ADA Title II and EN 301 549 cite WCAG 2.1 AA); a profile is a selection, not a statement of legal compliance.
/// </summary>
public sealed class ValidationProfile
{
    private readonly HashSet<string> _numbers = new(StringComparer.Ordinal);

    private ValidationProfile(string name, WcagVersion? version, WcagLevel? level)
    {
        Name = name;
        Version = version;
        Level = level;
        var list = new List<SuccessCriterion>();
        foreach (SuccessCriterion criterion in WcagCriteria.All)
        {
            // IsInVersion covers earlier criteria the version kept and drops ones it removed.
            bool included = version is null || (criterion.IsInVersion(version.Value) && criterion.Level <= level!.Value);
            if (included)
            {
                list.Add(criterion);
                _numbers.Add(criterion.Number);
            }
        }

        Criteria = list;
    }

    /// <summary>WCAG 2.0 Level A and AA (the standard Section 508 cites).</summary>
    public static ValidationProfile Wcag20AA { get; } = new("wcag-2.0-aa", WcagVersion.Wcag20, WcagLevel.AA);

    /// <summary>WCAG 2.1 Level A and AA (the standard ADA Title II and EN 301 549 cite), including the 2.0 criteria 2.1 kept.</summary>
    public static ValidationProfile Wcag21AA { get; } = new("wcag-2.1-aa", WcagVersion.Wcag21, WcagLevel.AA);

    /// <summary>WCAG 2.2 Level A and AA, including the earlier criteria 2.2 kept (not 4.1.1 Parsing, which 2.2 removed).</summary>
    public static ValidationProfile Wcag22AA { get; } = new("wcag-2.2-aa", WcagVersion.Wcag22, WcagLevel.AA);

    /// <summary>Every criterion of every version and level, the removed ones included, and every best practice.</summary>
    public static ValidationProfile All { get; } = new("all", null, null);

    /// <summary>The profile's stable name.</summary>
    public string Name { get; }

    /// <summary>The WCAG version, or <see langword="null"/> for <see cref="All"/>.</summary>
    public WcagVersion? Version { get; }

    /// <summary>The highest level included, or <see langword="null"/> for <see cref="All"/>.</summary>
    public WcagLevel? Level { get; }

    /// <summary>The criteria the profile includes, in number order.</summary>
    public IReadOnlyList<SuccessCriterion> Criteria { get; }

    /// <summary>True when the profile includes the criterion.</summary>
    public bool Includes(SuccessCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        return _numbers.Contains(criterion.Number);
    }

    /// <summary>
    /// True when the profile includes the rule: some criterion the rule checks is in the profile. A rule with no
    /// criteria (a best practice) is included only by <see cref="All"/>.
    /// </summary>
    public bool Includes(RuleDescriptor rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (ReferenceEquals(this, All))
        {
            return true;
        }

        foreach (SuccessCriterion criterion in rule.Criteria)
        {
            if (_numbers.Contains(criterion.Number))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds a built-in profile by its <see cref="Name"/>, or returns <see langword="null"/>.</summary>
    public static ValidationProfile? Find(string name) => name switch
    {
        "wcag-2.0-aa" => Wcag20AA,
        "wcag-2.1-aa" => Wcag21AA,
        "wcag-2.2-aa" => Wcag22AA,
        "all" => All,
        _ => null,
    };

    /// <inheritdoc/>
    public override string ToString() => Name;
}
