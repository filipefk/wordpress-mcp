using McpToolkit;

namespace WordPressMcpHttp;

// Decodifica o header x-api-key da requisição em curso (já validado pelo McpApiKeyMiddleware)
// e expõe o id da sessão MCP.
public sealed class HttpRequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    private const string SessionIdHeader = "Mcp-Session-Id";

    public WordPressCredentials? Credentials =>
        McpApiKey.TryRead(httpContextAccessor.HttpContext, out var apiKey) ? apiKey.Credentials : null;

    public string? SessionId =>
        httpContextAccessor.HttpContext?.Request.Headers[SessionIdHeader].FirstOrDefault();
}
