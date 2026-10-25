# CSS parsing and stylesheet snapshots

Normative behaviour of `RecursiveAI.OneHtml.Css`, written so another language can reproduce it. The algorithms are
those of CSS Syntax Module Level 3 (Editor's Draft of 20 October 2026, drafts.csswg.org/css-syntax-3/); this file
records only what that text leaves open or what this library adds.

## Input and offsets

- Input is decoded text (UTF-16 code units). Every range is a half-open `[start, end)` pair of offsets into that text.
- Preprocessing (§3.3) is applied while reading, not by rewriting the text, so offsets stay offsets into the original:
  CR, FF and CRLF are each one newline; U+0000 and lone surrogates act as U+FFFD (an ident code point, so they
  continue identifiers and are copied into values as U+FFFD).
- Byte input goes through the §3.2 determination (`CssEncoding.DetermineFallbackEncoding`) with a caller-supplied
  label lookup and decoder; the library has no encoding tables of its own in this layer.

## Tokens

- Tokens are exactly those of §4. Comments are not tokens: they are recorded as ranges in a separate list.
- Every character of the input belongs to exactly one token or one comment, so concatenating tokens and comments in
  offset order gives back the input. `CssSourceWriter.Write` does this and fails loudly if it ever does not.
- Token values are decoded from the source on request (escapes, U+FFFD replacement). A backslash at the very end of the
  input stands for U+FFFD, except inside a string, where it is dropped (§4.3.5, §4.3.7).
- Unicode-range tokens exist only in the value of a `unicode-range` declaration, which is tokenized a second time
  from its source text (§5.5.6, §5.5.11). Those extra tokens sit after the ordinary ones and are not part of the
  text-covering sequence.
- `CssToken.NumberValue` is a double parsed from the number's text; magnitudes beyond the double range are infinite.

## Brackets

Function and `(`, `[`, `{` openers are paired with their closers in one pass with an explicit stack: a closer closes
the innermost open block only if it is that block's ending token, otherwise it is an ordinary token (§5.5.9, §5.5.10).
An opener left open at end of input has no closer and runs to the end. The same pass enforces the nesting limit.
All parsing then skips a nested block by jumping to its matching closer, so nothing recurses on input depth.

## Nodes

A parse yields a tree of nodes in source order. Kinds:

- **Qualified rule**: prelude (token range) and a `{}` block. Style rules are qualified rules.
- **At-rule**: name, prelude, and either a terminating `;` or a `{}` block.
- **Declaration**: name, colon, value (leading and trailing whitespace and `!important` removed), important flag,
  optional terminating `;`. A name starting `--` (after unescaping) is a custom property: its value is kept verbatim
  as source text and its name is case-sensitive.
- **Invalid**: source the specification says to drop ("return nothing"). Kept so every construct has a range.

Block contents are parsed with §5.5.5 into children in source order. Declarations and rules stay interleaved; a
consumer that needs CSS Nesting's grouping of declarations into nested-declaration rules derives it from the order
(declarations after a nested rule start a new group).

- In a list of declarations (`style` attribute text) at-rules are kept as at-rule nodes; the specification discards
  them, so a consumer that wants exactly the specification's result reads only the declarations.
- `@charset` is an ordinary at-rule node; it is not a rule (§8.3) and the CSSOM must skip it.
- A rule whose prelude begins `--name:` is dropped as invalid (§5.5.3) rather than parsed as a rule; nested, it
  swallows the rest of the would-be declaration.
- Declarations are tried first for every construct in a block. The attempt fails fast (before scanning to a
  semicolon) when a `{}` block appears in a non-custom value anywhere but as the whole value, which keeps parsing
  linear on long runs of nested rules.
- Validity beyond syntax (is this a known property, a valid selector, does a custom property value contain an
  unmatched `]`) is not decided here.

### Ranges

- A node's range runs from its first token to its closing `}` or terminating `;`; for a declaration with no `;`
  to its last value or `!important` token. A block that is never closed ends at the end of the text.
- Prelude range: from the first to the last non-whitespace prelude token (comments inside kept). Value range likewise.
- Content range of a block: the text strictly between the braces (to the end of the text if unclosed).
- Declaration parts have their own ranges: name, colon, value, `!important` (from `!` to the end of `important`),
  semicolon. Whitespace and comments between parts belong to no part.

## Limits

`CssParseOptions` defaults: input 16,777,216 code units, 8,388,608 tokens, nesting depth 256 (blocks and functions
together). Input length is checked before tokenizing, token count as tokens are added (the arrays grow only up to the
limit), nesting during bracket pairing. Exceeding one throws `CssLimitExceededException` with the limit that was hit.
Malformed CSS never throws. Cancellation is checked every 4096 tokens and every 4096 parser steps.

## Serialization

- `CssSourceWriter` / `Stylesheet.ToSourceText()`: the source text, rebuilt from tokens and comments.
- `CssSerializer`: canonical text from the structure alone, following CSSOM (identifier and string escaping, `name:
  value;` declarations, `{ }` for empty blocks, grouping rules on indented lines) and §9 (a `/**/` between token pairs
  that would otherwise merge; a unit that would read as an exponent is escaped; a `\` delim is followed by a newline).
  Comments are dropped, whitespace runs become one space, names of non-custom properties are lowercased, unclosed
  constructs are closed. It re-parses to the same structure.

## Known deviations of the css-parsing-tests suite

The suite follows the 2021 candidate draft. 22 of its cases are expected to differ from the current text and are
listed with the deciding sentence in `tests/OneHtml.Tests/Css/CssParsingTests.cs`: 1 case with U+0080..U+00B6 as
ident characters (§4.2 lists the non-ASCII ident ranges), 9 with unicode-range tokens outside a `unicode-range`
descriptor (§4.3.1), 2 with `~=`-style match tokens (not in §4), 8 for declaration value trimming and the
semicolon stop token (§5.5.6), 2 with CDO/CDC kept in a rule list (§5.5.1). The suite lists parse errors for
unterminated strings and urls inside values; the test harness derives them from `CssToken.IsTerminated`.
