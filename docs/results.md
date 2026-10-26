# Results and exports

A `CheckReport` (see [checker.md](checker.md)) holds `RuleResult`s. This page defines the result model and the two JSON
exports. Both exports are deterministic: the same report gives the same bytes on every platform. Both are written with
`System.Text.Json` writers; nothing uses reflection.

## The result model

| Member | Meaning |
|---|---|
| `Rule`, `RuleId` | the rule that produced it, with its descriptor |
| `Outcome` | `Failed`, `Passed`, `NeedsReview` (static facts cannot decide), `NeedsRender` (rendered facts are needed), `Inapplicable` |
| `Target` | the element (or text node); the null node for `Inapplicable` |
| `Locator` | a selector unique in the document (generated on first use; null when none exists) |
| `SourceBytes` | the byte range of the target's start tag in the input, or none |
| `Message`, `Findings` | one sentence; and the attributes and values found, in attribute order |
| `TaskImpact`, `TaskImpactReason` | `BlocksTask` with `interactive`, `form` or `dialog`, or `Cosmetic` |
| `CssOnly` | the style context had interaction state, so the result reflects the CSS alone |
| `FactsUsed`, `FactsMissing` | rendered facts a result used; for `NeedsRender`, the facts to supply |
| `SuggestedEdits` | edits, only where the rule names exactly one judgement-free fix |

Labels live on the rule: `Rule.Descriptor` has the sources (ACT id, axe id, failure technique, or ours), the criteria
(number, name, level, version introduced), `BestPractice`, `Needs`, the fix or the reason there is none, and tags; the rule
also has a description, a help sentence and an impact. Descriptions and help texts are written for this library.

A suggested edit is a normal `Edit` against the checked snapshot, carrying `meta` with the rule id, the criteria and
`"suggested": true`. Applying one is the caller's choice; `Document.Apply` and replay both accept them.

## Native JSON: `onehtml-results` version 1

Top level, in this order:

| Field | Value |
|---|---|
| `format` | `"onehtml-results"` |
| `version` | `1`. A change that alters the meaning of a field raises it; new fields do not. Readers ignore unknown fields. |
| `document` | `{ "sha256": hex of the input bytes, "url": as given in the export options (omitted when none) }` |
| `styleContext` | `viewportWidth`, `viewportHeight`, `media` (`screen`/`print`), `colorScheme`, `reducedMotion`, `contrast`, `forcedColors`, `cssOnly`, and `focus`, `hover`, `active`, `target` (locators) when set |
| `profile` | the profile name (`wcag-2.1-aa`, `all`, …) or `null` |
| `limitReached` | a rule dropped results at `MaxResultsPerRule` |
| `rules` | the rules that ran, in id order: `id`, `description`, `help`, `impact`, `bestPractice`, `needs` (`static`/`rendered-facts`), `sources` (`kind`: `act`/`axe`/`failure-technique`/`own`, `id`), `criteria` (`number`, `handle`, `level`, `version`), `tags`, `fix`, `noFixReason` |
| `results` | see below |
| `suggestedEdits` | one edit-format envelope (`docs/edit-format.md`) holding every distinct suggested edit, so replay needs one document |

Each result: `rule`, `outcome` (`failed`, `passed`, `needs-review`, `needs-render`, `inapplicable`), `message`, `target`
(`null`, or `tag`, `locator`, `source` with byte `start` and `end`), `findings` (`attribute`, `value`, `message`),
`taskImpact` (`blocksTask`, `reason`), `cssOnly`, `factsUsed`, `factsMissing`, and `suggested`: a list of
`{ "description", "edit": n }` where `n` indexes `suggestedEdits.edits`. Results keep the report order: rule id, document
order of the target, outcome.

Strings are UTF-8 JSON with the characters `<`, `>`, `&` and `'` unescaped (the output is not meant to be embedded in
HTML). Numbers are integers except the viewport size.

## Axe-compatible JSON

The shape follows the results object that axe-core's public API documentation describes: `violations`, `passes`,
`incomplete` and `inapplicable`, each a list of rule results with `id`, `impact`, `tags`, `description`, `help`,
`helpUrl` and `nodes`; each node with `html`, `impact`, `target`, `any`, `all`, `none` and, for failures,
`failureSummary`. Top-level `testEngine`, `testRunner`, `testEnvironment` and `toolOptions` are present; `url` is present
when given.

The mapping is lossy, and these are its rules:

| Ours | Axe-compatible |
|---|---|
| failed | `violations` |
| passed | `passes` |
| needs-review, needs-render | `incomplete`; the node's `onehtml.outcome` says which |
| inapplicable | `inapplicable` (a rule with no target) |
| rule id | the axe id where an axe rule is a counterpart; ours otherwise, with our tags |
| rule impact | the rule's impact on violation and incomplete nodes; `null` on passes and inapplicable |
| `target` | `[locator]` (one selector; no frames or shadow roots) |
| `html` | the target's start tag as written in the source, cut to `MaxHtmlLength` (300) characters; axe gives the serialised element |
| `any`/`all`/`none` | a failure or incomplete node has one check in `any` (id = rule id, `data` = findings, `message`); `all` and `none` are empty. Passed nodes have empty arrays. Our rules are not built from axe's checks, so nothing finer is available. |
| `failureSummary` | the result message and the findings' messages on separate lines (our wording, not axe's) |
| `helpUrl` | the Deque University page of the axe version for rules with an axe source; empty otherwise |
| `description`, `help` | our own sentences |
| not exported | `timestamp` (it would make the output differ between runs), `relatedNodes` (always empty), frame and shadow paths |
| added | per node, `onehtml`: `outcome`, `blocksTask`, `cssOnly`, `factsMissing`, `suggestedEdits` (count) |

A rule appears once per group it has results in; a rule with failures and passes is in both `violations` and `passes`.
Within a group rules are in id order and nodes in document order.

## Not provided yet

SARIF and EARL exports. There is no reader for the native JSON; consumers read it with any JSON parser, and the edit
envelopes inside it are read with `EditJson.Parse`.
