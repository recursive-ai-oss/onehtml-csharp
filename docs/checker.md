# The checker

`RecursiveAI.OneHtml.Accessibility.Validation` runs accessibility rules over a document and its accessibility tree and
returns labelled results. This page covers the API, how rules are selected, and how to add a private rule. The shape of
the results and the two JSON exports is in [results.md](results.md); the rules that ship are in
[rules/aria.md](rules/aria.md), [rules/names.md](rules/names.md), [rules/structure.md](rules/structure.md), [rules/tables.md](rules/tables.md),
[rules/forms.md](rules/forms.md), [rules/language.md](rules/language.md), [rules/contrast.md](rules/contrast.md), [rules/presentation.md](rules/presentation.md)
and [rules/media.md](rules/media.md); the data about the standards they cite is in [catalogue.md](catalogue.md).

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
