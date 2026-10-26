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

A target is a focusable, enabled, exposed HTML element with a non-presentational role. Sizes come from the browser's layout, so
**without facts every target is needs-render** (missing: *border box of the target*, *border boxes of the targets near it*).
With facts, a target passes when:

- its border box is at least 24 by 24 CSS px;
- it is an inline link in a sentence (exemption "Inline");
- it is an `input`, `select`, `textarea` or `button` no author rule sizes (width, height, min sizes, padding, border widths,
  font size, line height, `appearance`, `zoom` all from the user agent): "User agent control";
- a 24 px circle centred on its box touches no other target's box and no circle of another undersized target ("Spacing").

Otherwise it fails. The exceptions that need a person (an equivalent control elsewhere, an essential size) are not evaluated and
the finding says so. No suggested edit: enlarging a target changes the layout.
