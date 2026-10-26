using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>States and properties of an element: ARIA attributes the role supports, plus the HTML-AAM native mappings (WAI-ARIA 1.2 section 6, HTML-AAM section 3.6, Core-AAM section 5).</summary>
internal static class StateBuilder
{
    public static AxStates Build(Node el, AriaRole? role, bool nativeDisabled, bool ariaDisabledChain, bool markupFocusable, Func<string> selectValue, Func<Node, bool?>? optionSelected = null, StateHelpers? helpers = null)
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
        // Chromium states an ancestor's disabled state only on the descendants that can take focus (a link, a control, an element with a
        // tabindex), and an explicit aria-disabled=false on the element itself ends it (profile entry disabled-inheritance). An option of
        // a disabled select or optgroup is disabled too.
        string? ownDisabled = el.GetAttribute("aria-disabled") is { } rawDisabled ? TreeText.AsciiLower(TreeText.Trim(rawDisabled)) : null;
        s.Disabled = nativeDisabled || Aria("aria-disabled") == "true" || (ariaDisabledChain && markupFocusable && ownDisabled != "false")
            || (html && el.LocalName == "option" && InDisabledGroup(el));

        // ---- expanded ----
        // Chromium states aria-expanded on a list item too, which WAI-ARIA 1.2 does not allow it (profile entry expanded-list-item).
        string? expanded = Aria("aria-expanded") ?? Native("aria-expanded")
            ?? (roleName == "listitem" && el.GetAttribute("aria-expanded") is { } rawExpanded && TreeText.AsciiLower(TreeText.Trim(rawExpanded)) is "true" or "false" ? TreeText.AsciiLower(TreeText.Trim(rawExpanded)) : null);
        if (expanded is "true" or "false")
        {
            s.Expanded = expanded == "true";
        }
        else if (roleName == "combobox" && html && el.LocalName == "select")
        {
            // Only a drop-down select states that it is collapsed; an ARIA combobox says nothing until its author does (profile entry
            // combobox-expanded).
            s.Expanded = false;
        }

        // ---- checked ----
        // A native checkbox given a role that has no checked state (button, say) states none.
        string? isChecked = (role is null || Supported("aria-checked") ? Native("aria-checked") : null) ?? Aria("aria-checked");
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
        if (html && el.LocalName == "option" && optionSelected?.Invoke(el) is { } picked)
        {
            // A native option is selected when HTML's selectedness says so: the last option with a selected attribute, or the first
            // enabled one of a single-choice select when none has it (HTML section 4.10.7, profile entry option-selectedness). Chromium
            // states nothing on a disabled option (profile entry selected-states).
            if (!s.Disabled)
            {
                s.Selected = picked;
            }
        }
        else if (selected is "true" or "false")
        {
            s.Selected = selected == "true";
        }
        else if (roleName is "option" or "tab" or "treeitem" && !(helpers?.SiblingHasAriaSelected(el) ?? false))
        {
            // An item that says nothing is not selected, unless a sibling does say (profile entry selected-states).
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

        s.ConstraintInvalid = html && ConstraintValidity.Fails(el, nativeDisabled, helpers);

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

        BuildRange(el, html, roleName, markupFocusable, Aria, Native, s);

        // ---- value ----
        if (html && el.LocalName == "input" && HtmlSemantics.GetInputType(el) == HtmlInputType.File)
        {
            // The label of an empty file chooser, in Chromium's English UI (profile entry file-input-value).
            s.Value = "No file chosen";
        }
        else if (roleName is "textbox" or "searchbox" or "combobox")
        {
            if (html && el.LocalName == "input")
            {
                // A password shows one bullet for each character of its value, which is not exposed; the other text types lose their line
                // breaks to the value sanitization algorithm (HTML 4.10.5.1.1; profile entry input-password-file).
                string raw = el.GetAttribute("value") ?? string.Empty;
                s.Value = HtmlSemantics.GetInputType(el) == HtmlInputType.Password
                    ? new string('\u2022', raw.Length)
                    : raw.Contains('\n', StringComparison.Ordinal) || raw.Contains('\r', StringComparison.Ordinal) ? raw.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal) : raw;
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
            else if (TextValue(el) is { Length: > 0 } textValue)
            {
                // A custom text box, search box or combobox (a div or a button with the role) states the text it holds.
                s.Value = textValue;
            }
        }
        else if (roleName is null && html && el.LocalName == "input" && HtmlSemantics.GetInputType(el) is HtmlInputType.Date or HtmlInputType.Color)
        {
            // The date and colour pickers have no WAI-ARIA role but state their value (profile entry internal-roles).
            // A value that is not a valid date string is sanitized to nothing (HTML 4.10.5.1.7).
            s.Value = HtmlSemantics.GetInputType(el) == HtmlInputType.Color ? TreeText.AsciiLower(el.GetAttribute("value") ?? "#000000")
                : el.GetAttribute("value") is { } dv && ConstraintValidity.IsValidTemporal(HtmlInputType.Date, dv) ? dv : string.Empty;
        }
        else if (s.ValueNow is { } vn && (roleName is "slider" or "spinbutton" or "scrollbar" or "meter" or "progressbar" || roleName == "separator" && markupFocusable))
        {
            s.Value = FormatNumber(vn);
        }
        else if (html && el.GetAttribute("contenteditable") is { } own && (own.Length == 0 || TreeText.EqualsIgnoreCase(own, "true") || TreeText.EqualsIgnoreCase(own, "plaintext-only"))
            && !HasBlockChild(el) && TextValue(el) is { Length: > 0 } hostText)
        {
            // An editing host states its text as a value; one with block content would need line breaks worked out, which is left out
            // (profile entry editable-value).
            s.Value = hostText;
        }

        // ---- live regions ----
        // Chromium states live, atomic and relevant together on a live region only: aria-live=off, or no live setting, states none of them
        // (profile entry live-region-properties). An alert, log or status is live by its role; atomic defaults to the role's value, else false;
        // relevant defaults to "additions text" (WAI-ARIA 1.2 aria-relevant).
        string? live = Aria("aria-live");
        if (live is not ("polite" or "assertive" or "off") && role is not null && role.ImplicitValues.TryGetValue("aria-live", out string? il))
        {
            live = il;
        }

        if (live is "polite" or "assertive")
        {
            s.Live = live;
            string? atomic = Aria("aria-atomic");
            if (atomic is "true" or "false")
            {
                s.Atomic = atomic == "true";
            }
            else
            {
                s.Atomic = role is not null && role.ImplicitValues.TryGetValue("aria-atomic", out string? ia) && ia == "true";
            }

            s.Relevant = Aria("aria-relevant") is { Length: > 0 } rel ? rel : "additions text";
        }

        s.Busy = Aria("aria-busy") == "true";
        s.Modal = Aria("aria-modal") == "true";

        string? current = Aria("aria-current");
        if (current is not null && current != "false")
        {
            s.Current = current is "true" or "page" or "step" or "location" or "date" or "time" ? current : "true";
        }

        // WAI-ARIA 1.2 aria-haspopup: true is menu; a missing, false or unknown value is none. Chromium states it on any element (not only the
        // roles that support it), and gives every combobox a listbox popup unless the author names another (a drop-down select opens a menu;
        // profile entry has-popup). A text field's aria-autocomplete adds none.
        string? popup = el.GetAttribute("aria-haspopup") is { } rawPopup ? TreeText.AsciiLower(TreeText.Trim(rawPopup)) : null;
        if (popup is "true")
        {
            popup = "menu";
        }

        if (popup is "menu" or "listbox" or "tree" or "grid" or "dialog")
        {
            s.HasPopup = popup;
        }
        else if (roleName == "combobox")
        {
            s.HasPopup = html && el.LocalName == "select" ? "menu" : "listbox";
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
        s.Multiline = Aria("aria-multiline") == "true" || Native("aria-multiline") == "true"
            || (roleName == "textbox" && html && el.LocalName != "input" && el.GetAttribute("contenteditable") is { } cem && (cem.Length == 0 || TreeText.EqualsIgnoreCase(cem, "true")));
        // Chromium states the autocomplete mode of a text field only: an author's aria-autocomplete on a button or a div is not reported
        // (profile entry autocomplete-text-fields).
        string? ac = Aria("aria-autocomplete");
        if (ac is "inline" or "list" or "both" && html && el.LocalName is "input" or "textarea")
        {
            s.AutoComplete = ac;
        }
        else if (html && el.LocalName == "input" && roleName == "combobox" && el.HasAttribute("list"))
        {
            // A text field with suggestions (a list attribute) completes from a list, as HTML-AAM maps it.
            s.AutoComplete = "list";
        }

        if (el.GetAttribute("aria-keyshortcuts") is { } ks && TreeText.Trim(ks).Length > 0 && Supported("aria-keyshortcuts"))
        {
            s.KeyShortcuts = TreeText.Trim(ks);
        }
        else if (html && el.GetAttribute("accesskey") is { } access)
        {
            // HTML-AAM: an element with an accesskey exposes it as a keyboard shortcut; Chromium on Linux and Windows names it Alt+ and the
            // attribute as written, whatever it holds, an empty value and a list of keys included (profile entry accesskey-shortcut).
            s.KeyShortcuts = "Alt+" + access;
        }

        // Chromium leaves the description out of a generic element and of a presentational one (profile entry roledescription-roles).
        if (el.GetAttribute("aria-roledescription") is { } rd && TreeText.Trim(rd).Length > 0 && roleName is not (null or "generic" or "none" or "presentation"))
        {
            s.RoleDescription = TreeText.Trim(rd);
        }

        // Only a link and an image state a URL; an anchor an author gave another role (button, menuitem, tab) does not (profile entry url-roles).
        if (html && el.LocalName is "a" or "area" && roleName == "link" && el.GetAttribute("href") is { } href)
        {
            s.Url = href;
        }
        else if (html && el.LocalName == "img" && roleName == "img" && el.GetAttribute("src") is { Length: > 0 } src)
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
            else if (el.LocalName == "textarea" || (el.LocalName == "input" && HtmlSemantics.GetInputType(el) is HtmlInputType.Text or HtmlInputType.Search or HtmlInputType.Telephone or HtmlInputType.Url or HtmlInputType.Email or HtmlInputType.Password))
            {
                // Every text-entry control is editable, whatever role an author gives it (a text input with role=combobox).
                s.Editable = "plaintext";
            }
            else if (el.LocalName == "input" && HtmlSemantics.GetInputType(el) == HtmlInputType.Number)
            {
                // A number field is editable whatever role an author gives it.
                s.Editable = "plaintext";
            }
            else if (roleName == "spinbutton" && el.LocalName == "input")
            {
                s.Editable = "plaintext";
            }
            else
            {
                // The content of an editing host is editable as well, down to a contenteditable=false element (profile entry editable-inherited).
                s.Editable = helpers?.InheritedEditable(el);
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

    private static void BuildRange(Node el, bool html, string? roleName, bool focusable, Func<string, string?> aria, Func<string, string?> native, AxStates s)
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
            // An author's aria-valuemin, aria-valuemax and aria-valuenow win over the native attributes (profile entry range-aria-wins).
            min ??= Num(native("aria-valuemin")) ?? 0;
            max ??= Num(native("aria-valuemax")) ?? (range ? 100 : 0);
            now ??= Num(native("aria-valuenow"));
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
            if (roleName is "slider" or "scrollbar" or "meter" or "progressbar" || roleName == "separator" && focusable)
            {
                // A separator that takes focus is a slider for Chromium (profile entry range-defaults).
                min ??= 0;
                max ??= 100;
            }

            if (roleName == "spinbutton")
            {
                // A spin button that gives no bounds has the range 0 to 0, and rests at 0 when it gives no value (profile entry range-defaults).
                min ??= 0;
                max ??= 0;
                now ??= 0;
            }

            if (roleName == "slider" && now is null)
            {
                now = min!.Value + ((max!.Value - min.Value) / 2);
            }
        }

        if (now is null && roleName == "progressbar" && aria("aria-valuenow") is not null)
        {
            // A progress bar whose aria-valuenow is not a number rests at 0 (profile entry range-defaults).
            now = 0;
        }

        s.ValueNow = now;
        s.ValueMin = min;
        s.ValueMax = max;
    }

    /// <summary>True for an option whose select or optgroup is disabled.</summary>
    private static bool InDisabledGroup(Node option)
    {
        Node p = option.ParentElement;
        if (!p.IsNull && p.Namespace == ElementNamespace.Html && p.LocalName == "optgroup")
        {
            if (p.HasAttribute("disabled"))
            {
                return true;
            }

            p = p.ParentElement;
        }

        return !p.IsNull && p.Namespace == ElementNamespace.Html && p.LocalName == "select" && p.HasAttribute("disabled");
    }

    private static bool HasBlockChild(Node el)
    {
        for (Node c = el.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.IsElement && c.LocalName is not ("a" or "abbr" or "b" or "bdi" or "bdo" or "cite" or "code" or "data" or "dfn" or "em" or "i" or "kbd" or "mark" or "q" or "s" or "samp" or "small" or "span" or "strong" or "sub" or "sup" or "time" or "u" or "var"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The rendered-text approximation of an element's content: its text with scripts and styles left out, white space collapsed.</summary>
    private static string TextValue(Node el)
    {
        var sb = new System.Text.StringBuilder();
        int visited = 0;
        for (Node d = el.NextInDocumentOrder(el); !d.IsNull && visited++ < 20_000 && sb.Length < 8192; d = d.NextInDocumentOrder(el))
        {
            if (d.Kind == NodeKind.Text && d.ParentElement is { IsNull: false } pe && !(pe.Namespace == ElementNamespace.Html && pe.LocalName is "script" or "style" or "template" or "noscript"))
            {
                sb.Append(d.TextContent);
            }
        }

        return TreeText.Trim(TreeText.Collapse(sb.ToString()));
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
