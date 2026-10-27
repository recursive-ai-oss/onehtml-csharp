using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>The form state a user or a script can change after the document was parsed.</summary>
public enum FormStateKind : byte
{
    /// <summary>Checkedness of a checkbox or radio button, and selectedness of an <c>option</c> (<c>:checked</c>).</summary>
    Checked = 0,

    /// <summary>The disabled state a <c>disabled</c> attribute gives a control, <c>fieldset</c>, <c>optgroup</c> or <c>option</c> (<c>:disabled</c>, <c>:enabled</c>).</summary>
    Disabled = 1,

    /// <summary>The <c>indeterminate</c> state of a checkbox (<c>:indeterminate</c>).</summary>
    Indeterminate = 2,
}

/// <summary>
/// One change to the form state of one element, for <see cref="FormStateOverrides.With(ReadOnlySpan{FormStateChange})"/> and
/// <see cref="Css.LiveDocumentStyles.ApplyFormState"/>. A value type, so building a span of them allocates nothing.
/// </summary>
public readonly struct FormStateChange
{
    /// <summary>Makes a change.</summary>
    /// <param name="element">The element.</param>
    /// <param name="kind">Which state.</param>
    /// <param name="value">The new value; <see langword="null"/> removes the override, so the element reads its attributes again.</param>
    public FormStateChange(Node element, FormStateKind kind, bool? value)
    {
        Element = element;
        Kind = kind;
        Value = value;
    }

    /// <summary>The element whose state changes.</summary>
    public Node Element { get; }

    /// <summary>Which state.</summary>
    public FormStateKind Kind { get; }

    /// <summary>The new value, or <see langword="null"/> for "read the attributes".</summary>
    public bool? Value { get; }

    /// <summary>Sets checkedness (or an option's selectedness).</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value, or <see langword="null"/> to read the attributes.</param>
    public static FormStateChange Checked(Node element, bool? value) => new(element, FormStateKind.Checked, value);

    /// <summary>Sets the disabled state.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value, or <see langword="null"/> to read the attributes.</param>
    public static FormStateChange Disabled(Node element, bool? value) => new(element, FormStateKind.Disabled, value);

    /// <summary>Sets the indeterminate state of a checkbox.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value, or <see langword="null"/> to read the attributes (not indeterminate).</param>
    public static FormStateChange Indeterminate(Node element, bool? value) => new(element, FormStateKind.Indeterminate, value);
}

/// <summary>
/// Form state that differs from what the document's attributes say: a checkbox the user ticked, a control a script disabled, a
/// tri-state checkbox. Any number of elements can carry overrides. The object is immutable and belongs to one document; make a variant with
/// <c>With…</c>, put it in a <see cref="Css.StyleContext"/> (or an <see cref="InteractionState"/>) and every selector that reads form
/// state sees it. Without an override an element reads its attributes, as before.
/// </summary>
/// <remarks>
/// <para>
/// What an override means (HTML Standard 4.16.3, 4.10.5.1.15, 4.10.7): <see cref="FormStateKind.Checked"/> replaces the checkedness of an
/// <c>input</c> of type checkbox or radio, and the selectedness of an <c>option</c>, that the <c>checked</c> and <c>selected</c> attributes
/// would give (what <c>input.checked = …</c> and <c>option.selected = …</c> do); <see cref="FormStateKind.Disabled"/> replaces the presence of
/// the <c>disabled</c> attribute on a control, <c>fieldset</c>, <c>optgroup</c> or <c>option</c>; <see cref="FormStateKind.Indeterminate"/> sets the
/// <c>indeterminate</c> property of a checkbox. An override on any other kind of element is accepted and has no effect.
/// </para>
/// <para>
/// Overrides are literal: they do not do what a browser's script and user-interface layer does around them. Checking a radio button does not
/// uncheck the others of its group, and selecting an option does not deselect the others of a single-choice <c>select</c>; state each element
/// that changed.
/// </para>
/// <para>
/// Related state follows from the overrides because the pseudo-classes read it: <c>:enabled</c> is the complement of <c>:disabled</c>; a
/// disabled <c>fieldset</c> disables the controls in it (except in its first <c>legend</c>) and a disabled <c>optgroup</c> its options;
/// <c>:read-only</c> and <c>:read-write</c> follow disabledness; a radio button is <c>:indeterminate</c> when no member of its named group is
/// checked (its own checkedness when it has no name). Not following: <c>:default</c> (it reads content attributes), <c>:required</c>,
/// <c>:optional</c>, <c>:placeholder-shown</c> and the constraint-validation pseudo-classes <c>:valid</c>, <c>:invalid</c>, <c>:in-range</c> and
/// <c>:out-of-range</c>, which keep reading attributes (script-set values and custom validity are not modelled).
/// </para>
/// </remarks>
public sealed class FormStateOverrides : IEquatable<FormStateOverrides>
{
    private const int CheckedShift = 0;
    private const int DisabledShift = 2;
    private const int IndeterminateShift = 4;

    /// <summary>No overrides.</summary>
    public static FormStateOverrides Empty { get; } = new(null, null);

    private readonly Document? _doc;
    private readonly Dictionary<int, byte>? _sparse;
    private readonly byte[]? _dense;
    private Dictionary<int, int>? _deltaSparse;
    private int[]? _deltaDense;
    private int _count;

    private FormStateOverrides(Document? doc, Dictionary<int, byte>? sparse)
    {
        _doc = doc;
        _sparse = sparse;
    }

    // The live styles keep a mutable instance with arrays indexed by node, so a change costs no allocation and a read is an array load.
    private FormStateOverrides(Document doc)
    {
        _doc = doc;
        _dense = new byte[doc.NodeCount];
        _deltaDense = new int[doc.Index.Forms.RadioGroupMembers.Length];
    }

    /// <summary>True when no element has an override.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>The number of elements that have at least one override.</summary>
    public int Count => _count;

    /// <summary>The checkedness override of an element, or <see langword="null"/> when it reads its attributes.</summary>
    /// <param name="element">The element.</param>
    public bool? GetChecked(Node element) => Decode(Read(element), CheckedShift);

    /// <summary>The disabled override of an element, or <see langword="null"/> when it reads its attributes.</summary>
    /// <param name="element">The element.</param>
    public bool? GetDisabled(Node element) => Decode(Read(element), DisabledShift);

    /// <summary>The indeterminate override of an element, or <see langword="null"/> when it reads its attributes.</summary>
    /// <param name="element">The element.</param>
    public bool? GetIndeterminate(Node element) => Decode(Read(element), IndeterminateShift);

    /// <summary>These overrides with one checkedness changed.</summary>
    /// <param name="element">An element of the document the overrides belong to (any element when they are empty).</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the override.</param>
    /// <exception cref="ArgumentException">The node is not an element, or belongs to another document.</exception>
    public FormStateOverrides WithChecked(Node element, bool? value) => With(FormStateChange.Checked(element, value));

    /// <summary>These overrides with one disabled state changed.</summary>
    /// <param name="element">An element of the document the overrides belong to (any element when they are empty).</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the override.</param>
    /// <exception cref="ArgumentException">The node is not an element, or belongs to another document.</exception>
    public FormStateOverrides WithDisabled(Node element, bool? value) => With(FormStateChange.Disabled(element, value));

    /// <summary>These overrides with one indeterminate state changed.</summary>
    /// <param name="element">An element of the document the overrides belong to (any element when they are empty).</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the override.</param>
    /// <exception cref="ArgumentException">The node is not an element, or belongs to another document.</exception>
    public FormStateOverrides WithIndeterminate(Node element, bool? value) => With(FormStateChange.Indeterminate(element, value));

    /// <summary>These overrides with one change applied.</summary>
    /// <param name="change">The change.</param>
    /// <exception cref="ArgumentException">The node is not an element, or belongs to another document.</exception>
    public FormStateOverrides With(FormStateChange change) => With([change]);

    /// <summary>These overrides with several changes applied in order (a later change to the same element and state wins). One copy for all of them.</summary>
    /// <param name="changes">The changes.</param>
    /// <exception cref="ArgumentException">A node is not an element, or belongs to another document.</exception>
    public FormStateOverrides With(ReadOnlySpan<FormStateChange> changes)
    {
        Document? doc = _doc;
        foreach (FormStateChange c in changes)
        {
            Check(c.Element, ref doc);
        }

        if (doc is null)
        {
            return this;
        }

        var copy = new FormStateOverrides(doc, _sparse is null ? [] : new Dictionary<int, byte>(_sparse));
        if (_deltaSparse is not null)
        {
            copy._deltaSparse = new Dictionary<int, int>(_deltaSparse);
        }

        copy._count = _count;
        foreach (FormStateChange c in changes)
        {
            copy.Set(c.Element.Index, c.Kind, c.Value);
        }

        return copy._count == 0 ? Empty : copy;
    }

    /// <inheritdoc />
    public bool Equals(FormStateOverrides? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || _count != other._count)
        {
            return false;
        }

        if (_count == 0)
        {
            return true;
        }

        if (!ReferenceEquals(_doc, other._doc))
        {
            return false;
        }

        foreach ((int index, byte flags) in Entries())
        {
            if (other.Get(index) != flags)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FormStateOverrides);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        int h = _count;
        foreach ((int index, byte flags) in Entries())
        {
            h += index * 31 + flags;
        }

        return h;
    }

    // ---- internals ----

    internal static FormStateOverrides CreateLive(Document doc) => new(doc);

    internal byte Get(int index)
    {
        if (_dense is not null)
        {
            return (uint)index < (uint)_dense.Length ? _dense[index] : (byte)0;
        }

        return _sparse is not null && _sparse.TryGetValue(index, out byte f) ? f : (byte)0;
    }

    internal static bool? CheckedOf(byte flags) => Decode(flags, CheckedShift);

    internal static bool? DisabledOf(byte flags) => Decode(flags, DisabledShift);

    internal static bool? IndeterminateOf(byte flags) => Decode(flags, IndeterminateShift);

    /// <summary>The checked members a radio group gains (or loses, when negative) from the overrides, relative to its attributes.</summary>
    internal int GroupDelta(int group)
    {
        if (_deltaDense is not null)
        {
            return _deltaDense[group];
        }

        return _deltaSparse is not null && _deltaSparse.TryGetValue(group, out int d) ? d : 0;
    }

    internal bool IsLive => _dense is not null;

    internal bool BelongsTo(Document doc) => _count == 0 || ReferenceEquals(_doc, doc);

    /// <summary>Applies one change in place; true when it changed anything. Only for instances this class created for in-place use, and for building a copy.</summary>
    internal bool Set(int index, FormStateKind kind, bool? value)
    {
        int shift = kind switch { FormStateKind.Checked => CheckedShift, FormStateKind.Disabled => DisabledShift, _ => IndeterminateShift };
        int v = value is null ? 0 : value.Value ? 1 : 2;
        byte old = Get(index);
        byte now = (byte)((old & ~(3 << shift)) | (v << shift));
        if (old == now)
        {
            return false;
        }

        if (kind == FormStateKind.Checked && _doc!.Index.Forms.RadioGroupOf.TryGetValue(index, out int group))
        {
            bool attr = ElementState.Has(new Node(_doc, index), "checked");
            int delta = EffectiveChecked(now, attr) - EffectiveChecked(old, attr);
            if (delta != 0)
            {
                if (_deltaDense is not null)
                {
                    _deltaDense[group] += delta;
                }
                else
                {
                    _deltaSparse ??= [];
                    _deltaSparse.TryGetValue(group, out int cur);
                    _deltaSparse[group] = cur + delta;
                }
            }
        }

        if (old == 0)
        {
            _count++;
        }
        else if (now == 0)
        {
            _count--;
        }

        if (_dense is not null)
        {
            _dense[index] = now;
        }
        else if (now == 0)
        {
            _sparse!.Remove(index);
        }
        else
        {
            _sparse![index] = now;
        }

        return true;
    }

    private static int EffectiveChecked(byte flags, bool attribute) => Decode(flags, CheckedShift) is { } b ? (b ? 1 : 0) : (attribute ? 1 : 0);

    /// <summary>The elements with overrides and their flag bytes, in no defined order.</summary>
    internal IEnumerable<(int Index, byte Flags)> Entries()
    {
        if (_dense is not null)
        {
            for (int i = 0; i < _dense.Length; i++)
            {
                if (_dense[i] != 0)
                {
                    yield return (i, _dense[i]);
                }
            }
        }
        else if (_sparse is not null)
        {
            foreach (KeyValuePair<int, byte> kv in _sparse)
            {
                yield return (kv.Key, kv.Value);
            }
        }
    }

    /// <summary>An immutable copy of this object's content (the live state's current overrides).</summary>
    internal FormStateOverrides ToImmutable()
    {
        if (_count == 0)
        {
            return Empty;
        }

        var copy = new FormStateOverrides(_doc, []);
        foreach ((int index, byte flags) in Entries())
        {
            copy.SetFlags(index, flags);
        }

        return copy;
    }

    /// <summary>The overrides carried to another version of the document: entries follow <paramref name="oldToNew"/> (null for the same indices) and are dropped when the new node is not an element.</summary>
    internal FormStateOverrides Rebind(Document newDoc, int[]? oldToNew)
    {
        if (_count == 0)
        {
            return Empty;
        }

        if (ReferenceEquals(newDoc, _doc))
        {
            return this;
        }

        var copy = new FormStateOverrides(newDoc, []);
        foreach ((int index, byte flags) in Entries())
        {
            int to = oldToNew is null ? index : index < oldToNew.Length ? oldToNew[index] : -1;
            if (to >= 0 && to < newDoc.NodeCount && newDoc.Arena.Kind[to] == (byte)NodeKind.Element)
            {
                copy.SetFlags(to, flags);
            }
        }

        return copy._count == 0 ? Empty : copy;
    }

    /// <summary>Applies every override of <paramref name="other"/> (same document) to this in-place instance.</summary>
    internal void CopyFrom(FormStateOverrides other)
    {
        foreach ((int index, byte flags) in other.Entries())
        {
            SetFlags(index, flags);
        }
    }

    internal bool? ValueOf(int index, FormStateKind kind) =>
        Decode(Get(index), kind switch { FormStateKind.Checked => CheckedShift, FormStateKind.Disabled => DisabledShift, _ => IndeterminateShift });

    private void SetFlags(int index, byte flags)
    {
        Set(index, FormStateKind.Checked, Decode(flags, CheckedShift));
        Set(index, FormStateKind.Disabled, Decode(flags, DisabledShift));
        Set(index, FormStateKind.Indeterminate, Decode(flags, IndeterminateShift));
    }

    private static bool? Decode(byte flags, int shift) => ((flags >> shift) & 3) switch { 1 => true, 2 => false, _ => null };

    private byte Read(Node element)
    {
        if (element.IsNull || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element.", nameof(element));
        }

        return _count == 0 || !ReferenceEquals(element.Document, _doc) ? (byte)0 : Get(element.Index);
    }

    private static void Check(Node element, ref Document? doc)
    {
        if (element.IsNull || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element.", nameof(element));
        }

        if (doc is not null && !ReferenceEquals(doc, element.Document))
        {
            throw new ArgumentException("The node belongs to another document than the overrides.", nameof(element));
        }

        doc ??= element.Document;
    }
}
