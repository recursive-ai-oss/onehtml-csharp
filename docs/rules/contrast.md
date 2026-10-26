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

### Content that is not an ancestor

The walk above sees only the element and its ancestors. Something else can paint behind text: an image, a video or a canvas pulled
under it by a negative margin or a position, a positioned box with its own background, a background image on an overlay. The checker
never assumes the page colour for such text, so it asks what else may be there (`BackdropIndex`):

- **Painters that count.** Replaced content (`img`, `video`, `canvas`, `iframe`, `object`, `embed`, `svg`, `input type=image`) and any
  element with a `background-image` (not `background-clip: text`) are *image-like*: only pixels know what shows. A positioned or
  transformed element with a visible `background-color` is *solid*: it may repaint the colour behind the text. Boxes smaller than 3 px
  (spacer pixels, hairlines), hidden or fully transparent boxes and boxes clipped away by an ancestor do not count. Ancestors of the
  text never count (the walk already has them).
- **With rendered facts**, geometry decides: a painter counts when its clipped box meets one of the text's boxes (by more than half a
  pixel on each axis; the whole box for input labels and generated content) and it paints before the text (paint order; a missing
  order counts as before). An image-like painter makes the backdrop **unknown**: the result needs background samples or is
  needs-render. A solid painter sets the hazard: the browser's blended colour is used when facts have it, otherwise needs-render.
- **Without facts** there is no geometry, so every painter that *may* reach the text counts. An `absolute` painter reaches everything
  inside its containing block (the nearest positioned, transformed or filtered ancestor, else the whole page); a `fixed` one reaches the
  page; a transformed painter, and an image-like painter pulled by a negative margin or a relative or sticky offset, reach the subtree
  of its parent. The result is needs-render, naming the same missing facts.
- Not covered even with facts: an ordinary in-flow sibling never overlaps text, and a solid painter that is not positioned is not
  looked for; floats do not overlap text.

On the 1,000-page sample this moved 309 more `color-contrast` nodes with facts to needs-render and removed 20 `fail|pass` nodes (page
06fe9c4a687c: near-white text on a dark image the cascade could not see, judged against white before).

## Outcomes

| Situation | With no facts | With facts |
|---|---|---|
| Known backdrop colour(s) | passed, or failed with the ratio | same |
| Hazard (out of flow, transformed, a positioned box with a background that may overlap, ...) | needs-render, missing: *blended background colors of the element*, *paint order and stacking of the element* | the browser's blended background colour of the element replaces the chain; `FactsUsed` has *blended background color* |
| Unknown backdrop (image, gradient not read, generated panel, or an image-like painter that may overlap the text) | needs-render, missing: *background samples (median and 5th/95th percentile colours) behind the text* | the nearest element at or above the text with a supplied sample (see `rendered-facts.md`): the median, 5th and 95th percentile colours are the backdrops. All pass: passed. All fail: failed. Mixed: needs-review. `FactsUsed` has *background samples* |
| Facts say the text was not laid out, or lies wholly above/left of the page | no result | no result |

Two Chrome behaviours seen in the captures: the blended background colour of an element ignores a positioned
pseudo-element behind it and reports only the first colour of a gradient. The rule therefore never trusts it for those two
cases and asks for samples instead. With a gradient anywhere on the path and a hazard, the result also asks for samples.

The checker never guesses a colour: a missing background is a needs-render result naming exactly what to capture.

## Form controls

A `button`, `input`, `select` or `textarea` whose `color` no declaration of its own sets takes the browser's control colour (`ButtonText`
or `FieldText`, black under the default colour scheme), not its parent's: the rendering section of the HTML Standard does not give controls
a colour, but every browser's own sheet does, and Chrome's output on a white-on-teal cookie button confirms it (black, 7.16:1).

## Measured against axe-core 4.14.0

1,000-page sample, with facts (nodes; `docs/axe-differential.md` has the method). `color-contrast` agrees on 92.4 % of compared nodes,
`color-contrast-enhanced` on 92.5 %; the rest is almost all kept divergences:

| Class (ours\|axe) | Nodes | Disposition |
|---|---:|---|
| pass\|incomplete | 9,849 (enhanced 9,328) | Kept. Text made only of symbols (icon-font glyphs): axe says "only non-text characters" and is incomplete; ACT afw4f7 does not apply to text without letters or digits, and ACT wins. |
| none\|incomplete | 1,871 | Kept. Empty fields, hidden text, 1:1 colour pairs and elements axe thinks are overlapped; there is nothing to read. |
| fail\|incomplete | 850 | Kept. axe cannot decide because another element overlaps; with facts we know the stacking and the browser's blended colour. |
| fail\|none | 294 | Open. Text axe does not examine (mostly text it treats as hidden or clipped by layout it computes differently). |
| pass\|violation | 34 | Kept. axe checks text made of punctuation only; ACT exempts it. |
| fail\|pass | 20 (was 34; enhanced 168) | The `body link` attribute caused 14 (page 0ebed22c12e5: `link="#840000"` coloured the links, the cascade used the user-agent blue); fixed in the cascade. The rest is open (17 on page 1372fac512b6). |

The near-white-text-on-an-image class (54 nodes on page 06fe9c4a687c) is fixed by the non-ancestor painters above. Text-shadow colours count
as backdrops (ACT afw4f7 Passed Example 4); axe ignores shadows.

## link-in-text-block

A link (`a[href]`, inline) in a block that also holds other visible text (the first such run of text, outside links and
nested blocks, is the reference). Passed when one of these tells the link apart:

1. a text-decoration line (underline, overline, line-through) on the link or an inline ancestor, while the reference text has none;
2. a border on the link that the reference has not;
3. a font weight differing by 100 or more, or a different font style;
4. a link colour with at least 3:1 contrast against the reference text colour (both composited over the backdrop).

5. a background image on the link that the reference has not (an underline drawn as a gradient, a highlight).

Otherwise failed. The extra cue on hover and focus that technique G183 asks for with route 4 is state-dependent and is not
checked. Links alone in a list item or other block are not applicable. Difference from axe: axe also asks the link text for
contrast with its background; that is `color-contrast`.

### Measured against axe-core 4.14.0

1,000-page sample, with facts: 90.6 % of nodes agree (10,663 mismatching nodes before the rule followed axe, 250 now).

| Situation | Ours | axe-core |
|---|---|---|
| A "block of text" | The nearest block container whose text outside links is longer than the text of its links (white space collapsed); script text is not counted. | The same on 100+ probe links, except that `<script>` text counts as text (probe `s17`). |
| Link colour equal to the text colour | Fails: the colour contrast is 1:1 and nothing else sets the link apart (F73). | Passes (probes `s24`, `r24`; 66 sample nodes `fail|pass`). Divergence kept: F73 names exactly this failure. |
| Link wrapped in a block of other content | Skipped when the link has no text of its own. | `fail|none` 60 nodes on 32 pages (links with large boxes, such as image or card links): axe does not select them; the criterion for choosing them was not pinned down. Open. |
| Another element overlaps the link | Judged from the cascade. | Incomplete ("element overlap", 79 nodes `fail|incomplete`). Kept: our verdict does not depend on overlap. |

## ACT results and known gaps

See `testdata/act/visual-expectations.tsv`. Cases with text over an image or created in a shadow root by a script are not
decidable here (no facts; scripts are inert).

Not implemented: text in `input` values, placeholders and `select` options; text in SVG; images of text; interpolation between gradient
stops; the stacking order of positioned siblings (only the browser's blended colour speaks to it).
