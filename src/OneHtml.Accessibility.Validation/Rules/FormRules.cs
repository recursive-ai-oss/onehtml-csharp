using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Tree;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// The autofill detail tokens of the HTML Standard, section 4.10.18.7 "Autofill": an optional <c>section-*</c> token, an
/// optional <c>shipping</c> or <c>billing</c>, then either an autofill field name or an optional contact type followed by a
/// telephone or e-mail field name, then an optional <c>webauthn</c>.
/// </summary>
internal static class AutofillTokens
{
    private static readonly HashSet<string> s_fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "honorific-prefix", "given-name", "additional-name", "family-name", "honorific-suffix", "nickname", "username",
        "new-password", "current-password", "one-time-code", "organization-title", "organization", "street-address", "address-line1",
        "address-line2", "address-line3", "address-level4", "address-level3", "address-level2", "address-level1", "country",
        "country-name", "postal-code", "cc-name", "cc-given-name", "cc-additional-name", "cc-family-name", "cc-number", "cc-exp",
        "cc-exp-month", "cc-exp-year", "cc-csc", "cc-type", "transaction-currency", "transaction-amount", "language", "bday",
        "bday-day", "bday-month", "bday-year", "sex", "url", "photo",
    };

    private static readonly HashSet<string> s_contactFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "tel", "tel-country-code", "tel-national", "tel-area-code", "tel-local", "tel-local-prefix", "tel-local-suffix", "tel-extension",
        "email", "impp",
    };

    private static readonly HashSet<string> s_contactTypes = new(StringComparer.OrdinalIgnoreCase) { "home", "work", "mobile", "fax", "pager" };

    /// <summary>The value's tokens, split on the ASCII whitespace the HTML Standard uses.</summary>
    public static List<string> Split(string value)
    {
        var list = new List<string>();
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && value[i] is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                i++;
            }

            int start = i;
            while (i < value.Length && !(value[i] is ' ' or '\t' or '\n' or '\f' or '\r'))
            {
                i++;
            }

            if (i > start)
            {
                list.Add(value[start..i]);
            }
        }

        return list;
    }

    /// <summary>
    /// Checks a token list. Returns <see langword="null"/> when it is valid, otherwise the first problem. The single
    /// keywords <c>on</c> and <c>off</c> are valid for a control that wears the "expectation" mantle (every control except a hidden input).
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="allowWebauthn">True for <c>input</c> and <c>textarea</c>, the only controls where <c>webauthn</c> is valid.</param>
    public static string? Validate(IReadOnlyList<string> tokens, bool allowWebauthn)
    {
        if (tokens.Count == 1 && (tokens[0].Equals("on", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        int i = 0;
        if (i < tokens.Count && tokens[i].StartsWith("section-", StringComparison.OrdinalIgnoreCase))
        {
            i++;
        }

        if (i < tokens.Count && (tokens[i].Equals("shipping", StringComparison.OrdinalIgnoreCase) || tokens[i].Equals("billing", StringComparison.OrdinalIgnoreCase)))
        {
            i++;
        }

        if (i >= tokens.Count)
        {
            return "no autofill field name";
        }

        if (s_fields.Contains(tokens[i]))
        {
            i++;
        }
        else
        {
            bool hadType = false;
            if (s_contactTypes.Contains(tokens[i]))
            {
                hadType = true;
                i++;
                if (i >= tokens.Count)
                {
                    return "the contact type \"" + tokens[i - 1] + "\" needs a telephone or e-mail field name after it";
                }
            }

            if (!s_contactFields.Contains(tokens[i]))
            {
                return hadType
                    ? "the contact type \"" + tokens[i - 1] + "\" can only go with a telephone or e-mail field name, not \"" + tokens[i] + "\""
                    : "\"" + tokens[i] + "\" is not an autofill field name";
            }

            i++;
        }

        if (i < tokens.Count && allowWebauthn && tokens[i].Equals("webauthn", StringComparison.OrdinalIgnoreCase))
        {
            i++;
        }

        return i < tokens.Count ? "\"" + tokens[i] + "\" is not allowed here (tokens must follow the order: section, shipping or billing, contact type, field name, webauthn)" : null;
    }
}

/// <summary>axe <c>autocomplete-valid</c>, ACT 73f2c2.</summary>
internal sealed class AutocompleteValidRule : AriaRule
{
    public AutocompleteValidRule()
        : base("autocomplete-valid",
            "The autocomplete attribute of a form field must follow the HTML autofill token syntax.",
            "Change the autocomplete value to the field name that matches the data the field collects, such as email or street-address, optionally preceded by section-*, shipping or billing, and a contact type for telephone and e-mail fields.",
            null,
            "Removing a bad value would only hide the problem; the right token depends on what the field collects.",
            actIds: ["73f2c2"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (Node el in Elements(context))
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (el.Namespace != ElementNamespace.Html || el.LocalName is not ("input" or "select" or "textarea")
                || el.GetAttribute("autocomplete") is not { } value || LanguageSupport.IsBlank(value))
            {
                continue;
            }

            List<string> tokens = AutofillTokens.Split(value);
            // ACT 73f2c2 exclusions: the on/off toggle, disabled controls, fixed-value inputs, hidden controls, static controls.
            if (tokens.Count == 1 && (tokens[0].Equals("on", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("off", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (IsDisabled(el) || el.LocalName == "input" && el.GetAttribute("type") is { } type
                && (type.Equals("hidden", StringComparison.OrdinalIgnoreCase) || type.Equals("button", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("submit", StringComparison.OrdinalIgnoreCase) || type.Equals("reset", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AxNode? ax = context.Find(el);
            if (ax is null || ax.Hidden.IsNotRendered || (ax.Hidden.Reasons & HiddenReasons.VisibilityHidden) != 0)
            {
                continue;
            }

            if (!ax.Focus.IsTabbable && !IsWidget(ax))
            {
                continue;
            }

            string? problem = AutofillTokens.Validate(tokens, el.LocalName != "select");
            if (problem is null)
            {
                reporter.Passed(el);
            }
            else
            {
                reporter.Failed(el, "The autocomplete value \"" + value.Trim() + "\" is not valid: " + problem + ".", [new Finding("autocomplete", value, problem)]);
            }
        }
    }

    // The "disabled element" of ACT 73f2c2: disabled by the disabled attribute (own or through a disabled fieldset) or aria-disabled="true".
    private static bool IsDisabled(Node el)
    {
        if (el.HasAttribute("disabled"))
        {
            return true;
        }

        for (Node a = el; !a.IsNull && a.IsElement; a = a.ParentElement)
        {
            if (string.Equals(a.GetAttribute("aria-disabled"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Node parent = a.ParentElement;
            if (!parent.IsNull && parent.LocalName == "fieldset" && parent.Namespace == ElementNamespace.Html && parent.HasAttribute("disabled"))
            {
                // Controls inside the fieldset's first legend child are not disabled by it (HTML 4.10.18.5 "Enabling and disabling form controls").
                Node firstLegend = default;
                for (Node ch = parent.FirstChild; !ch.IsNull; ch = ch.NextSibling)
                {
                    if (ch.IsElement && ch.LocalName == "legend")
                    {
                        firstLegend = ch;
                        break;
                    }
                }

                if (firstLegend.IsNull || !IsInside(el, firstLegend))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsInside(Node el, Node ancestor)
    {
        for (Node a = el; !a.IsNull; a = a.Parent)
        {
            if (a == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWidget(AxNode ax)
    {
        AriaRole? role = ax.AriaRole;
        int guard = 0;
        while (role is not null && guard++ < 16)
        {
            if (role.Name == "widget")
            {
                return true;
            }

            AriaRole? next = null;
            foreach (string s in role.Superclasses)
            {
                if (s == "widget")
                {
                    return true;
                }

                next ??= AriaTaxonomy.FindRole(s);
            }

            role = next;
        }

        return false;
    }
}
