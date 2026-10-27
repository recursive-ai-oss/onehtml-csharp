using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// <c>@keyframes</c> rules and their resolved keyframes (CSS Animations 1 section 3, CSS Animations 2 section 3.2, CSS Cascade 5 section 6.4 for layers), checked
/// against Chrome for Testing 156.0.8078.47. Each case is a page with one animated element (paused, 10 s). For every case the keyframes
/// Chrome's <c>element.getAnimations()[n].effect.getKeyframes()</c> returned had the same animations, frame count, offsets, timing functions, composites and
/// property names as the ones below; the values here are this library's: the specified text with <c>var()</c> substituted, where Chrome reports computed values
/// (<c>red</c> is <c>rgb(255, 0, 0)</c>, <c>1em</c> is <c>20px</c>, <c>calc(10px * 2)</c> is <c>20px</c>, <c>0.5</c> is written <c>.5</c> here when the page wrote it so), and
/// the keyword <c>initial</c> for the parts a shorthand leaves out. A frame reads <c>offset|timing function|composition|property=value~...</c>; no composition is
/// <c>-</c>, <c>*</c> marks an implicit frame, <c>!</c> a value that was invalid at computed-value time. Known differences from Chrome: <c>shorthand2</c>, <c>shorthand4</c> (the
/// property table's <c>background-blend-mode</c>, <c>font-width</c> and <c>font-stretch</c>), <c>prop44</c> (Chrome's JavaScript name for <c>-webkit-text-fill-color</c>).
/// </summary>
public class KeyframesChromeTests
{
    private static readonly (string Id, string[] Styles, string Element, string Extra, string AnimationName, string[][] Expected)[] Cases =
    [
        ("basic", ["@keyframes basic { from { opacity: 0 } to { opacity: 1 } }"], "", "", "basic", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("unsorted", ["@keyframes unsorted { 50% { opacity: .5 } 100% { opacity: 1 } 0% { opacity: 0 } }"], "", "", "unsorted", [["0|ease|-|opacity=0", "0.5|ease|-|opacity=.5", "1|ease|-|opacity=1"]]),
        ("dupes", ["@keyframes dupes { from { margin-left: 0px } 50% { margin-left: 110px; opacity: 1 } 50% { opacity: 0.9 } to { margin-left: 200px } }"], "", "", "dupes", [["0|ease|-|margin-left=0px~opacity=1", "0.5|ease|-|margin-left=110px~opacity=0.9", "1|ease|-|margin-left=200px~opacity=1"]]),
        ("dupetf", ["@keyframes dupetf { 50% { opacity: 1; animation-timing-function: ease-in } 50% { opacity: .5 } to { opacity: 0 } }"], "", "", "dupetf", [["0|ease|replace*|opacity=1", "0.5|ease-in|-|opacity=1", "0.5|ease|-|opacity=.5", "1|ease|-|opacity=0"]]),
        ("dupetf2", ["@keyframes dupetf2 { 50% { opacity: 1; animation-timing-function: ease-in } 50% { opacity: .5; animation-timing-function: linear } }"], "", "", "dupetf2", [["0|ease|replace*|opacity=1", "0.5|ease-in|-|opacity=1", "0.5|linear|-|opacity=.5", "1|ease|replace*|opacity=1"]]),
        ("multisel", ["@keyframes multisel { 0%, 100% { opacity: 1 } 50% { opacity: 0 } }"], "", "", "multisel", [["0|ease|-|opacity=1", "0.5|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("fromdup", ["@keyframes fromdup { from, 0% { opacity: 1 } to, 100% { opacity: 0 } }"], "", "", "fromdup", [["0|ease|-|opacity=1", "1|ease|-|opacity=0"]]),
        ("fromdup2", ["@keyframes fromdup2 { from { opacity: 1 } 0% { color: red } to { opacity: 0 } }"], "", "", "fromdup2", [["0|ease|-|color=red~opacity=1", "1|ease|-|color=canvastext~opacity=0"]]),
        ("badsel1", ["@keyframes badsel1 { -10% { opacity: 1 } 101% { opacity: 2 } to { opacity: 0 } }"], "", "", "badsel1", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("badsel2", ["@keyframes badsel2 { 0 { opacity: 1 } foo { opacity: 2 } to { opacity: 0 } }"], "", "", "badsel2", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("badsel3", ["@keyframes badsel3 { 50%, bogus { opacity: 1 } to { opacity: 0 } }"], "", "", "badsel3", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("badsel4", ["@keyframes badsel4 { 50%, 120% { opacity: 1 } to { opacity: 0 } }"], "", "", "badsel4", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("important", ["@keyframes important { from { opacity: 1 !important; margin-left: 3px } to { margin-left: 4px } }"], "", "", "important", [["0|ease|-|margin-left=3px", "1|ease|-|margin-left=4px"]]),
        ("invalidval", ["@keyframes invalidval { from { opacity: foo; margin-left: 1px } to { margin-left: 2px } }"], "", "", "invalidval", [["0|ease|-|margin-left=1px", "1|ease|-|margin-left=2px"]]),
        ("animprops", ["@keyframes animprops { from { animation-duration: 3s; animation-name: zzz; animation-delay: 1s; opacity: 1 } to { opacity: 0 } }"], "", "", "animprops", [["0|ease|-|opacity=1", "1|ease|-|opacity=0"]]),
        ("transprops", ["@keyframes transprops { from { transition: all 1s; transition-property: color; opacity: 1 } to { opacity: 0 } }"], "", "", "transprops", [["0|ease|-|opacity=1", "1|ease|-|opacity=0"]]),
        ("shorthand1", ["@keyframes shorthand1 { from { margin: 1px 2px } to { margin: 5px } }"], "", "", "shorthand1", [["0|ease|-|margin-bottom=1px~margin-left=2px~margin-right=2px~margin-top=1px", "1|ease|-|margin-bottom=5px~margin-left=5px~margin-right=5px~margin-top=5px"]]),
        ("shorthand2", ["@keyframes shorthand2 { from { background: red } to { background: blue } }"], "", "", "shorthand2", [["0|ease|-|background-attachment=initial~background-blend-mode=initial~background-clip=initial~background-color=red~background-image=initial~background-origin=initial~background-position-x=initial~background-position-y=initial~background-repeat=initial~background-size=initial", "1|ease|-|background-attachment=initial~background-blend-mode=initial~background-clip=initial~background-color=blue~background-image=initial~background-origin=initial~background-position-x=initial~background-position-y=initial~background-repeat=initial~background-size=initial"]]),
        ("shorthand3", ["@keyframes shorthand3 { from { border-top: 1px solid red } to { border-top: 3px dashed blue } }"], "", "", "shorthand3", [["0|ease|-|border-top-color=red~border-top-style=solid~border-top-width=1px", "1|ease|-|border-top-color=blue~border-top-style=dashed~border-top-width=3px"]]),
        ("shorthand4", ["@keyframes shorthand4 { from { font: 12px serif } to { font: 20px sans-serif } }"], "", "", "shorthand4", [["0|ease|-|font-family=serif~font-feature-settings=initial~font-kerning=initial~font-language-override=initial~font-optical-sizing=initial~font-size=12px~font-size-adjust=initial~font-style=initial~font-variant-alternates=initial~font-variant-caps=initial~font-variant-east-asian=initial~font-variant-emoji=initial~font-variant-ligatures=initial~font-variant-numeric=initial~font-variant-position=initial~font-variation-settings=initial~font-weight=initial~font-width=initial~line-height=initial", "1|ease|-|font-family=sans-serif~font-feature-settings=initial~font-kerning=initial~font-language-override=initial~font-optical-sizing=initial~font-size=20px~font-size-adjust=initial~font-style=initial~font-variant-alternates=initial~font-variant-caps=initial~font-variant-east-asian=initial~font-variant-emoji=initial~font-variant-ligatures=initial~font-variant-numeric=initial~font-variant-position=initial~font-variation-settings=initial~font-weight=initial~font-width=initial~line-height=initial"]]),
        ("logical1", ["@keyframes logical1 { from { margin-inline-start: 1px; margin-left: 2px } to { margin-left: 3px } }"], "", "", "logical1", [["0|ease|-|margin-left=2px", "1|ease|-|margin-left=3px"]]),
        ("logical2", ["@keyframes logical2 { from { margin-left: 2px; margin-inline-start: 1px } to { margin-left: 3px } }"], "", "", "logical2", [["0|ease|-|margin-left=1px", "1|ease|-|margin-left=3px"]]),
        ("logical3", ["@keyframes logical3 { from { padding-inline: 1px 2px } to { padding-inline: 3px } }"], "direction:rtl", "", "logical3", [["0|ease|-|padding-left=2px~padding-right=1px", "1|ease|-|padding-left=3px~padding-right=3px"]]),
        ("custom1", ["@keyframes custom1 { from { --x: 3; opacity: 0 } to { --x: 5; opacity: 1 } }"], "", "", "custom1", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("custom2", ["@keyframes custom2 { from { --x: 3px } to { width: var(--x) } }"], "--x: 10px", "", "custom2", [["0|ease|-|width=auto", "1|ease|-|width=10px"]]),
        ("var1", ["@keyframes var1 { from { color: var(--c); width: var(--w) } to { color: blue; width: 50px } }"], "--c: red; --w: 10px", "", "var1", [["0|ease|-|color=red~width=10px", "1|ease|-|color=blue~width=50px"]]),
        ("var2", ["@keyframes var2 { from { width: var(--none, 5px) } to { width: 50px } }"], "", "", "var2", [["0|ease|-|width=5px", "1|ease|-|width=50px"]]),
        ("var3", ["@keyframes var3 { from { width: var(--undef); opacity: 0 } to { width: 50px; opacity: 1 } }"], "", "", "var3", [["0|ease|-|opacity=0~width=unset!", "1|ease|-|opacity=1~width=50px"]]),
        ("var4", ["@keyframes var4 { from { margin: var(--w) 2px } to { margin: 5px } }"], "--w: 10px", "", "var4", [["0|ease|-|margin-bottom=10px~margin-left=2px~margin-right=2px~margin-top=10px", "1|ease|-|margin-bottom=5px~margin-left=5px~margin-right=5px~margin-top=5px"]]),
        ("var5", ["@keyframes var5 { from { width: var(--bad) } to { width: 50px } }"], "--bad: red", "", "var5", [["0|ease|-|width=red", "1|ease|-|width=50px"]]),
        ("var6", ["@keyframes var6 { from { width: calc(var(--w) * 2) } to { width: 50px } }"], "--w: 10px", "", "var6", [["0|ease|-|width=calc(10px * 2)", "1|ease|-|width=50px"]]),
        ("var7", ["@keyframes var7 { from { color: var(--c) } to { color: var(--c2, green) } }"], "--c: var(--d); --d: red", "", "var7", [["0|ease|-|color=red", "1|ease|-|color=green"]]),
        ("var8", ["@keyframes var8 { from { opacity: var(--o) } to { opacity: 1 } }"], "--o: initial", "", "var8", [["0|ease|-|opacity=unset!", "1|ease|-|opacity=1"]]),
        ("var9", ["@keyframes var9 { from { width: var(--w) } to { width: 50px } }"], "--w: 1em; font-size: 20px", "", "var9", [["0|ease|-|width=1em", "1|ease|-|width=50px"]]),
        ("var10", ["@keyframes var10 { from { animation-timing-function: var(--tf); opacity: 0 } to { opacity: 1 } }"], "--tf: linear", "", "var10", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("var11", ["@keyframes var11 { from { opacity: 0 } to { opacity: var(--o) } }"], "--o: 0.5; --p: 1", "", "var11", [["0|ease|-|opacity=0", "1|ease|-|opacity=0.5"]]),
        ("var12", ["@keyframes var12 { from { width: var(--w) } to { width: 50px } }"], "", "", "var12", [["0|ease|-|width=unset!", "1|ease|-|width=50px"]]),
        ("layer1", ["@layer l1a, l1b; @layer l1b { @keyframes layer1 { from { opacity: 0.1 } to { opacity: 0.2 } } } @layer l1a { @keyframes layer1 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "layer1", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("layer2", ["@layer l2a { @keyframes layer2 { from { opacity: 0.1 } to { opacity: 0.2 } } } @keyframes layer2 { from { opacity: 0.3 } to { opacity: 0.4 } } "], "", "", "layer2", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("layer3", ["@keyframes layer3 { from { opacity: 0.3 } to { opacity: 0.4 } } @layer l3a { @keyframes layer3 { from { opacity: 0.1 } to { opacity: 0.2 } } }"], "", "", "layer3", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("layer4", ["@layer l4a { @keyframes layer4 { from { opacity: 0.1 } to { opacity: 0.2 } } @keyframes layer4 { from { opacity: 0.5 } to { opacity: 0.6 } } }"], "", "", "layer4", [["0|ease|-|opacity=0.5", "1|ease|-|opacity=0.6"]]),
        ("layer5", ["@layer l5a { @layer l5b { @keyframes layer5 { from { opacity: 0.1 } to { opacity: 0.2 } } } @keyframes layer5 { from { opacity: 0.5 } to { opacity: 0.6 } } }"], "", "", "layer5", [["0|ease|-|opacity=0.5", "1|ease|-|opacity=0.6"]]),
        ("layer6", ["@layer l6a { @keyframes layer6 { from { opacity: 0.1 } to { opacity: 0.2 } } }", "@layer l6b { @keyframes layer6 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "layer6", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("layer7", ["@layer l7b { @keyframes layer7 { from { opacity: 0.1 } to { opacity: 0.2 } } }", "@layer l7a, l7b; @layer l7a { @keyframes layer7 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "layer7", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("layer8", ["@layer l8a { @keyframes layer8 { from { opacity: 0.1 } to { opacity: 0.2 } } } @layer l8a { @keyframes layer8 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "layer8", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("layer9", ["@layer { @keyframes layer9 { from { opacity: 0.1 } to { opacity: 0.2 } } } @layer { @keyframes layer9 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "layer9", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("sheets", ["@keyframes sheets { from { opacity: 0.1 } to { opacity: 0.2 } }", "@keyframes sheets { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "sheets", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("later", ["@keyframes later { from { opacity: 0.1 } to { opacity: 0.2 } } @keyframes later { from { margin-left: 3px } to { margin-left: 4px } }"], "", "", "later", [["0|ease|-|margin-left=3px", "1|ease|-|margin-left=4px"]]),
        ("later_empty", ["@keyframes later_empty { from { opacity: 0.1 } to { opacity: 0.2 } } @keyframes later_empty { }"], "", "", "later_empty", [[]]),
        ("later_invalid", ["@keyframes later_invalid { from { opacity: 0.1 } to { opacity: 0.2 } } @keyframes later_invalid { from { opacity: foo } }"], "", "", "later_invalid", [["0|ease|-|"]]),
        ("wk1", ["@keyframes wk1 { from { opacity: 0.1 } to { opacity: 0.2 } } @-webkit-keyframes wk1 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "wk1", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("wk2", ["@-webkit-keyframes wk2 { from { opacity: 0.3 } to { opacity: 0.4 } } @keyframes wk2 { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "wk2", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("wk3", ["@-webkit-keyframes wk3 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "wk3", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("wk4", ["@-webkit-keyframes wk4 { from { opacity: 0.1 } to { opacity: 0.2 } } @-webkit-keyframes wk4 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "wk4", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("wk5", ["@keyframes wk5 { from { opacity: 0.1 } to { opacity: 0.2 } }", "@-webkit-keyframes wk5 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "wk5", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("wk6", ["@-webkit-keyframes wk6 { from { -webkit-transform: scale(1) } to { -webkit-transform: scale(2) } }"], "", "", "wk6", [["0|ease|-|transform=scale(1)", "1|ease|-|transform=scale(2)"]]),
        ("wk7", ["@-webkit-keyframes wk7 { from { opacity: 0.3 } to { opacity: 0.4 } } @keyframes wk7 { from { opacity: 0.1 } to { opacity: 0.2 } } @-webkit-keyframes wk7 { from { opacity: 0.5 } to { opacity: 0.6 } }"], "", "", "wk7", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("case_Foo", ["@keyframes case_Foo { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "case_foo", []),
        ("case_Foo2", ["@keyframes case_Foo2 { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "case_Foo2", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("strname", ["@keyframes \"strname\" { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "strname", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("strname2", ["@keyframes strname2 { from { opacity: 0.1 } to { opacity: 0.2 } } @keyframes \"strname2\" { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "strname2", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("strname3", ["@keyframes \"strname3\" { from { opacity: 0.1 } to { opacity: 0.2 } } @keyframes strname3 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "strname3", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("strname4", ["@keyframes \"str name4\" { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "\"str name4\"", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("nonekf", ["@keyframes none { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "none", []),
        ("nonekf2", ["@keyframes \"none\" { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "\"none\"", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("nonekf3", ["@keyframes None { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "None", []),
        ("initialkf", ["@keyframes initial { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "initial", []),
        ("initialkf2", ["@keyframes \"initial\" { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "\"initial\"", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("defaultkf", ["@keyframes default { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "default", []),
        ("defaultkf2", ["@keyframes \"default\" { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "\"default\"", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("emptynamekf", ["@keyframes \"\" { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "\"\"", []),
        ("escname", ["@keyframes \\31 esc { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "\\31 esc", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("media1", ["@media (min-width: 100000px) { @keyframes media1 { from { opacity: 0.1 } to { opacity: 0.2 } } }"], "", "", "media1", []),
        ("media2", ["@keyframes media2 { from { opacity: 0.1 } to { opacity: 0.2 } } @media (min-width: 1px) { @keyframes media2 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "media2", [["0|ease|-|opacity=0.3", "1|ease|-|opacity=0.4"]]),
        ("media3", ["@keyframes media3 { from { opacity: 0.1 } to { opacity: 0.2 } } @media (min-width: 100000px) { @keyframes media3 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "media3", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("supports1", ["@supports (display: grid) { @keyframes supports1 { from { opacity: 0.1 } to { opacity: 0.2 } } }"], "", "", "supports1", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("supports2", ["@supports (display: nonsense) { @keyframes supports2 { from { opacity: 0.1 } to { opacity: 0.2 } } }"], "", "", "supports2", []),
        ("nested1", ["div#x { @keyframes nested1 { from { opacity: 0.1 } to { opacity: 0.2 } } }"], "", "", "nested1", []),
        ("nested2", ["@media screen { @media (min-width: 1px) { @keyframes nested2 { from { opacity: 0.1 } to { opacity: 0.2 } } } }"], "", "", "nested2", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("onlytf", ["@keyframes onlytf { from { animation-timing-function: linear } to { opacity: 1 } }"], "", "", "onlytf", [["0|linear|-|", "0|ease|replace*|opacity=1", "1|ease|-|opacity=1"]]),
        ("composition1", ["@keyframes composition1 { from { opacity: 0; animation-composition: add } to { opacity: 1 } }"], "", "", "composition1", [["0|ease|add|opacity=0", "1|ease|-|opacity=1"]]),
        ("composition2", ["@keyframes composition2 { from { opacity: 0 } to { opacity: 1 } }"], "", "animation-composition: accumulate", "composition2", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("composition3", ["@keyframes composition3 { from { opacity: 0; animation-composition: foo } 50% { opacity: .5; animation-composition: replace } to { opacity: 1 } }"], "", "animation-composition: add", "composition3", [["0|ease|-|opacity=0", "0.5|ease|replace|opacity=.5", "1|ease|-|opacity=1"]]),
        ("tfdefault", ["@keyframes tfdefault { from { opacity: 0 } 50% { opacity: .5; animation-timing-function: steps(3) } to { opacity: 1; animation-timing-function: linear } }"], "", "animation-timing-function: ease-in", "tfdefault", [["0|ease-in|-|opacity=0", "0.5|steps(3)|-|opacity=.5", "1|linear|-|opacity=1"]]),
        ("tfdefault2", ["@keyframes tfdefault2 { from { opacity: 0 } to { opacity: 1 } }"], "", "animation-timing-function: cubic-bezier(0.1, 0.7, 1, 0.1)", "tfdefault2", [["0|cubic-bezier(0.1, 0.7, 1, 0.1)|-|opacity=0", "1|cubic-bezier(0.1, 0.7, 1, 0.1)|-|opacity=1"]]),
        ("tfinvalid", ["@keyframes tfinvalid { from { opacity: 0; animation-timing-function: foo } to { opacity: 1 } }"], "", "animation-timing-function: ease-in", "tfinvalid", [["0|ease-in|-|opacity=0", "1|ease-in|-|opacity=1"]]),
        ("tfimportant", ["@keyframes tfimportant { from { opacity: 0; animation-timing-function: linear !important } to { opacity: 1 } }"], "", "animation-timing-function: ease-in", "tfimportant", [["0|ease-in|-|opacity=0", "1|ease-in|-|opacity=1"]]),
        ("tflist", ["@keyframes tflist { from { opacity: 0; animation-timing-function: linear, ease-in } to { opacity: 1 } }"], "", "", "tflist", [["0|linear|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("multi", ["@keyframes multi1 { from { opacity: 0 } to { opacity: 1 } } @keyframes multi2 { from { margin-left: 0px } to { margin-left: 5px } }"], "", "animation-timing-function: ease-in, linear", "multi1, multi2", [["0|ease-in|-|opacity=0", "1|ease-in|-|opacity=1"], ["0|linear|-|margin-left=0px", "1|linear|-|margin-left=5px"]]),
        ("multi_missing", ["@keyframes multi_missing2 { from { margin-left: 0px } to { margin-left: 5px } }"], "", "animation-timing-function: ease-in, linear", "multi_missing1, multi_missing2", [["0|linear|-|margin-left=0px", "1|linear|-|margin-left=5px"]]),
        ("multi_cycle", ["@keyframes multi_cycle1 { from { margin-left: 0px } to { margin-left: 5px } } @keyframes multi_cycle2 { from { margin-left: 0px } to { margin-left: 5px } } @keyframes multi_cycle3 { from { margin-left: 0px } to { margin-left: 5px } }"], "", "animation-timing-function: ease-in, linear", "multi_cycle1, multi_cycle2, multi_cycle3", [["0|ease-in|-|margin-left=0px", "1|ease-in|-|margin-left=5px"], ["0|linear|-|margin-left=0px", "1|linear|-|margin-left=5px"], ["0|ease-in|-|margin-left=0px", "1|ease-in|-|margin-left=5px"]]),
        ("multi_same", ["@keyframes multi_same { from { margin-left: 0px } to { margin-left: 5px } }"], "", "animation-timing-function: ease-in, linear", "multi_same, multi_same", [["0|ease-in|-|margin-left=0px", "1|ease-in|-|margin-left=5px"], ["0|linear|-|margin-left=0px", "1|linear|-|margin-left=5px"]]),
        ("pct1", ["@keyframes pct1 { 12.5% { opacity: .1 } 0.5% { opacity: .2 } 100.0% { opacity: .3 } 50.00% { opacity: .4 } 5e1% { opacity: .5 } }"], "", "", "pct1", [["0|ease|replace*|opacity=1", "0.005|ease|-|opacity=.2", "0.125|ease|-|opacity=.1", "0.5|ease|-|opacity=.5", "1|ease|-|opacity=.3"]]),
        ("empty1", ["@keyframes empty1 { from { } to { } }"], "", "", "empty1", [["0|ease|-|", "1|ease|-|"]]),
        ("onlyto", ["@keyframes onlyto { to { opacity: 0 } }"], "", "", "onlyto", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("onlyfrom", ["@keyframes onlyfrom { from { opacity: 0 } }"], "", "", "onlyfrom", [["0|ease|-|opacity=0", "1|ease|replace*|opacity=1"]]),
        ("onlymid", ["@keyframes onlymid { 50% { opacity: 0 } }"], "", "", "onlymid", [["0|ease|replace*|opacity=1", "0.5|ease|-|opacity=0", "1|ease|replace*|opacity=1"]]),
        ("unknownprop", ["@keyframes unknownprop { from { foo: bar; opacity: 0 } to { opacity: 1 } }"], "", "", "unknownprop", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("uppercase", ["@keyframes uppercase { FROM { OPACITY: 0 } To { Opacity: 1 } }"], "", "", "uppercase", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("spaces", ["@keyframes spaces { 0 % { opacity: 0 } 50 % { opacity: .5 } to { opacity: 1 } }"], "", "", "spaces", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=1"]]),
        ("comments", ["@keyframes comments { /* a */ from /* b */ , /* c */ 50% /* d */ { opacity: /* e */ 0 } to { opacity: 1 } }"], "", "", "comments", [["0|ease|-|opacity=0", "0.5|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("nospace", ["@keyframes nospace{from,to{opacity:0}50%{opacity:1}}"], "", "", "nospace", [["0|ease|-|opacity=0", "0.5|ease|-|opacity=1", "1|ease|-|opacity=0"]]),
        ("mrg1", ["@keyframes mrg1 { 50% { opacity: 1; animation-timing-function: ease-in } 50% { margin-left: 5px; animation-timing-function: ease-in } }"], "", "", "mrg1", [["0|ease|replace*|margin-left=0~opacity=1", "0.5|ease-in|-|margin-left=5px~opacity=1", "1|ease|replace*|margin-left=0~opacity=1"]]),
        ("mrg2", ["@keyframes mrg2 { 50% { opacity: 1 } 50% { margin-left: 5px; animation-timing-function: ease } }"], "", "", "mrg2", [["0|ease|replace*|margin-left=0~opacity=1", "0.5|ease|-|margin-left=5px~opacity=1", "1|ease|replace*|margin-left=0~opacity=1"]]),
        ("mrg3", ["@keyframes mrg3 { 50% { opacity: 1; animation-composition: add } 50% { margin-left: 5px; animation-composition: add } 50% { color: red } }"], "", "", "mrg3", [["0|ease|replace*|color=canvastext~margin-left=0~opacity=1", "0.5|ease|add|margin-left=5px~opacity=1", "0.5|ease|-|color=red", "1|ease|replace*|color=canvastext~margin-left=0~opacity=1"]]),
        ("mrg4", ["@keyframes mrg4 { 50% { opacity: 1; animation-timing-function: ease-in } 50% { margin-left: 5px } 50% { color: red; animation-timing-function: ease-in } }"], "", "", "mrg4", [["0|ease|replace*|color=canvastext~margin-left=0~opacity=1", "0.5|ease|-|margin-left=5px", "0.5|ease-in|-|color=red~opacity=1", "1|ease|replace*|color=canvastext~margin-left=0~opacity=1"]]),
        ("wkl1", ["@layer wkl1a, wkl1b; @layer wkl1a { @keyframes wkl1 { from { opacity: 0.1 } to { opacity: 0.2 } } } @layer wkl1b { @-webkit-keyframes wkl1 { from { opacity: 0.3 } to { opacity: 0.4 } } }"], "", "", "wkl1", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("wkl2", ["@keyframes wkl2 { from { opacity: 0.1 } to { opacity: 0.2 } } @layer wkl2a { @keyframes wkl2 { from { opacity: 0.5 } to { opacity: 0.6 } } }  @-webkit-keyframes wkl2 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "wkl2", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("wkl3", ["@layer wkl3a { @keyframes wkl3 { from { opacity: 0.1 } to { opacity: 0.2 } } } @-webkit-keyframes wkl3 { from { opacity: 0.3 } to { opacity: 0.4 } }"], "", "", "wkl3", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("implcomp", ["@keyframes implcomp { to { opacity: 0 } }"], "", "animation-composition: accumulate", "implcomp", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("implcomp2", ["@keyframes implcomp2 { to { opacity: 0 } }"], "", "animation-composition: add, accumulate", "implcomp2", [["0|ease|replace*|opacity=1", "1|ease|-|opacity=0"]]),
        ("logical4", ["@keyframes logical4 { from { inline-size: 5px; width: 6px } to { width: 1px; inline-size: 2px } }"], "", "", "logical4", [["0|ease|-|width=6px", "1|ease|-|width=2px"]]),
        ("logical5", ["@keyframes logical5 { from { inset-inline: 1px 2px } to { left: 5px } }"], "writing-mode: vertical-rl", "", "logical5", [["0|ease|-|bottom=2px~left=auto~top=1px", "1|ease|-|bottom=auto~left=5px~top=auto"]]),
        ("badshort", ["@keyframes badshort { from { margin: var(--bad); opacity: 0 } to { opacity: 1 } }"], "--bad: red", "", "badshort", [["0|ease|-|margin-bottom=unset!~margin-left=unset!~margin-right=unset!~margin-top=unset!~opacity=0", "1|ease|-|margin-bottom=0~margin-left=0~margin-right=0~margin-top=0~opacity=1"]]),
        ("kwinherit", ["@keyframes kwinherit { from { opacity: inherit } to { opacity: initial } 50% { opacity: unset } 60% { color: revert } 70% { color: revert-layer } }"], "", "", "kwinherit", [["0|ease|-|color=canvastext~opacity=inherit", "0.5|ease|-|opacity=unset", "0.6|ease|-|color=revert", "0.7|ease|-|color=revert-layer", "1|ease|-|color=canvastext~opacity=initial"]]),
        ("dupprop", ["@keyframes dupprop { from { opacity: 1; opacity: foo; opacity: 0.5 } to { opacity: 0 } }"], "", "", "dupprop", [["0|ease|-|opacity=0.5", "1|ease|-|opacity=0"]]),
        ("shortlong", ["@keyframes shortlong { from { margin: 1px; margin-left: 5px } to { margin-left: 5px; margin: 1px } }"], "", "", "shortlong", [["0|ease|-|margin-bottom=1px~margin-left=5px~margin-right=1px~margin-top=1px", "1|ease|-|margin-bottom=1px~margin-left=1px~margin-right=1px~margin-top=1px"]]),
        ("allkw", ["@keyframes allkw { from { all: initial; opacity: 0 } to { opacity: 1 } }"], "", "", "allkw", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("unitless", ["@keyframes unitless { from { width: 5em; font-size: 20px; margin-left: 1rem; color: currentcolor } to { width: 50%; margin-left: calc(1px + 2px); color: #00f } }"], "font-size: 10px", "", "unitless", [["0|ease|-|color=currentcolor~font-size=20px~margin-left=1rem~width=5em", "1|ease|-|color=#00f~font-size=10px~margin-left=calc(1px + 2px)~width=50%"]]),
        ("rulesfirst", ["@keyframes rulesfirst { to { opacity: 1 } from { opacity: 0 } }"], "", "", "rulesfirst", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("emptymid", ["@keyframes emptymid { from { opacity: 0 } 50% { } to { opacity: 1 } }"], "", "", "emptymid", [["0|ease|-|opacity=0", "0.5|ease|-|", "1|ease|-|opacity=1"]]),
        ("order2", ["@keyframes order2 { 100% { opacity: 1 } 0% { opacity: 0 } 50% { opacity: .5 } 50% { color: red } 0% { color: blue } }"], "", "", "order2", [["0|ease|-|color=blue~opacity=0", "0.5|ease|-|color=red~opacity=.5", "1|ease|-|color=canvastext~opacity=1"]]),
        ("sameoffsetdiff", ["@keyframes sameoffsetdiff { 50% { opacity: .5; animation-timing-function: linear } 20% { opacity: .2 } 50% { color: red; animation-timing-function: steps(2) } 50% { margin-left: 1px } }"], "", "", "sameoffsetdiff", [["0|ease|replace*|color=canvastext~margin-left=0~opacity=1", "0.2|ease|-|opacity=.2", "0.5|linear|-|opacity=.5", "0.5|steps(2)|-|color=red", "0.5|ease|-|margin-left=1px", "1|ease|replace*|color=canvastext~margin-left=0~opacity=1"]]),
        ("animationname_list_none", ["@keyframes animationname_list_none { from { opacity: 0 } to { opacity: 1 } }"], "", "animation-timing-function: linear, ease-in", "none, animationname_list_none", [["0|ease-in|-|opacity=0", "1|ease-in|-|opacity=1"]]),
        ("kfupper", ["@KEYFRAMES kfupper { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "kfupper", [["0|ease|-|opacity=0.1", "1|ease|-|opacity=0.2"]]),
        ("kfmoz", ["@-moz-keyframes kfmoz { from { opacity: 0.1 } to { opacity: 0.2 } }"], "", "", "kfmoz", []),
        ("inheritparent", ["@keyframes inheritparent { from { opacity: 0 } to { opacity: 1 } }"], "", "", "inheritparent", [["0|ease|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("tflist2", ["@keyframes tflist2 { from { opacity: 0; animation-timing-function: linear, steps(2); animation-composition: add, accumulate } to { opacity: 1 } }"], "", "animation-timing-function: ease-in, ease-out, ease", "tflist2, tflist2", [["0|linear|add|opacity=0", "1|ease-in|-|opacity=1"], ["0|linear|add|opacity=0", "1|ease-out|-|opacity=1"]]),
        ("tflist3", ["@keyframes tflist3 { from { opacity: 0; animation-timing-function: linear, steps(2), ease-in } to { opacity: 1 } }"], "", "", "tflist3, tflist3, tflist3", [["0|linear|-|opacity=0", "1|ease|-|opacity=1"], ["0|linear|-|opacity=0", "1|ease|-|opacity=1"], ["0|linear|-|opacity=0", "1|ease|-|opacity=1"]]),
        ("prop0", ["@keyframes prop0 { from { display: none } to { display: block } }"], "", "", "prop0", [["0|ease|-|display=none", "1|ease|-|display=block"]]),
        ("prop1", ["@keyframes prop1 { from { visibility: hidden } to { visibility: visible } }"], "", "", "prop1", [["0|ease|-|visibility=hidden", "1|ease|-|visibility=visible"]]),
        ("prop2", ["@keyframes prop2 { from { position: relative } to { position: absolute } }"], "", "", "prop2", [["0|ease|-|position=relative", "1|ease|-|position=absolute"]]),
        ("prop3", ["@keyframes prop3 { from { direction: rtl } to { direction: ltr } }"], "", "", "prop3", [["0|ease|-|", "1|ease|-|"]]),
        ("prop4", ["@keyframes prop4 { from { unicode-bidi: embed } to { unicode-bidi: normal } }"], "", "", "prop4", [["0|ease|-|", "1|ease|-|"]]),
        ("prop5", ["@keyframes prop5 { from { writing-mode: vertical-rl } to { writing-mode: horizontal-tb } }"], "", "", "prop5", [["0|ease|-|", "1|ease|-|"]]),
        ("prop6", ["@keyframes prop6 { from { will-change: opacity } to { will-change: auto } }"], "", "", "prop6", [["0|ease|-|", "1|ease|-|"]]),
        ("prop7", ["@keyframes prop7 { from { contain: layout } to { contain: none } }"], "", "", "prop7", [["0|ease|-|", "1|ease|-|"]]),
        ("prop8", ["@keyframes prop8 { from { content: 'a' } to { content: 'b' } }"], "", "", "prop8", [["0|ease|-|content='a'", "1|ease|-|content='b'"]]),
        ("prop9", ["@keyframes prop9 { from { cursor: pointer } to { cursor: default } }"], "", "", "prop9", [["0|ease|-|cursor=pointer", "1|ease|-|cursor=default"]]),
        ("prop10", ["@keyframes prop10 { from { float: left } to { float: right } }"], "", "", "prop10", [["0|ease|-|float=left", "1|ease|-|float=right"]]),
        ("prop11", ["@keyframes prop11 { from { font-family: serif } to { font-family: monospace } }"], "", "", "prop11", [["0|ease|-|font-family=serif", "1|ease|-|font-family=monospace"]]),
        ("prop12", ["@keyframes prop12 { from { color-scheme: dark } to { color-scheme: light } }"], "", "", "prop12", [["0|ease|-|color-scheme=dark", "1|ease|-|color-scheme=light"]]),
        ("prop13", ["@keyframes prop13 { from { all: initial } to { all: inherit } }"], "", "", "prop13", [["0|ease|-|", "1|ease|-|"]]),
        ("prop15", ["@keyframes prop15 { from { translate: 1px } to { translate: 2px } }"], "", "", "prop15", [["0|ease|-|translate=1px", "1|ease|-|translate=2px"]]),
        ("prop16", ["@keyframes prop16 { from { transform: scale(1) } to { transform: scale(2) } }"], "", "", "prop16", [["0|ease|-|transform=scale(1)", "1|ease|-|transform=scale(2)"]]),
        ("prop17", ["@keyframes prop17 { from { z-index: 1 } to { z-index: 2 } }"], "", "", "prop17", [["0|ease|-|z-index=1", "1|ease|-|z-index=2"]]),
        ("prop18", ["@keyframes prop18 { from { width: 1px } to { width: 2px } }"], "", "", "prop18", [["0|ease|-|width=1px", "1|ease|-|width=2px"]]),
        ("prop19", ["@keyframes prop19 { from { container-type: size } to { container-type: normal } }"], "", "", "prop19", [["0|ease|-|", "1|ease|-|"]]),
        ("prop20", ["@keyframes prop20 { from { animation-composition: add } to { animation-composition: replace } }"], "", "", "prop20", [["0|ease|add|", "1|ease|replace|"]]),
        ("prop21", ["@keyframes prop21 { from { animation-range: 10% 20% } to { animation-range: 30% 40% } }"], "", "", "prop21", [["0|ease|-|", "1|ease|-|"]]),
        ("prop22", ["@keyframes prop22 { from { animation-timeline: auto } to { animation-timeline: none } }"], "", "", "prop22", [["0|ease|-|", "1|ease|-|"]]),
        ("prop23", ["@keyframes prop23 { from { animation-delay: 1s } to { animation-delay: 2s } }"], "", "", "prop23", [["0|ease|-|", "1|ease|-|"]]),
        ("prop24", ["@keyframes prop24 { from { animation: x 1s } to { animation: y 2s } }"], "", "", "prop24", [["0|ease|-|", "1|ease|-|"]]),
        ("prop25", ["@keyframes prop25 { from { transition-behavior: allow-discrete } to { transition-behavior: normal } }"], "", "", "prop25", [["0|ease|-|", "1|ease|-|"]]),
        ("prop26", ["@keyframes prop26 { from { transition-duration: 1s } to { transition-duration: 2s } }"], "", "", "prop26", [["0|ease|-|", "1|ease|-|"]]),
        ("prop27", ["@keyframes prop27 { from { -webkit-animation-name: q } to { -webkit-animation-name: r } }"], "", "", "prop27", [["0|ease|-|", "1|ease|-|"]]),
        ("prop28", ["@keyframes prop28 { from { text-orientation: upright } to { text-orientation: mixed } }"], "", "", "prop28", [["0|ease|-|", "1|ease|-|"]]),
        ("prop29", ["@keyframes prop29 { from { appearance: none } to { appearance: auto } }"], "", "", "prop29", [["0|ease|-|appearance=none", "1|ease|-|appearance=auto"]]),
        ("prop30", ["@keyframes prop30 { from { content-visibility: hidden } to { content-visibility: visible } }"], "", "", "prop30", [["0|ease|-|content-visibility=hidden", "1|ease|-|content-visibility=visible"]]),
        ("prop31", ["@keyframes prop31 { from { page: a } to { page: b } }"], "", "", "prop31", [["0|ease|-|page=a", "1|ease|-|page=b"]]),
        ("prop32", ["@keyframes prop32 { from { quotes: none } to { quotes: auto } }"], "", "", "prop32", [["0|ease|-|quotes=none", "1|ease|-|quotes=auto"]]),
        ("prop33", ["@keyframes prop33 { from { counter-increment: a 1 } to { counter-increment: b 2 } }"], "", "", "prop33", [["0|ease|-|counter-increment=a 1", "1|ease|-|counter-increment=b 2"]]),
        ("prop34", ["@keyframes prop34 { from { font-size: 10px } to { font-size: 20px } }"], "", "", "prop34", [["0|ease|-|font-size=10px", "1|ease|-|font-size=20px"]]),
        ("prop35", ["@keyframes prop35 { from { line-height: 1 } to { line-height: 2 } }"], "", "", "prop35", [["0|ease|-|line-height=1", "1|ease|-|line-height=2"]]),
        ("prop36", ["@keyframes prop36 { from { background-image: none } to { background-image: none } }"], "", "", "prop36", [["0|ease|-|background-image=none", "1|ease|-|background-image=none"]]),
        ("prop37", ["@keyframes prop37 { from { pointer-events: none } to { pointer-events: auto } }"], "", "", "prop37", [["0|ease|-|pointer-events=none", "1|ease|-|pointer-events=auto"]]),
        ("prop38", ["@keyframes prop38 { from { overflow: hidden } to { overflow: visible } }"], "", "", "prop38", [["0|ease|-|overflow-x=hidden~overflow-y=hidden", "1|ease|-|overflow-x=visible~overflow-y=visible"]]),
        ("prop39", ["@keyframes prop39 { from { top: 1px } to { top: 2px } }"], "", "", "prop39", [["0|ease|-|top=1px", "1|ease|-|top=2px"]]),
        ("prop40", ["@keyframes prop40 { from { inset: 1px } to { inset: 2px } }"], "", "", "prop40", [["0|ease|-|bottom=1px~left=1px~right=1px~top=1px", "1|ease|-|bottom=2px~left=2px~right=2px~top=2px"]]),
        ("prop41", ["@keyframes prop41 { from { grid-template-columns: 1fr } to { grid-template-columns: 2fr } }"], "", "", "prop41", [["0|ease|-|grid-template-columns=1fr", "1|ease|-|grid-template-columns=2fr"]]),
        ("prop43", ["@keyframes prop43 { from { filter: blur(1px) } to { filter: blur(2px) } }"], "", "", "prop43", [["0|ease|-|filter=blur(1px)", "1|ease|-|filter=blur(2px)"]]),
        ("prop44", ["@keyframes prop44 { from { -webkit-text-fill-color: red } to { -webkit-text-fill-color: blue } }"], "", "", "prop44", [["0|ease|-|-webkit-text-fill-color=red", "1|ease|-|-webkit-text-fill-color=blue"]]),
        ("prop45", ["@keyframes prop45 { from { view-transition-name: a } to { view-transition-name: b } }"], "", "", "prop45", [["0|ease|-|view-transition-name=a", "1|ease|-|view-transition-name=b"]]),
        ("prop46", ["@keyframes prop46 { from { anchor-name: --a } to { anchor-name: --b } }"], "", "", "prop46", [["0|ease|-|anchor-name=--a", "1|ease|-|anchor-name=--b"]]),
        ("prop47", ["@keyframes prop47 { from { zoom: 1 } to { zoom: 2 } }"], "", "", "prop47", [["0|ease|-|zoom=1", "1|ease|-|zoom=2"]]),
        ("prop48", ["@keyframes prop48 { from { rotate: 10deg } to { rotate: 20deg } }"], "", "", "prop48", [["0|ease|-|rotate=10deg", "1|ease|-|rotate=20deg"]]),
        ("prop49", ["@keyframes prop49 { from { scale: 1 } to { scale: 2 } }"], "", "", "prop49", [["0|ease|-|scale=1", "1|ease|-|scale=2"]]),
        ("prop50", ["@keyframes prop50 { from { outline: 1px solid red } to { outline: 2px solid blue } }"], "", "", "prop50", [["0|ease|-|outline-color=red~outline-style=solid~outline-width=1px", "1|ease|-|outline-color=blue~outline-style=solid~outline-width=2px"]]),
        ("prop52", ["@keyframes prop52 { from { clip-path: none } to { clip-path: circle(10px) } }"], "", "", "prop52", [["0|ease|-|clip-path=none", "1|ease|-|clip-path=circle(10px)"]]),
        ("prop53", ["@keyframes prop53 { from { text-decoration: underline } to { text-decoration: none } }"], "", "", "prop53", [["0|ease|-|text-decoration-color=initial~text-decoration-line=underline~text-decoration-style=initial~text-decoration-thickness=initial", "1|ease|-|text-decoration-color=initial~text-decoration-line=none~text-decoration-style=initial~text-decoration-thickness=initial"]]),
        ("prop54", ["@keyframes prop54 { from { box-shadow: none } to { box-shadow: none } }"], "", "", "prop54", [["0|ease|-|box-shadow=none", "1|ease|-|box-shadow=none"]]),
        ("prop55", ["@keyframes prop55 { from { --reg: 1 } to { --reg: 2 } }"], "", "", "prop55", [["0|ease|-|", "1|ease|-|"]]),
    ];

    public static IEnumerable<object[]> Data() => Enumerable.Range(0, Cases.Length).Select(i => new object[] { i });

    internal static (DocumentStyles Styles, Node Element) Page(string[] styles, string element, string extra, string animationName)
    {
        string html = "<!doctype html><html><head>" + string.Concat(styles.Select(s => "<style>" + s + "</style>"))
            + "</head><body><div id=\"x\" style=\"" + ("animation-name: " + animationName + "; animation-duration: 10s; animation-play-state: paused; " + extra + "; " + element).Replace("&", "&amp;").Replace("\"", "&quot;") + "\"></div></body></html>";
        Document doc = Document.Parse(html);
        return (DocumentStyles.Create(doc), doc.Root.DescendantElements.First(e => e.GetAttribute("id") == "x"));
    }

    internal static string Format(ResolvedKeyframe f) =>
        f.Offset.ToString("R", CultureInfo.InvariantCulture) + "|" + f.TimingFunction + "|" + (f.Composition ?? "-") + (f.IsImplicit ? "*" : "") + "|"
        + string.Join("~", f.Values.Select(x => x.Property + "=" + x.Value + (x.InvalidAtComputedValueTime ? "!" : "")));

    [Theory]
    [MemberData(nameof(Data))]
    public void Matches_Chrome(int index)
    {
        (string id, string[] styles, string element, string extra, string name, string[][] expected) = Cases[index];
        (DocumentStyles ds, Node el) = Page(styles, element, extra, name);
        IReadOnlyList<ResolvedKeyframes?> animations = ds.GetAnimationKeyframes(ds.GetStyle(el));
        string[][] actual = [.. animations.Where(a => a is not null).Select(a => a!.Keyframes.Select(Format).ToArray())];
        Assert.True(expected.Length == actual.Length, id + ": " + actual.Length + " animations");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }
}
