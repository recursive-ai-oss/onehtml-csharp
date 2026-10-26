using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// Language tags as RFC 5646 (BCP 47) defines them: whether a tag is well-formed, and whether its primary language subtag is
/// registered in the IANA Language Subtag Registry. Comparisons are ASCII case-insensitive, as the RFC requires.
/// </summary>
internal static class LanguageTag
{
    private static readonly Lazy<Registry> s_registry = new(LoadRegistry);

    // RFC 5646 section 2.2.8 "irregular" grandfathered tags: well-formed only as whole tags, because their pieces do not follow the grammar.
    private static readonly string[] s_irregular =
    [
        "en-gb-oed", "i-ami", "i-bnn", "i-default", "i-enochian", "i-hak", "i-klingon", "i-lux", "i-mingo", "i-navajo", "i-pwn", "i-tao",
        "i-tay", "i-tsu", "sgn-be-fr", "sgn-be-nl", "sgn-ch-de",
    ];

    /// <summary>The registry's File-Date, the pin of the language data.</summary>
    public static string RegistryDate => s_registry.Value.FileDate;

    /// <summary>
    /// The primary language subtag: the part before the first hyphen, with ASCII whitespace trimmed around the tag. Empty when the tag is empty.
    /// </summary>
    public static string Primary(string tag)
    {
        ReadOnlySpan<char> t = Trim(tag);
        int dash = t.IndexOf('-');
        return (dash < 0 ? t : t[..dash]).ToString();
    }

    /// <summary>True when the primary language subtag is a registered language (or in the private-use block qaa-qtz).</summary>
    /// <remarks>
    /// This is the "known primary language tag" of the ACT language rules. It does not look at the rest of the tag, so
    /// <c>en-US-GB</c> is known, while <c>eng</c> (registered only for languages that have no two-letter code), <c>i-lux</c> and <c>dutch</c> are not.
    /// </remarks>
    public static bool IsKnownPrimary(string tag)
    {
        string p = Primary(tag);
        if (p.Length < 2 || p.Length > 8)
        {
            return false;
        }

        Span<char> lower = stackalloc char[8];
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            if (c is >= 'A' and <= 'Z')
            {
                c = (char)(c + 32);
            }
            else if (c is not (>= 'a' and <= 'z'))
            {
                return false;
            }

            lower[i] = c;
        }

        return s_registry.Value.Contains(lower[..p.Length]);
    }

    /// <summary>True when the tag matches the Language-Tag production of RFC 5646 section 2.1 (syntax only; subtags are not looked up).</summary>
    public static bool IsWellFormed(string tag)
    {
        ReadOnlySpan<char> t = Trim(tag);
        if (t.Length == 0)
        {
            return false;
        }

        foreach (string irregular in s_irregular)
        {
            if (t.Equals(irregular, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string[] parts = t.ToString().Split('-');
        foreach (string p in parts)
        {
            if (p.Length is < 1 or > 8 || !AllAlnum(p))
            {
                return false;
            }
        }

        int i = 0;
        if (parts[0].Length == 1 && parts[0][0] is 'x' or 'X')
        {
            return PrivateUse(parts, 0);
        }

        // language = 2*3ALPHA ["-" extlang] / 4ALPHA / 5*8ALPHA
        string lang = parts[i++];
        if (!AllAlpha(lang) || lang.Length < 2)
        {
            return false;
        }

        if (lang.Length <= 3)
        {
            for (int e = 0; e < 3 && i < parts.Length && parts[i].Length == 3 && AllAlpha(parts[i]); e++)
            {
                i++;
            }
        }

        // script = 4ALPHA
        if (i < parts.Length && parts[i].Length == 4 && AllAlpha(parts[i]))
        {
            i++;
        }

        // region = 2ALPHA / 3DIGIT
        if (i < parts.Length && (parts[i].Length == 2 && AllAlpha(parts[i]) || parts[i].Length == 3 && AllDigit(parts[i])))
        {
            i++;
        }

        // variant = 5*8alphanum / (DIGIT 3alphanum)
        while (i < parts.Length && (parts[i].Length >= 5 || parts[i].Length == 4 && parts[i][0] is >= '0' and <= '9'))
        {
            i++;
        }

        // extension = singleton 1*("-" (2*8alphanum)); singleton is any letter or digit except x
        while (i < parts.Length && parts[i].Length == 1 && parts[i][0] is not ('x' or 'X'))
        {
            i++;
            int start = i;
            while (i < parts.Length && parts[i].Length >= 2)
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }
        }

        if (i == parts.Length)
        {
            return true;
        }

        return parts[i].Length == 1 && parts[i][0] is 'x' or 'X' && PrivateUse(parts, i);
    }

    private static bool PrivateUse(string[] parts, int x) => parts.Length > x + 1;

    private static ReadOnlySpan<char> Trim(string s) => s.AsSpan().Trim(" \t\n\f\r");

    private static bool AllAlpha(string s)
    {
        foreach (char c in s)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllDigit(string s)
    {
        foreach (char c in s)
        {
            if (c is not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllAlnum(string s)
    {
        foreach (char c in s)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Registry
    {
        private readonly HashSet<string> _languages;
        private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
        private readonly (string Low, string High)[] _ranges;

        public Registry(string fileDate, HashSet<string> languages, (string, string)[] ranges)
        {
            FileDate = fileDate;
            _languages = languages;
            _lookup = languages.GetAlternateLookup<ReadOnlySpan<char>>();
            _ranges = ranges;
        }

        public string FileDate { get; }

        public bool Contains(ReadOnlySpan<char> lower)
        {
            if (_lookup.Contains(lower))
            {
                return true;
            }

            foreach ((string lo, string hi) in _ranges)
            {
                if (lower.Length == lo.Length && lower.CompareTo(lo, StringComparison.Ordinal) >= 0 && lower.CompareTo(hi, StringComparison.Ordinal) <= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static Registry LoadRegistry()
    {
        Assembly assembly = typeof(LanguageTag).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("Rules.language-subtags.json")
            ?? throw new InvalidOperationException("Missing embedded data: language-subtags.json");
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement e in root.GetProperty("languages").EnumerateArray())
        {
            set.Add(e.GetString()!);
        }

        var ranges = new List<(string, string)>();
        foreach (JsonElement r in root.GetProperty("ranges").EnumerateArray())
        {
            ranges.Add((r[0].GetString()!, r[1].GetString()!));
        }

        return new Registry(root.GetProperty("source").GetProperty("fileDate").GetString()!, set, [.. ranges]);
    }
}
