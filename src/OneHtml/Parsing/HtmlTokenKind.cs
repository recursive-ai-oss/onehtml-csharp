namespace RecursiveAI.OneHtml.Parsing;

/// <summary>The kind of token <see cref="HtmlTokenizer.Next"/> produced.</summary>
public enum HtmlTokenKind
{
    /// <summary>No token yet: <see cref="HtmlTokenizer.Next"/> has not been called.</summary>
    None = 0,

    /// <summary>A run of character tokens. Its text is <see cref="HtmlTokenizer.Data"/>.</summary>
    Character,

    /// <summary>A start tag.</summary>
    StartTag,

    /// <summary>An end tag.</summary>
    EndTag,

    /// <summary>A comment, including bogus comments. Its text is <see cref="HtmlTokenizer.Data"/>.</summary>
    Comment,

    /// <summary>A DOCTYPE.</summary>
    Doctype,

    /// <summary>A processing instruction. Its target is <see cref="HtmlTokenizer.Name"/>, its data <see cref="HtmlTokenizer.Data"/>.</summary>
    ProcessingInstruction,

    /// <summary>The end of the input. Every later call returns it again.</summary>
    EndOfFile,
}

/// <summary>The tokenizer states a tree builder may switch to (HTML 13.2.6.2 and 13.2.5.69).</summary>
public enum TokenizerState
{
    /// <summary>The data state.</summary>
    Data = 0,

    /// <summary>The RCDATA state, for <c>title</c> and <c>textarea</c>.</summary>
    Rcdata,

    /// <summary>The RAWTEXT state, for <c>style</c>, <c>xmp</c>, <c>iframe</c>, <c>noembed</c>, <c>noframes</c> and <c>noscript</c> with scripting.</summary>
    Rawtext,

    /// <summary>The script data state.</summary>
    ScriptData,

    /// <summary>The PLAINTEXT state.</summary>
    Plaintext,

    /// <summary>The CDATA section state. The tokenizer enters it by itself on <c>&lt;![CDATA[</c> when <see cref="HtmlTokenizer.InForeignContent"/> is set.</summary>
    CdataSection,
}

/// <summary>How an attribute's value was written in the source.</summary>
public enum AttributeQuote
{
    /// <summary>The attribute has no value at all (<c>&lt;input disabled&gt;</c>).</summary>
    NoValue = 0,

    /// <summary>The value was not quoted (<c>a=b</c>; also <c>a=</c> followed by <c>&gt;</c>).</summary>
    Unquoted,

    /// <summary>The value was in single quotes.</summary>
    SingleQuoted,

    /// <summary>The value was in double quotes.</summary>
    DoubleQuoted,
}
