using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Parsing;

/// <summary>
/// The HTML tokenizer (HTML 13.2.5), a pull parser over decoded, newline-normalized text.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Next"/> to read one token; the token's data is then in the properties of this object and
/// stays valid until the next call. Nothing is allocated per token: names and text are spans of the source text or
/// of a buffer reused for the next token, and every token carries its source range as UTF-16 offsets
/// (<see cref="Start"/>, <see cref="End"/>), which <see cref="SourceText.ToByteOffset"/> turns into bytes.
/// </para>
/// <para>
/// The seam to the tree builder: the builder calls <see cref="SetState"/> after a start tag that switches the
/// tokenizer (RCDATA, RAWTEXT, script data, PLAINTEXT) and sets <see cref="InForeignContent"/> before each call to
/// <see cref="Next"/> so that <c>&lt;![CDATA[</c> is a CDATA section only in foreign content. The tokenizer tracks
/// the last start tag name itself, for "appropriate end tag" matching.
/// </para>
/// <para>
/// Token boundaries differ from the standard's one-character tokens in one way: adjacent characters come as one
/// <see cref="HtmlTokenKind.Character"/> token. A character reference is its own token (its text is the decoded
/// characters, its range the reference as written), and so is each U+0000 that is replaced by U+FFFD. Everything else
/// is the standard's behaviour. An end of file inside a tag abandons the tag, as the standard says.
/// </para>
/// <para>An instance is not thread-safe.</para>
/// </remarks>
public sealed partial class HtmlTokenizer
{
    private const int MaxTagNameLetters = 64;

    private readonly string _text;
    private readonly ParseLimits _limits;
    private readonly List<ParseError>? _errors;
    private List<ParseError>? _preErrors;
    private int _preErrorIndex;

    private int _pos;
    private State _state;
    private bool _foreign;

    // The current token.
    private HtmlTokenKind _kind;
    private int _start;
    private int _end;
    private TextRef _data;
    private TextRef _name;
    private bool _hasName;
    private bool _selfClosing;
    private string? _publicId;
    private string? _systemId;
    private bool _forceQuirks;

    // Scratch text for names and values that differ from the source (lowercased, decoded). Reused by every token.
    private char[] _buf = new char[256];
    private int _bufLen;

    // The name of the last start tag emitted (or set by the caller), for appropriate end tag matching.
    private readonly char[] _lastStart = new char[MaxTagNameLetters];
    private int _lastStartLength;
    private bool _lastStartIsLetters;

    /// <summary>Creates a tokenizer over decoded text.</summary>
    /// <param name="source">The decoded input. Offsets in tokens are offsets into <see cref="SourceText.Text"/>.</param>
    /// <param name="options">Options; <see langword="null"/> means all defaults.</param>
    /// <exception cref="HtmlLimitExceededException">The text is longer than <see cref="ParseLimits.MaxInputLength"/>.</exception>
    public HtmlTokenizer(SourceText source, HtmlTokenizerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        options ??= new HtmlTokenizerOptions();
        _limits = options.Limits ?? source.Limits;
        if (source.Text.Length > _limits.MaxInputLength)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxInputLength), _limits.MaxInputLength, source.Text.Length);
        }

        _text = source.Text;
        if (options.RecordErrors)
        {
            _errors = new List<ParseError>();
            _preErrors = FindInputStreamErrors(_text);
        }
    }

    /// <summary>Creates a tokenizer over a string, which is wrapped with <see cref="SourceText.FromString"/> (newlines normalized).</summary>
    /// <param name="text">The markup.</param>
    /// <param name="options">Options; <see langword="null"/> means all defaults.</param>
    /// <exception cref="HtmlLimitExceededException">The text is longer than <see cref="ParseLimits.MaxInputLength"/>.</exception>
    public HtmlTokenizer(string text, HtmlTokenizerOptions? options = null)
        : this(SourceText.FromString(text, options?.Limits), options)
    {
    }

    /// <summary>The source being tokenized.</summary>
    public SourceText Source { get; }

    /// <summary>
    /// The parse errors found so far, in the order of their offsets, or <see langword="null"/> when
    /// <see cref="HtmlTokenizerOptions.RecordErrors"/> is off. The tokenizer only appends; the owner may clear it.
    /// </summary>
    public List<ParseError>? Errors => _errors;

    /// <summary>
    /// True while the adjusted current node is an element outside the HTML namespace. Set it before each
    /// <see cref="Next"/> call; it makes <c>&lt;![CDATA[</c> a CDATA section instead of a bogus comment.
    /// </summary>
    public bool InForeignContent
    {
        get => _foreign;
        set => _foreign = value;
    }

    /// <summary>The kind of the current token.</summary>
    public HtmlTokenKind Kind => _kind;

    /// <summary>
    /// The offset of the first character of the current token in the text. For a tag, comment, DOCTYPE or
    /// processing instruction this is its <c>&lt;</c>; for characters, the first source character they came from.
    /// </summary>
    public int Start => _start;

    /// <summary>
    /// The offset after the last character of the current token. A tag ends after its <c>&gt;</c>; a comment after
    /// its closing <c>&gt;</c> (or at the end of the text when it was not closed); a CDATA section's text ends before
    /// the <c>]]&gt;</c>.
    /// </summary>
    public int End => _end;

    /// <summary>The offset of the next character to be read.</summary>
    public int Position => _pos;

    /// <summary>
    /// The text of a <see cref="HtmlTokenKind.Character"/> token, the data of a comment, or the data of a
    /// processing instruction. Empty for other kinds.
    /// </summary>
    public ReadOnlySpan<char> Data => Resolve(_data);

    /// <summary>
    /// The lowercase tag name of a tag token, the name of a DOCTYPE (see <see cref="HasDoctypeName"/>), or the target
    /// of a processing instruction (as written). Empty for other kinds.
    /// </summary>
    public ReadOnlySpan<char> Name => Resolve(_name);

    /// <summary>The offset of the first character of the name as written in the source (tag, DOCTYPE or processing instruction).</summary>
    public int NameStart => _name.SourceStart;

    /// <summary>The offset after the last character of the name as written in the source.</summary>
    public int NameEnd => _name.SourceEnd;

    /// <summary>True for a start tag that ended in <c>/&gt;</c>. The tree builder acknowledges it for void and foreign elements.</summary>
    public bool SelfClosing => _selfClosing;

    /// <summary>For a DOCTYPE: false when the name is missing, as opposed to empty.</summary>
    public bool HasDoctypeName => _hasName;

    /// <summary>For a DOCTYPE: the public identifier, or <see langword="null"/> when missing.</summary>
    public string? PublicId => _publicId;

    /// <summary>For a DOCTYPE: the system identifier, or <see langword="null"/> when missing.</summary>
    public string? SystemId => _systemId;

    /// <summary>For a DOCTYPE: the force-quirks flag.</summary>
    public bool ForceQuirks => _forceQuirks;

    /// <summary>
    /// The name of the last start tag emitted, which decides whether an end tag in RCDATA, RAWTEXT or script data
    /// is "appropriate". Settable for a tokenizer started in one of those states (for example a fragment parse
    /// whose context element is <c>title</c>). Names that are not made only of ASCII letters match nothing.
    /// </summary>
    public string? LastStartTagName
    {
        get => _lastStartLength == 0 ? null : new string(_lastStart, 0, _lastStartLength);
        set
        {
            _lastStartLength = 0;
            _lastStartIsLetters = false;
            if (value is null)
            {
                return;
            }

            if (value.Length > MaxTagNameLetters)
            {
                return;
            }

            _lastStartIsLetters = true;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c is >= 'A' and <= 'Z')
                {
                    c = (char)(c + 0x20);
                }

                _lastStart[i] = c;
                if (c is not (>= 'a' and <= 'z'))
                {
                    _lastStartIsLetters = false;
                }
            }

            _lastStartLength = value.Length;
        }
    }

    /// <summary>Switches the tokenizer state, as the tree builder does after certain start tags. Ignored once the end of the input was reached.</summary>
    /// <param name="state">The state to continue in.</param>
    public void SetState(TokenizerState state)
    {
        if (_state == State.Eof)
        {
            return;
        }

        _state = state switch
        {
            TokenizerState.Data => State.Data,
            TokenizerState.Rcdata => State.Rcdata,
            TokenizerState.Rawtext => State.Rawtext,
            TokenizerState.ScriptData => State.ScriptData,
            TokenizerState.Plaintext => State.Plaintext,
            TokenizerState.CdataSection => State.CdataSection,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    /// <summary>The attributes of the current tag, in source order, without the duplicates the standard removes.</summary>
    public AttributeViewList Attributes => new(this, duplicates: false);

    /// <summary>
    /// The attributes of the current tag that were dropped because an earlier one had the same name (a
    /// <see cref="HtmlParseError.DuplicateAttribute"/> error). Kept so that every attribute has a source range.
    /// </summary>
    public AttributeViewList DuplicateAttributes => new(this, duplicates: true);

    /// <summary>Reads the next token.</summary>
    /// <param name="cancellationToken">Checked once per call.</param>
    /// <returns>The kind of the token, also in <see cref="Kind"/>. <see cref="HtmlTokenKind.EndOfFile"/> repeats forever.</returns>
    /// <exception cref="HtmlLimitExceededException">A tag has more than <see cref="ParseLimits.MaxAttributesPerTag"/> attributes.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public HtmlTokenKind Next(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _kind = HtmlTokenKind.None;
        _data = default;
        _name = default;
        _hasName = false;
        _selfClosing = false;
        _publicId = null;
        _systemId = null;
        _forceQuirks = false;
        _attrCount = 0;
        _dupCount = 0;
        _bufLen = 0;
        while (!Step())
        {
        }

        if (_errors is not null)
        {
            FlushInputStreamErrors(_state == State.Eof ? int.MaxValue : _pos - 1);
        }

        return _kind;
    }

    private ReadOnlySpan<char> Resolve(TextRef r) =>
        r.InBuffer ? _buf.AsSpan(r.Start, r.Length) : _text.AsSpan(r.Start, r.Length);

    // Runs one step of the state machine. Returns true when a token has been produced.
    private bool Step()
    {
        switch (_state)
        {
            case State.Eof:
                _kind = HtmlTokenKind.EndOfFile;
                _start = _end = _text.Length;
                _pos = _text.Length;
                return true;
            default:
                return TextStep();
        }
    }

    private void ReportError(HtmlParseError error, int offset)
    {
        if (_errors is null)
        {
            return;
        }

        // An input-stream error on the character being read is reported before the tokenizer's error about it.
        FlushInputStreamErrors(offset);
        _errors.Add(new ParseError(error, offset));
    }

    // Input-stream errors (HTML 13.2.3.5) are found up front and merged by offset into the error list.
    private void FlushInputStreamErrors(int upToOffset)
    {
        if (_preErrors is null)
        {
            return;
        }

        while (_preErrorIndex < _preErrors.Count && _preErrors[_preErrorIndex].Offset <= upToOffset)
        {
            _errors!.Add(_preErrors[_preErrorIndex++]);
        }

        if (_preErrorIndex >= _preErrors.Count)
        {
            _preErrors = null;
        }
    }

    private void EnsureBuffer(int extra)
    {
        long needed = (long)_bufLen + extra;
        if (needed <= _buf.Length)
        {
            return;
        }

        // The buffer holds names and values of one token, each no longer than the source text it came from.
        long size = Math.Max(needed, (long)_buf.Length * 2);
        Array.Resize(ref _buf, (int)Math.Min(size, Array.MaxLength));
    }

    private void AppendBuffer(ReadOnlySpan<char> text)
    {
        EnsureBuffer(text.Length);
        text.CopyTo(_buf.AsSpan(_bufLen));
        _bufLen += text.Length;
    }

    private void AppendBuffer(char c)
    {
        EnsureBuffer(1);
        _buf[_bufLen++] = c;
    }

    private enum State
    {
        Data,
        Rcdata,
        Rawtext,
        ScriptData,
        Plaintext,
        CdataSection,
        ScriptDataEscaped,
        ScriptDataEscapedDash,
        ScriptDataEscapedDashDash,
        ScriptDataDoubleEscaped,
        ScriptDataDoubleEscapedDash,
        ScriptDataDoubleEscapedDashDash,
        Eof,
    }

    // A piece of text: a range of the source, or of the scratch buffer.
    internal struct TextRef
    {
        public int Start;
        public int Length;
        public bool InBuffer;
        public int SourceStart;
        public int SourceEnd;
    }
}

/// <summary>Options for <see cref="HtmlTokenizer"/>.</summary>
public sealed class HtmlTokenizerOptions
{
    /// <summary>
    /// Whether to collect parse errors in <see cref="HtmlTokenizer.Errors"/>. Default <see langword="false"/>; turning
    /// it on makes the tokenizer slower, because it also checks the whole input for the input-stream errors.
    /// </summary>
    public bool RecordErrors { get; init; }

    /// <summary>The limits; default the limits the <see cref="SourceText"/> was decoded under.</summary>
    public ParseLimits? Limits { get; init; }
}
