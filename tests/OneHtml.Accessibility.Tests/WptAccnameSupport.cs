using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Reads the JavaScript object and array literals the web-platform-tests pages use for their expectations (single or double quoted strings, bare keys, trailing commas).</summary>
internal static class JsLiteral
{
    public static JsonNode? Parse(string text)
    {
        int pos = 0;
        JsonNode? v = Value(text, ref pos);
        return v;
    }

    /// <summary>The text of the balanced <c>{…}</c> or <c>[…]</c> starting at <paramref name="start"/>.</summary>
    public static string Balanced(string text, int start)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
            }
            else if (c is '{' or '[')
            {
                depth++;
            }
            else if (c is '}' or ']')
            {
                if (--depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        throw new FormatException("unbalanced literal");
    }

    private static void Ws(string t, ref int p)
    {
        while (p < t.Length)
        {
            if (char.IsWhiteSpace(t[p]))
            {
                p++;
            }
            else if (t[p] == '/' && p + 1 < t.Length && t[p + 1] == '*')
            {
                int end = t.IndexOf("*/", p + 2, StringComparison.Ordinal);
                p = end < 0 ? t.Length : end + 2;
            }
            else if (t[p] == '/' && p + 1 < t.Length && t[p + 1] == '/')
            {
                while (p < t.Length && t[p] != '\n')
                {
                    p++;
                }
            }
            else
            {
                break;
            }
        }
    }

    private static JsonNode? Value(string t, ref int p)
    {
        Ws(t, ref p);
        char c = t[p];
        if (c == '{')
        {
            p++;
            var o = new JsonObject();
            while (true)
            {
                Ws(t, ref p);
                if (t[p] == '}')
                {
                    p++;
                    return o;
                }

                string key;
                if (t[p] is '\'' or '"')
                {
                    key = Str(t, ref p);
                }
                else
                {
                    int s = p;
                    while (p < t.Length && (char.IsLetterOrDigit(t[p]) || t[p] is '_' or '$'))
                    {
                        p++;
                    }

                    key = t[s..p];
                }

                Ws(t, ref p);
                p++; // ':'
                o[key] = Value(t, ref p);
                Ws(t, ref p);
                if (t[p] == ',')
                {
                    p++;
                }
            }
        }

        if (c == '[')
        {
            p++;
            var a = new JsonArray();
            while (true)
            {
                Ws(t, ref p);
                if (t[p] == ']')
                {
                    p++;
                    return a;
                }

                a.Add(Value(t, ref p));
                Ws(t, ref p);
                if (t[p] == ',')
                {
                    p++;
                }
            }
        }

        if (c is '\'' or '"')
        {
            return Str(t, ref p);
        }

        int st = p;
        while (p < t.Length && !(char.IsWhiteSpace(t[p]) || t[p] is ',' or '}' or ']'))
        {
            p++;
        }

        string word = t[st..p];
        if (word == "true")
        {
            return true;
        }

        if (word == "false")
        {
            return false;
        }

        if (word == "null")
        {
            return null;
        }

        return double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : word;
    }

    private static string Str(string t, ref int p)
    {
        char q = t[p++];
        var sb = new StringBuilder();
        while (t[p] != q)
        {
            if (t[p] == '\\')
            {
                p++;
                sb.Append(t[p] switch { 'n' => '\n', 't' => '\t', _ => t[p] });
            }
            else
            {
                sb.Append(t[p]);
            }

            p++;
        }

        p++;
        return sb.ToString();
    }
}

/// <summary>
/// Builds the markup that html-aam's <c>buildNameComputationTests</c> script builds into the page, step for step, so the
/// pages can be checked without running script. The markup is serialized and parsed, as the script's
/// <c>insertAdjacentHTML</c> does.
/// </summary>
internal static class AamAccnameGenerator
{
    private const string SampleImage = "data:image/png;base64,iVBORw0KGgo=";

    private static readonly Dictionary<string, string> NameValues = new()
    {
        ["from 1 aria-labelledby ref"] = "aria-labelledby ref element #1 text contents",
        ["from 2 aria-labelledby refs"] = "aria-labelledby ref element #2 text contents",
        ["from aria-label"] = "aria-label attribute value",
        ["from 1 label"] = "associated label #1 text contents",
        ["from 2 labels"] = "associated label #2 text contents",
        ["from subtree"] = "subtree text contents",
        ["from encapsulating label"] = "encapsulating label text contents",
        ["from first legend"] = "legend text contents",
        ["from first caption"] = "caption text contents",
        ["from value"] = "value attribute value",
        ["from label attribute"] = "label attribute value",
        ["from alt"] = "alt attribute value",
        ["from empty alt"] = string.Empty,
        ["from title"] = "title attribute value",
        ["from placeholder"] = "placeholder attribute value",
        ["from aria-placeholder"] = "aria-placeholder attribute value",
        ["from ancestor figure figcaption"] = "ancestor figure figcaption text contents",
    };

    private static readonly HashSet<string> Voids = new(StringComparer.Ordinal) { "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr" };

    private sealed class El
    {
        public El(string tag) => Tag = tag;

        public string Tag { get; }

        public List<(string Name, string Value)> Attrs { get; } = [];

        public List<object> Kids { get; } = [];

        public void Set(string name, string value)
        {
            int i = Attrs.FindIndex(a => a.Name == name);
            if (i >= 0)
            {
                Attrs[i] = (name, value);
            }
            else
            {
                Attrs.Add((name, value));
            }
        }

        public string Outer()
        {
            var sb = new StringBuilder();
            sb.Append('<').Append(Tag);
            foreach ((string n, string v) in Attrs)
            {
                sb.Append(' ').Append(n).Append("=\"").Append(v.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal)).Append('"');
            }

            sb.Append('>');
            if (Voids.Contains(Tag))
            {
                return sb.ToString();
            }

            foreach (object k in Kids)
            {
                switch (k)
                {
                    case El e:
                        sb.Append(e.Outer());
                        break;
                    case string s:
                        sb.Append(s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal));
                        break;
                    case Raw r:
                        sb.Append(r.Html);
                        break;
                }
            }

            sb.Append("</").Append(Tag).Append('>');
            return sb.ToString();
        }
    }

    private sealed record Raw(string Html);

    /// <summary>The page body for a page whose script is a <c>buildNameComputationTests</c> call, or null when the page has no such call.</summary>
    public static string? Generate(string pageHtml, string scriptText)
    {
        if (!scriptText.Contains("buildNameComputationTests", StringComparison.Ordinal))
        {
            return null;
        }

        var body = new StringBuilder();
        int id = 0;
        foreach (System.Text.RegularExpressions.Match cfg in System.Text.RegularExpressions.Regex.Matches(scriptText, @"testConfig\s*=\s*\{"))
        {
            int brace = cfg.Index + cfg.Length - 1;
            JsonObject config = (JsonObject)JsLiteral.Parse(JsLiteral.Balanced(scriptText, brace))!;
            var sources = new List<string>();
            foreach (JsonNode? s in config["nameSources"]!.AsArray())
            {
                sources.Add((string)s!);
            }

            foreach (KeyValuePair<string, JsonNode?> kv in (JsonObject)config["elements"]!)
            {
                List<string> remaining = [.. sources];
                while (remaining.Count > 0)
                {
                    body.Append(BuildOne(kv.Key, remaining, kv.Value as JsonObject, ++id));
                    remaining.RemoveAt(0);
                }
            }
        }

        return body.ToString();
    }

    private static string BuildOne(string selector, List<string> nameSources, JsonObject? props, int id)
    {
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(selector, "^(\\w+)(?:\\[(\\w+)=\"([\\w-]+)\"\\])?$");
        string element = m.Groups[1].Value;
        string expSource = nameSources[0];
        string nameTest = selector + " expecting name " + expSource;
        var pre = new StringBuilder("<h2>" + nameTest.Replace("<", "&lt;", StringComparison.Ordinal) + "</h2>");
        string expName = string.Empty;

        var test = new El(element);
        if (m.Groups[2].Success)
        {
            test.Set(m.Groups[2].Value, m.Groups[3].Value);
        }

        test.Set("id", "el-" + id);
        test.Set("class", "ex-label");
        test.Set("data-testname", nameTest);

        JsonArray? attrs = props?["attrs"]?.AsArray();
        bool Has(string a)
        {
            if (attrs is null)
            {
                return false;
            }

            foreach (JsonNode? n in attrs)
            {
                if ((string?)n == a)
                {
                    return true;
                }
            }

            return false;
        }

        if (Has("value"))
        {
            test.Set("value", element + " value attribute value");
        }

        if (Has("href"))
        {
            test.Set("href", "#");
        }

        if (Has("img src"))
        {
            test.Set("src", SampleImage);
        }

        El ret = test;
        if (props?["ancestors"] is JsonArray ancestors)
        {
            for (int i = ancestors.Count - 1; i >= 0; i--)
            {
                var a = new El((string)ancestors[i]!);
                a.Kids.Add(ret);
                ret = a;
            }
        }

        if (props?["descendants"] is JsonArray descendants)
        {
            string previous = element;
            El parent = test;
            foreach (JsonNode? d in descendants)
            {
                string tag = (string)d!;
                if (tag == "textNode")
                {
                    parent.Kids.Add(previous + " text node contents");
                }
                else
                {
                    var child = new El(tag);
                    parent.Kids.Add(child);
                    parent = child;
                }

                previous = tag;
            }
        }

        switch ((string?)props?["specialContext"])
        {
            case "area":
            {
                var map = new El("map");
                map.Set("name", "map-" + id);
                map.Kids.Add(test);
                var img = new El("img");
                img.Set("usemap", "#map-" + id);
                img.Set("src", SampleImage);
                test.Set("alt", "Mapped image alt attribute value");
                test.Set("shape", "rect");
                test.Set("coords", "0,0,20,20");
                ret = new El("div");
                ret.Kids.Add(map);
                ret.Kids.Add(img);
                break;
            }

            case "ruby":
                test.Kids.Add("ruby text node contents");
                AddWith(test, "rp", "rp #1");
                AddWith(test, "rt", "rt");
                AddWith(test, "rp", "rp #2");
                break;
            case "rp":
            {
                var ruby = new El("ruby");
                ruby.Kids.Add("ruby text node contents");
                ruby.Kids.Add(test);
                test.Kids.Add("rp #1");
                AddWith(ruby, "rt", "rt");
                AddWith(ruby, "rp", "rp #2");
                ret = ruby;
                break;
            }

            case "rt":
            {
                var ruby = new El("ruby");
                ruby.Kids.Add("ruby text node contents");
                AddWith(ruby, "rp", "rp #1");
                ruby.Kids.Add(test);
                test.Kids.Add("rt");
                AddWith(ruby, "rp", "rp #2");
                ret = ruby;
                break;
            }
        }

        if (expSource == "from ancestor figure figcaption")
        {
            var figure = new El("figure");
            figure.Kids.Add(ret);
            AddWith(figure, "figcaption", NameValues[expSource]);
            ret = figure;
            expName = NameValues[expSource];
        }

        if (expSource == "from encapsulating label")
        {
            var label = new El("label");
            label.Kids.Add(NameValues[expSource]);
            label.Kids.Add(ret);
            ret = label;
            expName = NameValues[expSource];
        }

        if (nameSources.Contains("from 1 label"))
        {
            pre.Append("<label for=\"el-" + id + "\">" + NameValues["from 1 label"] + "</label>");
            if (expSource == "from 1 label")
            {
                expName = NameValues["from 1 label"];
            }
        }

        if (nameSources.Contains("from 2 labels"))
        {
            pre.Append("<label for=\"el-" + id + "\">" + NameValues["from 2 labels"] + "</label>");
            if (expSource == "from 2 labels")
            {
                expName = NameValues["from 1 label"] + " " + NameValues["from 2 labels"];
            }
        }

        if (nameSources.Contains("from 1 aria-labelledby ref"))
        {
            pre.Append("<p id=\"el-" + id + "-label-1\">" + NameValues["from 1 aria-labelledby ref"] + "</p>");
            test.Set("aria-labelledby", "el-" + id + "-label-1");
            if (expSource == "from 1 aria-labelledby ref")
            {
                expName = NameValues["from 1 aria-labelledby ref"];
            }
        }

        if (nameSources.Contains("from 2 aria-labelledby refs"))
        {
            pre.Append("<p id=\"el-" + id + "-label-2\">" + NameValues["from 2 aria-labelledby refs"] + "</p>");
            test.Set("aria-labelledby", "el-" + id + "-label-1 el-" + id + "-label-2");
            if (expSource == "from 2 aria-labelledby refs")
            {
                expName = NameValues["from 1 aria-labelledby ref"] + " " + NameValues["from 2 aria-labelledby refs"];
            }
        }

        if (nameSources.Contains("from subtree"))
        {
            string name = element + " " + NameValues["from subtree"];
            test.Kids.Add(name);
            if (expSource == "from subtree")
            {
                expName = name;
            }
        }

        foreach (string src in new[] { "from first legend", "from first caption" })
        {
            if (nameSources.Contains(src))
            {
                string child = src.Replace("from first ", string.Empty, StringComparison.Ordinal);
                test.Kids.Insert(0, new Raw("<" + child + ">Duplicate " + NameValues[src] + "</" + child + ">"));
                test.Kids.Insert(0, new Raw("<" + child + ">" + NameValues[src] + "</" + child + ">"));
                if (expSource == src)
                {
                    expName = NameValues[src];
                }
            }
        }

        foreach (string src in new[] { "from aria-label", "from aria-placeholder", "from value", "from alt", "from title", "from placeholder", "from label attribute" })
        {
            if (nameSources.Contains(src))
            {
                string attr = src.Replace("from ", string.Empty, StringComparison.Ordinal).Replace(" attribute", string.Empty, StringComparison.Ordinal);
                string name = src == "from alt" ? NameValues[src] : element + " " + NameValues[src];
                test.Set(attr, name);
                if (expSource == src)
                {
                    expName = name;
                }
            }
        }

        if (expSource == "from empty alt")
        {
            test.Set("alt", string.Empty);
            expName = string.Empty;
        }

        if (test.Kids.Count == 0 && !Voids.Contains(element))
        {
            var img = new El("img");
            img.Set("src", SampleImage);
            test.Kids.Add(img);
        }

        test.Set("data-expectedlabel", expName);
        return pre + ret.Outer();
    }

    private static void AddWith(El parent, string tag, string text)
    {
        var e = new El(tag);
        e.Kids.Add(text);
        parent.Kids.Add(e);
    }
}
