# Form rules: autocomplete

The form-autocomplete area of the Validation package: axe-core 4.14.0 `autocomplete-valid` and ACT rule 73f2c2. Ids, tags
and impact come from the axe-core rule table as facts; the text is our own. How to run rules is in
[checker.md](checker.md), the result shape in [results.md](results.md).

| Rule | Sources | Criteria | Impact | Needs | Suggested edit |
|---|---|---|---|---|---|
| autocomplete-valid | axe, ACT 73f2c2 | 1.3.5 (WCAG 2.1 AA) | serious | static | none |

## What it checks

Every `input`, `select` and `textarea` with an `autocomplete` attribute that is not empty or only ASCII white space must
have a value that follows the HTML Standard's *autofill detail tokens* (section 4.10.18.7 "Autofill"), in this order, each
token matched ASCII case-insensitively:

1. optionally a token starting with `section-`;
2. optionally `shipping` or `billing`;
3. either one autofill field name from the Standard's list (`name`, `given-name`, `street-address`, `cc-number`, `bday-day`,
   `url`, `photo`, `language`, `sex` and the rest), **or** an optional contact type (`home`, `work`, `mobile`,
   `fax`, `pager`) followed by a telephone name (`tel` and the `tel-*` parts) or `email` or `impp`;
4. optionally `webauthn`, on `input` and `textarea` only.

The single token `on` or `off` is valid. The result lists the first problem, for example that a contact type is followed by a
name that does not take one (`work photo`), or that tokens are out of order (`billing shipping email`).

**Not examined** (ACT 73f2c2 applicability): the `on`/`off` toggle; a disabled control (the `disabled` attribute, a
disabled `fieldset` except inside its first `legend`, or `aria-disabled="true"` on the control or an ancestor); an `input` of
type `hidden`, `button`, `submit` or `reset`; a control that is not rendered (`display`, `hidden`) or has `visibility:
hidden`; and a control outside sequential focus navigation whose role is not a widget role.

## Decisions

- **No suggested edit.** Removing a bad `autocomplete` value would clear the finding without giving the field the purpose
  1.3.5 asks for, and the right token depends on what the field collects. Correcting a misspelling (`shiping`) is a guess
  at what the author meant.
- **Control groups are not checked.** The Standard also says that some field names are inappropriate for some controls
  (its table gives each name a control group). ACT's expectation lists only the token
  grammar and its test cases pass a `bday-day` on a `tel` input, so this rule checks the grammar only. Mismatches of that kind pass.
- **Differences from axe-core:** axe-core's table lists a needs-review half for this rule; this rule reports only
  failed and passed.
