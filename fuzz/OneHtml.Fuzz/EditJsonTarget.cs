using System;
using System.Text;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz target for the edit JSON reader and replay. The input is the JSON text. Checks: parsing either succeeds or
/// throws <see cref="EditJsonException"/> only; what parses writes back to JSON that parses and writes to itself; and
/// replaying it onto a fixed page and stylesheet either reports per-edit outcomes or applies, in which case the new
/// document keeps the structural invariants. Limits are small so the limit paths are reached quickly.
/// </summary>
internal static class EditJsonTarget
{
    private const string Page =
        "<!DOCTYPE html><html><head><style>.a { color: red }\n.b { margin: 0 }</style></head><body><nav id=main-nav><a href=/x>X</a></nav>"
        + "<p class=intro>Hello <b>there</b></p><!-- c --><ul><li>one<li>two</ul></body></html>";

    private static readonly EditJsonLimits Limits = new() { MaxBytes = 1 << 16, MaxDepth = 16, MaxEdits = 64, MaxStringBytes = 4096, MaxMetaBytes = 1024, MaxAttributes = 8, MaxAtRules = 8 };

    private static readonly EditJsonLimits Unbounded = new() { MaxBytes = int.MaxValue, MaxDepth = 16, MaxEdits = 64, MaxStringBytes = int.MaxValue, MaxMetaBytes = int.MaxValue, MaxAttributes = 8, MaxAtRules = 8 };

    private static readonly byte[] LinkedCss = Encoding.UTF8.GetBytes("a { b: c }\n@media print { .x { d: e } }\n");

    public static void Run(ReadOnlySpan<byte> input)
    {
        EditEnvelope env;
        try
        {
            env = EditJson.Parse(input, Limits);
        }
        catch (EditJsonException)
        {
            return;
        }

        string json = env.ToJson();
        EditEnvelope again = EditJson.Parse(json, Unbounded);
        if (again.ToJson() != json)
        {
            throw new InvalidOperationException("invariant broken: edit JSON does not write back to itself");
        }

        Document doc = Document.Parse(Page);
        var linked = LinkedStylesheet.Create("https://example.test/s.css", LinkedCss);
        try
        {
            ReplayResult r = env.Replay(doc, [linked]);
            if (r.Succeeded && r.Batch?.Document is { } result)
            {
                string? problem = RecursiveAI.OneHtml.Tests.DocumentInvariants.Check(result);
                if (problem is not null)
                {
                    throw new InvalidOperationException("invariant broken: " + problem);
                }
            }
        }
        catch (HtmlLimitExceededException)
        {
            // A configured limit is the designed failure mode.
        }
    }
}
