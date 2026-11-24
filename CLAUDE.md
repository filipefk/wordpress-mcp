# CLAUDE.md

## Visão geral

Servidor MCP (Model Context Protocol) sobre HTTP que expõe a REST API do WordPress (`/wp-json/wp/v2/*`) como tools, em .NET 10. Fala com o site remotamente via Application Password (Basic Auth) — diferente do adapter PHP oficial, que roda como plugin dentro do WordPress.

Segue o mesmo modelo de `../oracle-mcp-http`: host ASP.NET fino + `McpToolkit` (tools, sem dependência de ASP.NET) + `GeraApiKey`.

## Comandos

- Build: `dotnet build WordPressMcpHttp.slnx`
- Executar: `dotnet run --project src/Backend/WordPressMcpHttp` (http://localhost:5465)
- Só buildar/rodar quando solicitado explicitamente.

## Arquitetura

- `src/Backend/GeraApiKey` — `ApiKeyGenerator`: junta uma lista de strings numa key segura para header (ofuscação, não criptografia). Copiado sem alterações do oracle-mcp-http.
- `src/Backend/McpToolkit` — lógica e tools:
  - `WordPressTools` — classe `[McpServerToolType]` com as tools (posts/páginas, mídia, taxonomias, comentários, usuários, configurações, plugins/temas, site e `wp_rest_request` genérica).
  - `WordPressClient` — `HttpClient` (via `IHttpClientFactory`, cliente nomeado `wordpress`) que monta Basic Auth por requisição, lê `X-WP-Total`/`X-WP-TotalPages` e converte erros do WordPress em `InvalidOperationException` em pt-BR.
  - `WordPressArgs` — helpers de query string/corpo JSON; `WordPressCredentials`, `WordPressOptions`, `IRequestContext`.
- `src/Backend/WordPressMcpHttp` — host:
  - `Program.cs` — Serilog, rate limit global por IP, `AddMcpServer().WithHttpTransport().WithTools<WordPressTools>()`, `/mcp`, `/health`.
  - `McpApiKey` — único lugar que conhece a ordem dos valores da key: `[mcpApiKey, wpUrl, wpUser, applicationPassword]`.
  - `Middleware/McpApiKeyMiddleware` — valida `x-api-key` contra `McpAuth:ApiKeys` (fail-closed, comparação em tempo constante).
  - `Middleware/McpTrafficLoggingMiddleware` — loga tráfego de `/mcp` mascarando `x-api-key`/`Authorization` e campos de senha.
  - `Endpoints/ApiKeyEndpoints` — `POST /api-key/generate|decode|test-connection`; `wwwroot/index.html` é a tela.

## Precedência das credenciais do WordPress

1. `x-api-key` (credenciais gravadas na key — permite vários sites/usuários).
2. Configuração do servidor: seção `WordPress` ou variáveis `WORDPRESS_URL`, `WORDPRESS_USER`, `WORDPRESS_APP_PASSWORD`.

A senha nunca é devolvida nem logada.

## Convenções

- Tools: métodos `snake_case` com `[McpServerTool(Title, ReadOnly, Destructive, Idempotent, OpenWorld = true)]` e `[Description]` em pt-BR; retornam `string` JSON; último parâmetro `CancellationToken ct = default`.
- Entrada/saída via `JsonObject`/`JsonNode`, sem DTOs. Listas retornam `{total, totalPages, items}`.
- Padrões seguros: `create_post` cria como `draft`; `delete_*` vai para a lixeira, salvo `force=true`.
- Textos voltados ao usuário em pt-BR. Sem comentários `<summary>` (XML doc) no C#.
- Credenciais reais ficam fora do git (`appsettings.Development.json` só com chave de exemplo; `.env` ignorado).
