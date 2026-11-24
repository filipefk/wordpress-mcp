namespace McpToolkit;

// Estado da requisição HTTP em curso, implementado pelo host (o toolkit não depende de ASP.NET).
public interface IRequestContext
{
    // Credenciais do WordPress vindas do header x-api-key (null quando a key não as carrega).
    WordPressCredentials? Credentials { get; }

    // Header Mcp-Session-Id da requisição.
    string? SessionId { get; }
}
