using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>Reads the embedded reference data files (<c>Catalogue/Data/*.json</c>). Each file has a <c>version</c> field.</summary>
internal static class CatalogueData
{
    private const int SupportedVersion = 1;

    /// <summary>Parses the named embedded file. The caller disposes the document.</summary>
    internal static JsonDocument Open(string fileName)
    {
        Assembly assembly = typeof(CatalogueData).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("Catalogue." + fileName)
            ?? throw new InvalidOperationException("Missing embedded catalogue data: " + fileName);
        JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.GetProperty("version").GetInt32() != SupportedVersion)
        {
            document.Dispose();
            throw new InvalidOperationException("Unsupported version of catalogue data: " + fileName);
        }

        return document;
    }

    internal static string[] Strings(JsonElement array)
    {
        var result = new string[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            result[i++] = item.GetString()!;
        }

        return result;
    }

    internal static WcagVersion ParseVersion(string text) => text switch
    {
        "2.0" => WcagVersion.Wcag20,
        "2.1" => WcagVersion.Wcag21,
        "2.2" => WcagVersion.Wcag22,
        _ => throw new InvalidOperationException("Unknown WCAG version: " + text),
    };

    /// <summary>Compares dotted numbers such as "1.4.10" and "1.4.3" numerically, part by part.</summary>
    internal static int CompareNumbers(string a, string b)
    {
        string[] x = a.Split('.');
        string[] y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            int c = int.Parse(x[i], CultureInfo.InvariantCulture).CompareTo(int.Parse(y[i], CultureInfo.InvariantCulture));
            if (c != 0)
            {
                return c;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
