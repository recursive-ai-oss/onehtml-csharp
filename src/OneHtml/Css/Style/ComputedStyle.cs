using System;
using RecursiveAI.OneHtml.Selectors;
using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// Physical counterparts of the flow-relative (logical) properties under a writing mode and direction (CSS Logical
/// Properties 1 §4, CSS Writing Modes 4 §6.2 and §6.3). Used to let a logical declaration compete with its physical
/// partner in the cascade (CSS Cascade 5 §6.4 "logical property groups").
/// </summary>
internal static class LogicalMap
{
    // Mode index = writing mode * 2 + (rtl ? 1 : 0); writing modes: 0 horizontal-tb, 1 vertical-rl, 2 vertical-lr, 3 sideways-rl, 4 sideways-lr.
    public const int ModeCount = 10;

    private static readonly int[][] Table = Build();

    /// <summary>The physical property id for a logical one in a mode, or -1 when the property is not flow-relative.</summary>
    public static int Physical(int id, int mode) => Table[id] is { } t ? t[mode] : -1;

    public static bool IsLogical(int id) => Table[id] is not null;

    public static int ModeIndex(string writingMode, bool rtl)
    {
        int wm = writingMode switch
        {
            "vertical-rl" => 1,
            "vertical-lr" => 2,
            "sideways-rl" => 3,
            "sideways-lr" => 4,
            _ => 0,
        };
        return (wm * 2) + (rtl ? 1 : 0);
    }

    private static int[][] Build()
    {
        var table = new int[PropertyIds.Count][];
        for (int id = 0; id < PropertyIds.Count; id++)
        {
            string name = PropertyIds.Info[id].Name;
            if (PropertyIds.Info[id].LegacyAliasOf is not null || !(name.Contains("block", StringComparison.Ordinal) || name.Contains("inline", StringComparison.Ordinal) || name.Contains("-start-", StringComparison.Ordinal) || name.Contains("-end-", StringComparison.Ordinal)))
            {
                continue;
            }

            int[]? row = null;
            for (int mode = 0; mode < ModeCount; mode++)
            {
                string? phys = Transform(name, mode);
                if (phys is null)
                {
                    break;
                }

                int pid = PropertyIds.Get(phys);
                if (pid < 0 || pid == id)
                {
                    row = null;
                    break;
                }

                row ??= new int[ModeCount];
                row[mode] = pid;
            }

            table[id] = row!;
        }

        return table;
    }

    private static string? Transform(string name, int mode)
    {
        int wm = mode / 2;
        bool rtl = (mode & 1) != 0;
        bool horizontal = wm == 0;
        string blockStart = wm switch { 0 => "top", 1 or 3 => "right", _ => "left" };
        string blockEnd = wm switch { 0 => "bottom", 1 or 3 => "left", _ => "right" };
        string inlineStart;
        string inlineEnd;
        if (horizontal)
        {
            inlineStart = rtl ? "right" : "left";
            inlineEnd = rtl ? "left" : "right";
        }
        else
        {
            bool startTop = wm == 4 ? rtl : !rtl;
            inlineStart = startTop ? "top" : "bottom";
            inlineEnd = startTop ? "bottom" : "top";
        }

        // Corner radii: border-<block>-<inline>-radius.
        if (name.StartsWith("border-", StringComparison.Ordinal) && name.EndsWith("-radius", StringComparison.Ordinal))
        {
            string[] p = name.Split('-');
            if (p.Length == 4 && p[1] is "start" or "end" && p[2] is "start" or "end")
            {
                string v = p[1] == "start" ? blockStart : blockEnd;
                string h = p[2] == "start" ? inlineStart : inlineEnd;
                if (v is "left" or "right")
                {
                    (v, h) = (h, v);
                }

                return "border-" + v + "-" + h + "-radius";
            }

            return null;
        }

        string r = name;
        if (r.Contains("block-start", StringComparison.Ordinal) || r.Contains("block-end", StringComparison.Ordinal) || r.Contains("inline-start", StringComparison.Ordinal) || r.Contains("inline-end", StringComparison.Ordinal))
        {
            r = r.Replace("block-start", blockStart, StringComparison.Ordinal)
                .Replace("block-end", blockEnd, StringComparison.Ordinal)
                .Replace("inline-start", inlineStart, StringComparison.Ordinal)
                .Replace("inline-end", inlineEnd, StringComparison.Ordinal);
            return r.StartsWith("inset-", StringComparison.Ordinal) ? r["inset-".Length..] : r;
        }

        if (r.EndsWith("inline-size", StringComparison.Ordinal))
        {
            return r[..^"inline-size".Length] + (horizontal ? "width" : "height");
        }

        if (r.EndsWith("block-size", StringComparison.Ordinal))
        {
            return r[..^"block-size".Length] + (horizontal ? "height" : "width");
        }

        if (r.EndsWith("-inline", StringComparison.Ordinal))
        {
            return r[..^"-inline".Length] + (horizontal ? "-x" : "-y");
        }

        if (r.EndsWith("-block", StringComparison.Ordinal))
        {
            return r[..^"-block".Length] + (horizontal ? "-y" : "-x");
        }

        return null;
    }
}

/// <summary>The computed custom properties of an element: a flat map, shared with the parent when the element declares none.</summary>
internal sealed class CustomProps
{
    public static readonly CustomProps Empty = new(new Dictionary<string, string>(StringComparer.Ordinal), null);

    public CustomProps(Dictionary<string, string> values, Dictionary<string, StyleValue>? winners)
    {
        Values = values;
        Winners = winners;
    }

    /// <summary>All computed (valid) custom properties, own and inherited.</summary>
    public Dictionary<string, string> Values { get; }

    /// <summary>The declarations that won for this element's own custom properties, or null when it has none.</summary>
    public Dictionary<string, StyleValue>? Winners { get; }
}

/// <summary>
/// The computed style of one element, or of many: styles are shared between elements that cascade to the same values
/// under the same parent style (most siblings). Immutable and safe to read from any thread; typed values are built on
/// first read and cached.
/// </summary>
/// <remarks>
/// A property's value is a <see cref="StyleValue"/> (the winning declaration's tokens, with CSS-wide keywords,
/// <c>var()</c> and inheritance resolved). Typed accessors parse those tokens lazily. Layout-dependent values (percentages
/// of a containing block, <c>auto</c> sizes) are kept as specified.
/// </remarks>
public sealed partial class ComputedStyle
{
    private static readonly StyleValue?[] Initials = new StyleValue?[PropertyIds.Count];

    private readonly int[] _ownIds;
    private readonly StyleValue[] _ownValues;
    private readonly StyleValue?[] _inherited;
    private readonly CustomProps _custom;
    private readonly int _mode;
    private int _declared;

    internal ComputedStyle(StyleEngine engine, ComputedStyle? parent, int[] ownIds, StyleValue[] ownValues, StyleValue?[] inherited, CustomProps custom, int mode)
    {
        Engine = engine;
        Parent = parent;
        _ownIds = ownIds;
        _ownValues = ownValues;
        _inherited = inherited;
        _custom = custom;
        _mode = mode;
        Id = engine.NextStyleId();
    }

    private int[] _keyIds = [];
    private StyleValue[] _keyVals = [];
    private string[] _keyCNames = [];
    private StyleValue[] _keyCVals = [];

    /// <summary>Bits for the chain-resolved properties (color, font-size, font-weight) that a declaration, not inheritance, gave this style.</summary>
    internal int Declared { set => _declared = value; }

    internal StyleEngine Engine { get; }

    /// <summary>The next style with the same key hash in the engine's sharing table.</summary>
    internal ComputedStyle? CacheNext { get; set; }

    internal void SetKey(int[] ids, StyleValue[] vals, string[] cNames, StyleValue[] cVals)
    {
        _keyIds = ids;
        _keyVals = vals;
        _keyCNames = cNames;
        _keyCVals = cVals;
    }

    internal void GetKey(out int[] ids, out StyleValue[] vals, out string[] cNames, out StyleValue[] cVals)
    {
        ids = _keyIds;
        vals = _keyVals;
        cNames = _keyCNames;
        cVals = _keyCVals;
    }

    /// <summary>Whether this style was built from the same parent and the same winning declarations (so it can be shared).</summary>
    internal bool KeyMatches(ComputedStyle? parent, ReadOnlySpan<int> ids, ReadOnlySpan<StyleValue> vals, ReadOnlySpan<string> cNames, ReadOnlySpan<StyleValue> cVals)
    {
        if (!ReferenceEquals(Parent, parent) || _keyIds.Length != ids.Length || _keyCNames.Length != cNames.Length)
        {
            return false;
        }

        for (int i = 0; i < ids.Length; i++)
        {
            if (_keyIds[i] != ids[i] || !ReferenceEquals(_keyVals[i], vals[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < cNames.Length; i++)
        {
            if (!string.Equals(_keyCNames[i], cNames[i], StringComparison.Ordinal) || !ReferenceEquals(_keyCVals[i], cVals[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The parent element's style, or null for the root element.</summary>
    public ComputedStyle? Parent { get; }

    /// <summary>A number unique among the styles of one <see cref="DocumentStyles"/>, for telling shared styles apart.</summary>
    public int Id { get; }

    /// <summary>True when the element's <c>direction</c> is <c>rtl</c>.</summary>
    public bool IsRightToLeft => (_mode & 1) != 0;

    /// <summary>The element's <c>writing-mode</c> keyword (<c>horizontal-tb</c>, <c>vertical-rl</c>, …).</summary>
    public string WritingMode => (_mode >> 1) switch { 1 => "vertical-rl", 2 => "vertical-lr", 3 => "sideways-rl", 4 => "sideways-lr", _ => "horizontal-tb" };

    internal int Mode => _mode;

    internal static StyleValue InitialOf(int id)
    {
        StyleValue? v = Volatile.Read(ref Initials[id]);
        if (v is null)
        {
            PropertyInfo info = PropertyIds.Info[id];
            v = new StyleValue(id, null, info.InitialValue, CssWideKeyword.None, null, false, null) { IsInitial = true };
            v = Interlocked.CompareExchange(ref Initials[id], v, null) ?? v;
        }

        return v;
    }

    internal StyleValue Get(int id)
    {
        int physical = LogicalMap.Physical(id, _mode);
        if (physical >= 0)
        {
            id = physical;
        }

        int slot = PropertyIds.InheritedSlot[id];
        if (slot >= 0)
        {
            return _inherited[slot] ?? InitialOf(id);
        }

        int i = Array.BinarySearch(_ownIds, id);
        return i >= 0 ? _ownValues[i] : InitialOf(id);
    }

    internal bool TryGetOwn(int id, out StyleValue value)
    {
        int slot = PropertyIds.InheritedSlot[id];
        if (slot >= 0)
        {
            StyleValue? v = _inherited[slot];
            value = v!;
            return v is not null;
        }

        int i = Array.BinarySearch(_ownIds, id);
        value = i >= 0 ? _ownValues[i] : null!;
        return i >= 0;
    }

    internal StyleValue?[] InheritedValues => _inherited;

    internal int[] OwnIds => _ownIds;

    internal StyleValue[] OwnValues => _ownValues;

    internal CustomProps Custom => _custom;

    /// <summary>
    /// The value of a property: the winning declaration's tokens after the cascade, or the initial value (inherited
    /// properties without a declaration take the parent's). Logical properties read through their physical partner
    /// under this element's writing mode. A shorthand reads as its own declared token list, if it was kept whole (a
    /// shorthand without an expansion grammar), else as its initial value; read the longhands instead.
    /// Custom properties are read with <see cref="TryGetCustomProperty"/>.
    /// </summary>
    /// <param name="property">A property name, any ASCII case; aliases (<c>-webkit-…</c>) read their standard property.</param>
    /// <returns>The value, or null for a name that is not a known property.</returns>
    public StyleValue? GetValue(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        int id = PropertyIds.Get(property);
        return id < 0 ? null : Get(id);
    }

    /// <summary>
    /// The value of a property by id (<see cref="PropertyTable.IdOf"/>), as <see cref="GetValue(string)"/> reads it. Allocation-free: the
    /// returned object is the shared one the style table holds.
    /// </summary>
    /// <param name="propertyId">An id from <see cref="PropertyTable.IdOf"/>.</param>
    /// <returns>The value, or null for an id that is not a property.</returns>
    public StyleValue? GetValue(int propertyId) => (uint)propertyId < (uint)PropertyIds.Count ? Get(propertyId) : null;

    /// <summary>The value of a property as CSS text, or the empty string for an unknown property. See <see cref="GetValue(string)"/>.</summary>
    /// <param name="property">A property name.</param>
    public string GetPropertyValue(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
        {
            return TryGetCustomProperty(property, out string v) ? v : string.Empty;
        }

        if (SerializeShorthand(property) is { } shorthand)
        {
            return shorthand;
        }

        return GetValue(property)?.Text ?? string.Empty;
    }

    /// <summary>
    /// The value of <c>white-space</c> or <c>text-wrap</c> serialized from the longhands that carry it (CSS Text 4 sections 3.1 and 5.1: a
    /// shorthand's value is the shortest string its longhands' values form, as <c>getComputedStyle</c> shows it); null for any other property.
    /// </summary>
    internal string? SerializeShorthand(string property)
    {
        if (string.Equals(property, "white-space", StringComparison.OrdinalIgnoreCase))
        {
            string collapse = GetPropertyValue("white-space-collapse").Trim();
            bool nowrap = string.Equals(GetPropertyValue("text-wrap-mode").Trim(), "nowrap", StringComparison.OrdinalIgnoreCase);
            bool isCollapse = string.Equals(collapse, "collapse", StringComparison.OrdinalIgnoreCase);
            bool preserve = string.Equals(collapse, "preserve", StringComparison.OrdinalIgnoreCase);
            bool breaks = string.Equals(collapse, "preserve-breaks", StringComparison.OrdinalIgnoreCase);
            if (isCollapse)
            {
                return nowrap ? "nowrap" : "normal";
            }

            if (preserve)
            {
                return nowrap ? "pre" : "pre-wrap";
            }

            if (breaks && !nowrap)
            {
                return "pre-line";
            }

            return nowrap ? collapse + " nowrap" : collapse;
        }

        if (string.Equals(property, "text-wrap", StringComparison.OrdinalIgnoreCase))
        {
            string style = GetPropertyValue("text-wrap-style").Trim();
            bool nowrap = string.Equals(GetPropertyValue("text-wrap-mode").Trim(), "nowrap", StringComparison.OrdinalIgnoreCase);
            bool auto = string.Equals(style, "auto", StringComparison.OrdinalIgnoreCase);
            if (nowrap)
            {
                return auto ? "nowrap" : "nowrap " + style;
            }

            return auto ? "wrap" : style;
        }

        return null;
    }

    /// <summary>Where a property's value was declared (see <see cref="StyleValue.Source"/>); null for an initial value or unknown property.</summary>
    /// <param name="property">A property name; custom properties included.</param>
    public DeclarationSource? GetSource(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.StartsWith("--", StringComparison.Ordinal))
        {
            for (ComputedStyle? s = this; s is not null; s = s.Parent)
            {
                if (s._custom.Winners is { } w && w.TryGetValue(property, out StyleValue? v))
                {
                    return v.Source;
                }
            }

            return null;
        }

        return GetValue(property)?.Source;
    }

    /// <summary>
    /// True when a declaration of the property won the cascade for this element, whatever its value: <c>inherit</c>, <c>unset</c> and the
    /// other CSS-wide keywords count, which is what separates an element that sets <c>color: inherit</c> from one that merely inherits.
    /// False when the value is only inherited or initial, and for an unknown name or a custom property.
    /// </summary>
    /// <param name="property">A property name, any ASCII case.</param>
    public bool IsDeclared(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        int id = PropertyIds.Get(property);
        if (id < 0)
        {
            return false;
        }

        foreach (int k in _keyIds)
        {
            if (k == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The computed value of a custom property (after <c>var()</c> substitution), or false when it is unset or guaranteed-invalid.</summary>
    /// <param name="name">The property name with its <c>--</c>; case-sensitive.</param>
    /// <param name="value">The value as CSS text.</param>
    public bool TryGetCustomProperty(string name, out string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _custom.Values.TryGetValue(name, out value!);
    }

    /// <summary>All computed custom properties (own and inherited), by name.</summary>
    public IReadOnlyDictionary<string, string> CustomProperties => _custom.Values;

    /// <summary>
    /// True when every property of the two styles has the same value text and the custom properties are equal. Used to
    /// check that an overlay or restyle gives what a full recompute gives; sources are not compared.
    /// </summary>
    /// <param name="other">Another style.</param>
    public bool ValueEquals(ComputedStyle other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return FirstDifference(other) is null;
    }

    /// <summary>The name of the first property whose value differs from <paramref name="other"/> (custom properties included), or null when none does.</summary>
    /// <param name="other">Another style.</param>
    public string? FirstDifference(ComputedStyle other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
        {
            return null;
        }

        for (int id = 0; id < PropertyIds.Count; id++)
        {
            if (PropertyIds.Info[id].LegacyAliasOf is not null)
            {
                continue;
            }

            StyleValue a = Get(id);
            StyleValue b = other.Get(id);
            if (!ReferenceEquals(a, b) && a.Text != b.Text)
            {
                return PropertyIds.Info[id].Name;
            }
        }

        if (_custom.Values.Count != other._custom.Values.Count)
        {
            return "--(count)";
        }

        foreach (KeyValuePair<string, string> kv in _custom.Values)
        {
            if (!other._custom.Values.TryGetValue(kv.Key, out string? ov) || ov != kv.Value)
            {
                return kv.Key;
            }
        }

        return null;
    }

    /// <summary>A cheaper <see cref="ValueEquals"/> for styles built by different engines: compares the stored values and the custom properties, which cover everything that differs between two styles.</summary>
    internal bool FastValueEquals(ComputedStyle other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_mode != other._mode || _ownIds.Length != other._ownIds.Length)
        {
            return false;
        }

        for (int i = 0; i < _ownIds.Length; i++)
        {
            // The value text is not all there is: the origin of the winning declaration tells a user-agent default from an
            // author's choice of the same value (the accessibility tree reads it for the hidden attribute).
            if (_ownIds[i] != other._ownIds[i] || (!ReferenceEquals(_ownValues[i], other._ownValues[i])
                && (_ownValues[i].Text != other._ownValues[i].Text || _ownValues[i].Source?.Origin != other._ownValues[i].Source?.Origin)))
            {
                return false;
            }
        }

        for (int i = 0; i < _inherited.Length; i++)
        {
            StyleValue? a = _inherited[i];
            StyleValue? b = other._inherited[i];
            if (!ReferenceEquals(a, b) && (a is null || b is null ? (a ?? InitialOf(PropertyIds.InheritedIds[i])).Text != (b ?? InitialOf(PropertyIds.InheritedIds[i])).Text : a.Text != b.Text))
            {
                return false;
            }
        }

        if (ReferenceEquals(_custom, other._custom))
        {
            return true;
        }

        if (_custom.Values.Count != other._custom.Values.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> kv in _custom.Values)
        {
            if (!other._custom.Values.TryGetValue(kv.Key, out string? ov) || ov != kv.Value)
            {
                return false;
            }
        }

        return true;
    }
}
