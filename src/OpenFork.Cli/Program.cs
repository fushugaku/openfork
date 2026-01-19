using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFork.Cli;
using OpenFork.Cli.Tui;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Config;
using OpenFork.Core.Events;
using OpenFork.Core.Lsp;
using OpenFork.Core.Mcp;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using OpenFork.Providers;
using OpenFork.Search.Config;
using OpenFork.Search.Services;
using OpenFork.Storage;
using OpenFork.Storage.Repositories;
using Terminal.Gui;

var configPath = Environment.GetEnvironmentVariable("OPENFORK_CONFIG")
                 ?? FindConfigPath() ?? Path.Combine(Environment.CurrentDirectory, "config", "appsettings.json");

var logPath = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "..", "data", "openfork.log");
var promptsPath = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "prompts");

PromptLoader.Initialize(promptsPath);

var configBuilder = new ConfigurationBuilder()
    .AddEnvironmentVariables();

if (File.Exists(configPath))
{
    configBuilder.AddJsonFile(configPath, optional: false, reloadOnChange: true);
}

var configuration = configBuilder.Build();

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddProvider(new FileLoggerProvider(logPath));
    })
    .ConfigureServices(services =>
    {
        services.Configure<AppSettings>(configuration);
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>().Value);

        // Configure HttpClient with appropriate timeouts for streaming
        services.AddHttpClient().ConfigureHttpClientDefaults(builder =>
        {
            builder.ConfigureHttpClient(client =>
            {
                // Long timeout for streaming responses (10 minutes)
                client.Timeout = TimeSpan.FromMinutes(10);
            });
            builder.UseSocketsHttpHandler((handler, _) =>
            {
                // Keep connections alive longer for streaming
                handler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5);
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(10);
                handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests;
                handler.KeepAlivePingTimeout = TimeSpan.FromSeconds(30);
                handler.KeepAlivePingDelay = TimeSpan.FromSeconds(60);
                // Enable connection reuse
                handler.EnableMultipleHttp2Connections = true;
            });
        });
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SchemaInitializer>();

        // Event Bus - core pub/sub system
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // Permission System
        services.AddSingleton<InMemoryUserPromptService>();
        services.AddSingleton<IUserPromptService>(sp => sp.GetRequiredService<InMemoryUserPromptService>());
        services.AddSingleton<IPermissionService, PermissionService>();

        // Token Management (3-Layer System)
        services.AddSingleton<ITokenEstimator, TokenEstimator>();
        services.AddSingleton<IOutputTruncationService, OutputTruncationService>();
        services.AddSingleton<IOutputPruningService, OutputPruningService>();
        services.AddScoped<ICompactionService, CompactionService>();

        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<IMessagePartRepository, MessagePartRepository>();
        services.AddSingleton<IAppStateRepository, AppStateRepository>();
        services.AddSingleton<IPipelineRepository, PipelineRepository>();

        services.AddSingleton<IProviderResolver, ProviderResolver>();
        services.AddSingleton<LspService>();
        services.AddSingleton<ToolRegistry>(sp => new ToolRegistry(sp.GetService<LspService>()));

        services.AddSingleton<ProjectService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<AgentService>();
        services.AddSingleton<PipelineService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<AppStateService>();
        services.AddSingleton<BootstrapService>();

        // Agent Registry (loads built-in + appsettings.json agents)
        services.AddSingleton<IAgentRegistry, AgentRegistry>();

        // Subagent System
        services.AddSingleton<ISubSessionRepository, SubSessionRepository>();
        services.AddSingleton<SubagentConcurrencyManager>();
        services.AddSingleton<ISubagentService, SubagentService>();
        services.AddSingleton<TaskTool>();

        // Hooks System
        services.AddSingleton<IHookService, HookService>();
        services.AddSingleton<HookLoader>();

        // Pipeline Tools
        services.AddSingleton<IToolFileLoader, ToolFileLoader>();

        // MCP Integration
        services.AddSingleton<IMcpServerManager, McpServerManager>();

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new SearchConfig
            {
                QdrantHost = settings.Search.QdrantHost,
                QdrantPort = settings.Search.QdrantPort,
                OllamaUrl = settings.Search.OllamaUrl,
                EmbeddingModel = settings.Search.EmbeddingModel,
                EmbeddingDimension = settings.Search.EmbeddingDimension
            };
        });
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<VectorStoreService>();
        services.AddSingleton<FileIndexerService>();
        services.AddSingleton<ProjectIndexService>();

        services.AddSingleton<HistoryService>();
        services.AddSingleton<HistoryCompactService>();
        services.AddSingleton<SemanticHistoryProvider>();

        services.AddSingleton<ConsoleApp>();
    })
    .Build();

var schema = host.Services.GetRequiredService<SchemaInitializer>();
await schema.InitializeAsync();

var bootstrap = host.Services.GetRequiredService<BootstrapService>();
bootstrap.ConfigDirectory = Path.GetDirectoryName(configPath);
await bootstrap.InitializeAsync();

var chatService = host.Services.GetRequiredService<ChatService>();
var settings = host.Services.GetRequiredService<AppSettings>();

if (settings.Search.EnableSemanticSearch)
{
    var historyProvider = host.Services.GetRequiredService<SemanticHistoryProvider>();
    chatService.SetHistoryProvider(historyProvider);
}

var logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
var app = host.Services.GetRequiredService<ConsoleApp>();

try
{
    logger.LogInformation("Starting OpenFork TUI application...");
    
    // Initialize Terminal.Gui
    logger.LogInformation("Calling Application.Init()...");
    Application.Init();
    logger.LogInformation("Application.Init() completed");
    
    // Verify Application.Top exists
    if (Application.Top == null)
    {
        logger.LogError("Application.Top is null after Init()");
        Console.WriteLine("ERROR: Application.Top is null after Init()");
        return;
    }
    logger.LogInformation("Application.Top verified: {TopType}", Application.Top.GetType().Name);
    
    logger.LogInformation("Calling app.RunAsync()...");
    await app.RunAsync(CancellationToken.None);
    logger.LogInformation("app.RunAsync() completed");
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error in main loop");
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}
finally
{
    logger.LogInformation("Shutting down Terminal.Gui...");
    // Clean shutdown
    Application.Shutdown();
    logger.LogInformation("Application shutdown complete");
}

static string? FindConfigPath()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "config", "appsettings.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}
