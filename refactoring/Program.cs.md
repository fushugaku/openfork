# Refactoring: Program.cs

## Overview
Replace Console.CancelKeyPress handler and initialize Terminal.Gui application lifecycle.

## Current Implementation
```csharp
// Handle Ctrl+C to exit cleanly
Console.CancelKeyPress += (sender, e) =>
{
    Environment.Exit(0);
};

await app.RunAsync(CancellationToken.None);
```

## Issues with Current Approach
- `Environment.Exit(0)` forces immediate termination
- No cleanup of Terminal.Gui resources
- Interrupts any active operations

## Target Implementation

### 1. Remove Console.CancelKeyPress handler
Terminal.Gui handles Ctrl+C natively through its application loop.

### 2. Initialize Terminal.Gui Application
```csharp
using Terminal.Gui;

// ... existing DI setup ...

var app = host.Services.GetRequiredService<ConsoleApp>();

// Initialize Terminal.Gui
Application.Init();

try
{
    await app.RunAsync(CancellationToken.None);
}
finally
{
    // Clean shutdown
    Application.Shutdown();
}
```

### 3. Handle cancellation token properly
```csharp
// Create cancellation token source for graceful shutdown
using var cts = new CancellationTokenSource();

Application.Init();

try
{
    // Register for application quit event
    Application.Top.KeyPress += (e) =>
    {
        if (e.KeyEvent.Key == Key.Esc || e.KeyEvent.Key == (Key.Q | Key.CtrlMask))
        {
            cts.Cancel();
            e.Handled = true;
        }
    };

    await app.RunAsync(cts.Token);
}
finally
{
    Application.Shutdown();
}
```

## Migration Steps

1. **Add Terminal.Gui usings**
   ```csharp
   using Terminal.Gui;
   ```

2. **Remove old Ctrl+C handler**
   ```csharp
   // DELETE THIS:
   Console.CancelKeyPress += (sender, e) =>
   {
       Environment.Exit(0);
   };
   ```

3. **Add Terminal.Gui initialization**
   ```csharp
   Application.Init();
   ```

4. **Wrap app.RunAsync in try-finally**
   ```csharp
   try
   {
       await app.RunAsync(CancellationToken.None);
   }
   finally
   {
       Application.Shutdown();
   }
   ```

## Full Refactored Program.cs

```csharp
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

// Initialize Terminal.Gui
Application.Init();

try
{
    await app.RunAsync(CancellationToken.None);
}
finally
{
    // Clean shutdown
    Application.Shutdown();
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
```

## Testing
1. Run the application: `dotnet run`
2. Verify Terminal.Gui initializes correctly
3. Test Esc key exits gracefully
4. Verify no console corruption on exit
