# Contrast rules

Rules: `color-contrast` (WCAG 1.4.3, AA), `color-contrast-enhanced` (WCAG 1.4.6, AAA), `link-in-text-block` (WCAG 1.4.1).
Sources: axe-core rule ids, tags and impacts; the W3C ACT rules *Text has minimum contrast* (afw4f7) and *Text has enhanced
contrast* (09o5cg). Descriptions are our own. None of the three has a suggested edit: a colour is a design choice and many
pairs would pass.

## The arithmetic

- Relative luminance and contrast ratio are the WCAG 2.2 definitions, computed on 8-bit sRGB channels
  (linearisation threshold 0.04045, as in CSS Color 4). Colours of other spaces are converted to sRGB by the cascade's colour
  type first. Composites are rounded to 8 bits per channel, as a display does.
- The ratio is never rounded: 4.4775 does not meet 4.5. Messages truncate to two decimals, so "4.50" is only written for a
  ratio of at least 4.5.
- Large text is at least 18 pt (24 px), or at least 14 pt (18.67 px) with a computed `font-weight` of 700 or more. Thresholds:
  normal 4.5:1 and large 3:1 for AA; 7:1 and 4.5:1 for AAA.

## What is judged

Text of an HTML element that is drawn (it has a tree node, is not `display: none`, `hidden`, `visibility: hidden`, fully
transparent, or moved far off the page: a positioned element 1000 px or more above or left of the origin, a clipped
one-pixel box, a `text-indent` of -1000 px or less). The target of a result is the element whose child text it is; one
result covers all of its direct text. Generated `::before` and `::after` text (string content only) is judged under the
pseudo-element's own style and reported on its host element, with a finding that says which.

Not judged (no result):

- text inside a disabled widget, a label of a disabled widget (`label` or `aria-labelledby`), or a disabled group
  (`fieldset disabled`, `aria-disabled` on a group), the "incidental" exception of 1.4.3;
- text whose colour is exactly the colour behind it (ACT afw4f7 assumptions: not a presentation of text);
- text of SVG and MathML elements.

Passed without a ratio: text with no letter or digit (rows of symbols), which expresses nothing in human language (ACT afw4f7).

Text inside widgets (buttons, links, inputs) is judged here. The ACT rules leave it to other rules, but SC 1.4.3 covers it.

## Where the colours come from

The foreground is the computed `color`, composited with the product of the `opacity` of the element and its ancestors.
The background is built from the root down: the canvas (white; the canvas colour of a dark scheme when the page opts in and the
context prefers dark), then for each element on the path its `background-color`, then its background images, each composited
with that element's opacity chain. The result is a set of possible backdrop colours:

- a solid chain gives one colour;
- a gradient gives its stop colours (a stop that is not opaque lets the layer below show). Per ACT, "the highest possible
  contrast" counts: the text passes if the best of the colours passes. Interpolated colours between stops are not
  sampled;
- the colours of a `text-shadow` are added as further backgrounds, because ACT afw4f7 counts them (Passed Example 4). WCAG
  itself is silent; axe ignores shadows. This is a deliberate difference from axe;
- an image (`url()`, `image-set()`, a gradient the reader cannot parse), `background-clip: text`, or a positioned
  (`absolute`/`fixed`) generated box that paints a background makes the backdrop **unknown**.

Hazards: an element that is `position: absolute|fixed`, transformed, filtered, or has a blend mode may have other content
behind it. An opaque background of its own, or a gradient with only opaque stops, clears the hazard for its subtree.

## Outcomes

| Situation | With no facts | With facts |
|---|---|---|
| Known backdrop colour(s) | passed, or failed with the ratio | same |
| Hazard (out of flow, transformed, ...) | needs-render, missing: *blended background colors of the element*, *paint order and stacking of the element* | the browser's blended background colour of the element replaces the chain; `FactsUsed` has *blended background color* |
| Unknown backdrop (image, gradient not read, generated panel) | needs-render, missing: *background samples (median and 5th/95th percentile colours) behind the text* | the nearest element at or above the text with a supplied sample (see `rendered-facts.md`): the median, 5th and 95th percentile colours are the backdrops. All pass: passed. All fail: failed. Mixed: needs-review. `FactsUsed` has *background samples* |
| Facts say the text was not laid out, or lies wholly above/left of the page | no result | no result |

Two Chrome behaviours seen in the captures: the blended background colour of an element ignores a positioned
pseudo-element behind it and reports only the first colour of a gradient. The rule therefore never trusts it for those two
cases and asks for samples instead. With a gradient anywhere on the path and a hazard, the result also asks for samples.

The checker never guesses a colour: a missing background is a needs-render result naming exactly what to capture.

## Form controls

A `button`, `input`, `select` or `textarea` whose `color` no declaration of its own sets takes the browser's control colour (`ButtonText`
or `FieldText`, black under the default colour scheme), not its parent's: the rendering section of the HTML Standard does not give controls
a colour, but every browser's own sheet does, and Chrome's output on a white-on-teal cookie button confirms it (black, 7.16:1).

## link-in-text-block

A link (`a[href]`, inline) in a block that also holds other visible text (the first such run of text, outside links and
nested blocks, is the reference). Passed when one of these tells the link apart:

1. a text-decoration line (underline, overline, line-through) on the link or an inline ancestor, while the reference text has none;
2. a border on the link that the reference has not;
3. a font weight differing by 100 or more, or a different font style;
4. a link colour with at least 3:1 contrast against the reference text colour (both composited over the backdrop).

Otherwise failed. The extra cue on hover and focus that technique G183 asks for with route 4 is state-dependent and is not
checked. Links alone in a list item or other block are not applicable. Difference from axe: axe also asks the link text for
contrast with its background; that is `color-contrast`.

## ACT results and known gaps

See `testdata/act/visual-expectations.tsv`. Cases with text over an image or created in a shadow root by a script are not
decidable here (no facts; scripts are inert).

Not implemented: text in `input` values, placeholders and `select` options; text in SVG; images of text; interpolation between gradient
stops; the stacking order of positioned siblings (only the browser's blended colour speaks to it).
