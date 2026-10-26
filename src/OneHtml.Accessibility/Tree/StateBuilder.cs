using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>States and properties of an element: ARIA attributes the role supports, plus the HTML-AAM native mappings (WAI-ARIA 1.2 section 6, HTML-AAM section 3.6, Core-AAM section 5).</summary>
internal static class StateBuilder
{
    public static AxStates Build(Node el, AriaRole? role, bool nativeDisabled, bool ariaDisabledChain, Func<string> selectValue)
    {
        var s = new AxStates();
        bool html = el.Namespace == ElementNamespace.Html;
        IReadOnlyList<ImplicitProperty> native = html ? HtmlImplicitProperties.Get(el) : [];

        string? Native(string name)
        {
            foreach (ImplicitProperty p in native)
            {
                if (p.Name == name)
                {
                    return p.Value;
                }
            }

            return null;
        }

        bool Supported(string name)
        {
            if (role is not null && role.Prohibited.Contains(name))
            {
                return false;
            }

            if (role is not null && role.Supported.Contains(name))
            {
                return true;
            }

            return AriaTaxonomy.FindProperty(name) is { IsGlobal: true };
        }

        string? Aria(string name)
        {
            string? v = el.GetAttribute(name);
            if (v is null || !Supported(name))
            {
                return null;
            }

            v = TreeText.AsciiLower(TreeText.Trim(v));
            return v.Length == 0 || v == "undefined" ? null : v;
        }

        string? roleName = role?.Name;

        // ---- disabled ----
        s.Disabled = nativeDisabled || Aria("aria-disabled") == "true" || ariaDisabledChain;

        // ---- expanded ----
        string? expanded = Aria("aria-expanded") ?? Native("aria-expanded");
        if (expanded is "true" or "false")
        {
            s.Expanded = expanded == "true";
        }
        else if (roleName == "combobox")
        {
            s.Expanded = false;
        }

        // ---- checked ----
        string? isChecked = Native("aria-checked") ?? Aria("aria-checked");
        if (isChecked is "true" or "false" or "mixed")
        {
            s.Checked = isChecked == "true" ? AxTristate.True : isChecked == "mixed" ? AxTristate.Mixed : AxTristate.False;
        }
        else if (roleName is "checkbox" or "radio" or "switch" or "menuitemcheckbox" or "menuitemradio")
        {
            s.Checked = AxTristate.False;
        }

        // ---- selected ----
        string? selected = Native("aria-selected") ?? Aria("aria-selected");
        if (selected is "true" or "false")
        {
            s.Selected = selected == "true";
        }
        else if (roleName is "option" or "tab")
        {
            s.Selected = false;
        }

        // ---- pressed ----
        string? pressed = Aria("aria-pressed");
        if (pressed is "true" or "false" or "mixed")
        {
            s.Pressed = pressed == "true" ? AxTristate.True : pressed == "mixed" ? AxTristate.Mixed : AxTristate.False;
        }

        s.Required = Native("aria-required") == "true" || Aria("aria-required") == "true";
        s.ReadOnly = Native("aria-readonly") == "true" || Aria("aria-readonly") == "true";
        string? invalid = Aria("aria-invalid");
        if (invalid is "true" or "grammar" or "spelling")
        {
            s.Invalid = invalid;
        }

        // ---- level ----
        if (Aria("aria-level") is { } lv && TryInt(lv, out int level) && level >= 1)
        {
            s.Level = level;
        }
        else if (Native("aria-level") is { } nlv && TryInt(nlv, out int nl))
        {
            s.Level = nl;
        }
        else if (roleName == "heading")
        {
            s.Level = ChromiumProfile.DefaultHeadingLevel;
        }

        // ---- position in set (given; the tree computes the rest) ----
        if (Aria("aria-posinset") is { } pi && TryInt(pi, out int posinset) && posinset >= 1)
        {
            s.PosInSet = posinset;
        }

        if (Aria("aria-setsize") is { } ss && TryInt(ss, out int setsize) && (setsize >= 0 || setsize == -1))
        {
            s.SetSize = setsize;
        }

        BuildRange(el, html, roleName, Aria, Native, s);

        // ---- value ----
        if (roleName is "textbox" or "searchbox" or "combobox")
        {
            if (html && el.LocalName == "input")
            {
                // A password's characters are never exposed (profile entry input-password-file).
                if (HtmlSemantics.GetInputType(el) != HtmlInputType.Password)
                {
                    s.Value = el.GetAttribute("value") ?? string.Empty;
                }
            }
            else if (html && el.LocalName == "textarea")
            {
                string t = el.TextContent ?? string.Empty;
                s.Value = t.StartsWith('\n') ? t[1..] : t;
            }
            else if (html && el.LocalName == "select")
            {
                s.Value = selectValue();
            }
        }
        else if (roleName is null && html && el.LocalName == "input" && HtmlSemantics.GetInputType(el) is HtmlInputType.Date or HtmlInputType.Color)
        {
            // The date and colour pickers have no WAI-ARIA role but state their value (profile entry internal-roles).
            s.Value = el.GetAttribute("value") ?? (HtmlSemantics.GetInputType(el) == HtmlInputType.Color ? "#000000" : string.Empty);
        }
        else if (roleName == "listbox" && html && el.LocalName == "select")
        {
            s.Value = selectValue();
        }
        else if (s.ValueNow is { } vn && roleName is "slider" or "spinbutton" or "scrollbar" or "meter" or "progressbar")
        {
            s.Value = FormatNumber(vn);
        }

        // ---- live regions ----
        string? live = Aria("aria-live");
        if (live is "off" or "polite" or "assertive")
        {
            s.Live = live;
        }
        else if (role is not null && role.ImplicitValues.TryGetValue("aria-live", out string? il))
        {
            s.Live = il;
        }

        string? atomic = Aria("aria-atomic");
        if (atomic is "true" or "false")
        {
            s.Atomic = atomic == "true";
        }
        else if (role is not null && role.ImplicitValues.TryGetValue("aria-atomic", out string? ia))
        {
            s.Atomic = ia == "true";
        }

        if (Aria("aria-relevant") is { } rel)
        {
            s.Relevant = rel;
        }

        s.Busy = Aria("aria-busy") == "true";
        s.Modal = Aria("aria-modal") == "true";

        string? current = Aria("aria-current");
        if (current is not null && current != "false")
        {
            s.Current = current is "true" or "page" or "step" or "location" or "date" or "time" ? current : "true";
        }

        string? popup = Aria("aria-haspopup");
        if (popup is not null && popup != "false")
        {
            s.HasPopup = popup is "true" or "menu" or "listbox" or "tree" or "grid" or "dialog" ? popup : "true";
        }
        else if (popup is null && html && el.LocalName == "select" && roleName == "combobox")
        {
            // A drop-down select opens a menu of its options (profile entry select-popup).
            s.HasPopup = "menu";
        }

        string? orientation = Aria("aria-orientation");
        if (orientation is "horizontal" or "vertical")
        {
            s.Orientation = orientation;
        }
        else if (role is not null && role.ImplicitValues.TryGetValue("aria-orientation", out string? io))
        {
            s.Orientation = io;
        }

        s.MultiSelectable = Aria("aria-multiselectable") == "true" || Native("aria-multiselectable") == "true";
        s.Multiline = Aria("aria-multiline") == "true" || Native("aria-multiline") == "true";
        string? ac = Aria("aria-autocomplete");
        if (ac is "inline" or "list" or "both")
        {
            s.AutoComplete = ac;
        }

        if (el.GetAttribute("aria-keyshortcuts") is { } ks && TreeText.Trim(ks).Length > 0 && Supported("aria-keyshortcuts"))
        {
            s.KeyShortcuts = TreeText.Trim(ks);
        }

        if (el.GetAttribute("aria-roledescription") is { } rd && TreeText.Trim(rd).Length > 0)
        {
            s.RoleDescription = TreeText.Trim(rd);
        }

        if (html && el.LocalName is "a" or "area" && el.GetAttribute("href") is { } href)
        {
            s.Url = href;
        }
        else if (html && el.LocalName == "img" && el.GetAttribute("src") is { Length: > 0 } src)
        {
            s.Url = src;
        }

        // ---- editable (HTML section 6.8) ----
        if (html)
        {
            string? ce = el.GetAttribute("contenteditable");
            if (ce is not null && (ce.Length == 0 || TreeText.EqualsIgnoreCase(ce, "true")))
            {
                s.Editable = "richtext";
            }
            else if (roleName is "textbox" or "searchbox" && el.LocalName is "input" or "textarea")
            {
                s.Editable = "plaintext";
            }
            else if (roleName == "spinbutton" && el.LocalName == "input")
            {
                s.Editable = "plaintext";
            }
        }

        // Chromium states the value of a native slider or spin button as its value text as well.
        if (html && el.LocalName == "input" && s.ValueText is null && s.Value is { Length: > 0 } nv && roleName is "slider" or "spinbutton"
            && HtmlSemantics.GetInputType(el) is HtmlInputType.Range or HtmlInputType.Number)
        {
            s.ValueText = nv;
        }

        return s;
    }

    private static void BuildRange(Node el, bool html, string? roleName, Func<string, string?> aria, Func<string, string?> native, AxStates s)
    {
        bool rangeRole = roleName is "slider" or "spinbutton" or "scrollbar" or "meter" or "progressbar" or "separator";
        if (!rangeRole)
        {
            return;
        }

        double? now = Num(aria("aria-valuenow"));
        double? min = Num(aria("aria-valuemin"));
        double? max = Num(aria("aria-valuemax"));
        string? text = el.GetAttribute("aria-valuetext");
        if (text is not null && TreeText.Trim(text).Length > 0)
        {
            s.ValueText = text;
        }

        if (html && el.LocalName == "input" && HtmlSemantics.GetInputType(el) is HtmlInputType.Range or HtmlInputType.Number)
        {
            bool range = HtmlSemantics.GetInputType(el) == HtmlInputType.Range;
            // A number input with no min or max reports 0 for both (profile entry number-input-bounds).
            min = Num(native("aria-valuemin")) ?? min ?? 0;
            max = Num(native("aria-valuemax")) ?? max ?? (range ? 100 : 0);
            now = Num(native("aria-valuenow")) ?? now;
            if (range)
            {
                // HTML section 4.10.5.1.13: the default value is the midpoint, and the value is clamped into the range.
                double lo = min ?? 0;
                double hi = Math.Max(max ?? 100, lo);
                min = lo;
                max = hi;
                now = Math.Clamp(now ?? (lo + ((hi - lo) / 2)), lo, hi);
            }
        }
        else if (html && el.LocalName == "progress")
        {
            min = 0;
            double m = Num(native("aria-valuemax")) ?? 1;
            max = m > 0 ? m : 1;
            now = Num(native("aria-valuenow")) is { } pv ? Math.Clamp(pv, 0, max.Value) : null;
        }
        else if (html && el.LocalName == "meter")
        {
            min = Num(native("aria-valuemin")) ?? 0;
            max = Math.Max(Num(native("aria-valuemax")) ?? 1, min.Value);
            now = Math.Clamp(Num(native("aria-valuenow")) ?? 0, min.Value, max.Value);
        }
        else
        {
            // WAI-ARIA 1.2: meter, progressbar, scrollbar and slider default to 0 and 100; a slider with no value sits at the midpoint.
            if (roleName is "slider" or "scrollbar" or "meter" or "progressbar")
            {
                min ??= 0;
                max ??= 100;
            }

            if (roleName == "slider" && now is null)
            {
                now = min!.Value + ((max!.Value - min.Value) / 2);
            }
        }

        s.ValueNow = now;
        s.ValueMin = min;
        s.ValueMax = max;
    }

    private static double? Num(string? v)
    {
        if (v is null || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
        {
            return null;
        }

        return d;
    }

    internal static string FormatNumber(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryInt(string v, out int result) => int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
}
