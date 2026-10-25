namespace RecursiveAI.OneHtml.Parsing;

/// <summary>
/// The tokenizer parse errors of the HTML standard (13.2.2), one member per error code. Use
/// <see cref="HtmlParseErrors.GetCode"/> for the standard's spelling, for example <c>eof-in-tag</c>.
/// </summary>
public enum HtmlParseError
{
    /// <summary>The <c>abrupt-closing-of-empty-comment</c> parse error.</summary>
    AbruptClosingOfEmptyComment,

    /// <summary>The <c>abrupt-doctype-public-identifier</c> parse error.</summary>
    AbruptDoctypePublicIdentifier,

    /// <summary>The <c>abrupt-doctype-system-identifier</c> parse error.</summary>
    AbruptDoctypeSystemIdentifier,

    /// <summary>The <c>absence-of-digits-in-numeric-character-reference</c> parse error.</summary>
    AbsenceOfDigitsInNumericCharacterReference,

    /// <summary>The <c>cdata-in-html-content</c> parse error.</summary>
    CdataInHtmlContent,

    /// <summary>The <c>character-reference-outside-unicode-range</c> parse error.</summary>
    CharacterReferenceOutsideUnicodeRange,

    /// <summary>The <c>control-character-in-input-stream</c> parse error.</summary>
    ControlCharacterInInputStream,

    /// <summary>The <c>control-character-reference</c> parse error.</summary>
    ControlCharacterReference,

    /// <summary>The <c>disallowed-processing-instruction-target</c> parse error.</summary>
    DisallowedProcessingInstructionTarget,

    /// <summary>The <c>duplicate-attribute</c> parse error.</summary>
    DuplicateAttribute,

    /// <summary>The <c>end-tag-with-attributes</c> parse error.</summary>
    EndTagWithAttributes,

    /// <summary>The <c>end-tag-with-trailing-solidus</c> parse error.</summary>
    EndTagWithTrailingSolidus,

    /// <summary>The <c>eof-before-tag-name</c> parse error.</summary>
    EofBeforeTagName,

    /// <summary>The <c>eof-in-cdata</c> parse error.</summary>
    EofInCdata,

    /// <summary>The <c>eof-in-comment</c> parse error.</summary>
    EofInComment,

    /// <summary>The <c>eof-in-doctype</c> parse error.</summary>
    EofInDoctype,

    /// <summary>The <c>eof-in-processing-instruction</c> parse error.</summary>
    EofInProcessingInstruction,

    /// <summary>The <c>eof-in-script-html-comment-like-text</c> parse error.</summary>
    EofInScriptHtmlCommentLikeText,

    /// <summary>The <c>eof-in-tag</c> parse error.</summary>
    EofInTag,

    /// <summary>The <c>incorrectly-closed-comment</c> parse error.</summary>
    IncorrectlyClosedComment,

    /// <summary>The <c>incorrectly-opened-comment</c> parse error.</summary>
    IncorrectlyOpenedComment,

    /// <summary>The <c>invalid-character-sequence-after-doctype-name</c> parse error.</summary>
    InvalidCharacterSequenceAfterDoctypeName,

    /// <summary>The <c>invalid-first-character-of-processing-instruction-target</c> parse error.</summary>
    InvalidFirstCharacterOfProcessingInstructionTarget,

    /// <summary>The <c>invalid-first-character-of-tag-name</c> parse error.</summary>
    InvalidFirstCharacterOfTagName,

    /// <summary>The <c>invalid-processing-instruction-target</c> parse error.</summary>
    InvalidProcessingInstructionTarget,

    /// <summary>The <c>missing-attribute-value</c> parse error.</summary>
    MissingAttributeValue,

    /// <summary>The <c>missing-doctype-name</c> parse error.</summary>
    MissingDoctypeName,

    /// <summary>The <c>missing-doctype-public-identifier</c> parse error.</summary>
    MissingDoctypePublicIdentifier,

    /// <summary>The <c>missing-doctype-system-identifier</c> parse error.</summary>
    MissingDoctypeSystemIdentifier,

    /// <summary>The <c>missing-end-tag-name</c> parse error.</summary>
    MissingEndTagName,

    /// <summary>The <c>missing-quote-before-doctype-public-identifier</c> parse error.</summary>
    MissingQuoteBeforeDoctypePublicIdentifier,

    /// <summary>The <c>missing-quote-before-doctype-system-identifier</c> parse error.</summary>
    MissingQuoteBeforeDoctypeSystemIdentifier,

    /// <summary>The <c>missing-semicolon-after-character-reference</c> parse error.</summary>
    MissingSemicolonAfterCharacterReference,

    /// <summary>The <c>missing-whitespace-after-doctype-public-keyword</c> parse error.</summary>
    MissingWhitespaceAfterDoctypePublicKeyword,

    /// <summary>The <c>missing-whitespace-after-doctype-system-keyword</c> parse error.</summary>
    MissingWhitespaceAfterDoctypeSystemKeyword,

    /// <summary>The <c>missing-whitespace-before-doctype-name</c> parse error.</summary>
    MissingWhitespaceBeforeDoctypeName,

    /// <summary>The <c>missing-whitespace-between-attributes</c> parse error.</summary>
    MissingWhitespaceBetweenAttributes,

    /// <summary>The <c>missing-whitespace-between-doctype-public-and-system-identifiers</c> parse error.</summary>
    MissingWhitespaceBetweenDoctypePublicAndSystemIdentifiers,

    /// <summary>The <c>nested-comment</c> parse error.</summary>
    NestedComment,

    /// <summary>The <c>noncharacter-character-reference</c> parse error.</summary>
    NoncharacterCharacterReference,

    /// <summary>The <c>noncharacter-in-input-stream</c> parse error.</summary>
    NoncharacterInInputStream,

    /// <summary>The <c>null-character-reference</c> parse error.</summary>
    NullCharacterReference,

    /// <summary>The <c>surrogate-character-reference</c> parse error.</summary>
    SurrogateCharacterReference,

    /// <summary>The <c>surrogate-in-input-stream</c> parse error.</summary>
    SurrogateInInputStream,

    /// <summary>The <c>unexpected-character-after-doctype-system-identifier</c> parse error.</summary>
    UnexpectedCharacterAfterDoctypeSystemIdentifier,

    /// <summary>The <c>unexpected-character-in-attribute-name</c> parse error.</summary>
    UnexpectedCharacterInAttributeName,

    /// <summary>The <c>unexpected-character-in-unquoted-attribute-value</c> parse error.</summary>
    UnexpectedCharacterInUnquotedAttributeValue,

    /// <summary>The <c>unexpected-equals-sign-before-attribute-name</c> parse error.</summary>
    UnexpectedEqualsSignBeforeAttributeName,

    /// <summary>The <c>unexpected-null-character</c> parse error.</summary>
    UnexpectedNullCharacter,

    /// <summary>The <c>unexpected-solidus-in-tag</c> parse error.</summary>
    UnexpectedSolidusInTag,

    /// <summary>The <c>unknown-named-character-reference</c> parse error.</summary>
    UnknownNamedCharacterReference,
}

/// <summary>Names for <see cref="HtmlParseError"/> values.</summary>
public static class HtmlParseErrors
{
    /// <summary>Returns the standard's code for an error, such as <c>missing-end-tag-name</c>.</summary>
    /// <param name="error">The error.</param>
    public static string GetCode(HtmlParseError error) => error switch
    {
        HtmlParseError.AbruptClosingOfEmptyComment => "abrupt-closing-of-empty-comment",
        HtmlParseError.AbruptDoctypePublicIdentifier => "abrupt-doctype-public-identifier",
        HtmlParseError.AbruptDoctypeSystemIdentifier => "abrupt-doctype-system-identifier",
        HtmlParseError.AbsenceOfDigitsInNumericCharacterReference => "absence-of-digits-in-numeric-character-reference",
        HtmlParseError.CdataInHtmlContent => "cdata-in-html-content",
        HtmlParseError.CharacterReferenceOutsideUnicodeRange => "character-reference-outside-unicode-range",
        HtmlParseError.ControlCharacterInInputStream => "control-character-in-input-stream",
        HtmlParseError.ControlCharacterReference => "control-character-reference",
        HtmlParseError.DisallowedProcessingInstructionTarget => "disallowed-processing-instruction-target",
        HtmlParseError.DuplicateAttribute => "duplicate-attribute",
        HtmlParseError.EndTagWithAttributes => "end-tag-with-attributes",
        HtmlParseError.EndTagWithTrailingSolidus => "end-tag-with-trailing-solidus",
        HtmlParseError.EofBeforeTagName => "eof-before-tag-name",
        HtmlParseError.EofInCdata => "eof-in-cdata",
        HtmlParseError.EofInComment => "eof-in-comment",
        HtmlParseError.EofInDoctype => "eof-in-doctype",
        HtmlParseError.EofInProcessingInstruction => "eof-in-processing-instruction",
        HtmlParseError.EofInScriptHtmlCommentLikeText => "eof-in-script-html-comment-like-text",
        HtmlParseError.EofInTag => "eof-in-tag",
        HtmlParseError.IncorrectlyClosedComment => "incorrectly-closed-comment",
        HtmlParseError.IncorrectlyOpenedComment => "incorrectly-opened-comment",
        HtmlParseError.InvalidCharacterSequenceAfterDoctypeName => "invalid-character-sequence-after-doctype-name",
        HtmlParseError.InvalidFirstCharacterOfProcessingInstructionTarget => "invalid-first-character-of-processing-instruction-target",
        HtmlParseError.InvalidFirstCharacterOfTagName => "invalid-first-character-of-tag-name",
        HtmlParseError.InvalidProcessingInstructionTarget => "invalid-processing-instruction-target",
        HtmlParseError.MissingAttributeValue => "missing-attribute-value",
        HtmlParseError.MissingDoctypeName => "missing-doctype-name",
        HtmlParseError.MissingDoctypePublicIdentifier => "missing-doctype-public-identifier",
        HtmlParseError.MissingDoctypeSystemIdentifier => "missing-doctype-system-identifier",
        HtmlParseError.MissingEndTagName => "missing-end-tag-name",
        HtmlParseError.MissingQuoteBeforeDoctypePublicIdentifier => "missing-quote-before-doctype-public-identifier",
        HtmlParseError.MissingQuoteBeforeDoctypeSystemIdentifier => "missing-quote-before-doctype-system-identifier",
        HtmlParseError.MissingSemicolonAfterCharacterReference => "missing-semicolon-after-character-reference",
        HtmlParseError.MissingWhitespaceAfterDoctypePublicKeyword => "missing-whitespace-after-doctype-public-keyword",
        HtmlParseError.MissingWhitespaceAfterDoctypeSystemKeyword => "missing-whitespace-after-doctype-system-keyword",
        HtmlParseError.MissingWhitespaceBeforeDoctypeName => "missing-whitespace-before-doctype-name",
        HtmlParseError.MissingWhitespaceBetweenAttributes => "missing-whitespace-between-attributes",
        HtmlParseError.MissingWhitespaceBetweenDoctypePublicAndSystemIdentifiers => "missing-whitespace-between-doctype-public-and-system-identifiers",
        HtmlParseError.NestedComment => "nested-comment",
        HtmlParseError.NoncharacterCharacterReference => "noncharacter-character-reference",
        HtmlParseError.NoncharacterInInputStream => "noncharacter-in-input-stream",
        HtmlParseError.NullCharacterReference => "null-character-reference",
        HtmlParseError.SurrogateCharacterReference => "surrogate-character-reference",
        HtmlParseError.SurrogateInInputStream => "surrogate-in-input-stream",
        HtmlParseError.UnexpectedCharacterAfterDoctypeSystemIdentifier => "unexpected-character-after-doctype-system-identifier",
        HtmlParseError.UnexpectedCharacterInAttributeName => "unexpected-character-in-attribute-name",
        HtmlParseError.UnexpectedCharacterInUnquotedAttributeValue => "unexpected-character-in-unquoted-attribute-value",
        HtmlParseError.UnexpectedEqualsSignBeforeAttributeName => "unexpected-equals-sign-before-attribute-name",
        HtmlParseError.UnexpectedNullCharacter => "unexpected-null-character",
        HtmlParseError.UnexpectedSolidusInTag => "unexpected-solidus-in-tag",
        HtmlParseError.UnknownNamedCharacterReference => "unknown-named-character-reference",
        _ => error.ToString(),
    };
}
