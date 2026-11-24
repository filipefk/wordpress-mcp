namespace McpToolkit;

// Configuração de fallback do servidor (seção "WordPress"), usada quando a API key não traz credenciais.
public sealed class WordPressOptions
{
    public string Url { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string ApplicationPassword { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
}
