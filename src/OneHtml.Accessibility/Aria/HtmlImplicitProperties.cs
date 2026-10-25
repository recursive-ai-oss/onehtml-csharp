using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>A WAI-ARIA state or property that an HTML element or attribute implies (HTML-AAM section 3.6).</summary>
/// <param name="Name">The WAI-ARIA attribute name, such as <c>aria-required</c>.</param>
/// <param name="Value">The implied value.</param>
/// <param name="Source">The HTML-AAM entry id (without the <c>att-</c> or <c>el-</c> prefix) the mapping comes from, such as <c>required</c>; see <see cref="HtmlAam"/>.</param>
public readonly record struct ImplicitProperty(string Name, string Value, string Source);

/// <summary>
/// The WAI-ARIA states and properties HTML markup implies on an element, from the attribute tables of HTML-AAM
/// section 3.6 and the element table's property clauses (heading level, text box multiline).
/// </summary>
/// <remarks>
/// Only values the markup alone determines are produced. State that needs script (the <c>indeterminate</c> IDL
/// attribute, a dialog's modal state), layout, the set a list item belongs to, or the validity of a value
/// (<c>pattern</c>) is left to later steps. A native attribute wins over the matching <c>aria-*</c> attribute where
/// HTML-AAM says so; this type reports the native side only.
/// </remarks>
public static class HtmlImplicitProperties
{
    /// <summary>Lists the implied properties of an element, in a stable order. Empty for non-elements and non-HTML elements.</summary>
    /// <param name="element">An element.</param>
    public static IReadOnlyList<ImplicitProperty> Get(Node element)
    {
        var result = new List<ImplicitProperty>();
        if (!HtmlSemantics.IsHtml(element))
        {
            return result;
        }

        string local = element.LocalName;
        HtmlInputType type = HtmlSemantics.GetInputType(element);
        bool isInput = type != HtmlInputType.NotInput;
        bool textual = type is HtmlInputType.Text or HtmlInputType.Search or HtmlInputType.Telephone or HtmlInputType.Url or HtmlInputType.Email or HtmlInputType.Password;
        ReadOnlySpan<char> v;

        // el-h1-h6
        if (local.Length == 2 && local[0] == 'h' && local[1] is >= '1' and <= '6')
        {
            result.Add(new ImplicitProperty("aria-level", local[1].ToString(), "h1-h6"));
        }

        // el-textarea
        if (local == "textarea")
        {
            result.Add(new ImplicitProperty("aria-multiline", "true", "textarea"));
        }

        // att-checked, att-checked-absent: input type=checkbox or radio.
        if (type is HtmlInputType.Checkbox or HtmlInputType.Radio)
        {
            bool on = HtmlSemantics.Has(element, "checked");
            result.Add(new ImplicitProperty("aria-checked", on ? "true" : "false", on ? "checked" : "checked-absent"));
        }

        // att-disabled, att-disabled-fieldset: the attribute itself; disabled-by-fieldset is reported by NodeRole.IsDisabled.
        if (HtmlSemantics.CanBeDisabled(element) && HtmlSemantics.Has(element, "disabled"))
        {
            result.Add(new ImplicitProperty("aria-disabled", "true", local == "fieldset" ? "disabled-fieldset" : "disabled"));
        }

        // att-required
        if ((isInput || local is "select" or "textarea") && HtmlSemantics.Has(element, "required") && type != HtmlInputType.Hidden)
        {
            result.Add(new ImplicitProperty("aria-required", "true", "required"));
        }

        // att-readonly
        if ((isInput || local == "textarea") && HtmlSemantics.Has(element, "readonly"))
        {
            result.Add(new ImplicitProperty("aria-readonly", "true", "readonly"));
        }

        // att-placeholder: a non-empty placeholder wins over aria-placeholder.
        if ((textual || local == "textarea") && HtmlSemantics.TryGetAttribute(element, "placeholder", out v) && !v.IsEmpty)
        {
            result.Add(new ImplicitProperty("aria-placeholder", v.ToString(), "placeholder"));
        }

        // att-multiple-select
        if (local == "select" && HtmlSemantics.Has(element, "multiple"))
        {
            result.Add(new ImplicitProperty("aria-multiselectable", "true", "multiple-select"));
        }

        // att-selected
        if (local == "option")
        {
            result.Add(new ImplicitProperty("aria-selected", HtmlSemantics.Has(element, "selected") ? "true" : "false", "selected"));
        }

        // att-list: the datalist the list attribute names.
        if (textual && HtmlSemantics.TryGetAttribute(element, "list", out v) && !v.IsEmpty)
        {
            result.Add(new ImplicitProperty("aria-controls", v.ToString(), "list"));
        }

        // att-min, att-max, att-value-meter: meter and progress.
        if (local is "meter" or "progress")
        {
            if (local == "meter" && HtmlSemantics.TryGetAttribute(element, "min", out v) && AttributeValueChecker.IsRealNumber(v))
            {
                result.Add(new ImplicitProperty("aria-valuemin", v.ToString(), "min"));
            }

            if (HtmlSemantics.TryGetAttribute(element, "max", out v) && AttributeValueChecker.IsRealNumber(v))
            {
                result.Add(new ImplicitProperty("aria-valuemax", v.ToString(), "max"));
            }

            if (HtmlSemantics.TryGetAttribute(element, "value", out v) && AttributeValueChecker.IsRealNumber(v))
            {
                result.Add(new ImplicitProperty("aria-valuenow", v.ToString(), "value-meter"));
            }
        }

        // att-min-input, att-max-input, att-value-input: range and number inputs.
        if (type is HtmlInputType.Range or HtmlInputType.Number)
        {
            if (HtmlSemantics.TryGetAttribute(element, "min", out v) && AttributeValueChecker.IsRealNumber(v))
            {
                result.Add(new ImplicitProperty("aria-valuemin", v.ToString(), "min-input"));
            }

            if (HtmlSemantics.TryGetAttribute(element, "max", out v) && AttributeValueChecker.IsRealNumber(v))
            {
                result.Add(new ImplicitProperty("aria-valuemax", v.ToString(), "max-input"));
            }

            if (HtmlSemantics.TryGetAttribute(element, "value", out v) && AttributeValueChecker.IsRealNumber(v))
            {
                result.Add(new ImplicitProperty("aria-valuenow", v.ToString(), "value-input"));
            }
        }

        // att-colspan, att-rowspan
        if (local is "td" or "th")
        {
            if (HtmlSemantics.TryGetAttribute(element, "colspan", out v) && HtmlSemantics.TryParseInteger(v, out long cs) && cs > 1)
            {
                result.Add(new ImplicitProperty("aria-colspan", cs.ToString(System.Globalization.CultureInfo.InvariantCulture), "colspan"));
            }

            if (HtmlSemantics.TryGetAttribute(element, "rowspan", out v) && HtmlSemantics.TryParseInteger(v, out long rs) && rs > 1)
            {
                result.Add(new ImplicitProperty("aria-rowspan", rs.ToString(System.Globalization.CultureInfo.InvariantCulture), "rowspan"));
            }
        }

        // att-open-details: reported on the summary element, as HTML-AAM says.
        if (local == "summary")
        {
            Node parent = element.ParentElement;
            if (!parent.IsNull && HtmlSemantics.IsHtml(parent, "details"))
            {
                result.Add(new ImplicitProperty("aria-expanded", HtmlSemantics.Has(parent, "open") ? "true" : "false", "open-details"));
            }
        }

        return result;
    }
}
