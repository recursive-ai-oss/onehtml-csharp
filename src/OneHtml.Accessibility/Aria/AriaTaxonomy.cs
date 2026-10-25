using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>The value type of a WAI-ARIA state or property (WAI-ARIA 1.2 section 6.2, "Value Types").</summary>
public enum AriaValueType
{
    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary><c>true</c>, <c>false</c> or <c>undefined</c>.</summary>
    BooleanOrUndefined,

    /// <summary><c>true</c>, <c>false</c> or <c>mixed</c> (and <c>undefined</c>).</summary>
    Tristate,

    /// <summary>A single ID reference.</summary>
    IdReference,

    /// <summary>A space-separated list of ID references.</summary>
    IdReferenceList,

    /// <summary>An integer.</summary>
    IntegerValue,

    /// <summary>A real number.</summary>
    Number,

    /// <summary>Free text.</summary>
    TextValue,

    /// <summary>One of a fixed set of tokens.</summary>
    Token,

    /// <summary>A space-separated list of tokens from a fixed set.</summary>
    TokenList,
}

/// <summary>Where a role or property is defined.</summary>
public enum AriaModule
{
    /// <summary>WAI-ARIA 1.2 (W3C Recommendation, 6 June 2023).</summary>
    Aria12,

    /// <summary>Digital Publishing WAI-ARIA Module 1.1 (the <c>doc-*</c> roles).</summary>
    DigitalPublishing,

    /// <summary>WAI-ARIA Graphics Module 1.0 (the <c>graphics-*</c> roles).</summary>
    Graphics,

    /// <summary>The WAI-ARIA 1.3 Editor's Draft; used only for the few additions browsers already ship.</summary>
    Aria13Draft,
}

/// <summary>A WAI-ARIA role with the characteristics WAI-ARIA 1.2 section 5.3 gives it.</summary>
public sealed class AriaRole
{
    internal AriaRole(
        string name, AriaModule module, bool isAbstract, string[] superclasses, string[] requiredContext,
        string[][] requiredOwned, string[] required, string[] requiredWhenFocusable, string[] supported,
        string[] deprecated, string[] prohibited, bool nameFromContents, bool nameFromAuthor, bool nameProhibited,
        bool nameRequired, bool childrenPresentational, IReadOnlyDictionary<string, string> implicitValues)
    {
        Name = name;
        Module = module;
        IsAbstract = isAbstract;
        Superclasses = superclasses;
        RequiredContext = requiredContext;
        RequiredOwned = requiredOwned;
        RequiredAttributes = required;
        RequiredWhenFocusable = requiredWhenFocusable;
        SupportedAttributes = supported;
        DeprecatedAttributes = deprecated;
        ProhibitedAttributes = prohibited;
        NameFromContents = nameFromContents;
        NameFromAuthor = nameFromAuthor;
        NameProhibited = nameProhibited;
        NameRequired = nameRequired;
        ChildrenPresentational = childrenPresentational;
        ImplicitValues = implicitValues;
        Supported = supported.ToFrozenSet(AsciiIgnoreCaseComparer.Instance);
        Deprecated = deprecated.ToFrozenSet(AsciiIgnoreCaseComparer.Instance);
        Prohibited = prohibited.ToFrozenSet(AsciiIgnoreCaseComparer.Instance);
    }

    internal FrozenSet<string> Supported { get; }

    internal FrozenSet<string> Deprecated { get; }

    internal FrozenSet<string> Prohibited { get; }

    /// <summary>The role name, such as <c>button</c>.</summary>
    public string Name { get; }

    /// <summary>The specification module that defines the role.</summary>
    public AriaModule Module { get; }

    /// <summary>True for abstract roles, which authors must not use.</summary>
    public bool IsAbstract { get; }

    /// <summary>The direct superclass roles.</summary>
    public IReadOnlyList<string> Superclasses { get; }

    /// <summary>Roles of which an element with this role must be a descendant ("Required Context Role").</summary>
    public IReadOnlyList<string> RequiredContext { get; }

    /// <summary>
    /// "Required Owned Elements": each entry is a chain of roles, such as <c>["group", "option"]</c> for an option
    /// inside a group; a one-element chain is a direct owned role. Any of the chains satisfies the requirement.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> RequiredOwned { get; }

    /// <summary>States and properties the role requires.</summary>
    public IReadOnlyList<string> RequiredAttributes { get; }

    /// <summary>States and properties required only when the element is focusable (<c>separator</c>'s <c>aria-valuenow</c>).</summary>
    public IReadOnlyList<string> RequiredWhenFocusable { get; }

    /// <summary>Every state and property the role supports, including the inherited and required ones. Sorted.</summary>
    public IReadOnlyList<string> SupportedAttributes { get; }

    /// <summary>Supported attributes the specification marks deprecated on this role. Sorted.</summary>
    public IReadOnlyList<string> DeprecatedAttributes { get; }

    /// <summary>States and properties authors must not use on the role (for example <c>aria-label</c> on <c>generic</c>).</summary>
    public IReadOnlyList<string> ProhibitedAttributes { get; }

    /// <summary>True when the role's name may come from its content.</summary>
    public bool NameFromContents { get; }

    /// <summary>True when the role's name may come from author-supplied attributes.</summary>
    public bool NameFromAuthor { get; }

    /// <summary>True when the role must not be named.</summary>
    public bool NameProhibited { get; }

    /// <summary>True when the role needs an accessible name.</summary>
    public bool NameRequired { get; }

    /// <summary>True when the descendants of an element with this role are presentational.</summary>
    public bool ChildrenPresentational { get; }

    /// <summary>Default values of states and properties for the role, by attribute name, such as <c>aria-live</c> → <c>polite</c> for <c>log</c>.</summary>
    public IReadOnlyDictionary<string, string> ImplicitValues { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>A WAI-ARIA state or property.</summary>
public sealed class AriaProperty
{
    internal AriaProperty(
        string name, AriaModule module, bool isState, AriaValueType type, string[] tokens, string? defaultValue,
        bool isGlobal, string[] globalExceptRoles, bool isDeprecated)
    {
        Name = name;
        Module = module;
        IsState = isState;
        ValueType = type;
        Tokens = tokens;
        DefaultValue = defaultValue;
        IsGlobal = isGlobal;
        GlobalExceptRoles = globalExceptRoles;
        IsDeprecated = isDeprecated;
        TokenSet = tokens.ToFrozenSet(AsciiIgnoreCaseComparer.Instance);
        IsInteger = type == AriaValueType.IntegerValue;
    }

    internal FrozenSet<string> TokenSet { get; }

    internal bool IsInteger { get; }

    /// <summary>The attribute name, such as <c>aria-checked</c>.</summary>
    public string Name { get; }

    /// <summary>The specification module that defines it.</summary>
    public AriaModule Module { get; }

    /// <summary>True for a state (changes with user interaction), false for a property.</summary>
    public bool IsState { get; }

    /// <summary>The value type.</summary>
    public AriaValueType ValueType { get; }

    /// <summary>The allowed tokens for <see cref="AriaValueType.Token"/> and <see cref="AriaValueType.TokenList"/>, and the literal values for the boolean types.</summary>
    public IReadOnlyList<string> Tokens { get; }

    /// <summary>The value that applies when the attribute is absent, or <see langword="null"/> when the specification gives none.</summary>
    public string? DefaultValue { get; }

    /// <summary>True when every role supports it (a "global" state or property).</summary>
    public bool IsGlobal { get; }

    /// <summary>For a global attribute, the roles that must not use it (<c>aria-labelledby</c> on <c>generic</c>).</summary>
    public IReadOnlyList<string> GlobalExceptRoles { get; }

    /// <summary>True when the whole attribute is deprecated (<c>aria-dropeffect</c>, <c>aria-grabbed</c>).</summary>
    public bool IsDeprecated { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The WAI-ARIA taxonomy as data: roles, states and properties with the characteristics the specifications give them.
/// Generated from the pinned specifications by <c>tools/aria/generate-aria.py</c>; see <c>docs/accessibility-roles.md</c>.
/// Role and attribute lookups are ASCII case-insensitive.
/// </summary>
public static class AriaTaxonomy
{
    private const int SupportedVersion = 1;

    private static readonly AriaRole[] s_roles;
    private static readonly AriaProperty[] s_properties;
    private static readonly FrozenDictionary<string, AriaRole> s_roleByName;
    private static readonly FrozenDictionary<string, AriaProperty> s_propertyByName;
    private static readonly FrozenDictionary<string, string[]> s_requiredOwnedClosure;

    static AriaTaxonomy()
    {
        using (JsonDocument doc = Open("aria-roles.json"))
        {
            JsonElement list = doc.RootElement.GetProperty("roles");
            s_roles = new AriaRole[list.GetArrayLength()];
            int i = 0;
            foreach (JsonElement r in list.EnumerateArray())
            {
                var implicitValues = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty p in r.GetProperty("implicit").EnumerateObject())
                {
                    implicitValues[p.Name] = p.Value.GetString()!;
                }

                string[] nameFrom = Strings(r.GetProperty("nameFrom"));
                var owned = new List<string[]>();
                foreach (JsonElement chain in r.GetProperty("requiredOwned").EnumerateArray())
                {
                    owned.Add(Strings(chain));
                }

                s_roles[i++] = new AriaRole(
                    r.GetProperty("name").GetString()!, ParseModule(r.GetProperty("module").GetString()!),
                    r.GetProperty("abstract").GetBoolean(), Strings(r.GetProperty("superclasses")),
                    Strings(r.GetProperty("requiredContext")), owned.ToArray(), Strings(r.GetProperty("required")),
                    Strings(r.GetProperty("requiredWhenFocusable")), Strings(r.GetProperty("supported")),
                    Strings(r.GetProperty("deprecated")), Strings(r.GetProperty("prohibited")),
                    Array.IndexOf(nameFrom, "contents") >= 0, Array.IndexOf(nameFrom, "author") >= 0,
                    Array.IndexOf(nameFrom, "prohibited") >= 0, r.GetProperty("nameRequired").GetBoolean(),
                    r.GetProperty("childrenPresentational").GetBoolean(), implicitValues);
            }
        }

        using (JsonDocument doc = Open("aria-properties.json"))
        {
            JsonElement list = doc.RootElement.GetProperty("properties");
            s_properties = new AriaProperty[list.GetArrayLength()];
            int i = 0;
            foreach (JsonElement p in list.EnumerateArray())
            {
                string[] tokens = Strings(p.GetProperty("tokens"));
                s_properties[i++] = new AriaProperty(
                    p.GetProperty("name").GetString()!, ParseModule(p.GetProperty("module").GetString()!),
                    p.GetProperty("isState").GetBoolean(), ParseType(p.GetProperty("type").GetString()!), tokens,
                    p.GetProperty("default").GetString(), p.GetProperty("global").GetBoolean(),
                    Strings(p.GetProperty("globalExceptRoles")), p.GetProperty("deprecated").GetBoolean());
            }
        }

        var roleMap = new Dictionary<string, AriaRole>(AsciiIgnoreCaseComparer.Instance);
        foreach (AriaRole r in s_roles)
        {
            roleMap[r.Name] = r;
        }

        s_roleByName = roleMap.ToFrozenDictionary(AsciiIgnoreCaseComparer.Instance);
        var propMap = new Dictionary<string, AriaProperty>(AsciiIgnoreCaseComparer.Instance);
        foreach (AriaProperty p in s_properties)
        {
            propMap[p.Name] = p;
        }

        s_propertyByName = propMap.ToFrozenDictionary(AsciiIgnoreCaseComparer.Instance);
        s_requiredOwnedClosure = BuildOwnedClosure(roleMap).ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>All roles, ordered by module and then name.</summary>
    public static IReadOnlyList<AriaRole> Roles => s_roles;

    /// <summary>All states and properties, ordered by name.</summary>
    public static IReadOnlyList<AriaProperty> Properties => s_properties;

    /// <summary>
    /// Finds a role by name, ASCII case-insensitively. Synonyms are not resolved here (<c>image</c>, which WAI-ARIA 1.3
    /// adds for <c>img</c>, is not a role of its own); see <see cref="RoleComputer"/>.
    /// </summary>
    /// <param name="name">A role name.</param>
    /// <returns>The role, or <see langword="null"/>.</returns>
    public static AriaRole? FindRole(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return s_roleByName.GetValueOrDefault(name);
    }

    /// <summary>Finds a role by a span of characters, ASCII case-insensitively, without allocating.</summary>
    /// <param name="name">A role name.</param>
    /// <returns>The role, or <see langword="null"/>.</returns>
    public static AriaRole? FindRole(ReadOnlySpan<char> name) =>
        s_roleByName.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out AriaRole? role) ? role : null;

    /// <summary>Finds a state or property by attribute name (<c>aria-checked</c>), ASCII case-insensitively.</summary>
    /// <param name="name">An attribute name.</param>
    /// <returns>The property, or <see langword="null"/>.</returns>
    public static AriaProperty? FindProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return s_propertyByName.GetValueOrDefault(name);
    }

    /// <summary>Finds a state or property by a span of characters, ASCII case-insensitively, without allocating.</summary>
    /// <param name="name">An attribute name.</param>
    /// <returns>The property, or <see langword="null"/>.</returns>
    public static AriaProperty? FindProperty(ReadOnlySpan<char> name) =>
        s_propertyByName.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out AriaProperty? p) ? p : null;

    /// <summary>
    /// The roles an element with this role requires as owned elements, followed through the owned roles' own
    /// requirements (a table owns rows, and rows own cells). Empty when the role has no required owned elements.
    /// </summary>
    /// <param name="roleName">A role name.</param>
    /// <returns>Role names, in a stable order.</returns>
    public static IReadOnlyList<string> GetRequiredOwnedClosure(string roleName)
    {
        ArgumentNullException.ThrowIfNull(roleName);
        return s_requiredOwnedClosure.GetValueOrDefault(roleName) ?? [];
    }

    private static Dictionary<string, string[]> BuildOwnedClosure(Dictionary<string, AriaRole> roles)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (AriaRole role in roles.Values)
        {
            if (role.RequiredOwned.Count == 0)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var order = new List<string>();
            var work = new Stack<AriaRole>();
            work.Push(role);
            while (work.Count > 0)
            {
                AriaRole current = work.Pop();
                foreach (IReadOnlyList<string> chain in current.RequiredOwned)
                {
                    foreach (string owned in chain)
                    {
                        if (seen.Add(owned))
                        {
                            order.Add(owned);
                            if (roles.TryGetValue(owned, out AriaRole? next))
                            {
                                work.Push(next);
                            }
                        }
                    }
                }
            }

            result[role.Name] = order.ToArray();
        }

        return result;
    }

    private static JsonDocument Open(string file)
    {
        Assembly assembly = typeof(AriaTaxonomy).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("Aria." + file)
            ?? throw new InvalidOperationException("Missing embedded data: " + file);
        JsonDocument doc = JsonDocument.Parse(stream);
        if (doc.RootElement.GetProperty("version").GetInt32() != SupportedVersion)
        {
            doc.Dispose();
            throw new InvalidOperationException("Unsupported version of data: " + file);
        }

        return doc;
    }

    internal static string[] Strings(JsonElement array)
    {
        var result = new string[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            result[i++] = item.GetString()!;
        }

        return result;
    }

    private static AriaModule ParseModule(string s) => s switch
    {
        "aria-1.2" => AriaModule.Aria12,
        "dpub-aria-1.1" => AriaModule.DigitalPublishing,
        "graphics-aria-1.0" => AriaModule.Graphics,
        "aria-1.3-ed" => AriaModule.Aria13Draft,
        _ => throw new InvalidOperationException("Unknown module " + s),
    };

    private static AriaValueType ParseType(string s) => s switch
    {
        "boolean" => AriaValueType.Boolean,
        "booleanUndefined" => AriaValueType.BooleanOrUndefined,
        "tristate" => AriaValueType.Tristate,
        "idref" => AriaValueType.IdReference,
        "idrefList" => AriaValueType.IdReferenceList,
        "integer" => AriaValueType.IntegerValue,
        "number" => AriaValueType.Number,
        "string" => AriaValueType.TextValue,
        "token" => AriaValueType.Token,
        "tokenList" => AriaValueType.TokenList,
        _ => throw new InvalidOperationException("Unknown value type " + s),
    };
}
