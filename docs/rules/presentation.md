# Visual presentation rules

Rules: `avoid-inline-spacing` (WCAG 1.4.12, ACT 24afc2 / 78fd32 / 9e45ec), `css-orientation-lock` (WCAG 1.3.4, ACT b33eff)
and `target-size` (WCAG 2.5.8, 2.2 AA). Ids, tags and impacts follow axe-core; descriptions are our own.

## avoid-inline-spacing

Applies to an HTML element that is drawn and whose `style` attribute declares `letter-spacing`, `line-height` or
`word-spacing`. Per property, it passes when any of:

- not important: the attribute's winning declaration (the last important one, else the last) is not `!important`;
- cascade: another rule's value won (the cascade source of the value is not the style attribute);
- the value is `inherit` or `unset`, which leaves the value to the parent;
- wide enough: letter spacing at least 0.12, word spacing at least 0.16, line height at least 1.5 times the computed font size.
  `normal` and `initial` count as zero spacing and fail. A value this reader cannot turn into a length (`calc()`, `var()`) is needs-review.

One result per element; the finding names each failing property.

**Line height applies only to text that wraps** (ACT 78fd32). With facts, the text boxes of the element's text must show two lines.
Without them a failing line height is needs-render (missing: *text boxes of the element ...*), and a property that fits or is
not important is still judged; `white-space: nowrap` skips the check.

**Suggested edit (fix decision).** When a property fails, is declared exactly once in the attribute, and is important, the
rule suggests `setStyle` with the same value and `important: false`. It is the one change that makes the declaration
adjustable by a reader's style sheet without changing the page's rendering (a normal inline declaration still beats the
page's normal rules). The edit never changes the value. When the property is declared twice no edit is offered, because
dropping `!important` from the last copy would let the first one win. Every suggestion is applied, replayed and re-checked in the tests.

## css-orientation-lock

Applies only when an author sheet mentions `orientation`. The page is styled twice, in a 1280x800 and an 800x1280 viewport, and
the rotation about the z axis is compared for every drawn HTML element whose transform contains a rotation function
(`rotate`, `rotateZ`, `rotate3d` about z, `matrix`, `matrix3d`) or that sets the `rotate` property. A difference of 90 degrees
modulo 180, within one degree, fails; another difference passes; elements whose transform is the same in both orientations
are not applicable (ACT b33eff). No suggested edit: dropping the rotation or reworking the layout is a design decision.
Limitation: a rotation that depends on a width query rather than the orientation is compared as well.

## target-size

A target is a focusable, enabled HTML element with a widget role, or a `details > summary`. Controls hidden from the accessibility tree
(`aria-hidden`, inert) still sit on the page: they take up space as neighbours but get no verdict. Sizes come from the browser's layout, so
**without facts every target is needs-render** (missing: *border box of the target*, *border boxes of the targets near it*).
With facts, the target's own border box (descendants do not enlarge it), cut down by every ancestor that clips with `overflow: hidden|clip`
(not `auto` or `scroll`, and not the root or body, which scroll the viewport), is judged in this order:

1. at least 24 by 24 CSS px: passed;
2. an inline link in a sentence (exemption "Inline"): passed;
3. an `input`, `select`, `textarea` or `button` no author rule sizes (width, height, min sizes, padding, border widths, font size,
   line height, `appearance`, `zoom` all from the user agent): passed, "User agent control";
4. another element covers it completely, so it cannot be pressed: passed, and it takes no space from its neighbours (below);
5. a 24 px circle centred on its box touches no other target's box and no circle of another undersized target: passed ("Spacing"). A
   target whose destination is the same as its neighbour's (equal resolved `href`, other than `#` or `javascript:`) is one target;
6. otherwise, content of the target that sticks out of its box (the area a pointer hits is not the box), or a negative `tabindex` on the target or
   the neighbour: needs-review;
7. otherwise failed.

The exceptions that need a person (an equivalent control elsewhere, an essential size) are not evaluated and the finding says so. No
suggested edit: enlarging a target changes the layout.

**Covered completely** means one element, other than an ancestor or descendant, with pointer events and visible, has a box that spans the
target's box (within half a pixel) and either paints later (paint order from the facts), or is later in tree order in the same stacking
context while neither it nor the target sits in a positioned, floated or transformed layer: the browser hits what comes later in the tree
first among ordinary content, whatever it paints. Shapes inside an `svg` do not count (their boxes are in the drawing's own units); the
`svg` itself does.

### Measured against axe-core 4.14.0

1,000-page sample, with facts: 96.2 % of nodes agree (92.2 % before this work; 7,751 mismatching nodes, 3,541 now).

| Class (ours\|axe) | Nodes | Disposition |
|---|---:|---|
| pass\|violation "user agent control" | 1,739 | Kept. WCAG 2.5.8 exempts a control whose size the browser sets and the page leaves alone; axe measures it. |
| pass\|violation | 738 | Open. Mostly targets axe calls *partially obscured*: it measures the part of the box other elements leave open ("smallest space is 89.5px by 22.4px"), and overlapping boxes of neighbouring links are the usual cause. Reproducing it as a rectangle subtraction over every later-painted box made 123 more nodes wrong (inline boxes that wrap lines have a union box that is not hit area; wide link boxes in tag clouds), so the border box stays the size. Also 'insufficient size' where our circle test is clear. |
| fail\|pass | 287 | Open. Down from 628: the open mobile menus under the page body are now fully covered (tree order). What is left: spacing where axe counts large neighbours differently, and a stacking-context wrapper (page 0ee5e7fde7d3) we call a cover and axe does not. |
| review\|none | 376 | Kept. Targets with negative `tabindex` or overflowing content: axe does not evaluate them or passes without a rule. |
| none\|violation | 169 | Open. Partially obscured targets and 13 px radios and summaries that we do not select (a summary inside `details` closed to the page, radios without a widget role in our tree). |
| fail\|none | 69 | Open. |
| fail\|incomplete | 15 | Kept. axe reports "content overflow" only after size and spacing both fail; we agree on the order and differ on what overflows. |

Measured axe behaviour, from 1,000 pages and probes (`testdata/axe/probes/target-size-1..6.html`): the target is the element's own border box; the
order is size, in-sentence, user-agent control, fully covered, spacing, then content overflow (incomplete); `summary` is a target; `aria-hidden`
controls are neighbours without a verdict; the same href is exempt except `#` and `javascript:`.
