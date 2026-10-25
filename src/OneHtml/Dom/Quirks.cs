using System;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>The quirks mode a DOCTYPE selects (HTML 13.2.6.4.1, the "initial" insertion mode).</summary>
internal static class Quirks
{
    // The public identifiers that put the document in quirks mode when the identifier starts with them. Compared
    // ASCII case-insensitively.
    private static readonly string[] s_quirksPublicPrefixes =
    [
        "+//Silmaril//dtd html Pro v0r11 19970101//",
        "-//AS//DTD HTML 3.0 asWedit + extensions//",
        "-//AdvaSoft Ltd//DTD HTML 3.0 asWedit + extensions//",
        "-//IETF//DTD HTML 2.0 Level 1//",
        "-//IETF//DTD HTML 2.0 Level 2//",
        "-//IETF//DTD HTML 2.0 Strict Level 1//",
        "-//IETF//DTD HTML 2.0 Strict Level 2//",
        "-//IETF//DTD HTML 2.0 Strict//",
        "-//IETF//DTD HTML 2.0//",
        "-//IETF//DTD HTML 2.1E//",
        "-//IETF//DTD HTML 3.0//",
        "-//IETF//DTD HTML 3.2 Final//",
        "-//IETF//DTD HTML 3.2//",
        "-//IETF//DTD HTML 3//",
        "-//IETF//DTD HTML Level 0//",
        "-//IETF//DTD HTML Level 1//",
        "-//IETF//DTD HTML Level 2//",
        "-//IETF//DTD HTML Level 3//",
        "-//IETF//DTD HTML Strict Level 0//",
        "-//IETF//DTD HTML Strict Level 1//",
        "-//IETF//DTD HTML Strict Level 2//",
        "-//IETF//DTD HTML Strict Level 3//",
        "-//IETF//DTD HTML Strict//",
        "-//IETF//DTD HTML//",
        "-//Metrius//DTD Metrius Presentational//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 2.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 2.0 Tables//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML Strict//",
        "-//Microsoft//DTD Internet Explorer 3.0 HTML//",
        "-//Microsoft//DTD Internet Explorer 3.0 Tables//",
        "-//Netscape Comm. Corp.//DTD HTML//",
        "-//Netscape Comm. Corp.//DTD Strict HTML//",
        "-//O'Reilly and Associates//DTD HTML 2.0//",
        "-//O'Reilly and Associates//DTD HTML Extended 1.0//",
        "-//O'Reilly and Associates//DTD HTML Extended Relaxed 1.0//",
        "-//SQ//DTD HTML 2.0 HoTMetaL + extensions//",
        "-//SoftQuad Software//DTD HoTMetaL PRO 6.0::19990601::extensions to HTML 4.0//",
        "-//SoftQuad//DTD HoTMetaL PRO 4.0::19971010::extensions to HTML 4.0//",
        "-//Spyglass//DTD HTML 2.0 Extended//",
        "-//Sun Microsystems Corp.//DTD HotJava HTML//",
        "-//Sun Microsystems Corp.//DTD HotJava Strict HTML//",
        "-//W3C//DTD HTML 3 1995-03-24//",
        "-//W3C//DTD HTML 3.2 Draft//",
        "-//W3C//DTD HTML 3.2 Final//",
        "-//W3C//DTD HTML 3.2//",
        "-//W3C//DTD HTML 3.2S Draft//",
        "-//W3C//DTD HTML 4.0 Frameset//",
        "-//W3C//DTD HTML 4.0 Transitional//",
        "-//W3C//DTD HTML Experimental 19960712//",
        "-//W3C//DTD HTML Experimental 970421//",
        "-//W3C//DTD W3 HTML//",
        "-//W3O//DTD W3 HTML 3.0//",
        "-//WebTechs//DTD Mozilla HTML 2.0//",
        "-//WebTechs//DTD Mozilla HTML//",
    ];

    /// <summary>Computes the mode for a DOCTYPE token.</summary>
    /// <param name="forceQuirks">The token's force-quirks flag.</param>
    /// <param name="name">The token's name (empty when missing).</param>
    /// <param name="publicId">The public identifier, or null when missing.</param>
    /// <param name="systemId">The system identifier, or null when missing.</param>
    public static QuirksMode FromDoctype(bool forceQuirks, ReadOnlySpan<char> name, string? publicId, string? systemId)
    {
        ReadOnlySpan<char> pub = publicId;
        ReadOnlySpan<char> sys = systemId;
        if (forceQuirks || !name.SequenceEqual("html"))
        {
            return QuirksMode.Quirks;
        }

        if (publicId is not null
            && (AsciiEquals(pub, "-//W3O//DTD W3 HTML Strict 3.0//EN//")
                || AsciiEquals(pub, "-/W3C/DTD HTML 4.0 Transitional/EN")
                || AsciiEquals(pub, "HTML")))
        {
            return QuirksMode.Quirks;
        }

        if (systemId is not null && AsciiEquals(sys, "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd"))
        {
            return QuirksMode.Quirks;
        }

        if (publicId is not null)
        {
            foreach (string prefix in s_quirksPublicPrefixes)
            {
                if (AsciiStartsWith(pub, prefix))
                {
                    return QuirksMode.Quirks;
                }
            }

            bool sysMissingOrEmpty = systemId is null || systemId.Length == 0;
            bool html401 = AsciiStartsWith(pub, "-//W3C//DTD HTML 4.01 Frameset//") || AsciiStartsWith(pub, "-//W3C//DTD HTML 4.01 Transitional//");
            if (sysMissingOrEmpty && html401)
            {
                return QuirksMode.Quirks;
            }

            if (AsciiStartsWith(pub, "-//W3C//DTD XHTML 1.0 Frameset//") || AsciiStartsWith(pub, "-//W3C//DTD XHTML 1.0 Transitional//"))
            {
                return QuirksMode.LimitedQuirks;
            }

            if (!sysMissingOrEmpty && html401)
            {
                return QuirksMode.LimitedQuirks;
            }
        }

        return QuirksMode.NoQuirks;
    }

    public static bool AsciiEquals(ReadOnlySpan<char> s, string expected) =>
        s.Length == expected.Length && AsciiStartsWith(s, expected);

    public static bool AsciiStartsWith(ReadOnlySpan<char> s, string prefix)
    {
        if (s.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (Lower(s[i]) != Lower(prefix[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static char Lower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c;
}
