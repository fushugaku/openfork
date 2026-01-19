using OpenFork.Core.Tools;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for loading pipeline tools from *.tool.json files.
/// </summary>
public interface IToolFileLoader
{
    /// <summary>
    /// Scans a directory for *.tool.json files and loads them as pipeline tools.
    /// </summary>
    /// <param name="directory">The directory to scan for tool definition files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of loaded tools.</returns>
    Task<List<ITool>> LoadToolsAsync(string directory, CancellationToken ct = default);

    /// <summary>
    /// Loads a single pipeline tool from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the *.tool.json file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded tool, or null if loading failed.</returns>
    Task<ITool?> LoadToolAsync(string filePath, CancellationToken ct = default);
}
