using GeraApiKey;
using McpToolkit;

namespace WordPressMcpHttp.Endpoints;

// Endpoints para gerar a API Key do header x-api-key, desfazê-la e testar as credenciais do WordPress.
// Protegidos pelo McpApiKeyMiddleware em modo Raw: aqui o x-api-key é a chave MCP em texto puro,
// conferida contra McpAuth:ApiKeys. São POST para os segredos irem no corpo, nunca na URL.
public static class ApiKeyEndpoints
{
    public sealed record GenerateApiKeyRequest(string? McpApiKey, string? WordPressUrl, string? WordPressUser, string? ApplicationPassword);

    public sealed record GenerateApiKeyResponse(string ApiKey);

    public sealed record DecodeApiKeyRequest(string? ApiKey);

    public sealed record DecodeApiKeyResponse(string McpApiKey, string? WordPressUrl, string? WordPressUser, string? ApplicationPassword);

    public sealed record TestConnectionRequest(string? WordPressUrl, string? WordPressUser, string? ApplicationPassword);

    private static readonly TimeSpan TestConnectionTimeout = TimeSpan.FromSeconds(30);

    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api-key");

        group.MapPost("/generate", (GenerateApiKeyRequest request) =>
        {
            // Sem a chave MCP a key gerada nunca passaria no McpApiKeyMiddleware — falha aqui, com motivo.
            if (string.IsNullOrWhiteSpace(request.McpApiKey))
                return Results.BadRequest(new { error = "Informe o mcpApiKey." });

            var provided = new[] { request.WordPressUrl, request.WordPressUser, request.ApplicationPassword }
                .Count(v => !string.IsNullOrWhiteSpace(v));
            if (provided is > 0 and < 3)
                return Results.BadRequest(new { error = "Informe URL, usuário e Application Password do WordPress juntos (ou nenhum, para usar a configuração do servidor)." });

            var apiKey = new McpApiKey(request.McpApiKey, request.WordPressUrl?.Trim(), request.WordPressUser?.Trim(), request.ApplicationPassword?.Trim());
            try
            {
                return Results.Ok(new GenerateApiKeyResponse(ApiKeyGenerator.Generate(apiKey.ToValues())));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/decode", (DecodeApiKeyRequest request) =>
        {
            if (!ApiKeyGenerator.TryParse(request.ApiKey, out var values))
                return Results.BadRequest(new { error = "API Key inválida." });

            var apiKey = McpApiKey.FromValues(values);
            return Results.Ok(new DecodeApiKeyResponse(apiKey.McpKey, apiKey.WordPressUrl, apiKey.WordPressUser, apiKey.ApplicationPassword));
        });

        // Consulta o usuário autenticado com as credenciais informadas, para a tela validá-las antes de gerar a key.
        // As credenciais não são guardadas nem logadas.
        group.MapPost("/test-connection", async (TestConnectionRequest request, WordPressClient client, ILogger<TestConnectionRequest> logger, CancellationToken ct) =>
        {
            var credentials = WordPressCredentials.TryCreate(request.WordPressUrl, request.WordPressUser, request.ApplicationPassword);
            if (credentials is null)
                return Results.BadRequest(new { error = "Informe URL, usuário e Application Password do WordPress." });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TestConnectionTimeout);
            try
            {
                return Results.Ok(await client.TestConnectionAsync(credentials, timeout.Token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.BadRequest(new { error = $"Tempo esgotado ({TestConnectionTimeout.TotalSeconds:0}s) ao conectar no WordPress." });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Falha no teste de conexão WordPress: {Message}", ex.Message);
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
