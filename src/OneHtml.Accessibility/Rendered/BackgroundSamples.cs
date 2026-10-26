using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>Thrown when background-sample JSON cannot be read.</summary>
public sealed class BackgroundSamplesFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong.</param>
    public BackgroundSamplesFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception for a JSON error.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="inner">The underlying error.</param>
    public BackgroundSamplesFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>One element's background sample and how the sample file names the element.</summary>
/// <param name="SnapshotNode">The element's node index in the snapshot's main document, or <see langword="null"/>.</param>
/// <param name="BackendNodeId">The element's backend node id from the snapshot, or <see langword="null"/>. Takes precedence over <paramref name="SnapshotNode"/> when both are given.</param>
/// <param name="Sample">The sampled colours.</param>
public sealed record BackgroundSampleEntry(int? SnapshotNode, long? BackendNodeId, BackgroundSample Sample);

/// <summary>
/// Background pixel summaries supplied by the caller (Chrome's snapshot carries none). The file format is
/// <c>{"format":"onehtml-background-samples","version":1,"viewport":{"width":1280,"height":800},"samples":[{"backendNodeId":12,
/// "median":"#003366","p5":"#002255","p95":"#114477","pixels":4800}]}</c>; see <c>docs/rendered-facts.md</c>.
/// </summary>
public sealed class BackgroundSamples
{
    /// <summary>The value of the <c>format</c> member.</summary>
    public const string FormatName = "onehtml-background-samples";

    /// <summary>The version this reader understands.</summary>
    public const int FormatVersion = 1;

    /// <summary>Largest accepted input in bytes.</summary>
    public const int MaxInputBytes = 64 * 1024 * 1024;

    /// <summary>Most entries accepted.</summary>
    public const int MaxEntries = 1_000_000;

    /// <summary>Creates a sample set.</summary>
    /// <param name="entries">The entries.</param>
    /// <param name="viewportWidth">The viewport width the samples were taken at, or 0 when unstated.</param>
    /// <param name="viewportHeight">The viewport height, or 0 when unstated.</param>
    public BackgroundSamples(IReadOnlyList<BackgroundSampleEntry> entries, double viewportWidth = 0, double viewportHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    /// <summary>The entries in file order.</summary>
    public IReadOnlyList<BackgroundSampleEntry> Entries { get; }

    /// <summary>The viewport width the samples were taken at; 0 when unstated.</summary>
    public double ViewportWidth { get; }

    /// <summary>The viewport height the samples were taken at; 0 when unstated.</summary>
    public double ViewportHeight { get; }

    /// <summary>Reads a sample file.</summary>
    /// <param name="utf8Json">The JSON, UTF-8.</param>
    /// <exception cref="BackgroundSamplesFormatException">The JSON is malformed, of another format or version, has a colour that does not parse, or is over a limit.</exception>
    public static BackgroundSamples Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaxInputBytes)
        {
            throw new BackgroundSamplesFormatException($"The input is {utf8Json.Length} bytes; the limit is {MaxInputBytes}.");
        }

        try
        {
            var r = new Utf8JsonReader(utf8Json, new JsonReaderOptions { MaxDepth = 16 });
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject)
            {
                throw new BackgroundSamplesFormatException("The input must be a JSON object.");
            }

            string? format = null;
            int version = -1;
            double vw = 0, vh = 0;
            var entries = new List<BackgroundSampleEntry>();
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                if (r.ValueTextEquals("format"u8))
                {
                    r.Read();
                    format = r.TokenType == JsonTokenType.String ? r.GetString() : null;
                }
                else if (r.ValueTextEquals("version"u8))
                {
                    r.Read();
                    version = r.TokenType == JsonTokenType.Number && r.TryGetInt32(out int v) ? v : -1;
                }
                else if (r.ValueTextEquals("viewport"u8))
                {
                    r.Read();
                    (vw, vh) = ReadViewport(ref r);
                }
                else if (r.ValueTextEquals("samples"u8))
                {
                    r.Read();
                    if (r.TokenType != JsonTokenType.StartArray)
                    {
                        throw new BackgroundSamplesFormatException("\"samples\" must be an array.");
                    }

                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    {
                        if (entries.Count >= MaxEntries)
                        {
                            throw new BackgroundSamplesFormatException($"More than {MaxEntries} samples.");
                        }

                        entries.Add(ReadEntry(ref r, entries.Count));
                    }
                }
                else
                {
                    r.Read();
                    r.Skip();
                }
            }

            if (format != FormatName)
            {
                throw new BackgroundSamplesFormatException($"The \"format\" member must be \"{FormatName}\".");
            }

            if (version != FormatVersion)
            {
                throw new BackgroundSamplesFormatException($"Version {version} is not supported; this reader reads version {FormatVersion}.");
            }

            return new BackgroundSamples(entries, vw, vh);
        }
        catch (JsonException e)
        {
            throw new BackgroundSamplesFormatException("The input is not valid JSON: " + e.Message, e);
        }
        catch (InvalidOperationException e)
        {
            throw new BackgroundSamplesFormatException("A value has an unexpected type: " + e.Message, e);
        }
        catch (FormatException e)
        {
            throw new BackgroundSamplesFormatException("A value is malformed: " + e.Message, e);
        }
    }

    private static (double W, double H) ReadViewport(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject)
        {
            throw new BackgroundSamplesFormatException("\"viewport\" must be an object.");
        }

        double w = 0, h = 0;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("width"u8))
            {
                r.Read();
                w = Number(ref r, "viewport.width");
            }
            else if (r.ValueTextEquals("height"u8))
            {
                r.Read();
                h = Number(ref r, "viewport.height");
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }

        return (w, h);
    }

    private static double Number(ref Utf8JsonReader r, string what)
    {
        if (r.TokenType != JsonTokenType.Number)
        {
            throw new BackgroundSamplesFormatException($"\"{what}\" must be a number.");
        }

        double v = r.GetDouble();
        return double.IsFinite(v) ? v : throw new BackgroundSamplesFormatException($"\"{what}\" is not finite.");
    }

    private static BackgroundSampleEntry ReadEntry(ref Utf8JsonReader r, int n)
    {
        if (r.TokenType != JsonTokenType.StartObject)
        {
            throw new BackgroundSamplesFormatException($"samples[{n}] must be an object.");
        }

        int? snapshotNode = null;
        long? backend = null;
        Rgba? median = null, p5 = null, p95 = null;
        int pixels = 0;
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("backendNodeId"u8))
            {
                r.Read();
                backend = r.TokenType == JsonTokenType.Number && r.TryGetInt64(out long b) ? b : throw new BackgroundSamplesFormatException($"samples[{n}].backendNodeId must be an integer.");
            }
            else if (r.ValueTextEquals("snapshotNode"u8))
            {
                r.Read();
                snapshotNode = r.TokenType == JsonTokenType.Number && r.TryGetInt32(out int s) ? s : throw new BackgroundSamplesFormatException($"samples[{n}].snapshotNode must be an integer.");
            }
            else if (r.ValueTextEquals("pixels"u8))
            {
                r.Read();
                pixels = r.TokenType == JsonTokenType.Number && r.TryGetInt32(out int p) && p >= 0 ? p : throw new BackgroundSamplesFormatException($"samples[{n}].pixels must be a non-negative integer.");
            }
            else if (r.ValueTextEquals("median"u8))
            {
                r.Read();
                median = Color(ref r, n, "median");
            }
            else if (r.ValueTextEquals("p5"u8))
            {
                r.Read();
                p5 = Color(ref r, n, "p5");
            }
            else if (r.ValueTextEquals("p95"u8))
            {
                r.Read();
                p95 = Color(ref r, n, "p95");
            }
            else
            {
                r.Read();
                r.Skip();
            }
        }

        if (backend is null && snapshotNode is null)
        {
            throw new BackgroundSamplesFormatException($"samples[{n}] names no element: give backendNodeId or snapshotNode.");
        }

        if (median is not { } m)
        {
            throw new BackgroundSamplesFormatException($"samples[{n}] has no median.");
        }

        return new BackgroundSampleEntry(snapshotNode, backend, new BackgroundSample(m, p5 ?? m, p95 ?? m, pixels));
    }

    private static Rgba Color(ref Utf8JsonReader r, int n, string what)
    {
        if (r.TokenType == JsonTokenType.String && Rgba.TryParse(r.GetString(), out Rgba c))
        {
            return c;
        }

        throw new BackgroundSamplesFormatException($"samples[{n}].{what} is not a colour (#rrggbb, rgb() or rgba()).");
    }
}
