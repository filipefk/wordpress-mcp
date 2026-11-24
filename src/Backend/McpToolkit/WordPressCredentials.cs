namespace McpToolkit;

// Credenciais de um site WordPress: URL base, usuário e Application Password (Basic Auth).
public sealed record WordPressCredentials(string BaseUrl, string User, string ApplicationPassword)
{
    public static WordPressCredentials? TryCreate(string? baseUrl, string? user, string? applicationPassword) =>
        string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(applicationPassword)
            ? null
            : new WordPressCredentials(baseUrl.Trim(), user.Trim(), applicationPassword.Trim());
}
