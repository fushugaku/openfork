using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Config;
using OpenFork.Core.Hooks;
using OpenFork.Core.Hooks.BuiltIn;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for loading hooks from configuration and project files.
/// </summary>
public class HookLoader
{
    private readonly IHookService _hookService;
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HookLoader> _logger;

    public HookLoader(
        IHookService hookService,
        AppSettings settings,
        HttpClient httpClient,
        ILoggerFactory loggerFactory,
        ILogger<HookLoader> logger)
    {
        _hookService = hookService;
        _settings = settings;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Load all hooks from configuration and project files.
    /// </summary>
    public async Task LoadHooksAsync(CancellationToken ct = default)
    {
        if (!_settings.Hooks.Enabled)
        {
            _logger.LogInformation("Hooks system is disabled");
            return;
        }

        // Load built-in hooks
        LoadBuiltInHooks();

        // Load from settings
        foreach (var config in _settings.Hooks.Custom)
        {
            var hook = CreateHookFromConfig(config);
            if (hook != null)
            {
                _hookService.Register(hook);
                _logger.LogDebug("Registered custom hook: {Name}", config.Name);
            }
        }

        // Load project-level hooks
        await LoadProjectHooksAsync(ct);
    }

    private void LoadBuiltInHooks()
    {
        var builtInSettings = _settings.Hooks.BuiltIn;

        if (builtInSettings.Logging)
        {
            // Register logging hooks for all triggers
            var loggingLogger = _loggerFactory.CreateLogger<LoggingHook>();
            foreach (HookTrigger trigger in Enum.GetValues<HookTrigger>())
            {
                _hookService.Register(new LoggingHook(trigger, loggingLogger));
            }
            _logger.LogDebug("Registered logging hooks for all triggers");
        }

        if (builtInSettings.CommandValidation)
        {
            _hookService.Register(new CommandValidationHook());
            _logger.LogDebug("Registered command validation hook");
        }

        if (builtInSettings.FileBackup)
        {
            var backupLogger = _loggerFactory.CreateLogger<FileBackupHook>();
            _hookService.Register(new FileBackupHook(_settings.Hooks.BackupDirectory, backupLogger));
            _logger.LogDebug("Registered file backup hook");
        }
    }

    private IHook? CreateHookFromConfig(HookConfig config)
    {
        return config.Type switch
        {
            HookType.Command => new CommandHook(
                config,
                _loggerFactory.CreateLogger<CommandHook>()),

            HookType.Webhook => new WebhookHook(
                config,
                _httpClient,
                _loggerFactory.CreateLogger<WebhookHook>()),

            _ => null
        };
    }

    private async Task LoadProjectHooksAsync(CancellationToken ct)
    {
        var projectHooksPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".openfork", "hooks.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "openfork.hooks.json"),
        };

        foreach (var projectHooksPath in projectHooksPaths)
        {
            if (File.Exists(projectHooksPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(projectHooksPath, ct);
                    var projectHooks = JsonSerializer.Deserialize<ProjectHooksConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    foreach (var config in projectHooks?.Hooks ?? new List<HookConfig>())
                    {
                        var hook = CreateHookFromConfig(config);
                        if (hook != null)
                        {
                            _hookService.Register(hook);
                            _logger.LogInformation("Loaded project hook: {Name} from {Path}", config.Name, projectHooksPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load project hooks from {Path}", projectHooksPath);
                }
            }
        }
    }
}
