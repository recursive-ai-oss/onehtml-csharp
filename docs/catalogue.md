# Rule catalogue data

The Validation package ships reference data about the standards its rules check against, and a few types to hang rule
implementations on. This page lists the data, where each file comes from, and how a rule is added.
The criterion-by-rule table is in [coverage.md](coverage.md).

## Data files

All files are JSON in `src/OneHtml.Accessibility.Validation/Catalogue/Data/`, embedded in the assembly, with a top-level
`version` (currently `1`) and a `source` block (URL, SHA-256 of the input, retrieval date, notes). They are generated
by `tools/catalogue/generate.py` from inputs that `tools/catalogue/sources.py` downloads and hash-checks; running the
generator twice gives identical bytes. The library reads them through a small `System.Text.Json` document reader: no
reflection, trim- and AOT-safe. The library does not fetch anything.

| File | Contents | Source and pin | Count |
|---|---|---|---|
| `wcag-criteria.json` | number, anchor id, published short name, level, version introduced, version removed | W3C `https://www.w3.org/WAI/WCAG22/wcag.json`, SHA-256 `3a034865…cf75`, retrieved 2026-10-25 | 87 criteria |
| `act-rules.json` | id, name, status, type, WCAG criteria, techniques, other requirements, input aspects, input rules | `w3c/wcag-act-rules` `wcag-mapping.json` at commit `a7a18b1d…`, SHA-256 `5d111710…c4f5` | 94 rules |
| `axe-rules.json` | rule id, section, impact, tags, result kinds, linked ACT rules | axe-core 4.14.0 published `doc/rule-descriptions.md`, SHA-256 `635ad70e…f261` | 105 rules |

### WCAG criteria

- WCAG 2.0 has 61 criteria (25 A, 13 AA, 23 AAA), 2.1 has 78 (30, 20, 28) and 2.2 has 86 (31, 24, 31).
  The table has 87 rows because 4.1.1 Parsing is in 2.0 and 2.1 and was removed in 2.2 (`removedIn` is `2.2`).
- The published data leaves the level of 4.1.1 empty and adds a note to its name. The generator sets level A and the
  name `Parsing`, as the 2.0 and 2.1 Recommendations define it.
- A criterion belongs to a version when `introducedIn <= version < removedIn`.

### ACT rules

- Status is `deprecated` when the W3C list flags the rule deprecated, `proposed` when it flags it proposed, otherwise
  `approved`. Today: 37 approved, 50 proposed, 7 deprecated.
- `criteria` are the WCAG criteria in the rule's requirements (the `wcag20:`, `wcag21:` and `wcag22:` keys).
  `techniques` are the `wcag-technique:` keys; `otherRequirements` keeps the rest (for example `aria12:…`) as published.
- `inputAspects` are normalised to DOM Tree, CSS Styling, Accessibility Tree, Language, Audio output, Visual output and
  Source code. Composite rules list `inputRules` instead of aspects.
- The ACT test cases are in `testdata/act/`: the index `testcases.json` (1134 cases for 91 rules) and the case files. [act-coverage.md](act-coverage.md) lists every ACT rule with the rule that implements it, or the reason none does, and how its cases came out.
  Their SHA-256 values are in `testdata/SHA256SUMS`, and a test checks them. `ActTestCases.Parse` reads the index text
  the caller supplies. Media and other assets that cases refer to are not copied (see `testdata/MANIFEST.md`).

### axe-core identifiers

- Only facts are taken from the published table: identifiers, tags, impact, result kinds and links to ACT rules. No
  description or help text is copied, and none is stored; add our own wording when a rule is implemented.
- Sections: 60 WCAG 2.0 A/AA, 3 WCAG 2.1 A/AA, 1 WCAG 2.2 A/AA, 3 WCAG 2.x AAA, 27 best practice, 6 experimental and
  5 deprecated rules.
- A tag `wcagNNN` names a criterion: principle and guideline are one digit each, so `wcag1410` is 1.4.10 and `wcag143`
  is 1.4.3. Level tags (`wcag2a`, `wcag21aa`, …) name a version and level. `AxeRule.CriterionNumbers` and
  `AxeRule.LevelTags` do both.

## Types

In `RecursiveAI.OneHtml.Accessibility.Validation.Catalogue`:

- `SuccessCriterion`, `WcagVersion`, `WcagLevel`, `WcagCriteria` (the table, lookups by number).
- `ActRule`, `ActRuleStatus`, `ActRules`, `ActTestCase`, `ActTestCases`.
- `AxeRule`, `AxeRules`.
- `RuleSource` (ACT id, axe id, failure technique or ours), `RuleNeeds` (`Static` or `RenderedFacts`) and
  `RuleDescriptor` (id, sources, criteria, best-practice flag, needs, fix or the reason there is none, tags).
- `ValidationProfile`: `Wcag20AA`, `Wcag21AA`, `Wcag22AA` and `All`.
- `CoverageMatrix.ToMarkdown()`, which writes `docs/coverage.md`.

### Profiles

A profile for version V and level L includes the criteria that are in V (introduced at or before V and not removed by
it) and have a level at or below L. So 2.1 AA includes the 2.0 criteria that 2.1 kept, and 2.2 AA leaves out 4.1.1.
The names cite standards (Section 508 cites WCAG 2.0 AA; ADA Title II and EN 301 549 cite WCAG 2.1 AA); a profile is a
selection of criteria, not a claim about legal compliance. `All` includes every criterion, the removed one too, and
every best practice. A rule is in a profile when any criterion it checks is; a rule with no criteria is in `All` only.

| Profile | Criteria |
|---|---|
| `wcag-2.0-aa` | 38 |
| `wcag-2.1-aa` | 50 |
| `wcag-2.2-aa` | 55 |
| `all` | 87 |

## Adding a rule

1. Give the rule a stable id (axe's id when an axe rule is its counterpart) and a `RuleDescriptor`: every source it has,
   the criteria by number (the constructor rejects unknown numbers), `BestPractice` for rules with no criterion,
   `Needs` (`RenderedFacts` when layout or painting is required), and either a `Fix` (exactly one judgement-free
   edit) or a `NoFixReason`. Write the description in our own words.
2. If it has an ACT source, its id must exist in `act-rules.json`; the ACT cases in `testdata/act/` are its acceptance
   tests.
3. Implement `IAccessibilityRule` with that descriptor and add it to `BuiltInRules` (see [checker.md](checker.md); the ARIA
   rules are in [rules/aria.md](rules/aria.md)).
4. Refresh `docs/coverage.md` with `ONEHTML_UPDATE_DOCS=1` when running the Validation tests.

## Refreshing the data

```
python3 tools/catalogue/sources.py <cache-dir> [--act-testcases]   # download, check against the pins
python3 tools/catalogue/generate.py <cache-dir>                    # rewrite Catalogue/Data/*.json
```

To move to a newer source, change the pin in `sources.py`, run both, and review the diff of the data files and
`coverage.md`.
