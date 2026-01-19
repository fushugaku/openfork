namespace OpenFork.Core.Hooks;

/// <summary>
/// Settings for the hooks system.
/// </summary>
public class HookSettings
{
    /// <summary>Whether hooks are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Directory for file backups.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Built-in hook settings.</summary>
    public BuiltInHookSettings BuiltIn { get; set; } = new();

    /// <summary>Custom hook configurations.</summary>
    public List<HookConfig> Custom { get; set; } = new();
}

/// <summary>
/// Settings for built-in hooks.
/// </summary>
public class BuiltInHookSettings
{
    /// <summary>Enable logging hooks.</summary>
    public bool Logging { get; set; } = true;

    /// <summary>Enable command validation hook.</summary>
    public bool CommandValidation { get; set; } = true;

    /// <summary>Enable file backup hook.</summary>
    public bool FileBackup { get; set; } = true;
}

/// <summary>
/// Project-level hooks configuration file format.
/// </summary>
public class ProjectHooksConfig
{
    public List<HookConfig> Hooks { get; set; } = new();
}
