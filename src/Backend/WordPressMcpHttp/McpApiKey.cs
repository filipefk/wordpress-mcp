using GeraApiKey;
using McpToolkit;
using System.Diagnostics.CodeAnalysis;

namespace WordPressMcpHttp;

// Conteúdo do header x-api-key, gerado pelo GeraApiKey.ApiKeyGenerator a partir de
// [mcpApiKey, wordPressUrl, wordPressUser, applicationPassword] — nesta ordem. FromValues/ToValues são o único
// lugar do código que conhece essa ordem; a biblioteca GeraApiKey só vê uma lista de strings.
// As credenciais do WordPress são opcionais: ausentes, cai no fallback do servidor (seção "WordPress").
public sealed class McpApiKey(string mcpKey, string? wordPressUrl, string? wordPressUser, string? applicationPassword)
{
    public const string HeaderName = "x-api-key";

    public string McpKey { get; } = mcpKey;
    public string? WordPressUrl { get; } = wordPressUrl;
    public string? WordPressUser { get; } = wordPressUser;
    public string? ApplicationPassword { get; } = applicationPassword;

    // Só devolve credenciais quando URL, usuário e senha estão todos presentes.
    public WordPressCredentials? Credentials =>
        WordPressCredentials.TryCreate(WordPressUrl, WordPressUser, ApplicationPassword);

    public static McpApiKey FromValues(IReadOnlyList<string> values) =>
        new(values.Count > 0 ? values[0] : string.Empty, ValueAt(values, 1), ValueAt(values, 2), ValueAt(values, 3));

    // Valores vazios do fim são descartados: uma key só com a chave MCP não carrega separadores inúteis.
    public IReadOnlyList<string> ToValues()
    {
        var values = new List<string> { McpKey, WordPressUrl ?? string.Empty, WordPressUser ?? string.Empty, ApplicationPassword ?? string.Empty };
        while (values.Count > 1 && values[^1].Length == 0)
            values.RemoveAt(values.Count - 1);
        return values;
    }

    public static bool TryRead(HttpContext? context, [NotNullWhen(true)] out McpApiKey? apiKey)
    {
        apiKey = null;
        var header = ReadHeader(context);
        if (!ApiKeyGenerator.TryParse(header, out var values)) return false;

        apiKey = FromValues(values);
        return true;
    }

    public static string? ReadHeader(HttpContext? context) =>
        context?.Request.Headers[HeaderName].FirstOrDefault();

    private static string? ValueAt(IReadOnlyList<string> values, int index) =>
        index < values.Count && !string.IsNullOrWhiteSpace(values[index]) ? values[index] : null;
}
