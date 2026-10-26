using System;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz target for the cascade: the input is a flags byte, a page, a NUL, and a style sheet that is both appended to the
/// page as a style element and supplied as the linked/imported sheet. The target computes every style, then a
/// focus/hover overlay, and requires the overlay to equal a full recompute. Only the library's typed exceptions may escape.
/// </summary>
internal static class StyleTarget
{
    private static readonly ParseLimits Limits = new() { MaxInputLength = 1 << 16, MaxAttributesPerTag = 32, MaxNodes = 1 << 12, MaxOpenElements = 128 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data.Length > (1 << 16))
        {
            return;
        }

        int flags = data[0];
        string all = Encoding.UTF8.GetString(data[1..]);
        int nul = all.IndexOf('\0', StringComparison.Ordinal);
        string html = nul < 0 ? all : all[..nul];
        string css = nul < 0 ? string.Empty : all[(nul + 1)..];
        byte[] sheet = Encoding.UTF8.GetBytes(css);
        var options = new StyleSourceOptions
        {
            BaseUrl = "https://example.test/p.html",
            Resolver = url => url.EndsWith("/s.css", StringComparison.Ordinal) ? new StylesheetResource(sheet) : null,
            MaxSheets = 64,
            MaxRules = 1 << 14,
            MaxLayers = 1 << 10,
        };
        try
        {
            Document doc = Document.Parse(html + "<link rel=stylesheet href=s.css><style>" + css + "</style>", new ParseOptions { Limits = Limits });
            DocumentStyles styles = DocumentStyles.Create(doc, null, options);
            styles.ComputeAll();
            Node pick = default;
            int want = (flags >> 2) % 7, seen = 0;
            foreach (Node e in doc.Root.DescendantElements)
            {
                pick = e;
                if (seen++ >= want)
                {
                    break;
                }
            }

            if (pick.IsNull)
            {
                return;
            }

            StyleContext ctx = (flags & 1) != 0 ? StyleContext.Default.WithFocus(pick, (flags & 2) != 0 ? FocusSource.PointingDevice : FocusSource.Keyboard) : StyleContext.Default.WithHover(pick);
            DocumentStyles overlay = styles.WithContext(ctx);
            DocumentStyles full = DocumentStyles.Create(doc, ctx, options);
            foreach (Node e in doc.Root.DescendantElements)
            {
                string? diff = overlay.GetStyle(e).FirstDifference(full.GetStyle(e));
                if (diff is not null)
                {
                    throw new InvalidOperationException("overlay differs from a full recompute in " + diff);
                }
            }
        }
        catch (HtmlLimitExceededException)
        {
            // A configured limit is the designed failure mode.
        }
    }
}
