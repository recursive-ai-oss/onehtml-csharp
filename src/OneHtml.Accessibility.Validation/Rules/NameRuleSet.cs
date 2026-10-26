using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>The names-and-labels rules, built from the subjects each one applies to.</summary>
internal static class NameRuleSet
{
    private static bool Role(AxNode n, string name) => n.AriaRole is { } r && r.Name == name;

    private static bool IsImgRole(AxNode n) => Role(n, "img");

    internal static IEnumerable<IAccessibilityRule> Create()
    {
        // axe image-alt, ACT 23a2a8: an img whose role is img (alt="" without a name makes it presentational, which is not applicable).
        yield return new ImageAltRule();
        // ACT 59796f / axe input-image-alt: input type=image is an image button.
        yield return new SubjectNameRule("input-image-alt", "An image button must have alternative text that says what it does.",
            "Add an alt attribute to the image button that describes its action.", "image button",
            (c, n) => NameSupport.Is(n.DomNode, "input") && NameSupport.InputType(n.DomNode) == "image" && Role(n, "button"), ["59796f"],
            // The label a browser supplies when the author gave none ("Submit") is not a text alternative.
            n => !NameSupport.IsEmptyText(n.Name.Value) && !(n.Name.Source == AxTextSource.NativeAttribute && n.Name.Attribute is null));
        yield return new SubjectNameRule("area-alt", "An area element of an image map that is a link must have alternative text.",
            "Add an alt attribute to the area that says where the link goes.", "image map area",
            (c, n) => NameSupport.Is(n.DomNode, "area") && n.DomNode.HasAttribute("href") && Role(n, "link"), ["c487ae"]);
        yield return new ObjectAltRule();
        // SVG-AAM / ACT 7d6734: an SVG element with the role img, graphics-symbol or graphics-document.
        yield return new SubjectNameRule("svg-img-alt", "An SVG element with an image role must have a text alternative.",
            "Add a title child, aria-label or aria-labelledby that says what the graphic shows.", "SVG image",
            (c, n) => n.DomNode.NamespaceURI == NameSupport.Svg && n.RoleOrigin == Aria.RoleOrigin.Explicit && n.AriaRole is { Name: "img" or "graphics-symbol" or "graphics-document" }, ["7d6734"]);
        yield return new SubjectNameRule("role-img-alt", "An element given the img role must have a text alternative.",
            "Add an aria-label or aria-labelledby that says what the element shows.", "element with role img",
            (c, n) => n.DomNode.NamespaceURI != NameSupport.Svg && !NameSupport.Is(n.DomNode, "img") && n.RoleOrigin == Aria.RoleOrigin.Explicit && IsImgRole(n), ["23a2a8"]);
        yield return new ImageRedundantAltRule();
        // ACT 97a4e1 / axe button-name and input-button-name: the button element, and input buttons.
        yield return new SubjectNameRule("button-name", "A button element must have an accessible name.",
            "Put text in the button, or add an aria-label or aria-labelledby.", "button",
            (c, n) => NameSupport.Is(n.DomNode, "button") && Role(n, "button"), ["97a4e1"]);
        yield return new SubjectNameRule("input-button-name", "An input button must have an accessible name.",
            "Give the input a value attribute, or an aria-label or aria-labelledby.", "input button",
            (c, n) => NameSupport.Is(n.DomNode, "input") && NameSupport.InputType(n.DomNode) is "button" or "submit" or "reset" && Role(n, "button"), ["97a4e1"]);
        yield return new SubjectNameRule("link-name", "A link must have an accessible name.",
            "Put text or an image with alternative text in the link, or add an aria-label or aria-labelledby.", "link",
            (c, n) => NameSupport.Is(n.DomNode, "a") && n.DomNode.HasAttribute("href") && n.AriaRole is { Name: "link" or "doc-biblioref" or "doc-glossref" or "doc-noteref" }, ["c487ae"]);
        yield return new SubjectNameRule("select-name", "A select element must have an accessible name.",
            "Associate a label element with the select, or add an aria-label or aria-labelledby.", "select",
            (c, n) => NameSupport.Is(n.DomNode, "select") && n.AriaRole is { Name: not ("none" or "presentation") }, ["e086e5"]);
        yield return new SubjectNameRule("summary-name", "The summary of a details element must have an accessible name.",
            "Put text in the summary, or add an aria-label or aria-labelledby.", "summary",
            (c, n) => IsDetailsSummary(n.DomNode), ["2t702h"]);
        yield return new SubjectNameRule("label", "A form field must have a label.",
            "Associate a visible label element with the field, or add an aria-label or aria-labelledby.", "form field",
            (c, n) => FormFields.IsField(c, n) && !NameSupport.Is(n.DomNode, "select"), ["e086e5"]);
        yield return new LabelTitleOnlyRule();
        yield return new FormFieldMultipleLabelsRule();
        yield return new SubjectNameRule("frame-title", "A frame or iframe must have an accessible name.",
            "Add a title attribute to the frame that says what it contains.", "frame",
            (c, n) => n.DomNode.NamespaceURI == NameSupport.Html && n.DomNode.LocalName is "iframe" or "frame" && n.AriaRole is not { Name: "none" or "presentation" } && !NegativeTabIndex(n.DomNode), ["cae760"]);
        yield return new FrameTitleUniqueRule();
        yield return new DocumentTitleRule();
        yield return new LabelContentNameRule();
        yield return new SubjectNameRule("empty-heading", "A heading must have text.",
            "Put text in the heading, or remove the heading element if it is only for spacing.", "heading",
            (c, n) => Role(n, "heading"), ["ffd0e9"]);
        yield return new IdenticalLinksRule();

        // Judgement-only ACT rules: the library lists the candidates and a person decides.
        yield return new ReviewOnlyRule("image-name-descriptive", "The accessible name of an image should describe it.",
            "Reword the name so that it says what the image shows or does.", "Check that the image's name describes it.",
            (c, n) => IsImgRole(n) || (n.AriaRole is { Name: "graphics-document" or "graphics-symbol" } || NameSupport.Is(n.DomNode, "canvas")) && !InsideLinkOrButton(n), ["qt1vmo"]);
        yield return new ReviewOnlyRule("link-in-context-descriptive", "A link's purpose should be clear from its name and its context.",
            "Reword the link, or put the missing context in its sentence, paragraph, list item or table cell.", "Check that the link's purpose is clear from its name together with its context.",
            (c, n) => Role(n, "link"), ["5effbb"]);
    }

    // ACT qt1vmo treats a graphic inside a link or button as part of that control's name, not as an image of its own.
    private static bool InsideLinkOrButton(AxNode n)
    {
        for (AxNode? p = n.Parent; p is not null; p = p.Parent)
        {
            if (p.AriaRole is { Name: "link" or "button" })
            {
                return true;
            }
        }

        return false;
    }

    // ACT cae760: an iframe taken out of sequential focus navigation by a negative tabindex is not applicable.
    private static bool NegativeTabIndex(Node e) =>
        e.GetAttribute("tabindex") is { } t && int.TryParse(t.AsSpan().Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int v) && v < 0;

    private static bool IsDetailsSummary(Node e)
    {
        if (!NameSupport.Is(e, "summary") || !NameSupport.Is(e.Parent, "details"))
        {
            return false;
        }

        for (Node s = e.PreviousSibling; !s.IsNull; s = s.PreviousSibling)
        {
            if (NameSupport.Is(s, "summary"))
            {
                return false;
            }
        }

        return true;
    }
}
