# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- `LiveDocumentStyles.GetPseudoStyle` no longer throws away every cached generated-box style on each state change. A box is kept while the style it inherits from (the element's, the track's for the slider thumb and fill, the highlight style above for `::selection`) and, when a rule for generated boxes reads interaction state, the state are unchanged, and is otherwise rebuilt on its next read from the shared style table. A state change followed by reads of the boxes now allocates nothing in steady state (before, about 800 bytes per pointer move over a range input with `input:hover::-webkit-slider-thumb` on the page). Computed values are unchanged.

## [1.5.0] - 2026-10-27

### Added

- `PseudoElementKind.SliderThumb`, `SliderTrack` and `SliderFill`: `GetPseudoStyle` on `DocumentStyles` and `LiveDocumentStyles` serves the parts of an `input type=range`. Names: `::slider-thumb`, `::slider-track`, `::slider-fill`, `::-webkit-slider-thumb`, `::-webkit-slider-runnable-track`, `::-moz-range-thumb`, `::-moz-range-track`, `::-moz-range-progress`. Other elements get `null`. The thumb and the fill inherit from the track, the track from the input; the user-agent sheet carries the defaults Chrome 156 reports for the thumb and track; the input's `:hover`, `:active`, `:focus`, `:focus-visible` and `:disabled` before the pseudo-element follow the context, form state and live changes. A state pseudo-class on the part itself is not supported. See `docs/cascade.md`.

### Fixed

- A generated box read from `LiveDocumentStyles.GetPseudoStyle` is recomputed after a state change that restyled elements, so it no longer keeps values inherited from the element's style before the change when no rule for the box reads state.

## [1.4.0] - 2026-10-27

### Added

- `FormStateOverrides`, `FormStateChange`, `FormStateKind`, `StyleContext.FormState` / `WithFormState`, `InteractionState.FormState` and `LiveDocumentStyles.ApplyFormState`: checkedness, disabledness and the `indeterminate` state that a user or script changed, for any number of elements, on both the snapshot path (`DocumentStyles.Create` / `WithContext`) and the live path. `:checked` (and an option's selectedness), `:disabled`, `:enabled`, `:indeterminate`, `:read-only` and `:read-write` read them, with what follows from them (a disabled `fieldset` or `optgroup`, a named radio group). The live path restyles in place with no allocation in steady state and fills `ChangedElementIndices` as for interaction state. Without overrides every value is as before. See `docs/cascade.md`.
- `SelectorDependencies.ScriptFormState`: set on selectors that read form state a script can change.
- `PseudoElementKind.Selection` and `PseudoElementKind.Placeholder`: `GetPseudoStyle` on `DocumentStyles` and `LiveDocumentStyles` now serves `::selection` (highlight properties only, with highlight inheritance from the parent element) and `::placeholder` (text controls with a `placeholder`, with Chrome's user-agent defaults; `::-webkit-input-placeholder` is the same). `RestyleResult.PseudoElementsMayDiffer` covers both. See `docs/cascade.md`.
- `@keyframes` rules in the cascade. `DocumentStyles` and `LiveDocumentStyles` gain `GetKeyframesRule(name)` and `KeyframesRules` (the winning `KeyframesRule` per name, with its blocks, origin and layer), `ResolveKeyframes(style, name, animationIndex)` and `GetAnimationKeyframes(style)` (the keyframes of an element's animations with `var()` substituted from its custom properties, flow-relative properties mapped to physical ones, same-offset blocks merged and ordered and the element's own values added at 0% and 100%, as Chrome's `getKeyframes()` reports them; computed once per style, allocation-free to read again). `RestyleResult.ChangedKeyframes` names the rules a sheet or `style` edit changed. The library runs no clock and interpolates nothing. New types: `KeyframesRule`, `KeyframeBlock`, `KeyframeDeclaration`, `ResolvedKeyframes`, `ResolvedKeyframe`, `KeyframeValue`. See `docs/cascade.md`.
- `position: running(<custom-ident>)` (CSS GCPM 3) is kept as a typed value: `PositionValue.Running` and `ComputedStyle.RunningName`. The accessibility tree and the checker treat a running element as Chrome renders it, in flow.

### Changed

- Rules with `:checked`, `:disabled`, `:enabled`, `:indeterminate`, `:read-only` or `:read-write` are matched again for each element's cascade instead of being cached once per document, so they can follow form state. Their values are unchanged.
- `transition` and `animation` follow Chrome 156: a CSS-wide keyword as an item of a list invalidates the declaration (`transition: initial 1s`, `animation-name: x, inherit`); a negative time is the delay, not the duration (`animation: x -1s`); `default` is not a name; `auto` is the `animation` duration; `none` is valid in `transition` only for a single layer; `steps()`, `cubic-bezier()` and `linear()` arguments are checked. The list longhands of both are checked item by item, so a bad item drops the declaration and an earlier one stands.
- A declaration `position: running(name)` that used to be dropped now wins the cascade (`PositionValue` gains a last member, `Running`).

### Fixed

- A cascade that rolls declarations back (`revert`, `revert-layer`, `revert-rule`) reuses its working lists instead of allocating them for every element, so live restyles on such pages stay allocation-free.

## [1.3.0] - 2026-10-27

### Added

- `LiveDocumentStyles`, `ElementStateChange`: interaction state that moves in place for a renderer that changes hover, active, focus (with its source) or `:target` many times a second. `LiveDocumentStyles.Create(DocumentStyles)` owns its buffers and leaves the snapshot and its views untouched; `ApplyState` restyles only what the change reaches and allocates nothing in steady state; `ChangedElementIndices` and `ChangedElementsInto(Span<int>)` list the elements whose style changed; `GetStyle(int)` and `GetValue(elementIndex, propertyId)` read by element index. The result equals `DocumentStyles.Create` for the equivalent `StyleContext`. See `docs/cascade.md`.
- `PropertyTable.IdOf` and `ComputedStyle.GetValue(int)`: read a property by id without a name lookup.

### Changed

- `Node.HasAttribute(string)` no longer allocates the attribute's value.

## [1.2.0] - 2026-10-27

### Added

- `LengthContext.RootExRatio`, `RootCapRatio`, `RootChRatio` and `RootIcRatio`: the root element's font metrics for `rex`, `rcap`, `rch` and `ric` (CSS Values 4 section 6.1.1). 0 means unset and the element's ratio stands in, so existing callers see no change.
- `border-block` and `border-inline` expand to their start and end longhands, in every writing mode and direction, with `var()`, CSS-wide keywords and `!important`. Before, they set nothing.
- `white-space` and `text-wrap` expand to `white-space-collapse`, `text-wrap-mode` and `text-wrap-style`, and `GetPropertyValue`/`GetComputedValue` serialize the shorthands from them, as Chrome does.
- `RenderedFactsBuilder` (with `BoxInput`, `GeneratedBoxInput`, `RenderedFactsLimits` and `RenderedFactsLimitException`) in `RecursiveAI.OneHtml.Accessibility.Rendered`: builds `RenderedFacts` directly from per-node layout data keyed by a `Document`'s nodes, with the adapter's validation, per-count limits and no input-size cap. For a caller that already has boxes, paint order, text boxes and samples in memory and would otherwise serialize a `DOMSnapshot` only to parse it back. The `DOMSnapshot` path is unchanged. See `docs/rendered-facts.md`.

### Changed

- `white-space` and `text-wrap` inherit (through their longhands), as CSS Text 4 defines and Chrome does. A child of `white-space: pre` reads `pre` instead of `normal`; `white-space: nowrap` now sets `text-wrap-mode: nowrap`, and `white-space: pre` sets `white-space-collapse: preserve`. Across a 336-page corpus sample, 12,234 of 272,922 elements read differently for these properties. The accessibility tree and the checker's results are unchanged on the same pages.

### Fixed

- `rex`, `rcap`, `rch` and `ric` resolved against the element's font metrics instead of the root's when a caller supplied different ones; see `LengthContext.RootExRatio`.

## [1.1.1] - 2026-10-27

### Added

- `TokenRange.ToList()`: the same tokens as a `ComponentValueList`, with nothing copied.

### Fixed

- A shorthand whose value holds `var()` set nothing when it expanded to flow-relative longhands. `padding-inline: calc(var(--spacing) * 4)` (what Tailwind CSS 4 emits for `px-`, `py-` and `mx-`), `padding-block`, `margin-inline`, `margin-block`, `inset-inline`, `inset-block`, `scroll-padding-*`, `scroll-margin-*`, `border-inline-width`, `border-block-style`, `border-inline-start` and the other logical pairs now give the same values as Chrome, in every writing mode and direction. The substituted longhand was looked up by the physical property it maps to instead of the one the declaration set.
- The same lookup dropped a longhand under an alias name: `font: var(--x)` whose value holds a width keyword (`font-stretch`, now `font-width`) lost it.
- A CSS-wide keyword on a shorthand that has no expansion grammar was applied to its longhands only, so the shorthand itself read as its initial value: `vertical-align: inherit` (the user-agent rule for `tr`, `td` and `th`, so every table cell read `baseline` instead of its row's value), `text-align: initial`, `white-space: inherit`, `font-variant: initial`, `border-radius: inherit`, `place-items: inherit` and similar. `all` now sets those shorthands too.

## [1.1.0] - 2026-10-27

### Added

- `StyleSourceOptions.DeclarationValidator`: a caller-supplied grammar check consulted when author and user declarations enter the cascade, for properties the library does not type. A rejected declaration is dropped like one that failed to parse, so an earlier declaration stands (`width: 100%; width: -webkit-fill-available`). Without a validator nothing changes. See `docs/cascade.md`.
- `TokenRange` is public: the token cursor the library's value parsers use, now usable for a validator or any property grammar, with `TokenRange.Parse(text)` to make one from text.

## [1.0.0] - 2026-10-26

First release. All three packages share one version.

### Added

- `RecursiveAI.OneHtml`: the WHATWG HTML parser (encoding sniffing, tokenizer, every insertion mode, foreign content, templates, fragment parsing), passing the html5lib tokenizer and tree-construction tests; the DOM as immutable arena snapshots with a byte-level source map; Selectors Level 4 and unique locator generation; CSS Syntax Level 3 parsing of `<style>`, `style=""` and caller-supplied stylesheets; the cascade for every known property, with typed computed values for the accessibility-relevant set, under a `StyleContext` (viewport, media features, focus, hover, target, active) with incremental restyle; lossless editing of HTML, inline styles, `<style>` and linked stylesheets, where untouched bytes stay identical and every result is re-parsed and verified; and a versioned JSON edit format with replay that resolves, goes stale or reports ambiguity, but never picks the wrong element. See `docs/edit-format.md`.
- `RecursiveAI.OneHtml.Accessibility`: the accessibility tree (WAI-ARIA 1.2, HTML-AAM, AccName 1.2, Core-AAM, SVG-AAM) with each role, name and hidden state traced to its source, Chromium's choices listed in `docs/accessibility-chromium-profile.md`, incremental recompute after edits, a reading view, navigation lists, tree and reading-view diffs, an export in Chrome's AX-tree shape, and `RenderedFacts` from a Chrome `DOMSnapshot` supplied by the caller.
- `RecursiveAI.OneHtml.Accessibility.Validation`: one rule catalogue covering axe-core's WCAG and best-practice rules (the two with no counterpart are explained in `docs/axe-differential.md`) and every W3C-approved ACT rule, labelled by WCAG version, level and criterion; profiles for WCAG 2.0 AA, 2.1 AA, 2.2 AA and everything; contrast that never guesses a background; incremental re-check after an edit; suggested edits for the rules that have exactly one correct fix; native JSON, axe-compatible JSON and SARIF 2.1.0 output. Coverage per criterion is in `docs/coverage.md`.

[1.5.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.5.0
[1.4.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.4.0
[1.3.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.3.0
[1.2.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.2.0
[1.1.1]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.1.1
[1.1.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.1.0
[1.0.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.0.0
