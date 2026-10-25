using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>One stored response body from a corpus (or one loose file).</summary>
/// <param name="Url">The URL it was fetched from, or the file name for loose files.</param>
/// <param name="IsStylesheet">True for a stylesheet, false for an HTML page.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the body.</param>
/// <param name="Path">Where the body is stored.</param>
/// <param name="Bytes">Body length in bytes.</param>
/// <param name="ContentType">The response's <c>Content-Type</c> header, if known.</param>
internal sealed record CorpusItem(string Url, bool IsStylesheet, string Sha256, string Path, long Bytes, string? ContentType);

/// <summary>
/// Reads a corpus directory. A directory with a <c>manifest.jsonl</c> is a corpus version
/// (<c>bodies/ab/&lt;sha256&gt;</c> files, one manifest record per response); any other directory is read as loose files
/// (<c>.css</c> is a stylesheet, everything else a page), which lets the tools run over test inputs.
/// </summary>
internal static class Corpus
{
    /// <summary>Loads the items to process: 2xx pages and stylesheets that have a stored body, ordered by SHA-256 then URL.</summary>
    public static List<CorpusItem> Load(string dir, bool pages = true, bool stylesheets = true)
    {
        string manifest = System.IO.Path.Combine(dir, "manifest.jsonl");
        List<CorpusItem> items = File.Exists(manifest) ? LoadManifest(dir, manifest) : LoadLooseFiles(dir);
        items.RemoveAll(i => i.IsStylesheet ? !stylesheets : !pages);
        items.Sort(static (a, b) =>
        {
            int c = string.CompareOrdinal(a.Sha256, b.Sha256);
            return c != 0 ? c : string.CompareOrdinal(a.Url, b.Url);
        });
        return items;
    }

    private static List<CorpusItem> LoadManifest(string dir, string manifest)
    {
        var items = new List<CorpusItem>();
        foreach (string line in File.ReadLines(manifest))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement r = doc.RootElement;
            string kind = Str(r, "kind") ?? string.Empty;
            if (kind is not ("page" or "stylesheet"))
            {
                continue;
            }

            if (!r.TryGetProperty("status", out JsonElement st) || st.ValueKind != JsonValueKind.Number || st.GetInt32() is < 200 or > 299)
            {
                continue;
            }

            string? sha = Str(r, "body_sha256");
            if (sha is null || sha.Length < 3)
            {
                continue;
            }

            string path = System.IO.Path.Combine(dir, "bodies", sha[..2], sha);
            long bytes = r.TryGetProperty("body_bytes", out JsonElement bb) && bb.ValueKind == JsonValueKind.Number ? bb.GetInt64() : -1;
            items.Add(new CorpusItem(Str(r, "url") ?? sha, kind == "stylesheet", sha, path, bytes, Str(r, "content_type")));
        }

        return items;
    }

    private static List<CorpusItem> LoadLooseFiles(string dir)
    {
        var items = new List<CorpusItem>();
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            if (info.Length > 64 * 1024 * 1024)
            {
                continue;
            }

            string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            bool css = file.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
            items.Add(new CorpusItem(System.IO.Path.GetRelativePath(dir, file), css, sha, file, info.Length, null));
        }

        return items;
    }

    private static string? Str(JsonElement r, string name) =>
        r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Formats a count as a rate with a percentage, for summaries.</summary>
    public static string Pct(long part, long whole) =>
        whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.###", CultureInfo.InvariantCulture) + "%";
}
