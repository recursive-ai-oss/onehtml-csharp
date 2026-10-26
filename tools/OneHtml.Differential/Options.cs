using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>Command-line options shared by the corpus commands.</summary>
internal sealed class Options
{
    public string Corpus { get; private set; } = string.Empty;

    public string Out { get; private set; } = "out";

    public int Jobs { get; private set; } = 2;

    public int Limit { get; private set; } = int.MaxValue;

    public int TimeoutSeconds { get; private set; } = 30;

    public int Top { get; private set; } = 20;

    public int MaxReduce { get; private set; } = 100;

    public int Seed { get; private set; } = 1;

    public int PerPage { get; private set; } = 5;

    /// <summary>Items skipped (in sha256 order) before <see cref="Limit"/> counts; with <c>--limit</c> it resumes a long gate run.</summary>
    public int Skip { get; private set; }

    /// <summary>With <c>locators</c>, how many elements per page are checked by querying the whole document (the rest are checked by matching).</summary>
    public int Sample { get; private set; } = 2000;

    /// <summary>Where the <c>ax</c> command finds stylesheets (a corpus version or a directory of pages); null for none.</summary>
    public string? StyleCorpus { get; private set; }

    /// <summary>With <c>ax</c>, also writes every paired and unpaired node, side by side, to <c>dump/</c>.</summary>
    public bool Dump { get; private set; }

    /// <summary>The <c>ax</c> command's rendered-facts mode: <c>on</c>, <c>off</c> or <c>both</c>.</summary>
    public string Facts { get; private set; } = "both";

    /// <summary>Restricts <c>roundtrip</c> to <c>pages</c> or <c>stylesheets</c>; null for both.</summary>
    public string? Only { get; private set; }

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                o.Corpus = a;
                continue;
            }

            if (a == "--dump")
            {
                o.Dump = true;
                continue;
            }

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && a is not ("--out" or "--corpus" or "--facts" or "--only"))
            {
                return null;
            }

            switch (a)
            {
                case "--out": o.Out = args[++i]; break;
                case "--corpus": o.StyleCorpus = args[++i]; break;
                case "--only":
                    o.Only = args[++i];
                    if (o.Only is not ("pages" or "stylesheets"))
                    {
                        return null;
                    }

                    break;
                case "--facts":
                    o.Facts = args[++i];
                    if (o.Facts is not ("on" or "off" or "both"))
                    {
                        return null;
                    }

                    break;
                case "--jobs": o.Jobs = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--limit": o.Limit = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--timeout": o.TimeoutSeconds = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--top": o.Top = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--max-reduce": o.MaxReduce = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--seed": o.Seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--skip": o.Skip = Math.Max(0, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--sample": o.Sample = Math.Max(0, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--per-page": o.PerPage = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                default: return null;
            }
        }

        return o.Corpus.Length == 0 ? null : o;
    }

    public const string Usage = """
        options (after the corpus directory): --out <dir> (default ./out) --jobs <n> (default 2) --limit <n> (first n items in sha256 order)
          --timeout <seconds per item> (default 30) --top <n> (default 20) --max-reduce <classes to minimize> (default 100, angle only)
          --only pages|stylesheets (roundtrip only)
          --seed <n> (default 1) --per-page <targets per page> (default 5), replay only
          --skip <n> (items skipped first, to resume), --sample <n> (locators: elements per page checked by a whole-document query, default 2000)
          locators, styles, editfuzz: --per-page is the contexts and edit steps (styles) or transactions (editfuzz) per page; --corpus supplies stylesheets for styles (default: the corpus itself)
          ax: the positional argument is the capture directory; --corpus <dir> supplies stylesheets (a corpus version or a directory of
          pages), --facts on|off|both (default both), --dump (also write dump/<sha>-<viewport>.txt)
        """;
}

/// <summary>Runs one piece of work under a wall-clock timeout, so one bad input cannot stall a corpus run.</summary>
internal static class Guard
{
    /// <summary>
    /// Runs <paramref name="work"/> on its own thread with a cancellation token that fires after the timeout. If the
    /// work ignores the token for a further 5 s it is abandoned and reported as a hang.
    /// </summary>
    public static (T? Value, string? Failure) Run<T>(Func<CancellationToken, T> work, int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        Task<T> task = Task.Factory.StartNew(() => work(cts.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            // Wait rethrows a faulted task as an AggregateException, so a failing item is classified here and never
            // reaches the caller's parallel loop.
            if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds + 5)))
            {
                return (default, "hang");
            }
        }
        catch (AggregateException ex)
        {
            return (default, Classify(ex.GetBaseException()));
        }

        return (task.Result, null);
    }

    /// <summary>Names an exception for the failure classes: timeouts and the library's limit exceptions get their own class.</summary>
    public static string Classify(Exception ex) => ex switch
    {
        OperationCanceledException => "timeout",
        HtmlLimitExceededException or Css.CssLimitExceededException => "limit",
        _ => "exception:" + ex.GetType().Name,
    };
}
