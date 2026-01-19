using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Hooks.BuiltIn;

/// <summary>
/// Built-in hook that creates backups before file edits.
/// </summary>
public class FileBackupHook : IHook
{
    private readonly string _backupDirectory;
    private readonly ILogger<FileBackupHook> _logger;

    public string Id => "builtin-file-backup";
    public string Name => "File Backup";
    public HookTrigger Trigger => HookTrigger.PreEdit;
    public int Priority => 50;
    public bool Enabled { get; set; } = true;

    public FileBackupHook(string? backupDirectory, ILogger<FileBackupHook> logger)
    {
        _backupDirectory = backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openfork", "backups");
        _logger = logger;

        try
        {
            Directory.CreateDirectory(_backupDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create backup directory: {Path}", _backupDirectory);
        }
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(context.FilePath) || !File.Exists(context.FilePath))
        {
            return HookResult.Ok();
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = Path.GetFileName(context.FilePath);
            var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            var backupName = $"{safeFileName}.{timestamp}.bak";
            var backupPath = Path.Combine(_backupDirectory, backupName);

            await using var source = new FileStream(context.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var dest = new FileStream(backupPath, FileMode.Create, FileAccess.Write);
            await source.CopyToAsync(dest, ct);

            context.Data["backup_path"] = backupPath;
            _logger.LogDebug("Created backup: {Path}", backupPath);

            return HookResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create backup for {Path}", context.FilePath);
            // Don't block edit, just log warning
            return HookResult.Ok();
        }
    }
}
