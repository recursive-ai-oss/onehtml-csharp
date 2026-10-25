using System;
using System.Text;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// The WHATWG Encoding services CSS Syntax §3.2 needs. Implemented by the encoding layer; kept as an interface so
/// the stylesheet parser does not depend on it.
/// </summary>
public interface ICssEncodingProvider
{
    /// <summary>"Get an encoding" from a label: the canonical encoding name (for example <c>utf-8</c>, <c>utf-16le</c>), or <c>null</c> for an unknown label.</summary>
    /// <param name="label">An encoding label.</param>
    string? GetEncodingName(string label);

    /// <summary>
    /// The Encoding Standard's "decode": a byte order mark wins over <paramref name="fallbackEncodingName"/>, which is
    /// used only when there is none. Malformed sequences become U+FFFD.
    /// </summary>
    /// <param name="bytes">The stylesheet bytes.</param>
    /// <param name="fallbackEncodingName">A canonical encoding name returned by <see cref="GetEncodingName"/>.</param>
    string Decode(ReadOnlySpan<byte> bytes, string fallbackEncodingName);
}

/// <summary>CSS Syntax §3.2 "determine the fallback encoding of a stylesheet".</summary>
public static class CssEncoding
{
    private static ReadOnlySpan<byte> CharsetPrefix => "@charset \""u8;

    /// <summary>
    /// Determines the fallback encoding: the protocol's label if it is known, else a leading <c>@charset "..."; </c>
    /// declaration (exactly that byte form, in the first 1024 bytes), else the environment's label, else UTF-8.
    /// </summary>
    /// <param name="bytes">The stylesheet bytes.</param>
    /// <param name="protocolLabel">Encoding label from the transport (for example the <c>charset</c> of a <c>Content-Type</c>), or null.</param>
    /// <param name="environmentLabel">The referring document's encoding label, or null.</param>
    /// <param name="provider">Encoding label lookup.</param>
    /// <returns>A canonical encoding name.</returns>
    public static string DetermineFallbackEncoding(ReadOnlySpan<byte> bytes, string? protocolLabel, string? environmentLabel, ICssEncodingProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (protocolLabel is not null && provider.GetEncodingName(protocolLabel) is { } fromProtocol)
        {
            return fromProtocol;
        }

        ReadOnlySpan<byte> head = bytes.Length > 1024 ? bytes[..1024] : bytes;
        if (head.StartsWith(CharsetPrefix))
        {
            int i = CharsetPrefix.Length;
            int start = i;
            while (i < head.Length && (head[i] <= 0x21 || (head[i] >= 0x23 && head[i] <= 0x7F)))
            {
                i++;
            }

            if (i + 1 < head.Length && head[i] == 0x22 && head[i + 1] == 0x3B)
            {
                string label = Encoding.ASCII.GetString(head[start..i]);
                string? name = provider.GetEncodingName(label);
                if (name is not null)
                {
                    // The declaration is ASCII, so a UTF-16 label must be a lie.
                    return name is "utf-16be" or "utf-16le" ? "utf-8" : name;
                }
            }
        }

        if (environmentLabel is not null && provider.GetEncodingName(environmentLabel) is { } fromEnvironment)
        {
            return fromEnvironment;
        }

        return "utf-8";
    }
}

/// <summary>
/// An immutable stylesheet: the source text and its parse. Rules are <see cref="CssNode"/> views over flat arrays;
/// every rule, declaration, token and comment has a source range, and the unedited sheet serializes back to the exact
/// source text.
/// </summary>
public sealed class Stylesheet
{
    private Stylesheet(CssTree tree, string? location)
    {
        Tree = tree;
        Location = location;
    }

    /// <summary>The parse.</summary>
    public CssTree Tree { get; }

    /// <summary>The URL or name the caller gave the sheet, or <c>null</c>. The library never fetches it.</summary>
    public string? Location { get; }

    /// <summary>The decoded source text; all ranges index into it.</summary>
    public string Text => Tree.Text;

    /// <summary>
    /// The top-level nodes in source order. Qualified rules here are style rules; nodes of kind
    /// <see cref="CssNodeKind.Invalid"/> mark discarded source.
    /// </summary>
    public CssNodeList Rules => Tree.Nodes;

    /// <summary>Parses decoded stylesheet text (CSS Syntax §5.4.3). Malformed CSS never throws; see <see cref="CssParseOptions"/> for the limits that do.</summary>
    /// <param name="text">The stylesheet text, already decoded to UTF-16.</param>
    /// <param name="location">Optional URL or name, stored as <see cref="Location"/>.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <exception cref="CssLimitExceededException">A limit was exceeded.</exception>
    public static Stylesheet Parse(string text, string? location = null, CssParseOptions? options = null, CancellationToken cancellationToken = default)
        => new(CssTree.ParseStylesheet(text, options, cancellationToken), location);

    /// <summary>
    /// Decodes stylesheet bytes (CSS Syntax §3.2) with the given encoding services, then parses them. The text is
    /// decoded once and ranges refer to the decoded text.
    /// </summary>
    /// <param name="bytes">The stylesheet bytes.</param>
    /// <param name="provider">Encoding label lookup and decoder.</param>
    /// <param name="protocolEncodingLabel">Encoding label from the transport, or null.</param>
    /// <param name="environmentEncodingLabel">The referring document's encoding label, or null.</param>
    /// <param name="location">Optional URL or name, stored as <see cref="Location"/>.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static Stylesheet Parse(ReadOnlySpan<byte> bytes, ICssEncodingProvider provider, string? protocolEncodingLabel = null,
        string? environmentEncodingLabel = null, string? location = null, CssParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        CssParseOptions o = options ?? CssParseOptions.Default;
        if (bytes.Length > o.MaxInputLength * 4L)
        {
            throw new CssLimitExceededException(CssLimit.InputLength, o.MaxInputLength);
        }

        string fallback = CssEncoding.DetermineFallbackEncoding(bytes, protocolEncodingLabel, environmentEncodingLabel, provider);
        return Parse(provider.Decode(bytes, fallback), location, o, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the source text from the tokens and comments. A parse covers every character of the text with exactly
    /// one token or comment, so the result equals <see cref="Text"/>; this method checks that and throws if it does not.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tokens and comments do not tile the text (a bug).</exception>
    public string ToSourceText() => CssSourceWriter.Write(Tree);

    /// <summary>Serializes the sheet from its structure alone (see <see cref="CssSerializer"/>), not from the source text.</summary>
    public string Serialize() => CssSerializer.Serialize(Tree);
}

/// <summary>Rebuilds source text from a parse's tokens and comments.</summary>
public static class CssSourceWriter
{
    /// <summary>Rebuilds the source text of <paramref name="tree"/> by concatenating its tokens and comments in order.</summary>
    /// <param name="tree">A parse.</param>
    /// <returns>The text; always equal to <see cref="CssTree.Text"/>.</returns>
    /// <exception cref="InvalidOperationException">The tokens and comments do not tile the text (a bug).</exception>
    public static string Write(CssTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        string text = tree.Text;
        var sb = new StringBuilder(text.Length);
        int pos = 0;
        int ti = 0;
        int ci = 0;
        while (ti < tree.MainCount || ci < tree.CommentCount)
        {
            int start;
            int end;
            if (ci >= tree.CommentCount || (ti < tree.MainCount && tree.Tokens[ti].Start < tree.CommentOffsets[2 * ci]))
            {
                start = tree.Tokens[ti].Start;
                end = tree.Tokens[ti].End;
                ti++;
            }
            else
            {
                start = tree.CommentOffsets[2 * ci];
                end = tree.CommentOffsets[(2 * ci) + 1];
                ci++;
            }

            if (start != pos)
            {
                throw new InvalidOperationException("The CSS tokens and comments do not cover the text contiguously.");
            }

            sb.Append(text, start, end - start);
            pos = end;
        }

        if (pos != text.Length)
        {
            throw new InvalidOperationException("The CSS tokens and comments do not cover the whole text.");
        }

        return sb.ToString();
    }
}
