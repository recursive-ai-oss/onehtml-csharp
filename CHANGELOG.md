# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[1.1.1]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.1.1
[1.1.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.1.0
[1.0.0]: https://github.com/recursive-ai-oss/onehtml-csharp/releases/tag/v1.0.0
