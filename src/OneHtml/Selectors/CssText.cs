using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>Serialization helpers shared by the selector serializer and the locator generator.</summary>
internal static class CssText
{
    /// <summary>
    /// CSSOM "serialize an identifier", with one addition: a code point of U+0080 or above that CSS Syntax does not allow in an
    /// identifier (U+00BA, say, which only an escape can put in a name) is escaped too. CSSOM writes it as is, and the text
    /// would then not parse back to the same identifier.
    /// </summary>
    public static string Ident(string value)
    {
        string text = CssSerializer.SerializeIdentifier(value);
        bool needs = false;
        foreach (char c in text)
        {
            if (c >= 0x80 && !CssChars.IsNonAsciiIdent(c))
            {
                needs = true;
                break;
            }
        }

        if (!needs)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (c >= 0x80 && !CssChars.IsNonAsciiIdent(c))
            {
                sb.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
