using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Shorthands written with <c>var()</c> (above all the logical pair shorthands that Tailwind CSS 4 emits for its <c>px-</c>, <c>py-</c> and <c>mx-</c>
/// utilities), the logical-to-physical mapping under each writing mode, and CSS-wide keywords on shorthands the library keeps whole
/// (<c>vertical-align</c>, <c>text-align</c>, <c>white-space</c>, ...). Every expectation is what Chrome for Testing 156.0.8078.47 returned
/// from <c>getComputedStyle</c> for the same markup, with <c>rgb(255, 0, 0)</c> and <c>rgb(0, 0, 255)</c> written as <c>red</c> and <c>blue</c>
/// because colors stay as written here. The elements that are probed have <c>display:none</c> where a box would turn lengths into used values.
/// </summary>
public class ChromeShorthandTests
{
    private static readonly Dictionary<string, (string Html, (string Id, string Property, string Expected)[] Probes)> Cases = new()
    {
        ["padding-inline"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:var(--s)\"></div></div>", [("x", "padding-left", "4px"), ("x", "padding-right", "4px")]),
        ["padding-inline-two"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:var(--s) 2px\"></div></div>", [("x", "padding-left", "4px"), ("x", "padding-right", "2px")]),
        ["padding-block-two"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-block:var(--s) 8px\"></div></div>", [("x", "padding-top", "4px"), ("x", "padding-bottom", "8px")]),
        ["margin-inline-auto"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;margin-inline:var(--s) auto\"></div></div>", [("x", "margin-left", "4px"), ("x", "margin-right", "auto")]),
        ["margin-block-two"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;margin-block:var(--s) 3px\"></div></div>", [("x", "margin-top", "4px"), ("x", "margin-bottom", "3px")]),
        ["inset-inline"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;inset-inline:var(--s) 1px\"></div></div>", [("x", "left", "4px"), ("x", "right", "1px")]),
        ["inset-block"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;inset-block:var(--s) 3px\"></div></div>", [("x", "top", "4px"), ("x", "bottom", "3px")]),
        ["inset-physical"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;inset:var(--s) 1px\"></div></div>", [("x", "top", "4px"), ("x", "right", "1px"), ("x", "bottom", "4px"), ("x", "left", "1px")]),
        ["padding-physical"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding:var(--s) 2px\"></div></div>", [("x", "padding-top", "4px"), ("x", "padding-right", "2px"), ("x", "padding-bottom", "4px"), ("x", "padding-left", "2px")]),
        ["margin-physical"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;margin:var(--s) auto\"></div></div>", [("x", "margin-top", "4px"), ("x", "margin-right", "auto"), ("x", "margin-bottom", "4px"), ("x", "margin-left", "auto")]),
        ["scroll-padding-inline"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;scroll-padding-inline:var(--s) 2px\"></div></div>", [("x", "scroll-padding-left", "4px"), ("x", "scroll-padding-right", "2px")]),
        ["scroll-padding-block"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;scroll-padding-block:var(--s)\"></div></div>", [("x", "scroll-padding-top", "4px"), ("x", "scroll-padding-bottom", "4px")]),
        ["scroll-margin-inline"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;scroll-margin-inline:var(--s) 2px\"></div></div>", [("x", "scroll-margin-left", "4px"), ("x", "scroll-margin-right", "2px")]),
        ["scroll-margin-block"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;scroll-margin-block:2px var(--s)\"></div></div>", [("x", "scroll-margin-top", "2px"), ("x", "scroll-margin-bottom", "4px")]),
        ["border-inline-width"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-style:solid;border-inline-width:var(--s) 2px\"></div></div>", [("x", "border-left-width", "4px"), ("x", "border-right-width", "2px")]),
        ["border-block-width"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-style:solid;border-block-width:var(--s)\"></div></div>", [("x", "border-top-width", "4px"), ("x", "border-bottom-width", "4px")]),
        ["border-inline-style"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-inline-style:var(--st)\"></div></div>", [("x", "border-left-style", "dotted"), ("x", "border-right-style", "dotted")]),
        ["border-block-style"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-block-style:var(--st) solid\"></div></div>", [("x", "border-top-style", "dotted"), ("x", "border-bottom-style", "solid")]),
        ["border-inline-color"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-style:solid;border-inline-color:var(--c) blue\"></div></div>", [("x", "border-left-color", "red"), ("x", "border-right-color", "blue")]),
        ["border-block-color"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-style:solid;border-block-color:blue var(--c)\"></div></div>", [("x", "border-top-color", "blue"), ("x", "border-bottom-color", "red")]),
        ["border-inline-start"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-inline-start:var(--s) solid var(--c)\"></div></div>", [("x", "border-left-width", "4px"), ("x", "border-left-style", "solid"), ("x", "border-left-color", "red")]),
        ["border-block-end"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-block-end:var(--s) dashed var(--c)\"></div></div>", [("x", "border-bottom-width", "4px"), ("x", "border-bottom-style", "dashed"), ("x", "border-bottom-color", "red")]),
        ["border-inline-end-keyword-order"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-inline-end:solid var(--c) var(--s)\"></div></div>", [("x", "border-right-width", "4px"), ("x", "border-right-style", "solid"), ("x", "border-right-color", "red")]),
        ["mix-physical-then-logical"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-left:1px;padding-inline:var(--s)\"></div></div>", [("x", "padding-left", "4px"), ("x", "padding-right", "4px")]),
        ["mix-logical-then-physical"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:var(--s);padding-left:1px\"></div></div>", [("x", "padding-left", "1px"), ("x", "padding-right", "4px")]),
        ["mix-shorthand-then-longhand"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:var(--s);padding-inline-start:1px\"></div></div>", [("x", "padding-left", "1px"), ("x", "padding-right", "4px")]),
        ["mix-longhand-then-shorthand"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline-start:1px;padding-inline:var(--s)\"></div></div>", [("x", "padding-left", "4px"), ("x", "padding-right", "4px")]),
        ["invalid-after-substitution"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:var(--bad)\"></div></div>", [("x", "padding-left", "0px"), ("x", "padding-right", "0px")]),
        ["invalid-count"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:var(--s) var(--s) var(--s)\"></div></div>", [("x", "padding-left", "0px"), ("x", "padding-right", "0px")]),
        ["later-invalid-wins"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline:3px;padding-inline:var(--bad)\"></div></div>", [("x", "padding-left", "0px"), ("x", "padding-right", "0px")]),
        ["undefined-variable"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;margin-inline:var(--nope)\"></div></div>", [("x", "margin-left", "0px"), ("x", "margin-right", "0px")]),
        ["fallback-used"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;margin-inline:var(--nope, 6px) var(--s)\"></div></div>", [("x", "margin-left", "6px"), ("x", "margin-right", "4px")]),
        ["vertical-rl-padding"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:vertical-rl;padding-inline:var(--s) 2px;padding-block:5px var(--s)\"></div></div>", [("x", "padding-top", "4px"), ("x", "padding-right", "5px"), ("x", "padding-bottom", "2px"), ("x", "padding-left", "4px")]),
        ["vertical-lr-rtl-margin"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:vertical-lr;direction:rtl;margin-inline:var(--s) 2px;margin-block:5px var(--s)\"></div></div>", [("x", "margin-top", "2px"), ("x", "margin-right", "4px"), ("x", "margin-bottom", "4px"), ("x", "margin-left", "5px")]),
        ["rtl-padding"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;direction:rtl;padding-inline:var(--s) 2px\"></div></div>", [("x", "padding-left", "2px"), ("x", "padding-right", "4px")]),
        ["sideways-lr-padding"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:sideways-lr;padding-inline:var(--s) 2px;padding-block:5px var(--s)\"></div></div>", [("x", "padding-top", "2px"), ("x", "padding-right", "4px"), ("x", "padding-bottom", "4px"), ("x", "padding-left", "5px")]),
        ["sideways-rl-rtl-padding"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:sideways-rl;direction:rtl;padding-inline:var(--s) 2px;padding-block:5px var(--s)\"></div></div>", [("x", "padding-top", "2px"), ("x", "padding-right", "5px"), ("x", "padding-bottom", "4px"), ("x", "padding-left", "4px")]),
        ["vertical-rl-border-width"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:vertical-rl;border-style:solid;border-inline-width:var(--s) 2px;border-block-width:5px var(--s)\"></div></div>", [("x", "border-top-width", "4px"), ("x", "border-right-width", "5px"), ("x", "border-bottom-width", "2px"), ("x", "border-left-width", "4px")]),
        ["vertical-lr-inset"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:vertical-lr;inset-inline:var(--s) 1px;inset-block:2px var(--s)\"></div></div>", [("x", "top", "4px"), ("x", "right", "4px"), ("x", "bottom", "1px"), ("x", "left", "2px")]),
        ["longhand-padding-inline-start"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;padding-inline-start:var(--s)\"></div></div>", [("x", "padding-left", "4px")]),
        ["longhand-margin-block-end"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;margin-block-end:var(--s)\"></div></div>", [("x", "margin-bottom", "4px")]),
        ["longhand-inline-size"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;inline-size:var(--s);block-size:3px\"></div></div>", [("x", "width", "4px"), ("x", "height", "3px")]),
        ["longhand-inline-size-vertical"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;writing-mode:vertical-rl;inline-size:var(--s);block-size:3px\"></div></div>", [("x", "width", "3px"), ("x", "height", "4px")]),
        ["longhand-radius"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;border-start-start-radius:var(--s)\"></div></div>", [("x", "border-top-left-radius", "4px")]),
        ["longhand-inset-inline-end"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;\"><div id=x style=\"display:none;inset-inline-end:var(--s)\"></div></div>", [("x", "right", "4px")]),
        ["inherit-longhand-same-mode"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;padding-inline-start:inherit;padding-block-start:inherit\"></div></div>", [("x", "padding-top", "9px"), ("x", "padding-right", "3px"), ("x", "padding-bottom", "0px"), ("x", "padding-left", "0px")]),
        ["inherit-longhand-other-mode"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;writing-mode:horizontal-tb;padding-inline-start:inherit;padding-block-start:inherit\"></div></div>", [("x", "padding-top", "9px"), ("x", "padding-right", "0px"), ("x", "padding-bottom", "0px"), ("x", "padding-left", "7px")]),
        ["inherit-shorthand-same-mode"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;padding-inline:inherit\"></div></div>", [("x", "padding-top", "9px"), ("x", "padding-bottom", "2px")]),
        ["inherit-shorthand-other-mode"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;writing-mode:horizontal-tb;padding-inline:inherit;padding-block:inherit\"></div></div>", [("x", "padding-top", "9px"), ("x", "padding-right", "3px"), ("x", "padding-bottom", "2px"), ("x", "padding-left", "7px")]),
        ["initial-shorthand"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;padding-inline:initial\"></div></div>", [("x", "padding-top", "0px"), ("x", "padding-right", "0px"), ("x", "padding-bottom", "0px"), ("x", "padding-left", "0px")]),
        ["unset-shorthand"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;padding-inline:unset\"></div></div>", [("x", "padding-top", "0px"), ("x", "padding-right", "0px"), ("x", "padding-bottom", "0px"), ("x", "padding-left", "0px")]),
        ["inherit-shorthand-var-chain"] = ("<!doctype html><div style=\"--s:4px;--st:dotted;--c:red;--bad:red;writing-mode:vertical-rl;padding-top:9px;padding-right:3px;padding-left:7px;padding-bottom:2px\"><div id=x style=\"display:none;padding-inline:var(--s)\"></div></div>", [("x", "padding-top", "4px"), ("x", "padding-right", "0px"), ("x", "padding-bottom", "4px"), ("x", "padding-left", "0px")]),
        ["va-cell-inherits-row"] = ("<!doctype html><table><tr style=\"vertical-align:bottom\"><td id=x>a</td><th id=y>b</th></tr></table>", [("x", "vertical-align", "bottom"), ("y", "vertical-align", "bottom")]),
        ["va-cell-default"] = ("<!doctype html><table><tr><td id=x>a</td></tr></table>", [("x", "vertical-align", "middle")]),
        ["va-inherit"] = ("<!doctype html><span style=\"vertical-align:middle\"><i id=x style=\"vertical-align:inherit\">a</i></span>", [("x", "vertical-align", "middle")]),
        ["va-initial"] = ("<!doctype html><span style=\"vertical-align:middle\"><i id=x style=\"vertical-align:initial\">a</i></span>", [("x", "vertical-align", "baseline")]),
        ["va-unset"] = ("<!doctype html><span style=\"vertical-align:middle\"><i id=x style=\"vertical-align:unset\">a</i></span>", [("x", "vertical-align", "baseline")]),
        ["va-value"] = ("<!doctype html><span style=\"vertical-align:middle\"><i id=x style=\"vertical-align:top\">a</i></span>", [("x", "vertical-align", "top")]),
        ["va-revert-to-ua"] = ("<!doctype html><table><tr style=\"vertical-align:bottom\"><td id=x style=\"vertical-align:revert\">a</td></tr></table>", [("x", "vertical-align", "bottom")]),
        ["va-revert-layer"] = ("<!doctype html><style>@layer a{td{vertical-align:top}} @layer b{td{vertical-align:revert-layer}}</style><table><tr style=\"vertical-align:bottom\"><td id=x>a</td></tr></table>", [("x", "vertical-align", "top")]),
        ["va-author-cell-rule"] = ("<!doctype html><style>td{vertical-align:inherit}</style><table><tr style=\"vertical-align:top\"><td id=x>a</td></tr></table>", [("x", "vertical-align", "top")]),
        ["va-nested-table"] = ("<!doctype html><table style=\"vertical-align:top\"><tr><td><table><tr style=\"vertical-align:bottom\"><td id=x>a</td></tr></table></td></tr></table>", [("x", "vertical-align", "bottom")]),
        ["text-align-initial"] = ("<!doctype html><div style=\"text-align:center\"><p id=x style=\"text-align:initial\">a</p></div>", [("x", "text-align", "start")]),
        ["text-align-inherit"] = ("<!doctype html><div style=\"text-align:right\"><p id=x style=\"text-align:inherit\">a</p></div>", [("x", "text-align", "right")]),
        ["text-align-unset"] = ("<!doctype html><div style=\"text-align:right\"><p id=x style=\"text-align:unset\">a</p></div>", [("x", "text-align", "right")]),
        ["font-variant-initial"] = ("<!doctype html><div style=\"font-variant:small-caps\"><p id=x style=\"font-variant:initial\">a</p></div>", [("x", "font-variant", "normal")]),
        ["font-variant-inherit"] = ("<!doctype html><div style=\"font-variant:small-caps\"><p id=x style=\"font-variant:inherit\">a</p></div>", [("x", "font-variant", "small-caps")]),
        ["white-space-inherit"] = ("<!doctype html><div style=\"white-space:pre\"><p id=x style=\"white-space:inherit\">a</p></div>", [("x", "white-space", "pre")]),
        ["white-space-initial"] = ("<!doctype html><div style=\"white-space:pre\"><p id=x style=\"white-space:initial\">a</p></div>", [("x", "white-space", "normal")]),
        ["text-wrap-inherit"] = ("<!doctype html><div style=\"text-wrap:balance\"><p id=x style=\"text-wrap:inherit\">a</p></div>", [("x", "text-wrap", "balance")]),
        ["place-items-inherit"] = ("<!doctype html><div style=\"place-items:center\"><p id=x style=\"place-items:inherit\">a</p></div>", [("x", "place-items", "center")]),
        ["border-radius-inherit"] = ("<!doctype html><div style=\"border-radius:5px\"><p id=x style=\"border-radius:inherit\">a</p></div>", [("x", "border-radius", "5px")]),
        ["all-initial"] = ("<!doctype html><div style=\"text-align:center;vertical-align:middle;white-space:pre\"><p id=x style=\"all:initial\">a</p></div>", [("x", "text-align", "start"), ("x", "vertical-align", "baseline"), ("x", "white-space", "normal")]),
        ["all-inherit"] = ("<!doctype html><div style=\"text-align:center;vertical-align:middle;white-space:pre\"><p id=x style=\"all:inherit\">a</p></div>", [("x", "text-align", "center"), ("x", "vertical-align", "middle"), ("x", "white-space", "pre")]),
        ["all-unset"] = ("<!doctype html><div style=\"text-align:center;vertical-align:middle;white-space:pre\"><p id=x style=\"all:unset\">a</p></div>", [("x", "text-align", "center"), ("x", "vertical-align", "baseline")]),
        ["font-var-stretch"] = ("<!doctype html><div style=\"--f:semi-expanded 700 20px/30px Arial\"><p id=x style=\"font:var(--f)\">a</p></div>", [("x", "font-stretch", "112.5%"), ("x", "font-weight", "700"), ("x", "font-size", "20px"), ("x", "line-height", "30px")]),
        ["font-literal-stretch"] = ("<!doctype html><p id=x style=\"font:semi-expanded 700 20px/30px Arial\">a</p>", [("x", "font-stretch", "112.5%"), ("x", "font-weight", "700"), ("x", "font-size", "20px")]),
    };

    public static IEnumerable<TheoryDataRow<string>> Names()
    {
        foreach (string name in Cases.Keys)
        {
            yield return new TheoryDataRow<string>(name);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Computed_values_match_Chrome(string name)
    {
        (string html, (string Id, string Property, string Expected)[] probes) = Cases[name];
        DocumentStyles styles = CascadeTests.Styles(html);
        var failures = new StringBuilder();
        foreach ((string id, string property, string chrome) in probes)
        {
            string actual = styles.GetStyle(styles.Document.GetElementById(id)).GetComputedValue(property).Trim();
            string expected = chrome;
            if (property == "font-stretch" && expected == "112.5%")
            {
                expected = "semi-expanded"; // Chrome serializes the keyword as its percentage
            }

            if (actual == "0")
            {
                actual = "0px"; // the initial value of a length property is kept as the table writes it
            }

            if (actual != expected)
            {
                failures.Append(id).Append(' ').Append(property).Append(": expected ").Append(expected).Append(", got ").Append(actual).Append('\n');
            }
        }

        Assert.True(failures.Length == 0, name + "\n" + failures);
    }
}
