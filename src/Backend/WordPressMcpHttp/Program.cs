using McpToolkit;
using Serilog;
using Serilog.Events;
using System.Threading.RateLimiting;
using WordPressMcpHttp;
using WordPressMcpHttp.Endpoints;
using WordPressMcpHttp.Middleware;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var strLogLevel = configuration.GetValue<string>("Logging:LogLevel:Default");
var enumLogEventLevel = Enum.TryParse<LogEventLevel>(strLogLevel, true, out var parsedLogEventLevel)
    ? parsedLogEventLevel
    : LogEventLevel.Information;
builder.Host.UseSerilog((context, loggerConfiguration) =>
    loggerConfiguration
        .MinimumLevel.Is(enumLogEventLevel)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .WriteTo.Console(enumLogEventLevel)
        .WriteTo.File("logs/log.txt",
            enumLogEventLevel,
            rollingInterval: RollingInterval.Day));

builder.Services.Configure<WordPressOptions>(configuration.GetSection("WordPress"));
// Sem credenciais na seção "WordPress", aceita as variáveis de ambiente WORDPRESS_URL, WORDPRESS_USER e
// WORDPRESS_APP_PASSWORD — permite subir o servidor só com variáveis, sem appsettings.
builder.Services.PostConfigure<WordPressOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.Url))
        options.Url = configuration["WORDPRESS_URL"] ?? string.Empty;
    if (string.IsNullOrWhiteSpace(options.User))
        options.User = configuration["WORDPRESS_USER"] ?? string.Empty;
    if (string.IsNullOrWhiteSpace(options.ApplicationPassword))
        options.ApplicationPassword = configuration["WORDPRESS_APP_PASSWORD"] ?? string.Empty;
});
// Chaves MCP autorizadas (primeiro valor do header x-api-key) — lista vazia rejeita
// todas as chamadas a /mcp e /api-key.
builder.Services.Configure<McpAuthOptions>(configuration.GetSection("McpAuth"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IRequestContext, HttpRequestContext>();

builder.Services.AddHttpClient(WordPressClient.HttpClientName, client =>
    client.Timeout = TimeSpan.FromSeconds(Math.Max(configuration.GetValue("WordPress:TimeoutSeconds", 60), 1)));
builder.Services.AddSingleton<WordPressClient>();

// Limite por IP. Tem de ser o GlobalLimiter, e não RequireRateLimiting no endpoint: os UseWhen de
// autenticação rodam antes do roteamento, então uma política por endpoint só seria avaliada depois do 401.
var mcpPermits = configuration.GetValue("RateLimit:McpPermitsPerMinute", 300);
var apiKeyPermits = configuration.GetValue("RateLimit:ApiKeyPermitsPerMinute", 10);
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

        if (context.Request.Path.StartsWithSegments("/api-key"))
            return RateLimitPartition.GetFixedWindowLimiter($"api-key:{ip}", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = apiKeyPermits, Window = TimeSpan.FromMinutes(1) });

        if (context.Request.Path.StartsWithSegments("/mcp"))
            return RateLimitPartition.GetFixedWindowLimiter($"mcp:{ip}", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = mcpPermits, Window = TimeSpan.FromMinutes(1) });

        return RateLimitPartition.GetNoLimiter("sem-limite");
    });
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<WordPressTools>();

var app = builder.Build();

if (!configuration.GetSection("McpAuth:ApiKeys").GetChildren().Any(k => !string.IsNullOrEmpty(k.Value)))
    app.Logger.LogWarning("Nenhuma chave em McpAuth:ApiKeys — todas as chamadas a /mcp e /api-key serão rejeitadas (401).");

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseSerilogRequestLogging();

// Página da raiz (wwwroot/index.html) para gerar/decodificar a API Key pelos endpoints /api-key.
app.UseDefaultFiles();
app.UseStaticFiles();

// Antes dos UseWhen de autenticação, para o 429 vir antes do 401.
app.UseRateLimiter();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/mcp"),
    appBuilder => appBuilder
        .UseMiddleware<McpTrafficLoggingMiddleware>()
        .UseMiddleware<McpApiKeyMiddleware>(McpKeyFormat.Encoded));

// /api-key também exige x-api-key, mas com a chave MCP em texto puro. Sem o middleware de tráfego,
// para a Application Password do corpo não ir para o log.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api-key"),
    appBuilder => appBuilder.UseMiddleware<McpApiKeyMiddleware>(McpKeyFormat.Raw));

app.MapApiKeyEndpoints();
app.MapMcp("/mcp");

// Liveness para container/orquestrador — aberto, fora dos prefixos protegidos.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
