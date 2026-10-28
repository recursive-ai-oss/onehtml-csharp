using System;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>
/// The static form and element state behind <c>:checked</c>, <c>:disabled</c>, <c>:required</c>, <c>:valid</c> and the like
/// (HTML §4.16 "Selectors"), computed from attributes and tree position only. Script-set state (dirty values and
/// checkedness, custom validity, user edits) does not exist here; where a rule depends on it the static reading is
/// used and noted in <c>docs/selectors.md</c>.
/// </summary>
internal static class ElementState
{
    // ---- attribute helpers ----

    public static bool Has(Node n, string name) => Find(n, name) >= 0;

    public static string AttrString(Node n, string name)
    {
        int i = Find(n, name);
        return i < 0 ? string.Empty : n.AttrValue(i).ToString();
    }

    public static ReadOnlySpan<char> AttrSpan(Node n, string name)
    {
        int i = Find(n, name);
        return i < 0 ? default : n.AttrValue(i);
    }

    private static int Find(Node n, string name)
    {
        Arena a = n.Document!.Arena;
        NameTable names = n.Document.Names;
        int start = a.AttrStart[n.Index];
        int end = start + a.AttrCount[n.Index];
        for (int i = start; i < end; i++)
        {
            if ((a.AFlags[i] & AF.NsMask) == 0 && string.Equals(names.NameOf(a.AName[i]), name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsHtml(Node n, string localName) =>
        n.IsElement && n.Namespace == ElementNamespace.Html && string.Equals(n.LocalName, localName, StringComparison.Ordinal);

    private static bool IsHtmlIn(Node n, string a, string b) => IsHtml(n, a) || IsHtml(n, b);

    private static bool EqualsIgnoreAsciiCase(ReadOnlySpan<char> s, string lower) => LowerEquals(s, lower);

    internal static bool LowerEquals(ReadOnlySpan<char> s, string lower)
    {
        if (s.Length != lower.Length)
        {
            return false;
        }

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is >= 'A' and <= 'Z')
            {
                c = (char)(c + 0x20);
            }

            if (c != lower[i])
            {
                return false;
            }
        }

        return true;
    }

    public static Node Parent(Node n)
    {
        Arena a = n.Document!.Arena;
        int p = a.Parent[n.Index];
        return p >= 0 && a.Kind[p] == (byte)NodeKind.Element ? new Node(n.Document, p) : default;
    }

    // ---- input types ----

    public static InputType GetInputType(Node n)
    {
        if (!IsHtml(n, "input"))
        {
            return InputType.NotInput;
        }

        ReadOnlySpan<char> t = AttrSpan(n, "type");
        return t.Length switch
        {
            0 => InputType.Text,
            _ => Classify(t),
        };
    }

    private static InputType Classify(ReadOnlySpan<char> t)
    {
        if (LowerEquals(t, "hidden"))
        {
            return InputType.Hidden;
        }

        if (LowerEquals(t, "text"))
        {
            return InputType.Text;
        }

        if (LowerEquals(t, "search"))
        {
            return InputType.Search;
        }

        if (LowerEquals(t, "tel"))
        {
            return InputType.Tel;
        }

        if (LowerEquals(t, "url"))
        {
            return InputType.Url;
        }

        if (LowerEquals(t, "email"))
        {
            return InputType.Email;
        }

        if (LowerEquals(t, "password"))
        {
            return InputType.Password;
        }

        if (LowerEquals(t, "date"))
        {
            return InputType.Date;
        }

        if (LowerEquals(t, "month"))
        {
            return InputType.Month;
        }

        if (LowerEquals(t, "week"))
        {
            return InputType.Week;
        }

        if (LowerEquals(t, "time"))
        {
            return InputType.Time;
        }

        if (LowerEquals(t, "datetime-local"))
        {
            return InputType.DatetimeLocal;
        }

        if (LowerEquals(t, "number"))
        {
            return InputType.Number;
        }

        if (LowerEquals(t, "range"))
        {
            return InputType.Range;
        }

        if (LowerEquals(t, "color"))
        {
            return InputType.Color;
        }

        if (LowerEquals(t, "checkbox"))
        {
            return InputType.Checkbox;
        }

        if (LowerEquals(t, "radio"))
        {
            return InputType.Radio;
        }

        if (LowerEquals(t, "file"))
        {
            return InputType.File;
        }

        if (LowerEquals(t, "submit"))
        {
            return InputType.Submit;
        }

        if (LowerEquals(t, "image"))
        {
            return InputType.Image;
        }

        if (LowerEquals(t, "reset"))
        {
            return InputType.Reset;
        }

        if (LowerEquals(t, "button"))
        {
            return InputType.Button;
        }

        return InputType.Text;
    }

    private static bool ReadonlyApplies(InputType t) =>
        t is InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Email or InputType.Password
            or InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DatetimeLocal or InputType.Number;

    private static bool RequiredApplies(InputType t) =>
        ReadonlyApplies(t) || t is InputType.Checkbox or InputType.Radio or InputType.File;

    private static bool PlaceholderApplies(InputType t) =>
        t is InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Email or InputType.Password or InputType.Number;

    public static bool IsSubmitButton(Node n)
    {
        if (IsHtml(n, "button"))
        {
            ReadOnlySpan<char> t = AttrSpan(n, "type");
            return !(LowerEquals(t, "reset") || LowerEquals(t, "button"));
        }

        InputType it = GetInputType(n);
        return it is InputType.Submit or InputType.Image;
    }

    // ---- form owner ----

    /// <summary>The form owner (HTML 4.10.18.3) as an arena index, or -1.</summary>
    public static int FormOwner(Node n)
    {
        Document doc = n.Document!;
        int f = Find(n, "form");
        if (f >= 0)
        {
            Node target = doc.GetElementById(n.AttrValue(f).ToString());
            return IsHtml(target, "form") && target.GetRootNode() == n.GetRootNode() ? target.Index : -1;
        }

        for (Node p = Parent(n); !p.IsNull; p = Parent(p))
        {
            if (IsHtml(p, "form"))
            {
                return p.Index;
            }
        }

        return -1;
    }

    // ---- :disabled / :enabled ----

    private static bool IsFormControl(Node n) => IsHtml(n, "button") || IsHtml(n, "input") || IsHtml(n, "select") || IsHtml(n, "textarea");

    public static bool IsDisabledByFieldset(Node n) => IsDisabledByFieldset(n, null);

    public static bool IsDisabledByFieldset(Node n, FormStateOverrides? ov)
    {
        Node child = n;
        for (Node p = Parent(n); !p.IsNull; child = p, p = Parent(p))
        {
            if (IsHtml(p, "fieldset") && DisabledAttribute(p, ov))
            {
                // Descendants of the fieldset's first legend child are not disabled by it.
                Node firstLegend = default;
                foreach (Node c in p.Children)
                {
                    if (IsHtml(c, "legend"))
                    {
                        firstLegend = c;
                        break;
                    }
                }

                if (firstLegend.IsNull || firstLegend != child)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether the element is one that <c>:enabled</c>/<c>:disabled</c> apply to.</summary>
    public static bool CanBeDisabled(Node n) =>
        IsFormControl(n) || IsHtml(n, "fieldset") || IsHtml(n, "optgroup") || IsHtml(n, "option");

    public static bool IsDisabled(Node n) => IsDisabled(n, null);

    /// <summary>The <c>disabled</c> attribute's presence, or the override that replaces it (a script setting <c>disabled</c> sets the attribute).</summary>
    private static bool DisabledAttribute(Node n, FormStateOverrides? ov) =>
        ov is not null && FormStateOverrides.DisabledOf(ov.Get(n.Index)) is { } v ? v : Has(n, "disabled");

    /// <summary>The checkedness of a checkbox or radio: the override, or the <c>checked</c> attribute (the default checkedness).</summary>
    private static bool CheckednessOf(Node n, FormStateOverrides? ov) =>
        ov is not null && FormStateOverrides.CheckedOf(ov.Get(n.Index)) is { } v ? v : Has(n, "checked");

    public static bool IsDisabled(Node n, FormStateOverrides? ov)
    {
        if (IsFormControl(n))
        {
            return DisabledAttribute(n, ov) || IsDisabledByFieldset(n, ov);
        }

        if (IsHtml(n, "fieldset"))
        {
            return DisabledAttribute(n, ov) || IsDisabledByFieldset(n, ov);
        }

        if (IsHtml(n, "optgroup"))
        {
            return DisabledAttribute(n, ov);
        }

        if (IsHtml(n, "option"))
        {
            if (DisabledAttribute(n, ov))
            {
                return true;
            }

            Node p = Parent(n);
            return IsHtml(p, "optgroup") && DisabledAttribute(p, ov);
        }

        return false;
    }

    // ---- :checked, selectedness ----

    public static bool IsChecked(Node n) => IsChecked(n, null);

    public static bool IsChecked(Node n, FormStateOverrides? ov)
    {
        InputType t = GetInputType(n);
        if (t is InputType.Checkbox or InputType.Radio)
        {
            return CheckednessOf(n, ov);
        }

        if (!IsHtml(n, "option"))
        {
            return false;
        }

        // An override is the option's selectedness itself; the rest of the select is not touched (see FormStateOverrides).
        return ov is not null && FormStateOverrides.CheckedOf(ov.Get(n.Index)) is { } v ? v : IsSelectedOption(n);
    }

    private static Node SelectOf(Node option)
    {
        Node p = Parent(option);
        if (IsHtml(p, "optgroup"))
        {
            p = Parent(p);
        }

        return IsHtml(p, "select") ? p : default;
    }

    /// <summary>The option's selectedness after the HTML "selectedness setting algorithm" and "ask for a reset", from content attributes.</summary>
    public static bool IsSelectedOption(Node option)
    {
        Node select = SelectOf(option);
        bool own = Has(option, "selected");
        if (select.IsNull)
        {
            return own;
        }

        if (Has(select, "multiple"))
        {
            return own;
        }

        // Single-select: the last option with the attribute wins; with none, a one-row select selects the first enabled option.
        Node lastSelected = default;
        Node firstEnabled = default;
        foreach (Node c in select.Children)
        {
            if (IsHtml(c, "option"))
            {
                Note(c, false, ref lastSelected, ref firstEnabled);
            }
            else if (IsHtml(c, "optgroup"))
            {
                bool groupDisabled = Has(c, "disabled");
                foreach (Node o in c.Children)
                {
                    if (IsHtml(o, "option"))
                    {
                        Note(o, groupDisabled, ref lastSelected, ref firstEnabled);
                    }
                }
            }
        }

        if (!lastSelected.IsNull)
        {
            return lastSelected == option;
        }

        return DisplaySize(select) == 1 && firstEnabled == option;
    }

    private static void Note(Node option, bool inDisabledGroup, ref Node lastSelected, ref Node firstEnabled)
    {
        if (Has(option, "selected"))
        {
            lastSelected = option;
        }

        if (firstEnabled.IsNull && !Has(option, "disabled") && !inDisabledGroup)
        {
            firstEnabled = option;
        }
    }

    private static int DisplaySize(Node select)
    {
        ReadOnlySpan<char> s = AttrSpan(select, "size");
        if (s.Length > 0 && int.TryParse(s.Trim(" \t\n\f\r"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) && v > 0)
        {
            return v;
        }

        return Has(select, "multiple") ? 4 : 1;
    }

    private static void ForEachOption(Node select, Action<Node> action)
    {
        foreach (Node c in select.Children)
        {
            if (IsHtml(c, "option"))
            {
                action(c);
            }
            else if (IsHtml(c, "optgroup"))
            {
                foreach (Node o in c.Children)
                {
                    if (IsHtml(o, "option"))
                    {
                        action(o);
                    }
                }
            }
        }
    }

    // ---- :required / :optional ----

    public static bool RequiredOrOptionalApplies(Node n)
    {
        InputType t = GetInputType(n);
        return t != InputType.NotInput ? RequiredApplies(t) : IsHtml(n, "select") || IsHtml(n, "textarea");
    }

    // ---- :read-write / :read-only ----

    public static bool IsReadWrite(Node n) => IsReadWrite(n, null);

    public static bool IsReadWrite(Node n, FormStateOverrides? ov)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        InputType t = GetInputType(n);
        if (t != InputType.NotInput)
        {
            return ReadonlyApplies(t) && !Has(n, "readonly") && !IsDisabled(n, ov);
        }

        if (IsHtml(n, "textarea"))
        {
            return !Has(n, "readonly") && !IsDisabled(n, ov);
        }

        return IsEditable(n);
    }

    /// <summary>A text-entry control (<c>textarea</c>, or an <c>input</c> that takes typed text) or an editing host: the elements that match <c>:focus-visible</c> after pointer focus.</summary>
    public static bool IsTextEntryOrEditingHost(Node n)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        InputType t = GetInputType(n);
        if (t != InputType.NotInput)
        {
            return ReadonlyApplies(t);
        }

        return IsHtml(n, "textarea") || IsEditable(n);
    }

    private static bool IsEditable(Node n)
    {
        for (Node p = n; !p.IsNull; p = Parent(p))
        {
            int i = Find(p, "contenteditable");
            if (i < 0)
            {
                continue;
            }

            ReadOnlySpan<char> v = p.AttrValue(i);
            if (v.Length == 0 || LowerEquals(v, "true") || LowerEquals(v, "plaintext-only"))
            {
                return true;
            }

            if (LowerEquals(v, "false"))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the element can have the parts of a range slider (<c>::slider-thumb</c> and the others): an <c>input</c> whose type state is Range
    /// (HTML Standard 4.10.5.1.13; the <c>type</c> value is matched ASCII case-insensitively).
    /// </summary>
    public static bool HasRangeBox(Node n) => GetInputType(n) == InputType.Range;

    // ---- :placeholder-shown ----

    /// <summary>Whether the element can have a <c>::placeholder</c>: a text-like <c>input</c> or a <c>textarea</c> with a non-empty <c>placeholder</c> attribute.</summary>
    public static bool HasPlaceholderBox(Node n)
    {
        InputType t = GetInputType(n);
        return ((t != InputType.NotInput && PlaceholderApplies(t)) || IsHtml(n, "textarea")) && AttrSpan(n, "placeholder").Length > 0;
    }

    public static bool IsPlaceholderShown(Node n)
    {
        InputType t = GetInputType(n);
        bool isTextarea = IsHtml(n, "textarea");
        if (!(t != InputType.NotInput && PlaceholderApplies(t)) && !isTextarea)
        {
            return false;
        }

        if (AttrSpan(n, "placeholder").Length == 0)
        {
            return false;
        }

        return isTextarea ? (n.TextContent ?? string.Empty).Length == 0 : InputValue(n, t).Length == 0;
    }

    private static string InputValue(Node n, InputType t)
    {
        string v = AttrString(n, "value");
        return Sanitize(t, v);
    }

    /// <summary>The value sanitization algorithm (HTML 4.10.5.1) for the types where it matters here.</summary>
    private static string Sanitize(InputType t, string v)
    {
        switch (t)
        {
            case InputType.Text:
            case InputType.Search:
            case InputType.Tel:
            case InputType.Password:
                return v.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
            case InputType.Url:
            case InputType.Email:
                return v.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Trim(' ', '\t', '\f');
            case InputType.Number:
                return TryParseNumber(v, out _) ? v : string.Empty;
            case InputType.Date:
                return TryParseTemporal(t, v, out _) ? v : string.Empty;
            case InputType.Month:
            case InputType.Week:
            case InputType.Time:
            case InputType.DatetimeLocal:
                return TryParseTemporal(t, v, out _) ? v : string.Empty;
            default:
                return v;
        }
    }

    // ---- :default ----

    public static bool IsDefault(Node n)
    {
        InputType t = GetInputType(n);
        if (t is InputType.Checkbox or InputType.Radio)
        {
            return Has(n, "checked");
        }

        if (IsHtml(n, "option"))
        {
            return Has(n, "selected");
        }

        return IsSubmitButton(n) && n.Document!.Index.Forms.DefaultButtons.Contains(n.Index);
    }

    // ---- :indeterminate ----

    public static bool IsIndeterminate(Node n) => IsIndeterminate(n, null);

    public static bool IsIndeterminate(Node n, FormStateOverrides? ov)
    {
        if (IsHtml(n, "progress"))
        {
            return !Has(n, "value");
        }

        InputType type = GetInputType(n);
        if (type == InputType.Checkbox)
        {
            return ov is not null && FormStateOverrides.IndeterminateOf(ov.Get(n.Index)) is true;
        }

        if (type != InputType.Radio)
        {
            return false;
        }

        if (AttrSpan(n, "name").Length == 0)
        {
            return !CheckednessOf(n, ov);
        }

        // A named group is indeterminate when no member is checked: the members whose attribute says checked, adjusted by the overrides.
        if (ov is not null && n.Document!.Index.Forms is { } forms && forms.RadioGroupOf.TryGetValue(n.Index, out int group))
        {
            return forms.RadioGroupBaseChecked[group] + ov.GroupDelta(group) <= 0;
        }

        string name = AttrString(n, "name");

        var key = (FormOwner(n), Node.RootIndex(n.Document!.Arena, n.Index), name);
        return !n.Document.Index.Forms.RadioGroupChecked.TryGetValue(key, out bool any) || !any;
    }

    // ---- constraint validation (:valid, :invalid, :in-range, :out-of-range) ----

    private static bool IsBarred(Node n)
    {
        // HTML 4.10.18.3: disabled, readonly (where it applies), a datalist descendant, and some types are barred.
        if (IsDisabled(n))
        {
            return true;
        }

        for (Node p = Parent(n); !p.IsNull; p = Parent(p))
        {
            if (IsHtml(p, "datalist"))
            {
                return true;
            }
        }

        InputType t = GetInputType(n);
        if (t != InputType.NotInput)
        {
            return t is InputType.Hidden or InputType.Reset or InputType.Button || (ReadonlyApplies(t) && Has(n, "readonly"));
        }

        if (IsHtml(n, "textarea"))
        {
            return Has(n, "readonly");
        }

        if (IsHtml(n, "button"))
        {
            return !IsSubmitButton(n);
        }

        return false;
    }

    private static bool IsCandidate(Node n) => IsFormControl(n) && !IsBarred(n);

    public static bool IsValidMatch(Node n, bool wantValid)
    {
        if (IsHtml(n, "form") || IsHtml(n, "fieldset"))
        {
            // Valid when no candidate descendant control is invalid.
            Node root = n;
            for (Node d = n.NextInDocumentOrder(root); !d.IsNull; d = d.NextInDocumentOrder(root))
            {
                if (d.IsElement && IsCandidate(d) && !IsControlValid(d))
                {
                    return !wantValid;
                }
            }

            return wantValid;
        }

        if (!IsCandidate(n))
        {
            return false;
        }

        return IsControlValid(n) == wantValid;
    }

    private static bool IsControlValid(Node n)
    {
        InputType t = GetInputType(n);
        if (IsHtml(n, "select"))
        {
            return !(Has(n, "required") && SelectValueMissing(n));
        }

        if (IsHtml(n, "textarea"))
        {
            return !(Has(n, "required") && (n.TextContent ?? string.Empty).Length == 0);
        }

        if (t == InputType.NotInput)
        {
            return true;
        }

        if (Has(n, "required") && RequiredApplies(t) && ValueMissing(n, t))
        {
            return false;
        }

        string value = t is InputType.Checkbox or InputType.Radio or InputType.File ? string.Empty : InputValue(n, t);
        if (value.Length == 0)
        {
            return true;
        }

        switch (t)
        {
            case InputType.Email:
                if (Has(n, "multiple"))
                {
                    foreach (string part in value.Split(','))
                    {
                        if (!IsValidEmail(part.Trim(' ', '\t', '\n', '\f', '\r')))
                        {
                            return false;
                        }
                    }
                }
                else if (!IsValidEmail(value))
                {
                    return false;
                }

                break;
            case InputType.Url:
                if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    return false;
                }

                break;
        }

        if (t is InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Email or InputType.Password
            && !PatternMatches(AttrString(n, "pattern"), value))
        {
            return false;
        }

        return RangeState(n, t) == 0 && StepOk(n, t, value);
    }

    /// <summary>
    /// HTML 4.10.5.3.6: the value must match <c>^(?:pattern)$</c>. The engine is the BCL's in ECMAScript mode with a
    /// timeout; a pattern it cannot compile (or that runs out of time) is ignored, as an invalid pattern is.
    /// </summary>
    private static bool PatternMatches(string pattern, string value)
    {
        if (pattern.Length == 0 || pattern.Length > 4096)
        {
            return true;
        }

        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                value,
                "^(?:" + pattern + ")$",
                System.Text.RegularExpressions.RegexOptions.ECMAScript,
                TimeSpan.FromMilliseconds(50));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static bool ValueMissing(Node n, InputType t)
    {
        switch (t)
        {
            case InputType.Checkbox:
                return !Has(n, "checked");
            case InputType.Radio:
            {
                string name = AttrString(n, "name");
                if (name.Length == 0)
                {
                    return !Has(n, "checked");
                }

                var key = (FormOwner(n), Node.RootIndex(n.Document!.Arena, n.Index), name);
                return !n.Document.Index.Forms.RadioGroupChecked.TryGetValue(key, out bool any) || !any;
            }

            case InputType.File:
                return true;
            default:
                return InputValue(n, t).Length == 0;
        }
    }

    private static bool SelectValueMissing(Node select)
    {
        bool multiple = Has(select, "multiple");
        bool any = false;
        bool onlyPlaceholder = false;
        Node first = default;
        ForEachOption(select, o =>
        {
            if (first.IsNull)
            {
                first = o;
            }

            if (IsSelectedOption(o))
            {
                any = true;
                if (!multiple && o == first && IsHtml(Parent(o), "select") && OptionValue(o).Length == 0 && DisplaySize(select) == 1)
                {
                    onlyPlaceholder = true;
                }
            }
        });
        return !any || onlyPlaceholder;
    }

    private static string OptionValue(Node o) => Has(o, "value") ? AttrString(o, "value") : (o.TextContent ?? string.Empty).Trim(' ', '\t', '\n', '\f', '\r');

    /// <summary>HTML 4.10.5.1.5: the "valid e-mail address" production.</summary>
    private static bool IsValidEmail(string s)
    {
        int at = s.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == s.Length - 1 || s.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        for (int i = 0; i < at; i++)
        {
            char c = s[i];
            if (!(char.IsAsciiLetterOrDigit(c) || ".!#$%&'*+/=?^_`{|}~-".Contains(c, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        string[] labels = s[(at + 1)..].Split('.');
        foreach (string label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (char c in label)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---- ranges ----

    /// <summary>0 in range or no limits; -1 underflow; 1 overflow. Only for types with range limitations.</summary>
    private static int RangeState(Node n, InputType t)
    {
        if (!HasRange(t))
        {
            return 0;
        }

        string raw = Sanitize(t, AttrString(n, "value"));
        double? min = ParseLimit(n, t, "min");
        double? max = ParseLimit(n, t, "max");
        if (t == InputType.Range)
        {
            return 0; // The slider clamps its value into [min, max].
        }

        if (raw.Length == 0 || !ParseValue(t, raw, out double v))
        {
            return 0;
        }

        if (t == InputType.Time && min is not null && max is not null && min > max)
        {
            // A reversed range wraps around midnight: the value is out of range between max and min.
            return v > max && v < min ? 1 : 0;
        }

        if (min is not null && v < min)
        {
            return -1;
        }

        if (max is not null && v > max)
        {
            return 1;
        }

        return 0;
    }

    private static bool HasRange(InputType t) =>
        t is InputType.Number or InputType.Range or InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DatetimeLocal;

    public static bool IsInOutOfRange(Node n, bool wantOut)
    {
        InputType t = GetInputType(n);
        if (!HasRange(t) || !IsCandidate(n))
        {
            return false;
        }

        bool limits = t == InputType.Range || ParseLimit(n, t, "min") is not null || ParseLimit(n, t, "max") is not null;
        if (!limits)
        {
            return false;
        }

        bool out_ = RangeState(n, t) != 0;
        return out_ == wantOut;
    }

    private static double? ParseLimit(Node n, InputType t, string name)
    {
        string s = AttrString(n, name);
        if (s.Length == 0)
        {
            return null;
        }

        return ParseValue(t, s, out double v) ? v : null;
    }

    private static bool ParseValue(InputType t, string s, out double v)
    {
        if (t is InputType.Number or InputType.Range)
        {
            return TryParseNumber(s, out v);
        }

        return TryParseTemporal(t, s, out v);
    }

    private static bool StepOk(Node n, InputType t, string value)
    {
        if (t is not (InputType.Number or InputType.Range))
        {
            return true;
        }

        string stepAttr = AttrString(n, "step");
        double step = 1;
        if (stepAttr.Length > 0)
        {
            if (stepAttr.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!TryParseNumber(stepAttr, out step) || step <= 0)
            {
                step = 1;
            }
        }

        double min = ParseLimit(n, t, "min") ?? (TryParseNumber(AttrString(n, "value"), out double dv) ? dv : 0);
        if (!TryParseNumber(value, out double v))
        {
            return true;
        }

        double q = (v - min) / step;
        return Math.Abs(q - Math.Round(q)) < 1e-9 * Math.Max(1, Math.Abs(q));
    }

    /// <summary>HTML 2.3.4.3 "rules for parsing floating-point number values", for valid numbers only.</summary>
    internal static bool TryParseNumber(string s, out double v)
    {
        v = 0;
        int i = 0;
        int n = s.Length;
        if (i < n && s[i] == '-')
        {
            i++;
        }

        int digits = 0;
        while (i < n && char.IsAsciiDigit(s[i]))
        {
            i++;
            digits++;
        }

        if (i < n && s[i] == '.')
        {
            i++;
            int frac = 0;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
                frac++;
            }

            if (frac == 0)
            {
                return false;
            }

            digits += frac;
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < n && s[i] is 'e' or 'E')
        {
            i++;
            if (i < n && s[i] is '+' or '-')
            {
                i++;
            }

            int exp = 0;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
                exp++;
            }

            if (exp == 0)
            {
                return false;
            }
        }

        return i == n && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
    }

    /// <summary>Parses date, month, week, time and datetime-local strings into a comparable number.</summary>
    internal static bool TryParseTemporal(InputType t, string s, out double v)
    {
        v = 0;
        switch (t)
        {
            case InputType.Date:
                return TryDate(s.AsSpan(), out v);
            case InputType.Month:
            {
                if (s.Length < 7 || s[^3] != '-' || !TryInt(s.AsSpan(0, s.Length - 3), 4, out int y) || !TryInt(s.AsSpan(s.Length - 2), 2, out int m) || y < 1 || m is < 1 or > 12)
                {
                    return false;
                }

                v = ((y - 1970) * 12) + m - 1;
                return true;
            }

            case InputType.Week:
            {
                int w = s.IndexOf("-W", StringComparison.Ordinal);
                if (w < 4 || s.Length != w + 4 || !TryInt(s.AsSpan(0, w), 4, out int y) || !TryInt(s.AsSpan(w + 2), 2, out int wk) || y < 1 || wk < 1 || wk > WeeksInYear(y))
                {
                    return false;
                }

                v = (y * 53) + wk;
                return true;
            }

            case InputType.Time:
                return TryTime(s.AsSpan(), out v);
            case InputType.DatetimeLocal:
            {
                int sep = s.IndexOfAny(['T', ' ']);
                if (sep < 0 || !TryDate(s.AsSpan(0, sep), out double d) || !TryTime(s.AsSpan(sep + 1), out double tm))
                {
                    return false;
                }

                v = d + tm;
                return true;
            }

            default:
                return false;
        }
    }

    private static int WeeksInYear(int y)
    {
        // A year has 53 weeks when January 1 is a Thursday, or a Wednesday in a leap year.
        var jan1 = new DateTime(Math.Min(y, 9999), 1, 1);
        DayOfWeek d = jan1.DayOfWeek;
        return d == DayOfWeek.Thursday || (d == DayOfWeek.Wednesday && DateTime.IsLeapYear(Math.Min(y, 9999))) ? 53 : 52;
    }

    private static bool TryInt(ReadOnlySpan<char> s, int minDigits, out int v)
    {
        v = 0;
        if (s.Length < minDigits || s.Length > 9)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            v = (v * 10) + (c - '0');
        }

        return true;
    }

    private static bool TryDate(ReadOnlySpan<char> s, out double v)
    {
        v = 0;
        if (s.Length < 10 || s[^3] != '-' || s[^6] != '-' || !TryInt(s[..^6], 4, out int y) || !TryInt(s.Slice(s.Length - 5, 2), 2, out int m) || !TryInt(s[^2..], 2, out int d))
        {
            return false;
        }

        if (y < 1 || y > 9999 || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, m))
        {
            return false;
        }

        v = (new DateTime(y, m, d) - DateTime.UnixEpoch).TotalDays * 86400000d;
        return true;
    }

    private static bool TryTime(ReadOnlySpan<char> s, out double v)
    {
        v = 0;
        if (s.Length < 5 || s[2] != ':' || !TryInt(s[..2], 2, out int h) || !TryInt(s.Slice(3, 2), 2, out int mi) || h > 23 || mi > 59)
        {
            return false;
        }

        double ms = ((h * 60) + mi) * 60000d;
        if (s.Length > 5)
        {
            if (s[5] != ':' || s.Length < 8 || !TryInt(s.Slice(6, 2), 2, out int sec) || sec > 59)
            {
                return false;
            }

            ms += sec * 1000d;
            if (s.Length > 8)
            {
                if (s[8] != '.' || s.Length is < 10 or > 12)
                {
                    return false;
                }

                ReadOnlySpan<char> frac = s[9..];
                if (!TryInt(frac, 1, out int f))
                {
                    return false;
                }

                ms += f * Math.Pow(10, 3 - frac.Length);
            }
        }

        v = ms;
        return true;
    }

    // ---- :defined, :link, :open, headings ----

    public static bool IsDefined(Node n)
    {
        if (n.Namespace != ElementNamespace.Html)
        {
            return true;
        }

        return !(IsValidCustomElementName(n.LocalName) || Has(n, "is"));
    }

    private static bool IsValidCustomElementName(string s)
    {
        if (s.Length < 3 || s[0] is not (>= 'a' and <= 'z') || s.IndexOf('-', StringComparison.Ordinal) < 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return false;
            }
        }

        return s is not ("annotation-xml" or "color-profile" or "font-face" or "font-face-src" or "font-face-uri" or "font-face-format" or "font-face-name" or "missing-glyph");
    }

    public static bool IsLink(Node n) => n.Namespace == ElementNamespace.Html && (IsHtml(n, "a") || IsHtml(n, "area")) && Has(n, "href");

    public static bool IsOpen(Node n) => (IsHtml(n, "details") || IsHtml(n, "dialog")) && Has(n, "open");

    public static int HeadingLevel(Node n)
    {
        if (n.Namespace != ElementNamespace.Html)
        {
            return 0;
        }

        string name = n.LocalName;
        return name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6' ? name[1] - '0' : 0;
    }

    // ---- :lang() ----

    /// <summary>HTML 3.2.6 "language": the nearest lang/xml:lang, else the document's content-language pragma. A slice of the document text; allocation-free.</summary>
    public static bool TryGetLanguage(Node el, out ReadOnlySpan<char> language)
    {
        for (Node n = el; !n.IsNull; n = Parent(n))
        {
            AttributeList list = n.Attributes;
            bool haveXml = false;
            ReadOnlySpan<char> xml = default;
            for (int i = 0; i < list.Count; i++)
            {
                Attr at = list[i];
                if (at.Namespace == AttributeNamespace.None && at.LocalName == "lang")
                {
                    language = at.Value;
                    return true;
                }

                if (at.Namespace == AttributeNamespace.Xml && at.LocalName == "lang")
                {
                    xml = at.Value;
                    haveXml = true;
                }
            }

            if (haveXml)
            {
                language = xml;
                return true;
            }
        }

        string? pragma = el.Document!.Index.PragmaLanguage;
        language = pragma ?? string.Empty;
        return pragma is not null;
    }

    /// <summary>RFC 4647 §3.3.2 extended filtering, ASCII case-insensitive. Works on slices of both strings and allocates nothing.</summary>
    public static bool LangMatches(ReadOnlySpan<char> range, ReadOnlySpan<char> tag)
    {
        if (range.Length == 0)
        {
            return tag.Length == 0;
        }

        if (tag.Length == 0)
        {
            return false;
        }

        int rp = 0;
        int tp = 0;
        NextSubtag(range, ref rp, out ReadOnlySpan<char> r0);
        NextSubtag(tag, ref tp, out ReadOnlySpan<char> t0);
        if (!r0.SequenceEqual("*") && !SubtagEquals(r0, t0))
        {
            return false;
        }

        bool haveTag = NextSubtag(tag, ref tp, out ReadOnlySpan<char> ts);
        while (NextSubtag(range, ref rp, out ReadOnlySpan<char> rs))
        {
            if (rs.SequenceEqual("*"))
            {
                continue;
            }

            // Skip the tag's subtags until one equals this range subtag; a singleton subtag ends the search.
            while (true)
            {
                if (!haveTag)
                {
                    return false;
                }

                if (SubtagEquals(rs, ts))
                {
                    haveTag = NextSubtag(tag, ref tp, out ts);
                    break;
                }

                if (ts.Length == 1)
                {
                    return false;
                }

                haveTag = NextSubtag(tag, ref tp, out ts);
            }
        }

        return true;
    }

    // The next '-' separated subtag, as string.Split would give them (empty ones included); false once they are used up.
    private static bool NextSubtag(ReadOnlySpan<char> s, ref int pos, out ReadOnlySpan<char> subtag)
    {
        if (pos > s.Length)
        {
            subtag = default;
            return false;
        }

        ReadOnlySpan<char> rest = s[pos..];
        int dash = rest.IndexOf('-');
        if (dash < 0)
        {
            subtag = rest;
            pos = s.Length + 1;
        }
        else
        {
            subtag = rest[..dash];
            pos += dash + 1;
        }

        return true;
    }

    private static bool SubtagEquals(ReadOnlySpan<char> a, ReadOnlySpan<char> b) => a.Length == b.Length && ClassList.IsAsciiEqual(a, b);

    // ---- :dir() ----

    /// <summary>HTML 3.2.6.1: the directionality of an element: true for rtl.</summary>
    public static bool IsRtl(Node el)
    {
        for (Node n = el; !n.IsNull; n = Parent(n))
        {
            if (n.Namespace != ElementNamespace.Html)
            {
                continue;
            }

            ReadOnlySpan<char> d = AttrSpan(n, "dir");
            if (LowerEquals(d, "ltr"))
            {
                return false;
            }

            if (LowerEquals(d, "rtl"))
            {
                return true;
            }

            bool auto = LowerEquals(d, "auto") || (IsHtml(n, "bdi") && !(LowerEquals(d, "ltr") || LowerEquals(d, "rtl")));
            if (auto)
            {
                return AutoIsRtl(n);
            }

            if (GetInputType(n) == InputType.Tel)
            {
                return false;
            }
        }

        return false;
    }

    private static bool AutoIsRtl(Node n)
    {
        InputType t = GetInputType(n);
        if (IsHtml(n, "textarea") || t is InputType.Text or InputType.Search or InputType.Tel or InputType.Url or InputType.Email)
        {
            string text = IsHtml(n, "textarea") ? n.TextContent ?? string.Empty : AttrString(n, "value");
            return FirstStrong(text.AsSpan()) == 2;
        }

        Arena a = n.Document!.Arena;
        int scope = n.Index;
        int i = Node.NextPreorder(a, scope, scope);
        while (i >= 0)
        {
            var d = new Node(n.Document, i);
            bool skip = false;
            if (a.Kind[i] == (byte)NodeKind.Element)
            {
                if (d.Namespace == ElementNamespace.Html
                    && (IsHtml(d, "bdi") || IsHtml(d, "script") || IsHtml(d, "style") || IsHtml(d, "textarea")))
                {
                    skip = true;
                }
                else
                {
                    ReadOnlySpan<char> dv = AttrSpan(d, "dir");
                    skip = d.Namespace == ElementNamespace.Html && (LowerEquals(dv, "ltr") || LowerEquals(dv, "rtl") || LowerEquals(dv, "auto"));
                }
            }
            else if (a.Kind[i] == (byte)NodeKind.Text)
            {
                int s = FirstStrong(d.Data);
                if (s != 0)
                {
                    return s == 2;
                }
            }

            if (skip)
            {
                // Step over the subtree.
                int j = i;
                int next = -1;
                while (j >= 0 && j != scope)
                {
                    if (a.Next[j] >= 0)
                    {
                        next = a.Next[j];
                        break;
                    }

                    j = a.Parent[j];
                }

                i = next;
            }
            else
            {
                i = Node.NextPreorder(a, i, scope);
            }
        }

        return false;
    }

    /// <summary>0 none, 1 left-to-right, 2 right-to-left (a simplified Unicode bidi class: letters in the right-to-left scripts are R/AL).</summary>
    private static int FirstStrong(ReadOnlySpan<char> text)
    {
        int i = 0;
        while (i < text.Length)
        {
            Rune.DecodeFromUtf16(text[i..], out Rune r, out int used);
            i += used;
            if (!Rune.IsLetter(r))
            {
                continue;
            }

            int v = r.Value;
            bool rtl = v is (>= 0x0590 and <= 0x08FF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF) or (>= 0x10800 and <= 0x10FFF) or (>= 0x1E800 and <= 0x1EFFF);
            return rtl ? 2 : 1;
        }

        return 0;
    }
}
