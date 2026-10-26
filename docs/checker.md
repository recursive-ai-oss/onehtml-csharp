# The checker

`RecursiveAI.OneHtml.Accessibility.Validation` runs accessibility rules over a document and its accessibility tree and
returns labelled results. This page covers the API, how rules are selected, and how to add a private rule. The shape of
the results and the two JSON exports is in [results.md](results.md); the rules that ship are in
[rules/aria.md](rules/aria.md), [rules/names.md](rules/names.md), [rules/structure.md](rules/structure.md), [rules/tables.md](rules/tables.md),
[rules/forms.md](rules/forms.md), [rules/language.md](rules/language.md), [rules/contrast.md](rules/contrast.md), [rules/presentation.md](rules/presentation.md),
[rules/media.md](rules/media.md) and [rules/act-gaps.md](rules/act-gaps.md) (with the per-rule ACT account in [act-coverage.md](act-coverage.md)); the data about the standards they cite is in [catalogue.md](catalogue.md).

## Running a check

```csharp
Document doc = Document.Parse(bytes);
var checker = new Checker();
CheckReport report = checker.Check(doc);                       // default cascade and style context
CheckReport focused = checker.Check(AccessibilityTree.Build(doc, styles, StyleContext.Default.WithFocus(button)));
```

`Checker.Check(Document)` builds the cascade and the tree itself. `Checker.Check(AccessibilityTree)` checks a tree you
built, under any style context. Both take a `CheckOptions` and a `CancellationToken`; the token is observed between
rules and every few hundred nodes inside a rule, and cancelling throws `OperationCanceledException`. Nothing else
leaves a public method except argument exceptions.

The library does not read files or the network. Linked stylesheets reach the cascade through the style options you pass
when you build the `DocumentStyles`, as everywhere else in the family.

## Selecting rules

`CheckOptions` has four selections. A rule runs only if it satisfies every selection that is not empty.

| Option | Selects |
|---|---|
| `Profile` | rules that check a criterion of the profile (`ValidationProfile.Wcag20AA`, `Wcag21AA`, `Wcag22AA`, `All`). A rule with no criterion (a best practice) belongs to `All` only. |
| `Tags` | rules carrying at least one of the tags (`cat.aria`, `best-practice`, `wcag2a`, …; axe's tags for axe rules). |
| `Criteria` | rules that check at least one criterion by number (`4.1.2`). |
| `RuleIds` | exactly these rules; an unknown id is an `ArgumentException`. |

`IncludePasses = false` drops passed results; `MaxResultsPerRule` (default 100,000) stops one rule from filling memory
on a hostile page. A rule that reaches it drops further results and `CheckReport.LimitReached` is set.

A profile names the standard that cites the criteria (Section 508 and WCAG 2.0 AA, ADA Title II, EN 301 549 and WCAG 2.1
AA); it selects rules and is not a statement about legal compliance.

## The report

`CheckReport.Results` is ordered by rule id (ordinal), then by the document order of the target, then by outcome
(failed, passed, needs-review, needs-render). A rule that found nothing to check contributes one `Inapplicable` result
with no target. The order does not depend on the order rules were registered in or on the number of threads. `WithOutcome`,
`Count`, `RulesRun`, `Tree`, `Document` and `StyleContext` are there for convenience.

`report.SuggestedEdits()` returns the suggested edits of all results as one edit-format envelope (`docs/edit-format.md`),
without duplicates, and `report.SuggestedEditList()` returns the same edits as `Edit` objects for `Document.Apply`. The
library never applies them. Each carries `meta`: `{ "rule": id, "criteria": [...], "suggested": true }`.

## Private rules

A rule implements `IAccessibilityRule`:

```csharp
public interface IAccessibilityRule
{
    RuleDescriptor Descriptor { get; }   // id, sources, criteria, needs, fix or why none, tags
    string Description { get; }          // what the rule checks, one sentence
    string Help { get; }                 // how to fix a failure, one sentence
    Impact Impact { get; }               // minor, moderate, serious, critical
    void Evaluate(CheckContext context, RuleReporter reporter);
    RuleScope Scope => RuleScope.Unspecified;                       // optional: see "Re-checking after an edit"
    IReadOnlyCollection<string>? RelevantElements => null;          // optional, for page-level rules
}
```

`CheckContext` gives the `Document`, the `Styles` (the cascade), the `Tree`, the `StyleContext` and two helpers:
`Find(element)` (the tree node, or null when the element is not rendered) and `IsExposed(element)` (in the tree and not
hidden by `aria-hidden`, `inert` or `visibility`). The reporter has `Passed`, `Failed`, `NeedsReview` and
`NeedsRender`, each with a target and a sentence; `Failed` also takes findings and suggested edits, and `Suggest(edit,
description)` builds a suggested edit with the metadata filled in. Report in document order; the checker sorts anyway.

Register private rules with `new Checker(privateRules)`; their ids must differ from each other and from the built-in
ones (`ArgumentException` otherwise). `Checker.WithRules(rules)` makes a checker with only the rules given. Tags work
the same for private rules, so a consumer can mark its own with a tag and select them.

`Scope` and `RelevantElements` have default implementations, so a rule written before they existed still compiles and
behaves as before: it runs in full on every re-check. `CheckContext.Elements()` and `CheckContext.TreeNodes()` are the
elements and tree nodes a rule should iterate; in a re-check they are narrowed to the ones that need a second look.
`AllElements()` is the whole page, for a rule that needs to look beyond its targets to decide about one.

A rule must be deterministic, must not keep state between calls, must not recurse on the depth of the document, and
should observe `reporter.CancellationToken` in loops over the document.

## Task impact, context and facts

Each result says whether the problem can stand between a person and a task (`TaskImpact.BlocksTask`): the target is
focusable or has a widget role, or sits inside a `form`, a `dialog` or an `alertdialog` (element or role). The reason is
`interactive`, `form` or `dialog`. This is computed from the tree and does not depend on the rule.

`CssOnly` is true when the style context has interaction state (focus, hover, active, target). Script-driven states
are invisible to the library, so such results reflect the CSS alone.

Rules declare `RuleNeeds.Static` or `RuleNeeds.RenderedFacts`. Rendered facts reach the rules through
`CheckOptions.Facts` and `CheckContext.Facts` (null when the caller has none). They must have been captured for the
same `Document` object the tree was built from, or the checker throws `ArgumentException`. A rule that needs layout
decides from the facts when they are there and lists them in `FactsUsed`; when they are not, it either decides from the
cascade where nothing can move the text, or reports `NeedsRender` with the missing facts in `FactsMissing`, so the caller
knows what to capture. `label-content-name-mismatch` is the one rule of the names area that reads facts; the ARIA rules
and the rest of the names rules are static and leave both lists empty.

## Re-checking after an edit

A fixing loop is edit, check, keep or discard. `Checker.Recheck` makes the check after an edit cost what the edit
touched:

```csharp
EditResult edit = doc.Apply(edits);
RestyleResult restyle = styles.Restyle(edit);
AccessibilityTree updated = tree.Update(edit, restyle);
CheckReport next = checker.Recheck(previousReport, edit, restyle, updated);   // or the BatchResult overload
```

The result is equal to `checker.Check(updated, sameOptions)`: the same results, outcomes, messages, targets (handles into
the new snapshot, locators and byte ranges of the new source), suggested edits (aimed at the new snapshot) and order. The
report remembers the options it was made with. Only what an edit reached is re-run; everything else is carried over.
`report.Recheck` (a `RecheckInfo`) says how: whether it was incremental, how many rules ran in full, in part or were
carried over, and the number of nodes the edit reached.

**What an edit reaches.** A node is reached when any of these holds, comparing the old and new snapshots through the
edit's change set (`ChangeSet.MapToNew`):

- it was added, or the change set lists it as changed (attributes, text, children, parent);
- the restyle reports its style, or its pseudo-element styles, as possibly different;
- its accessibility-tree node was added or removed, or differs in anything a rule can read: role and how it was derived,
  name and description with their sources and the elements they came from, ignored status and reasons, hidden status and
  its origin, states, relations, focusability, parent in the tree, diagnostics. Position in the focus order is the one
  exception, because inserting one focusable shifts every later one; a rule that reads it must not declare a scope.

**Rule scopes.** A rule declares what its result for a target reads, with `RuleScope` flags. The checker widens the
reached nodes by those flags and runs the rule over the result only:

| Flag | The result also depends on | Widening |
|---|---|---|
| `Node` | the target's own attributes, text, style and tree node | none |
| `Descendants` | the target's DOM descendants | the DOM ancestors of every reached node |
| `Ancestors` | the target's DOM ancestors | the DOM descendants of every reached node |
| `Siblings` | the own attributes, text and tree node of the target's siblings | the siblings of every reached node, and all children of a parent that lost a child |
| `IdReferences` | elements it names by ID and elements that name it (`aria-labelledby`, `aria-describedby`, `aria-owns`, `aria-controls`, `aria-flowto`, `aria-details`, `aria-errormessage`, `aria-activedescendant`, `for`, `headers`, `form`, `list`, a `#fragment` in `href`), and a control and its `label` | the elements carrying an ID that a reached node has, had or names, the elements that name those IDs, and a label with the controls it labels |
| `Page` | the page as a whole (counts, first occurrence, one element) | the rule runs in full when the edit touched anything, or, if `RelevantElements` lists local names, when it touched an element of one of those names (a text node counts for its parent) |
| none (`Unspecified`) | anything | the rule runs in full on every re-check |

A scope is a promise, and a rule that declares one must keep three: each result is reported on the node whose inputs
the scope names (the checker drops a result reported anywhere else during a partial run); targets are found by iterating
`Elements()` or `TreeNodes()`, or by examining nodes independently of each other; and whatever the rule reads about
other nodes in order to decide (a count of elements with the same id, the set of ids that are referenced) is read
through `AllElements()` or the whole tree, never through the narrowed iteration. A scope may be wider than the truth,
never narrower. The equivalence tests in `RecheckEquivalenceTests` run random edit sequences and compare every re-check
with a fresh check, which is how a scope that is too narrow shows up.

**When the whole check runs again.** `Recheck` runs every rule in full, and `RecheckInfo.FullRunReason` says why, when:

- rendered facts take part: the previous report used some (an edit makes facts stale, so they are never carried over), or
  `facts` were passed for the edited document (the tree's own `Update` follows the same rule);
- the previous report dropped results at `MaxResultsPerRule`;
- the restyle was a full recompute, or the style context of the new tree differs from the old one.

A single rule runs in full when it declares no scope, when the nodes its scope reaches are more than 40% of the page's
elements, when a previous result cannot be remapped (a suggested edit whose target was removed, or a result with no
target), or when the merged results would pass `MaxResultsPerRule`. `ArgumentException` is thrown when the edit was not
applied to the previous report's document, the tree is not the edited document's, or the report came from a checker with
other rules.

**Scopes of the built-in rules.**

| Scope | Rules |
|---|---|
| `Node` | `aria-allowed-attr`, `aria-command-name`, `aria-conditional-attr`, `aria-deprecated-role`, `aria-dialog-name`, `aria-input-field-name`, `aria-meter-name`, `aria-progressbar-name`, `aria-prohibited-attr`, `aria-required-attr`, `aria-roles`, `aria-tab-name`, `aria-toggle-field-name`, `aria-tooltip-name`, `aria-treeitem-name`, `aria-valid-attr`, `autoplay-audio-control`, `blink`, `frame-focusable-content`, `image-alt`, `label-title-only`, `marquee`, `meta-viewport`, `meta-viewport-large`, `no-autoplay-audio`, `presentation-role-conflict` |
| `Node`, `Descendants` | `aria-braille-equivalent`, `aria-hidden-focus`, `aria-text`, `definition-list`, `list`, `nested-interactive`, `object-alt`, and the media review rules (`audio-alternative`, `audio-caption`, `autoplay-audio-duration`, `video-*`), which read the `track` children |
| `Node`, `Ancestors` | `area-alt`, `button-name`, `dlitem`, `empty-heading`, `frame-title`, `image-name-descriptive`, `input-button-name`, `input-image-alt`, `label`, `link-descriptive`, `link-in-context-descriptive`, `link-name`, `role-img-alt`, `select-name`, `server-side-image-map`, `summary-name`, `svg-img-alt` |
| `Node`, `Descendants`, `Ancestors` | `hidden-content` |
| `Node`, `Ancestors`, `Siblings` | `listitem` |
| `Node`, `Descendants`, `Ancestors`, `Siblings` | `aria-allowed-role` |
| `Node`, `Descendants`, `Ancestors`, `IdReferences` | `aria-required-children`, `aria-required-parent` |
| `Node`, `Ancestors`, `IdReferences` | `color-contrast`, `color-contrast-enhanced` (background paint follows the ancestor chain; disabled controls exempt the labels that name them) |
| `Node`, `IdReferences` | `aria-valid-attr-value`, `duplicate-id`, `duplicate-id-active`, `duplicate-id-aria`, `skip-link` |
| `Page` (`body`) | `aria-hidden-body` |
| `Page` (`html`, `title`) | `document-title`, `document-title-descriptive` |
| `Page` (`html`) | `html-has-lang`, `html-lang-matches-content`, `html-lang-valid`, `html-xml-lang-mismatch` |
| none | the landmark, heading-order, table, language-content, focus-order, group-comparison (`frame-title-unique`, `identical-links-*`, `accesskeys`) and layout-reading rules, and `focus-visible-indicator`, `scrollable-region-focusable`, `region`, `link-in-text-block`, `target-size`, `label-content-name-mismatch`, `p-as-heading`, `image-redundant-alt`, `image-of-text`, `image-decorative`, `label-descriptive`, `avoid-inline-spacing`, `css-orientation-lock`, `bypass`, `autocomplete-valid`, `tabindex`, `meta-refresh*` |

`focus-visible-indicator` compares the style of every element under a focus on each focusable element, so one edit can
change any of its results; it stays unscoped and dominates the time of a re-check on a page with many focusable elements.

**Cost.** The checker reads both trees once to find what changed, which is linear in the page and cheap next to the rules.
The rest is the unscoped rules, plus carrying the results of every scoped rule over: each carried result is rebuilt for the
new snapshot (target, byte range, task impact, suggested-edit targets), so a re-check allocates in proportion to the
number of results as well as to what changed. Measured rows are in `bench/README.md`.
