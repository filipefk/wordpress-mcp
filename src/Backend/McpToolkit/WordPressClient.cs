using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpToolkit;

public sealed record WordPressResponse(JsonNode? Data, int? Total, int? TotalPages);

// Cliente da REST API do WordPress (/wp-json/...) com Basic Auth por Application Password.
// As credenciais são resolvidas a cada requisição: x-api-key > seção "WordPress" do servidor.
// A senha nunca é devolvida nem logada.
public sealed class WordPressClient(
    IHttpClientFactory httpClientFactory,
    IRequestContext requestContext,
    IOptions<WordPressOptions> options)
{
    public const string HttpClientName = "wordpress";

    public WordPressCredentials ResolveCredentials()
    {
        var settings = options.Value;
        return requestContext.Credentials
            ?? WordPressCredentials.TryCreate(settings.Url, settings.User, settings.ApplicationPassword)
            ?? throw new InvalidOperationException(
                "Credenciais do WordPress não informadas: gere uma API Key com URL, usuário e Application Password " +
                "(header x-api-key) ou configure a seção 'WordPress' do servidor.");
    }

    public Task<WordPressResponse> GetAsync(string route, IDictionary<string, string?>? query, CancellationToken ct) =>
        SendAsync(ResolveCredentials(), HttpMethod.Get, route, query, null, ct);

    public Task<WordPressResponse> SendAsync(
        HttpMethod method, string route, IDictionary<string, string?>? query, JsonNode? body, CancellationToken ct) =>
        SendAsync(ResolveCredentials(), method, route, query, body, ct);

    public Task<WordPressResponse> SendAsync(
        WordPressCredentials credentials, HttpMethod method, string route, IDictionary<string, string?>? query, JsonNode? body, CancellationToken ct)
    {
        HttpContent? content = body is null
            ? null
            : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return ExecuteAsync(credentials, method, route, query, content, ct);
    }

    public Task<WordPressResponse> UploadAsync(
        string route, byte[] data, string fileName, string mimeType, IDictionary<string, string?>? query, CancellationToken ct)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        var safeName = fileName.Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        content.Headers.TryAddWithoutValidation("Content-Disposition", $"attachment; filename=\"{safeName}\"");
        return ExecuteAsync(ResolveCredentials(), HttpMethod.Post, route, query, content, ct);
    }

    // Valida as credenciais consultando o usuário autenticado; usada pelo endpoint /api-key/test-connection.
    public async Task<JsonObject> TestConnectionAsync(WordPressCredentials credentials, CancellationToken ct)
    {
        var response = await SendAsync(credentials, HttpMethod.Get, "wp/v2/users/me",
            new Dictionary<string, string?> { ["context"] = "edit" }, null, ct);
        var user = response.Data as JsonObject
            ?? throw new InvalidOperationException("Resposta inesperada do WordPress ao consultar o usuário autenticado.");

        return new JsonObject
        {
            ["site"] = credentials.BaseUrl,
            ["userId"] = user["id"]?.DeepClone(),
            ["userName"] = user["name"]?.DeepClone(),
            ["roles"] = user["roles"]?.DeepClone()
        };
    }

    private async Task<WordPressResponse> ExecuteAsync(
        WordPressCredentials credentials, HttpMethod method, string route, IDictionary<string, string?>? query,
        HttpContent? content, CancellationToken ct)
    {
        var uri = BuildUri(credentials.BaseUrl, route, query);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.User}:{credentials.ApplicationPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(BuildErrorMessage(response.StatusCode, text));

            JsonNode? data;
            try
            {
                data = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    "O WordPress respondeu algo que não é JSON. Verifique a URL do site e se a REST API (/wp-json) está acessível.");
            }

            return new WordPressResponse(data, ReadIntHeader(response, "X-WP-Total"), ReadIntHeader(response, "X-WP-TotalPages"));
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Falha ao acessar o WordPress ({uri.Host}): {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Tempo esgotado ao acessar o WordPress ({uri.Host}).");
        }
    }

    private static Uri BuildUri(string baseUrl, string route, IDictionary<string, string?>? query)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("URL do WordPress inválida: informe uma URL http(s) completa, ex.: https://meusite.com.br");

        var path = route.Trim().TrimStart('/');
        if (path.StartsWith("wp-json/", StringComparison.OrdinalIgnoreCase))
            path = path["wp-json/".Length..];
        if (path.Contains("..") || path.Contains("://"))
            throw new InvalidOperationException("Rota inválida.");

        var builder = new StringBuilder(baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/'));
        builder.Append("/wp-json/").Append(path);

        if (query is { Count: > 0 })
        {
            var parts = query
                .Where(p => p.Value is not null)
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
            var queryString = string.Join('&', parts);
            if (queryString.Length > 0)
                builder.Append(path.Contains('?') ? '&' : '?').Append(queryString);
        }

        return new Uri(builder.ToString());
    }

    private static string BuildErrorMessage(HttpStatusCode status, string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject error && error["message"] is not null)
            {
                var code = error["code"]?.ToString();
                var message = error["message"]!.ToString();
                return string.IsNullOrEmpty(code)
                    ? $"WordPress retornou {(int)status}: {message}"
                    : $"WordPress retornou {(int)status} ({code}): {message}";
            }
        }
        catch (JsonException)
        {
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "WordPress retornou 401: credenciais inválidas (verifique usuário e Application Password).",
            HttpStatusCode.NotFound => "WordPress retornou 404: rota não encontrada (verifique a URL do site e se a REST API está habilitada).",
            _ => $"WordPress retornou {(int)status}."
        };
    }

    private static int? ReadIntHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var value) ? value : null;
}
