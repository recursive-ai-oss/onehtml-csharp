using System;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// Resolves a URL reference against a base URL the way the URL Standard's basic URL parser serializes it for the special schemes
/// (<c>http</c>, <c>https</c>, <c>ws</c>, <c>wss</c>, <c>ftp</c>): scheme and host lower-cased, default port dropped, dot segments removed, an empty
/// path written as <c>/</c>, characters outside the component's percent-encode set written as upper-case <c>%XX</c> of their UTF-8 bytes, and
/// existing escapes left exactly as written (so <c>%e3</c> stays <c>%e3</c>). Other schemes, and references that cannot be resolved, come back
/// unchanged. It never touches the network; the result is a string only.
/// </summary>
internal static class UrlResolver
{
    private sealed class Parts
    {
        public string Scheme = string.Empty;
        public string UserInfo = string.Empty;
        public string Host = string.Empty;
        public string Port = string.Empty;
        public string Path = string.Empty;
        public string? Query;
        public string? Fragment;
    }

    /// <summary>Resolves <paramref name="reference"/> against <paramref name="baseUrl"/> (which may be null).</summary>
    public static string Resolve(string? baseUrl, string reference)
    {
        string s = Clean(reference);
        if (TryScheme(s, out string scheme))
        {
            if (!IsSpecial(scheme))
            {
                return s;
            }

            return Serialize(ParseSpecial(scheme, s[(scheme.Length + 1)..]));
        }

        if (baseUrl is null)
        {
            return s;
        }

        string b = Clean(baseUrl);
        if (!TryScheme(b, out string baseScheme) || !IsSpecial(baseScheme))
        {
            return s;
        }

        Parts bp = ParseSpecial(baseScheme, b[(baseScheme.Length + 1)..]);
        if (s.Length == 0)
        {
            bp.Fragment = null;
            return Serialize(bp);
        }

        if (IsSlash(s[0]) && s.Length > 1 && IsSlash(s[1]))
        {
            return Serialize(ParseSpecial(baseScheme, s));
        }

        var r = new Parts { Scheme = baseScheme, UserInfo = bp.UserInfo, Host = bp.Host, Port = bp.Port };
        SplitRest(s, out string path, out r.Query, out r.Fragment);
        if (s[0] == '#')
        {
            r.Path = bp.Path;
            r.Query = bp.Query;
        }
        else if (s[0] == '?')
        {
            r.Path = bp.Path;
        }
        else if (IsSlash(s[0]))
        {
            r.Path = path;
        }
        else
        {
            int slash = bp.Path.LastIndexOf('/');
            r.Path = (slash >= 0 ? bp.Path[..(slash + 1)] : "/") + path;
        }

        return Serialize(r);
    }

    private static string Clean(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && s[start] <= ' ')
        {
            start++;
        }

        while (end > start && s[end - 1] <= ' ')
        {
            end--;
        }

        var sb = new StringBuilder(end - start);
        for (int i = start; i < end; i++)
        {
            if (s[i] is not ('\t' or '\n' or '\r'))
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private static bool TryScheme(string s, out string scheme)
    {
        scheme = string.Empty;
        if (s.Length == 0 || !IsAsciiAlpha(s[0]))
        {
            return false;
        }

        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ':')
            {
                scheme = s[..i].ToLowerInvariant();
                return true;
            }

            if (!(IsAsciiAlpha(c) || (c is >= '0' and <= '9') || c is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsAsciiAlpha(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsSlash(char c) => c is '/' or '\\';

    private static bool IsSpecial(string scheme) => scheme is "http" or "https" or "ws" or "wss" or "ftp";

    private static string? DefaultPort(string scheme) => scheme switch { "http" or "ws" => "80", "https" or "wss" => "443", "ftp" => "21", _ => null };

    /// <summary>Parses what follows <c>scheme:</c> of a special URL: slashes, authority, path, query and fragment.</summary>
    private static Parts ParseSpecial(string scheme, string rest)
    {
        var p = new Parts { Scheme = scheme };
        int i = 0;
        while (i < rest.Length && IsSlash(rest[i]))
        {
            i++;
        }

        int authEnd = i;
        while (authEnd < rest.Length && !IsSlash(rest[authEnd]) && rest[authEnd] is not ('?' or '#'))
        {
            authEnd++;
        }

        string authority = rest[i..authEnd];
        int at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            p.UserInfo = authority[..at];
            authority = authority[(at + 1)..];
        }

        int colon = authority.LastIndexOf(':');
        if (colon >= 0 && authority.IndexOf(']', colon) < 0)
        {
            p.Port = authority[(colon + 1)..];
            authority = authority[..colon];
        }

        p.Host = NormalizeHost(authority);
        SplitRest(rest[authEnd..], out string path, out p.Query, out p.Fragment);
        p.Path = path;
        return p;
    }

    private static string NormalizeHost(string host)
    {
        bool ascii = true;
        foreach (char c in host)
        {
            if (c > 127)
            {
                ascii = false;
                break;
            }
        }

        if (!ascii)
        {
            try
            {
                return new IdnMapping().GetAscii(host).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return host;
            }
        }

        return host.ToLowerInvariant();
    }

    private static void SplitRest(string s, out string path, out string? query, out string? fragment)
    {
        query = null;
        fragment = null;
        int hash = s.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = s[(hash + 1)..];
            s = s[..hash];
        }

        int q = s.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            query = s[(q + 1)..];
            s = s[..q];
        }

        path = s;
    }

    private static string Serialize(Parts p)
    {
        var sb = new StringBuilder();
        sb.Append(p.Scheme).Append("://");
        if (p.UserInfo.Length > 0)
        {
            sb.Append(p.UserInfo).Append('@');
        }

        sb.Append(p.Host);
        if (p.Port.Length > 0 && p.Port != DefaultPort(p.Scheme))
        {
            sb.Append(':').Append(p.Port);
        }

        sb.Append(Encode(RemoveDotSegments(p.Path.Replace('\\', '/')), 0));
        if (p.Query is not null)
        {
            sb.Append('?').Append(Encode(p.Query, 1));
        }

        if (p.Fragment is not null)
        {
            sb.Append('#').Append(Encode(p.Fragment, 2));
        }

        return sb.ToString();
    }

    private static string RemoveDotSegments(string path)
    {
        if (path.Length == 0)
        {
            return "/";
        }

        var output = new System.Collections.Generic.List<string>();
        string[] segments = path.Split('/');
        for (int i = path.StartsWith('/') ? 1 : 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            bool last = i == segments.Length - 1;
            if (IsDot(seg))
            {
                if (last)
                {
                    output.Add(string.Empty);
                }
            }
            else if (IsDoubleDot(seg))
            {
                if (output.Count > 0)
                {
                    output.RemoveAt(output.Count - 1);
                }

                if (last)
                {
                    output.Add(string.Empty);
                }
            }
            else
            {
                output.Add(seg);
            }
        }

        return "/" + string.Join('/', output);
    }

    private static bool IsDot(string s) => s == "." || string.Equals(s, "%2e", StringComparison.OrdinalIgnoreCase);

    private static bool IsDoubleDot(string s) =>
        s == ".." || string.Equals(s, ".%2e", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "%2e.", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "%2e%2e", StringComparison.OrdinalIgnoreCase);

    /// <summary>Percent-encodes with the path (0), special query (1) or fragment (2) set of the URL Standard; existing escapes stay as written.</summary>
    private static string Encode(string s, int set)
    {
        StringBuilder? sb = null;
        Span<byte> bytes = stackalloc byte[8];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool encode = c <= ' ' || c >= '\u007F' || c is '"' or '<' or '>' || set switch
            {
                0 => c is '#' or '?' or '`' or '{' or '}',
                1 => c is '#' or '\'',
                _ => c == '`',
            };
            if (!encode)
            {
                sb?.Append(c);
                continue;
            }

            if (sb is null)
            {
                sb = new StringBuilder(s.Length + 8);
                sb.Append(s, 0, i);
            }

            int length = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            int n = Encoding.UTF8.GetBytes(s.AsSpan(i, length), bytes);
            for (int k = 0; k < n; k++)
            {
                sb.Append('%').Append(bytes[k].ToString("X2", CultureInfo.InvariantCulture));
            }

            i += length - 1;
        }

        return sb?.ToString() ?? s;
    }
}
