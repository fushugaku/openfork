using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenFork.Cli;
using OpenFork.Cli.Tui;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Config;
using OpenFork.Core.Lsp;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using OpenFork.Providers;
using OpenFork.Search.Config;
using OpenFork.Search.Services;
using OpenFork.Storage;
using OpenFork.Storage.Repositories;

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

        services.AddHttpClient();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SchemaInitializer>();

        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<IAppStateRepository, AppStateRepository>();
        services.AddSingleton<IAgentRepository, AgentRepository>();
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
await bootstrap.InitializeAsync();

var chatService = host.Services.GetRequiredService<ChatService>();
var settings = host.Services.GetRequiredService<AppSettings>();

if (settings.Search.EnableSemanticSearch)
{
    var historyProvider = host.Services.GetRequiredService<SemanticHistoryProvider>();
    chatService.SetHistoryProvider(historyProvider);
}

var app = host.Services.GetRequiredService<ConsoleApp>();

// Handle Ctrl+C to exit cleanly
Console.CancelKeyPress += (sender, e) =>
{
    // Allow default behavior - exit immediately
    // This will interrupt any active Spectre.Console prompt
    Environment.Exit(0);
};

await app.RunAsync(CancellationToken.None);

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
