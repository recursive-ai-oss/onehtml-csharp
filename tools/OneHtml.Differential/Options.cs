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

            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && a != "--out")
            {
                return null;
            }

            switch (a)
            {
                case "--out": o.Out = args[++i]; break;
                case "--jobs": o.Jobs = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--limit": o.Limit = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--timeout": o.TimeoutSeconds = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                case "--top": o.Top = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--max-reduce": o.MaxReduce = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--seed": o.Seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--per-page": o.PerPage = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                default: return null;
            }
        }

        return o.Corpus.Length == 0 ? null : o;
    }

    public const string Usage = """
        options (after the corpus directory): --out <dir> (default ./out) --jobs <n> (default 2) --limit <n> (first n items in sha256 order)
          --timeout <seconds per item> (default 30) --top <n> (default 20) --max-reduce <classes to minimize> (default 100, angle only)
          --seed <n> (default 1) --per-page <targets per page> (default 5), replay only
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
        if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds + 5)))
        {
            return (default, "hang");
        }

        if (task.IsFaulted)
        {
            return (default, Classify(task.Exception!.GetBaseException()));
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
