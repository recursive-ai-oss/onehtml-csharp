namespace RecursiveAI.OneHtml.Parsing;

/// <summary>A parse error and where it happened.</summary>
/// <param name="Error">Which error.</param>
/// <param name="Offset">
/// The UTF-16 offset in the decoded text of the input character the error is reported at: the character being
/// consumed when it was found, or the text length for an error at the end of the input. Use
/// <see cref="Encodings.SourceText.ToByteOffset"/> for the byte offset.
/// </param>
public readonly record struct ParseError(HtmlParseError Error, int Offset);
