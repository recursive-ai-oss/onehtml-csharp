using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>How well an entry of the <see cref="ChromiumProfile"/> is backed.</summary>
public enum ProfileEvidence
{
    /// <summary>A web-platform-tests page whose expectation the implementation passes. The pages test browsers, so this is evidence of the behaviour Chromium ships.</summary>
    TestSuite,

    /// <summary>A normative statement in the specification, with the choice among the permitted behaviours recorded.</summary>
    Specification,

    /// <summary>Inferred from public documentation or spec notes; to be confirmed by the Chrome differential.</summary>
    Unverified,
}

/// <summary>One place where the specifications leave a choice and this library made one.</summary>
/// <param name="Id">A stable identifier, used in <c>docs/accessibility-chromium-profile.md</c>.</param>
/// <param name="Topic">What the choice is about.</param>
/// <param name="Choice">What the library does.</param>
/// <param name="Observable">The browser behaviour the choice matches, and where it is documented.</param>
/// <param name="Evidence">How well the choice is backed.</param>
public sealed record ProfileEntry(string Id, string Topic, string Choice, string Observable, ProfileEvidence Evidence);

/// <summary>
/// The choices made where WAI-ARIA, HTML-AAM and Core-AAM leave room, each matching what Chromium exposes. The same
/// list is written out in <c>docs/accessibility-chromium-profile.md</c>.
/// </summary>
public static class ChromiumProfile
{
    /// <summary>
    /// The <c>aria-level</c> of an element with the <c>heading</c> role that has no level: 2. WAI-ARIA 1.2 requires the
    /// property and defines no default; browsers expose 2.
    /// </summary>
    public const int DefaultHeadingLevel = 2;

    /// <summary>The entries, in the order the documentation lists them.</summary>
    public static IReadOnlyList<ProfileEntry> Entries { get; } =
    [
        new("generic-fallback", "HTML elements HTML-AAM does not list", "Exposed with the generic role (font, center, marquee, unknown elements, autonomous custom elements).", "Chrome exposes unlisted elements as generic containers.", ProfileEvidence.Unverified),
        new("empty-alt-image", "img with an empty alt", "The role is none unless aria-label has text or aria-labelledby names an element. A title does not name it.", "wpt html-aam/roles-contextual.html el-img-empty-alt-title and the aria-label and aria-labelledby cases.", ProfileEvidence.TestSuite),
        new("unnamed-landmark-token", "role=region or role=form without a name", "The token is skipped like an unknown token, so the next token or the native role applies.", "wpt wai-aria/role/region-roles.html, form-roles.html, fallback-roles.html; WAI-ARIA 1.2 section 9.1.", ProfileEvidence.TestSuite),
        new("named-native-landmarks", "section, form and sectioned aside", "region, form and complementary only with an accessible name; otherwise generic. A title attribute counts as a name.", "wpt html-aam/roles-contextual.html el-section-title, el-aside-in-section-title.", ProfileEvidence.TestSuite),
        new("section-header-footer", "header and footer outside the body scope", "generic. The HTML-AAM draft's sectionheader and sectionfooter roles are not produced.", "The matching tentative wpt pages expect the draft roles and are listed as known failures.", ProfileEvidence.Unverified),
        new("landmark-scope", "What scopes header, footer and aside", "The nearest ancestor among article, aside, nav, section (sectioning), main, body. header and footer are landmarks only with no such ancestor except body; aside is complementary when the nearest is main or body.", "wpt html-aam/roles-contextual.html and aside-in-prefixed-article.html.", ProfileEvidence.TestSuite),
        new("li-accessibility-parent", "li", "listitem when the nearest ancestor with a role other than generic or presentational has the list role (ul, ol, menu, dir, role=list); otherwise generic.", "wpt html-aam/roles-generic.tentative.html el-li-within-generic-within-list-parent, el-li-within-explicit-role-list-parent.", ProfileEvidence.TestSuite),
        new("th-header-kind", "th in the auto scope state", "columnheader inside thead or in a row with no td; rowheader in a row that has a td. scope=col|colgroup gives columnheader, scope=row|rowgroup gives rowheader.", "wpt html-aam/table-roles.html. The auto rule is a simplification of the HTML header-cell algorithm.", ProfileEvidence.Unverified),
        new("computed-role-names", "Names of computed roles", "presentation is reported as none, img as image, directory as list.", "wpt wai-aria/role/synonym-roles.html; Core-AAM computed role.", ProfileEvidence.TestSuite),
        new("role-token-matching", "Role tokens", "Split on the HTML ASCII whitespace; matched ASCII case-insensitively; the first valid, non-abstract token wins; image is accepted for img.", "wpt wai-aria/role/fallback-roles.html, invalid-roles.html, roles.html.", ProfileEvidence.TestSuite),
        new("presentational-conflict", "none or presentation on a focusable element or one with a global aria-* attribute", "Ignored: the implicit role applies. Any state or property marked global in WAI-ARIA 1.2 counts, deprecated ones and prohibited ones included. Non-global attributes do not.", "wpt wai-aria/role/role_none_conflict_resolution.html and .tentative.html.", ProfileEvidence.TestSuite),
        new("inherited-presentational", "Required owned elements of a presentational element", "They are presentational too, even with global aria-* attributes; a focusable one is not. An owned element with its own non-presentational role keeps it.", "wpt wai-aria/role/role_none_conflict_resolution_spec_ambiguities.tentative.html.", ProfileEvidence.TestSuite),
        new("children-presentational", "Descendants of roles with presentational children", "Reported as none with the origin ChildrenPresentational, unless focusable.", "WAI-ARIA 1.2 section 5.2.9; wpt marks the nested-button cases ambiguous.", ProfileEvidence.Unverified),
        new("static-focusable", "Focusable by markup", "Valid tabindex, links with href, enabled form controls except hidden inputs, iframe, the first summary of a details, audio and video with controls, editing hosts, none of it inside an inert subtree. Style is not consulted.", "HTML 6.6.3 focusable area; iframe, media controls and summary behaviour are unverified.", ProfileEvidence.Unverified),
        new("empty-aria-value", "An empty aria-* value", "Treated as not specified and not reported.", "WAI-ARIA 1.2 section 9.2 for booleans; extended to all types.", ProfileEvidence.Unverified),
        new("heading-default-level", "role=heading without aria-level", "Level 2 (DefaultHeadingLevel).", "Browsers expose level 2; WAI-ARIA 1.2 leaves it undefined.", ProfileEvidence.Unverified),
        new("option-context", "option outside select and datalist", "No role.", "HTML-AAM lists option only in a list of options.", ProfileEvidence.Unverified),
        new("summary-role", "summary", "The first summary child of a details has no role of its own; any other summary is generic.", "wpt html-aam/summary.html.", ProfileEvidence.TestSuite),
        new("foreign-elements", "SVG and MathML elements", "Only the roots are mapped (svg: graphics-document, math: math); other foreign elements have no implicit role until SVG-AAM is applied.", "HTML-AAM defers both to their own mapping documents.", ProfileEvidence.Specification),
        new("switch-input", "input type=checkbox with the switch attribute", "switch.", "wpt html-aam/roles.tentative.html el-input-checkbox-switch.", ProfileEvidence.TestSuite),
        new("select-kind", "select", "listbox when multiple is present or size is above 1; otherwise combobox.", "wpt html-aam/roles.html el-select-listbox; HTML-AAM select entries.", ProfileEvidence.TestSuite),
        new("input-suggestions", "input with a list attribute", "combobox when list names a datalist in the same document, for the text, search, tel, url and email types.", "HTML-AAM el-input-textetc-autocomplete.", ProfileEvidence.Specification),
        new("required-attribute-native", "Required states and properties of an explicit role on a native element", "Supplied by the native element: heading level for h1-h6, checked for checkbox and radio inputs, selected for option, valuenow for range and number inputs, meter and progress, expanded and controls for select and text inputs. Required attributes are checked only for explicit roles that differ from the element's native role.", "ARIA in HTML conformance model and HTML-AAM section 3.6.", ProfileEvidence.Unverified),
        new("value-minimums", "Lower bounds of integer attributes", "aria-level, aria-posinset, aria-colindex, aria-rowindex, aria-colspan at least 1; aria-rowspan at least 0; aria-setsize, aria-colcount, aria-rowcount at least -1.", "WAI-ARIA 1.2 prose of those properties.", ProfileEvidence.Specification),
    ];
}
