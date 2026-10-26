using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// A validator for the JSON Schema draft-04 keywords the SARIF 2.1.0 schema uses: <c>type</c>, <c>properties</c>,
/// <c>additionalProperties</c>, <c>required</c>, <c>items</c>, <c>minItems</c>, <c>uniqueItems</c>, <c>minimum</c>,
/// <c>maximum</c>, <c>enum</c>, <c>pattern</c>, <c>format</c> (<c>date-time</c>, <c>uri</c>, <c>uri-reference</c>), <c>anyOf</c>,
/// <c>oneOf</c> and local <c>$ref</c>. A keyword outside that set in the schema is an error, so the validator never
/// silently skips a constraint.
/// </summary>
internal sealed class JsonSchemaValidator
{
    private static readonly HashSet<string> s_annotations = ["description", "default", "title", "id", "$schema", "definitions"];
    private static readonly HashSet<string> s_known =
    [
        "type", "properties", "additionalProperties", "required", "items", "minItems", "uniqueItems", "minimum", "maximum",
        "enum", "pattern", "format", "anyOf", "oneOf", "$ref",
    ];

    private readonly JsonElement _root;
    private readonly List<string> _errors = [];

    internal JsonSchemaValidator(JsonElement schema) => _root = schema;

    /// <summary>Validates the instance; returns the errors (empty when it is valid), at most 50.</summary>
    internal IReadOnlyList<string> Validate(JsonElement instance)
    {
        _errors.Clear();
        Check(_root, instance, "#");
        return _errors;
    }

    private bool Check(JsonElement schema, JsonElement value, string path)
    {
        int before = _errors.Count;
        foreach (JsonProperty keyword in schema.EnumerateObject())
        {
            if (s_annotations.Contains(keyword.Name))
            {
                continue;
            }

            if (!s_known.Contains(keyword.Name))
            {
                Fail(path, "validator does not support the schema keyword " + keyword.Name);
                continue;
            }

            JsonElement arg = keyword.Value;
            switch (keyword.Name)
            {
                case "$ref":
                    Check(Resolve(arg.GetString()!), value, path);
                    break;
                case "type":
                    if (!(arg.ValueKind == JsonValueKind.Array ? arg.EnumerateArray().Any(t => TypeMatches(t.GetString()!, value)) : TypeMatches(arg.GetString()!, value)))
                    {
                        Fail(path, "expected " + arg.GetRawText() + " but found " + value.ValueKind);
                    }

                    break;
                case "enum":
                    if (!arg.EnumerateArray().Any(e => e.GetRawText() == value.GetRawText()))
                    {
                        Fail(path, "value " + value.GetRawText() + " is not one of " + arg.GetRawText());
                    }

                    break;
                case "pattern":
                    if (value.ValueKind == JsonValueKind.String && !Regex.IsMatch(value.GetString()!, arg.GetString()!, RegexOptions.CultureInvariant))
                    {
                        Fail(path, "string does not match " + arg.GetString());
                    }

                    break;
                case "format":
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        CheckFormat(arg.GetString()!, value.GetString()!, path);
                    }

                    break;
                case "minimum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() < arg.GetDouble())
                    {
                        Fail(path, "below the minimum " + arg.GetRawText());
                    }

                    break;
                case "maximum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDouble() > arg.GetDouble())
                    {
                        Fail(path, "above the maximum " + arg.GetRawText());
                    }

                    break;
                case "required":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonElement name in arg.EnumerateArray())
                        {
                            if (!value.TryGetProperty(name.GetString()!, out _))
                            {
                                Fail(path, "missing required property " + name.GetString());
                            }
                        }
                    }

                    break;
                case "properties":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty member in value.EnumerateObject())
                        {
                            if (arg.TryGetProperty(member.Name, out JsonElement sub))
                            {
                                Check(sub, member.Value, path + "/" + member.Name);
                            }
                        }
                    }

                    break;
                case "additionalProperties":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement declared = schema.TryGetProperty("properties", out JsonElement p) ? p : default;
                        foreach (JsonProperty member in value.EnumerateObject())
                        {
                            bool known = declared.ValueKind == JsonValueKind.Object && declared.TryGetProperty(member.Name, out _);
                            if (known)
                            {
                                continue;
                            }

                            if (arg.ValueKind == JsonValueKind.False)
                            {
                                Fail(path, "unexpected property " + member.Name);
                            }
                            else if (arg.ValueKind == JsonValueKind.Object)
                            {
                                Check(arg, member.Value, path + "/" + member.Name);
                            }
                        }
                    }

                    break;
                case "items":
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        int i = 0;
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            Check(arg, item, path + "[" + (i++).ToString(CultureInfo.InvariantCulture) + "]");
                        }
                    }

                    break;
                case "minItems":
                    if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() < arg.GetInt32())
                    {
                        Fail(path, "fewer than " + arg.GetInt32() + " items");
                    }

                    break;
                case "uniqueItems":
                    if (arg.ValueKind == JsonValueKind.True && value.ValueKind == JsonValueKind.Array)
                    {
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (JsonElement item in value.EnumerateArray())
                        {
                            if (!seen.Add(item.GetRawText()))
                            {
                                Fail(path, "duplicate array item");
                            }
                        }
                    }

                    break;
                case "anyOf":
                    if (!arg.EnumerateArray().Any(s => Matches(s, value, path)))
                    {
                        Fail(path, "matches none of the anyOf alternatives");
                    }

                    break;
                case "oneOf":
                    if (arg.EnumerateArray().Count(s => Matches(s, value, path)) != 1)
                    {
                        Fail(path, "does not match exactly one of the oneOf alternatives");
                    }

                    break;
            }
        }

        return _errors.Count == before;
    }

    // Evaluates an alternative without keeping its errors.
    private bool Matches(JsonElement schema, JsonElement value, string path)
    {
        int mark = _errors.Count;
        bool ok = Check(schema, value, path);
        _errors.RemoveRange(mark, _errors.Count - mark);
        return ok;
    }

    private JsonElement Resolve(string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only local references are supported: " + reference);
        }

        JsonElement current = _root;
        foreach (string part in reference[2..].Split('/'))
        {
            current = current.GetProperty(part);
        }

        return current;
    }

    private static bool TypeMatches(string type, JsonElement v) => type switch
    {
        "object" => v.ValueKind == JsonValueKind.Object,
        "array" => v.ValueKind == JsonValueKind.Array,
        "string" => v.ValueKind == JsonValueKind.String,
        "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => v.ValueKind == JsonValueKind.Null,
        "number" => v.ValueKind == JsonValueKind.Number,
        "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
        _ => throw new InvalidOperationException("Unsupported type " + type),
    };

    private void CheckFormat(string format, string s, string path)
    {
        switch (format)
        {
            case "date-time":
                if (!DateTimeOffset.TryParseExact(s, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    Fail(path, "not a date-time: " + s);
                }

                break;
            case "uri":
                if (!Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9+.\-]*:[^\s]*$", RegexOptions.CultureInvariant) || !IsLegalUriText(s))
                {
                    Fail(path, "not an absolute URI: " + s);
                }

                break;
            case "uri-reference":
                if (!IsLegalUriText(s))
                {
                    Fail(path, "not a URI reference: " + s);
                }

                break;
            default:
                Fail(path, "validator does not support the format " + format);
                break;
        }
    }

    // RFC 3986: only unreserved and reserved characters and percent-encoded octets.
    private static bool IsLegalUriText(string s) =>
        Regex.IsMatch(s, @"^([A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=]|%[0-9A-Fa-f]{2})*$", RegexOptions.CultureInvariant);

    private void Fail(string path, string message)
    {
        if (_errors.Count < 50)
        {
            _errors.Add(path + ": " + message);
        }
    }
}
