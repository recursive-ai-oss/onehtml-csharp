using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>What the property's definition table gives as its initial value.</summary>
public enum PropertyInitialKind : byte
{
    /// <summary>A value in CSS syntax; see <see cref="PropertyInfo.InitialValue"/>.</summary>
    Value,

    /// <summary>The table says "see individual properties": a shorthand whose initial value is its longhands' initial values.</summary>
    SeeLonghands,

    /// <summary>Prose: the initial value depends on the user agent.</summary>
    UserAgent,

    /// <summary>Prose: not defined (for shorthand properties).</summary>
    NotDefined,

    /// <summary>The specification's table has no initial value for this property (legacy aliases mostly).</summary>
    Missing,
}

/// <summary>Whether a property is inherited.</summary>
public enum PropertyInheritance : byte
{
    /// <summary>Not inherited.</summary>
    No,

    /// <summary>Inherited.</summary>
    Yes,

    /// <summary>A shorthand: "see individual properties".</summary>
    SeeLonghands,

    /// <summary>The table gives no usable answer (a legacy alias, or text such as "n/a").</summary>
    Unspecified,
}

/// <summary>
/// One row of the generated property table: a CSS property as the specifications' definition tables describe it.
/// The strings are the tables' own text; <see cref="InitialValue"/> parses the initial value on first use.
/// </summary>
public sealed class PropertyInfo
{
    private static readonly string[] None = [];

    private CssTree? _initial;

    internal PropertyInfo(string name, string? initial, PropertyInitialKind initialKind, PropertyInheritance inherited, string? appliesTo,
        string? percentages, string? computedValue, string? syntax, string? aliasOf, string? longhands, string? resetLonghands,
        string? logicalGroup, bool isShorthand, bool isLogical)
    {
        Name = name;
        InitialText = initial;
        InitialKind = initialKind;
        Inherited = inherited;
        AppliesTo = appliesTo;
        Percentages = percentages;
        ComputedValueText = computedValue;
        Syntax = syntax;
        LegacyAliasOf = aliasOf;
        Longhands = longhands is null ? None : longhands.Split(' ');
        ResetLonghands = resetLonghands is null ? None : resetLonghands.Split(' ');
        LogicalPropertyGroup = logicalGroup;
        IsShorthand = isShorthand;
        IsLogical = isLogical;
    }

    /// <summary>The property name, lower case, for example <c>background-color</c>.</summary>
    public string Name { get; }

    /// <summary>The initial value as CSS text, or null when <see cref="InitialKind"/> is not <see cref="PropertyInitialKind.Value"/>.</summary>
    public string? InitialText { get; }

    /// <summary>Whether <see cref="InitialText"/> is CSS text or one of the table's prose forms.</summary>
    public PropertyInitialKind InitialKind { get; }

    /// <summary>Whether the property is inherited.</summary>
    public PropertyInheritance Inherited { get; }

    /// <summary>The "applies to" text of the definition table, or null.</summary>
    public string? AppliesTo { get; }

    /// <summary>The "percentages" text of the definition table, or null.</summary>
    public string? Percentages { get; }

    /// <summary>The "computed value" text of the definition table (prose, for reference), or null.</summary>
    public string? ComputedValueText { get; }

    /// <summary>The property's value definition syntax, for example <c>auto | &lt;length-percentage&gt;</c>, or null.</summary>
    public string? Syntax { get; }

    /// <summary>For a legacy alias (such as <c>-webkit-box-shadow</c>), the name of the property it is an alias of.</summary>
    public string? LegacyAliasOf { get; }

    /// <summary>For a shorthand, the longhands it sets, in the order of its definition. Empty for a longhand.</summary>
    public string[] Longhands { get; }

    /// <summary>For a shorthand, the longhands it resets to their initial values without a way to set them.</summary>
    public string[] ResetLonghands { get; }

    /// <summary>The logical property group (for example <c>margin</c> or <c>border-color</c>) whose physical and logical properties map onto each other, or null.</summary>
    public string? LogicalPropertyGroup { get; }

    /// <summary>True when the property has <see cref="Longhands"/>.</summary>
    public bool IsShorthand { get; }

    /// <summary>True for the flow-relative members of a logical property group (names with block, inline, start or end).</summary>
    public bool IsLogical { get; }

    /// <summary>
    /// The initial value parsed to component values, or <see cref="CssTree.Values"/> of an empty list when
    /// <see cref="InitialKind"/> is not <see cref="PropertyInitialKind.Value"/>. Parsed once, on first use.
    /// </summary>
    public ComponentValueList InitialValue
    {
        get
        {
            CssTree? tree = Volatile.Read(ref _initial);
            if (tree is null)
            {
                tree = CssTree.ParseComponentValueList(InitialKind == PropertyInitialKind.Value ? InitialText! : string.Empty);
                tree = Interlocked.CompareExchange(ref _initial, tree, null) ?? tree;
            }

            return tree.Values.Trimmed();
        }
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The property table generated from the specifications' property definitions (see <c>docs/css-values.md</c> for the
/// source and its date). Lookups are ASCII case-insensitive and ordinal.
/// </summary>
public static class PropertyTable
{
    private static readonly PropertyInfo[] Rows = PropertyTableData.Create();

    private static readonly FrozenDictionary<string, PropertyInfo> ByName = BuildIndex();

    private static readonly FrozenDictionary<string, PropertyInfo[]> Groups = BuildGroups();

    private static readonly FrozenDictionary<string, string[]> Parents = BuildParents();

    private static readonly FrozenDictionary<string, PropertyInfo>.AlternateLookup<ReadOnlySpan<char>> SpanLookup =
        ByName.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>All properties, ordered by name (ordinal).</summary>
    public static IReadOnlyList<PropertyInfo> All => Rows;

    /// <summary>The number of properties in the table.</summary>
    public static int Count => Rows.Length;

    /// <summary>The commit of the extract the table was generated from.</summary>
    public static string SourceCommit => PropertyTableData.SourceCommit;

    /// <summary>The date of that commit (yyyy-MM-dd).</summary>
    public static string SourceDate => PropertyTableData.SourceDate;

    /// <summary>Looks a property up by name; the comparison ignores ASCII case. Custom properties (<c>--x</c>) are not in the table.</summary>
    /// <param name="name">A property name.</param>
    /// <param name="info">The row, when found.</param>
    public static bool TryGet(ReadOnlySpan<char> name, out PropertyInfo info) => SpanLookup.TryGetValue(name, out info!);

    /// <summary>
    /// The numeric id of a property for <see cref="ComputedStyle.GetValue(int)"/>: stable for a run of the program, not across versions.
    /// Aliases (<c>-webkit-…</c>) give the id of their standard property. Allocation-free.
    /// </summary>
    /// <param name="name">A property name, any ASCII case. Custom properties (<c>--x</c>) have no id.</param>
    /// <returns>The id, or -1 for an unknown name.</returns>
    public static int IdOf(ReadOnlySpan<char> name) => PropertyIds.Get(name);

    /// <summary>Looks a property up by name; null when unknown.</summary>
    /// <param name="name">A property name.</param>
    public static PropertyInfo? Find(string name) => TryGet(name, out PropertyInfo? p) ? p : null;

    /// <summary>
    /// The members of a logical property group, physical and logical together (for example the group <c>margin</c>
    /// holds <c>margin-top</c> and <c>margin-inline-start</c>); empty for an unknown group.
    /// </summary>
    /// <param name="group">A <see cref="PropertyInfo.LogicalPropertyGroup"/> value.</param>
    public static IReadOnlyList<PropertyInfo> GetLogicalGroup(string group) => Groups.TryGetValue(group, out PropertyInfo[]? g) ? g : [];

    /// <summary>The names of all logical property groups, ordinal order.</summary>
    public static IEnumerable<string> LogicalGroupNames => Groups.Keys;

    /// <summary>The shorthands that set <paramref name="longhand"/>, including by reset (names, ordinal order).</summary>
    /// <param name="longhand">A property name.</param>
    public static IReadOnlyList<string> GetShorthandsOf(string longhand) => Parents.TryGetValue(longhand, out string[]? s) ? s : [];

    /// <summary>Follows <see cref="PropertyInfo.LegacyAliasOf"/> to the property an alias stands for; returns the row itself when it is not an alias.</summary>
    /// <param name="info">A row.</param>
    public static PropertyInfo ResolveAlias(PropertyInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        for (int i = 0; i < 4 && info.LegacyAliasOf is { } target && ByName.TryGetValue(target, out PropertyInfo? next); i++)
        {
            info = next;
        }

        return info;
    }

    private static FrozenDictionary<string, PropertyInfo> BuildIndex()
    {
        var d = new Dictionary<string, PropertyInfo>(Rows.Length, StringComparer.OrdinalIgnoreCase);
        foreach (PropertyInfo p in Rows)
        {
            d.Add(p.Name, p);
        }

        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, PropertyInfo[]> BuildGroups()
    {
        var d = new SortedDictionary<string, List<PropertyInfo>>(StringComparer.Ordinal);
        foreach (PropertyInfo p in Rows)
        {
            if (p.LogicalPropertyGroup is { } g)
            {
                if (!d.TryGetValue(g, out List<PropertyInfo>? list))
                {
                    d[g] = list = [];
                }

                list.Add(p);
            }
        }

        var r = new Dictionary<string, PropertyInfo[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<PropertyInfo>> kv in d)
        {
            r[kv.Key] = kv.Value.ToArray();
        }

        return r.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, string[]> BuildParents()
    {
        var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (PropertyInfo p in Rows)
        {
            foreach (string l in p.Longhands)
            {
                Add(d, l, p.Name);
            }

            foreach (string l in p.ResetLonghands)
            {
                Add(d, l, p.Name);
            }
        }

        var r = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<string>> kv in d)
        {
            r[kv.Key] = kv.Value.ToArray();
        }

        return r.ToFrozenDictionary(StringComparer.Ordinal);

        static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out List<string>? list))
            {
                map[key] = list = [];
            }

            list.Add(value);
        }
    }
}
