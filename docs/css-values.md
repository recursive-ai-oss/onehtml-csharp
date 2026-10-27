# CSS values, the property table, media queries and `@supports`

Normative behaviour of the value layer of `RecursiveAI.OneHtml.Css`, written so another language can reproduce it. It
sits on the tokens and component values of `css-syntax.md`. The algorithms are those of CSS Values and Units 4,
CSS Color 4 (with the Color 5 relative-color and `color-mix()` parts), CSS Variables 1, Media Queries 5 and CSS
Conditional 3 as of 20 October 2026; this file records what those texts leave open and what is not implemented.

## Property table

- **Source.** `ed/css.json` of the `@webref/css@latest` branch of <https://github.com/w3c/webref>, commit
  `bb927df3d6cc86f1c0beca204844a170a0843a18` (committed 2026-10-22), SHA-256
  `30cb8daa786d6c27a1f74c5d413311c785ee0eec1dd34c65e8a4e6681c86baca`. This is webref's merged extract of every CSS
  specification's property definitions, not the per-spec curated files.
- **Generator.** `tools/generate-css-properties.py <css.json>` writes `src/OneHtml/Css/Generated/PropertyTableData.g.cs`.
  It checks the input against the pinned SHA-256, is deterministic, and records commit and date in the generated file.
  The fetch commands are in the script's header. `tools/generate-css-colors.py` does the same for the named-color
  table of CSS Color 4 (`Generated/NamedColors.g.cs`).
- **Content.** 822 properties, of which 170 are shorthands, 64 are logical-property members (20 logical groups), and 64
  are legacy aliases. Each entry has its name, inheritance (yes, no, "see longhands", unspecified), initial value as
  text, applies-to, percentages, computed-value text, value syntax, alias target, longhands, reset-only longhands
  and logical group. The initial value is parsed to component values on first use.
- **Prose values.** An initial value is "text" only when the source gives a value that is CSS. Otherwise the entry says
  which kind of prose it is and the initial value is not guessed:
  - *see longhands* (98): `all`, `animation`, `background`, `border` and its sides, `color-adjust`, `columns`,
    `container`, `font`, `gap`, `list-style`, `margin-block`, `margin-inline`, `mask`, `offset`, `outline`,
    `padding-block`, `padding-inline`, `text-decoration`, `transition`, the `rule*` and `column-rule*` families, the
    `corner-*-shape` family, the `timeline-trigger*` and `*-timeline` shorthands, `place-items`, `cue`, `pause`, `rest`,
    and the others the generator reports with `PropertyInitialKind.SeeLonghands`;
  - *depends on the user agent* (1): `font-family`;
  - *not defined for shorthand properties* (1): `marker`;
  - *missing in the source* (73): the `-webkit-` aliases of the flexbox, animation, transition, transform, mask, box
    and appearance properties, and `column-width`, `font-stretch`, `grid-column-gap`, `grid-gap`, `grid-row-gap`,
    `stop-color`, `stop-opacity`, `word-wrap`.
  `PropertyTable.All.Where(p => p.InitialKind != PropertyInitialKind.Value)` lists them.
- **Typed properties.** 33 properties have a typed parser (`TypedProperties.IsTyped`): the accessibility set
  (`display`, `visibility`, `content`, `color` and the other color properties, `background-image`, `opacity`, `font-size`,
  `font-weight`, `font-style`, `font-family`, `outline-*`, `clip`, `clip-path`, `position` (the five keywords and `running(<custom-ident>)`, see `cascade.md`) and the offsets,
  `overflow-*`, `speak`, `speak-as`, `pointer-events`). Every other property keeps its value as component values.
- **Licences.** See `THIRD-PARTY-NOTICES.md`.

## CSS-wide keywords and shorthands

- `initial`, `inherit`, `unset`, `revert` and `revert-layer` are recognised only when they are the whole value
  (`CssWideKeywords.Detect`).
- `Shorthands.Expand(name, value)` returns the **leaf** longhands in table order; nested shorthands are flattened and
  reset-only longhands are `initial`. Status is `Expanded`, `Invalid` (the value does not match the grammar),
  `PendingSubstitution` (the value contains `var()` and cannot be split yet), `NotExpanded` (a shorthand without a
  grammar here: keep the value as written) or `NotShorthand`.
- Grammars exist for: shorthands whose value syntax ends in `{1,4}` or `{1,2}` with that many longhands (`margin`,
  `padding`, `inset`, `scroll-margin`, `border-color`, `border-style`, `border-width` and the logical pairs) and
  the two-longhand pair shorthands; shorthands with exactly a `-width`, `-style` and `-color` longhand (`border` and its
  sides, `outline`, `column-rule`); and `text-decoration`, `list-style`, `font`, `background`, `background-position`,
  `transition` and `animation`. `Shorthands.HasGrammar(name)` says which. Every other shorthand is `NotExpanded`.
- Where the table's syntax ends in `{1,4}` or `{1,2}` the expansion uses the CSS rule for omitted sides.

## Numbers, units and math

- A `CssQuantity` is a number, percentage or dimension as written, or the result of a math function. A literal too
  large for a double is held at the largest finite value; only a math function yields infinity.
- Units: absolute lengths, font-relative (`em ex cap ch ic lh rem rex rcap rch ric rlh`), viewport (`vw vh vi vb vmin
  vmax` and the `s`, `l`, `d` variants), container (`cqw cqh cqi cqb cqmin cqmax`), angle, time, frequency, resolution
  and flex units. `LengthContext` supplies the inputs for relative units; the library never measures fonts. When font
  metrics are unknown, `ex`=0.5em, `ch`=0.5em, `cap`=0.7em and `ic`=1em (Values 4 §6.1.1 fallbacks; cap is this
  library's choice). The root-relative units use the root element's own ratios, `RootExRatio`, `RootCapRatio`,
  `RootChRatio` and `RootIcRatio` (Values 4 §6.1.1); a ratio left at 0 means unset, and the element's ratio of the same
  metric stands in for it, so a caller that sets none sees `rex` = `rfs * ExRatio` as before. The cascade leaves them
  unset. Absolute units use the CSS reference pixel.
- Math functions: `calc min max clamp round mod rem sin cos tan asin acos atan atan2 pow sqrt hypot log exp abs sign`.
  Parsing uses an explicit stack, so nesting never recurses; the expression is capped at 65,536 nodes.
  - Type checking follows Values 4 §10.1: sums need the same type; a percentage in a calculation is invalid where the
    property takes no percentage, even when it cancels (`sign(50%)`).
  - Simplification: constants fold; `min`/`max`/`clamp` of constants with the same unit fold, percentages included; a
    sum is sorted number, percentage, then dimensions by unit name (§10.13). A `clamp()` bound written `none` stands
    alone and takes no part in arithmetic.
  - Serialization: a math function that folds to a constant serializes as `calc(<value><unit>)`; NaN and infinity as
    `calc(NaN)`, `calc(infinity)`.
  - **Not implemented:** products of two dimensions (`1px * 1px`) are kept as written rather than simplified,
    `progress()`, `random()`, `random-item()`, `calc-mix()`, `inherit()` and `attr()` in math (Values 5).

## Colors

- Forms: named colors, hex (3, 4, 6, 8 digits), `rgb() rgba() hsl() hsla() hwb() lab() lch() oklab() oklch()`,
  `color()` with the predefined spaces except `display-p3-linear`, `color-mix()`, relative color syntax,
  `light-dark()`, `currentcolor`, `transparent` and the system colors.
- **Legacy serialization** (hex, named, `rgb()`, `hsl()`, `hwb()`, with no `none`): `rgb(r, g, b)` or
  `rgba(r, g, b, a)` with channels rounded half up to integers 0..255 and the alpha stored as 8 bits and written with
  the two-decimal rule of Color 4 §16.1.1 (`#00000088` is `rgba(0, 0, 0, 0.53)`).
  - css-parsing-tests expects unrounded channels and a six-decimal alpha for these (it predates the 8-bit rule).
    The suite runner accepts a difference within the rounding; the current text wins.
- **Missing components.** `rgb()` with `none` serializes as `color(srgb ...)` with `none` kept; `hsl()` and `hwb()` stay
  in their own function with `none` kept (§16.2.2).
- **Modern forms** print their own function with numbers rounded to six decimals, shortest form. Color 4 requires at
  least round-trip precision, so six decimals is allowed; tests that expect six significant digits (`73.3386`) differ.
  Numbers of magnitude 1e15 or more use an exponent.
- Parse-time clamps: `lab`/`lch` lightness to 0..100 and `oklab`/`oklch` to 0..1, chroma at least 0, hue taken modulo
  360, `hsl` saturation and lightness to 0..100, `hwb` whiteness and blackness at least 0 (a sum of 100% or more is
  a gray), NaN from math becomes 0, an infinite hue becomes 0. `rgb()` channels clamp to 0..255.
- `color-mix()` in `hsl` or `hwb` returns sRGB. Hues interpolate with the four methods. Missing components take the
  other operand's value.
- Relative colors: channel keywords are numbers (`rgb` 0..255, `hsl` and `hwb` percentages as numbers 0..100). The
  result of `rgb()`, `hsl()` or `hwb()` relative syntax is `color(srgb ...)` unless a component is missing, when `hsl`
  and `hwb` keep their function. A keyword used alone keeps its component missing.
- **Powerless hues.** Converting into `hsl`, `hwb`, `lch` or `oklch` marks the hue missing when the source chroma is
  at most 0.0015 (lab, lch) or 0.000004 (oklab, oklch), the converted saturation is under 0.001%, or whiteness plus
  blackness is within 0.002% of 100%. An authored color already in the mix space keeps its hue.
- System colors take fixed light-scheme sRGB values (dark with `ColorParseOptions.DarkScheme`) and keep their keyword
  in `SystemName`; `ToCss()` prints the keyword.
- Gamut mapping to sRGB (`ToSrgb`) is the CSS Gamut Mapping algorithm in OKLCH with the JND of 0.02.
- **Not implemented:** `alpha()`, `contrast-color()`, `color-layers()`, `display-p3-linear`, single-operand
  `color-mix()`, `color-mix()` with 0% weights, and the specified-value serialization of `color-mix()`, relative colors
  and math functions inside colors (`ToCss()` returns the resolved color; `lab(calc(0.5) ...)` prints the number). A
  font-relative or container length inside a color calculation does not parse, because the color parser has no length
  context.

## `var()`

- `VarSubstitution.ResolveCustomProperties` computes custom properties (cycles and references to invalid properties
  become guaranteed-invalid and are left out). `Substitute` replaces `var()` in a value. Both are iterative.
- A cycle is a strongly connected component (Tarjan, iterative) of the graph whose edges are the `var()` references
  in a value, fallbacks included, whether or not the fallback would be used.
- A fallback has its leading whitespace dropped. A substituted value gets `/**/` between two pieces only where two
  name-like characters would fuse into one token.
- **Limits**, checked before the work they guard (`VarLimits`): 1,048,576 characters in one substituted value,
  2,097,152 token visits (every token of every expanded fallback counts, so an empty expansion is bounded too), and
  16,777,216 characters over one `ResolveCustomProperties` call. Exceeding one gives `SubstitutionStatus.LimitExceeded`,
  and the declaration is invalid at computed-value time. A billion-laughs input fails in milliseconds.
- `IsValidSyntax` checks `var()` at parse time (Variables 2): the first argument is one or more tokens, or a single
  non-empty `{}` block; a `{}` block cannot be mixed with other tokens there; whatever follows the first top-level
  comma is the fallback and may be empty. Whether the first argument names a custom property is checked at
  substitution, which accepts only a `--name` identifier (Variables 1).

## Media queries

- `MediaQueryList.Parse` follows Media Queries 5: media types, `not`/`only`/`and`/`or`, range syntax (`width >= 400px`,
  `400px < width <= 800px`), `<general-enclosed>` (unknown). `ToCss()` is the canonical text; an invalid query
  serializes and evaluates as `not all`.
- Evaluation is three-valued. A query whose condition is unknown does not match, and `not` of unknown is unknown, so
  `not all and (unknown-feature)` does not match.
- Unclosed blocks, functions and strings are closed at the end of the input, and the canonical text writes those closers
  out so that it parses back to itself.
- Features: `width height device-width device-height aspect-ratio device-aspect-ratio resolution color color-index
  monochrome grid (0 or 1) orientation hover any-hover pointer any-pointer update overflow-block overflow-inline scan
  color-gamut prefers-color-scheme prefers-reduced-motion prefers-reduced-transparency prefers-reduced-data
  prefers-contrast forced-colors scripting display-mode dynamic-range video-dynamic-range inverted-colors nav-controls`
  and `-webkit-device-pixel-ratio`. Other names, and values outside a feature's grammar, are unknown.
- `@container` queries are parsed as at-rules and are not evaluated (they need layout).

## `StyleContext`

`StyleContext` is an immutable record. Today it holds `MediaFeatures`: media type, width, height, device pixel ratio,
`prefers-color-scheme`, `prefers-reduced-motion`, `-transparency`, `-data`, `prefers-contrast`, `forced-colors`,
`hover`/`any-hover`, `pointer`/`any-pointer`, color bits, and `scripting`, which is always `none` because the library
never runs script. The defaults are a 1280x720 light-scheme screen. New members get defaults, so existing callers keep
working when the interaction state (focus, hover, `:target`, `:active`) is added.

## `@supports`

`SupportsCondition` parses `not`, `and`, `or`, parentheses, `(property: value)`, `selector(...)`, and
`<general-enclosed>` (false). `DeclarationSupport.IsSupported(property, value)` is true when:

1. the property is a custom property with a name; or
2. it is in the property table (aliases included) and the value is non-empty and syntactically acceptable (no bad
   strings or URLs, no stray closers, no top-level braces or semicolons), and
   - the value is a CSS-wide keyword or contains `var()`; or
   - the property is typed: its typed parser accepts the value; or
   - the property is a shorthand with a grammar: it expands; or
   - the property's value syntax is only keywords and simple types (`<length>`, `<color>`, ...): the value matches it; or
   - otherwise: nothing more is checked (the value is accepted).

`selector(...)` is decided by `SupportsOptions.SelectorSupported`, which gets the selector text; without a callback the
text only has to look like one complex selector (selector-shaped tokens, no top-level commas). The selectors
component is meant to supply the real check.

## Linked stylesheets

`WhatwgCssEncodingProvider.Instance` implements the CSS Syntax §3.2 byte-to-text steps with the WHATWG decoders: the
protocol label, the `@charset` rule, then the referrer's encoding and UTF-8. The decoder replaces CR and CRLF with LF.

## Deviations the test suites show

The replay of the web-platform-tests files (`testdata/css/wpt-expectations.txt`) and of css-parsing-tests lists every known
failure with its reason. In short: the specified value of `color-mix()`, relative colors and math in colors is not
kept; Values 5 and Color 6 functions are missing; typed arithmetic on dimensions is not simplified; the analogous-
component carry-forward of Color 4 §12.4 between different spaces is incomplete.
