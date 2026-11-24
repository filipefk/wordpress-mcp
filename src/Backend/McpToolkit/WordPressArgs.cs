using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace McpToolkit;

// Helpers para montar query string e corpo JSON a partir dos parâmetros das tools.
internal static partial class WordPressArgs
{
    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex RestBaseRegex();

    public static string RestBase(string? value, string fallback)
    {
        var restBase = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (!RestBaseRegex().IsMatch(restBase))
            throw new InvalidOperationException($"Valor inválido '{restBase}': use apenas letras, números, '-' e '_'.");
        return restBase;
    }

    public static Dictionary<string, string?> Query(params (string Key, object? Value)[] items)
    {
        var query = new Dictionary<string, string?>();
        foreach (var (key, value) in items)
        {
            var text = value switch
            {
                null => null,
                bool b => b ? "true" : "false",
                string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            };
            if (text is not null)
                query[key] = text;
        }
        return query;
    }

    // Lista "1,2,3" -> JsonArray de inteiros. Vazio/null -> null (campo omitido).
    public static JsonArray? IdArray(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var array = new JsonArray();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id))
                throw new InvalidOperationException($"Id inválido '{part}': informe inteiros separados por vírgula.");
            array.Add(id);
        }
        return array;
    }

    public static JsonObject Body(params (string Key, JsonNode? Value)[] items)
    {
        var body = new JsonObject();
        foreach (var (key, value) in items)
            if (value is not null)
                body[key] = value;
        return body;
    }

    public static JsonNode? Str(string? value) => value is null ? null : JsonValue.Create(value);

    public static JsonNode? Num(int? value) => value is null ? null : JsonValue.Create(value.Value);

    public static JsonObject? ParseObject(string? json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException($"O parâmetro '{paramName}' deve ser um objeto JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"O parâmetro '{paramName}' não é um JSON válido: {ex.Message}");
        }
    }

    // Copia os campos de extra para o corpo (sem sobrescrever os já definidos pelos parâmetros nomeados).
    public static JsonObject Merge(JsonObject body, JsonObject? extra)
    {
        if (extra is null) return body;
        foreach (var (key, value) in extra)
            if (!body.ContainsKey(key))
                body[key] = value?.DeepClone();
        return body;
    }

    public static string ListResult(WordPressResponse response) =>
        new JsonObject
        {
            ["total"] = response.Total,
            ["totalPages"] = response.TotalPages,
            ["items"] = response.Data?.DeepClone()
        }.ToJsonString();

    public static string Result(WordPressResponse response) =>
        response.Data?.ToJsonString() ?? "null";
}
