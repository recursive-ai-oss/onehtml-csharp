using System;

namespace RecursiveAI.OneHtml.Encodings;

/// <summary>
/// An encoding from the WHATWG Encoding Standard. Instances are singletons: compare with <c>==</c>.
/// The static members (<see cref="Utf8"/>, <see cref="Windows1252"/>, ...) and <see cref="All"/> are generated
/// from the standard's <c>encodings.json</c>.
/// </summary>
public sealed partial class WhatwgEncoding
{
    private const int MaxLabelLength = 64;

    internal WhatwgEncoding(string name, EncodingKind kind, int table)
    {
        Name = name;
        Kind = kind;
        Table = table;
    }

    /// <summary>The encoding's name as the Encoding Standard spells it, for example <c>windows-1252</c> or <c>Shift_JIS</c>.</summary>
    public string Name { get; }

    /// <summary>True for UTF-16BE and UTF-16LE.</summary>
    public bool IsUtf16 => Kind is EncodingKind.Utf16Be or EncodingKind.Utf16Le;

    internal EncodingKind Kind { get; }

    internal int Table { get; }

    /// <summary>
    /// The "get an encoding" algorithm: strips leading and trailing ASCII whitespace, compares ASCII
    /// case-insensitively with the standard's label table, and returns <see langword="null"/> for an unknown label
    /// (the standard's "failure").
    /// </summary>
    /// <param name="label">A label such as <c>latin1</c>, <c>UTF8</c> or <c> Shift_JIS </c>.</param>
    public static WhatwgEncoding? ForLabel(ReadOnlySpan<char> label)
    {
        // Encoding Standard 4.2 "get an encoding"
        int start = 0;
        int end = label.Length;
        while (start < end && IsAsciiWhitespace(label[start]))
        {
            start++;
        }

        while (end > start && IsAsciiWhitespace(label[end - 1]))
        {
            end--;
        }

        int length = end - start;
        if (length == 0 || length > MaxLabelLength)
        {
            return null;
        }

        Span<char> lower = stackalloc char[MaxLabelLength];
        for (int i = 0; i < length; i++)
        {
            char c = label[start + i];
            lower[i] = c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c;
        }

        return LookupLowerLabel(lower[..length]);
    }

    /// <inheritdoc />
    public override string ToString() => Name;

    private static bool IsAsciiWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';
}
