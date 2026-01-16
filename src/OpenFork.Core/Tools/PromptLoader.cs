namespace OpenFork.Core.Tools;

public static class PromptLoader
{
    private static readonly Dictionary<string, string> _cache = new();
    private static string? _promptsDirectory;

    public static void Initialize(string promptsDirectory)
    {
        _promptsDirectory = promptsDirectory;
        _cache.Clear();
    }

    public static string Load(string toolName, string fallback = "")
    {
        if (_cache.TryGetValue(toolName, out var cached))
            return cached;

        var prompt = LoadFromFile(toolName) ?? fallback;
        
        prompt = ApplyPlaceholders(prompt);
        
        _cache[toolName] = prompt;
        return prompt;
    }

    private static string? LoadFromFile(string toolName)
    {
        if (string.IsNullOrEmpty(_promptsDirectory))
            return null;

        var filePath = Path.Combine(_promptsDirectory, $"{toolName}.txt");
        if (!File.Exists(filePath))
            return null;

        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static string? _workingDirectory;

    public static void SetWorkingDirectory(string? workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ClearCache();
    }

    private static string ApplyPlaceholders(string prompt)
    {
        var result = prompt;
        
        result = result.Replace("${directory}", _workingDirectory ?? Environment.CurrentDirectory);
        result = result.Replace("${maxLines}", "2000");
        result = result.Replace("${maxBytes}", "51200");
        result = result.Replace("{{date}}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        
        return result;
    }

    public static void ClearCache()
    {
        _cache.Clear();
    }
}
