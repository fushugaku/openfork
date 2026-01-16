using System.Security.Cryptography;
using System.Text;
using MAB.DotIgnore;
using Microsoft.Extensions.Logging;
using OpenFork.Search.Config;
using OpenFork.Search.Models;

namespace OpenFork.Search.Services;

public class FileIndexerService
{
    private readonly SearchConfig _config;
    private readonly ILogger<FileIndexerService> _logger;

    public FileIndexerService(SearchConfig config, ILogger<FileIndexerService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public List<string> GetFilesToIndex(string projectRoot)
    {
        var ignoreList = LoadIgnorePatterns(projectRoot);
        var files = new List<string>();

        foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(projectRoot, file);
            
            if (ShouldIgnore(relativePath, ignoreList))
                continue;

            if (!IsIndexableFile(file))
                continue;

            files.Add(file);
        }

        return files;
    }

    public List<FileChunk> ChunkFile(string filePath, string projectRoot, long projectId)
    {
        var chunks = new List<FileChunk>();
        var relativePath = Path.GetRelativePath(projectRoot, filePath);
        
        string content;
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > _config.MaxFileSize)
            {
                _logger.LogDebug("Skipping large file {FilePath} ({Size} bytes)", filePath, fileInfo.Length);
                return chunks;
            }

            content = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read file {FilePath}", filePath);
            return chunks;
        }

        var fileHash = ComputeHash(content);
        var lastModified = File.GetLastWriteTimeUtc(filePath);
        var lines = content.Split('\n');

        var chunkIndex = 0;
        var currentChunk = new StringBuilder();
        var startLine = 1;
        var currentLine = 1;
        var charCount = 0;

        foreach (var line in lines)
        {
            currentChunk.AppendLine(line);
            charCount += line.Length + 1;

            if (charCount >= _config.ChunkSize)
            {
                chunks.Add(CreateChunk(
                    projectId,
                    filePath,
                    relativePath,
                    currentChunk.ToString().TrimEnd(),
                    startLine,
                    currentLine,
                    chunkIndex++,
                    fileHash,
                    lastModified));

                var overlapLines = GetOverlapLines(lines, currentLine, _config.ChunkOverlap);
                currentChunk.Clear();
                foreach (var overlapLine in overlapLines)
                    currentChunk.AppendLine(overlapLine);

                charCount = currentChunk.Length;
                startLine = currentLine - overlapLines.Count + 1;
            }

            currentLine++;
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(CreateChunk(
                projectId,
                filePath,
                relativePath,
                currentChunk.ToString().TrimEnd(),
                startLine,
                currentLine - 1,
                chunkIndex,
                fileHash,
                lastModified));
        }

        return chunks;
    }

    public string ComputeFileHash(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return ComputeHash(content);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static FileChunk CreateChunk(
        long projectId,
        string filePath,
        string relativePath,
        string content,
        int startLine,
        int endLine,
        int chunkIndex,
        string fileHash,
        DateTime lastModified)
    {
        return new FileChunk
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            FilePath = filePath,
            RelativePath = relativePath,
            Content = content,
            StartLine = startLine,
            EndLine = endLine,
            ChunkIndex = chunkIndex,
            FileHash = fileHash,
            LastModified = new DateTimeOffset(lastModified, TimeSpan.Zero)
        };
    }

    private List<string> GetOverlapLines(string[] lines, int currentLine, int overlapChars)
    {
        var result = new List<string>();
        var charCount = 0;
        
        for (var i = currentLine - 1; i >= 0 && charCount < overlapChars; i--)
        {
            result.Insert(0, lines[i]);
            charCount += lines[i].Length + 1;
        }

        return result;
    }

    private IgnoreList LoadIgnorePatterns(string projectRoot)
    {
        var ignoreList = new IgnoreList();
        
        ignoreList.AddRule(".git/");
        ignoreList.AddRule(".git/**");
        ignoreList.AddRule("node_modules/");
        ignoreList.AddRule("node_modules/**");
        ignoreList.AddRule("bin/");
        ignoreList.AddRule("bin/**");
        ignoreList.AddRule("obj/");
        ignoreList.AddRule("obj/**");
        ignoreList.AddRule(".vs/");
        ignoreList.AddRule(".vs/**");
        ignoreList.AddRule(".idea/");
        ignoreList.AddRule(".idea/**");

        var gitignorePath = Path.Combine(projectRoot, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            try
            {
                var patterns = File.ReadAllLines(gitignorePath);
                foreach (var pattern in patterns)
                {
                    var trimmed = pattern.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                        ignoreList.AddRule(trimmed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read .gitignore at {Path}", gitignorePath);
            }
        }

        var openforkIgnorePath = Path.Combine(projectRoot, ".openforkignore");
        if (File.Exists(openforkIgnorePath))
        {
            try
            {
                var patterns = File.ReadAllLines(openforkIgnorePath);
                foreach (var pattern in patterns)
                {
                    var trimmed = pattern.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                        ignoreList.AddRule(trimmed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read .openforkignore at {Path}", openforkIgnorePath);
            }
        }

        return ignoreList;
    }

    private bool ShouldIgnore(string relativePath, IgnoreList ignoreList)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        return ignoreList.IsIgnored(normalizedPath, false);
    }

    private bool IsIndexableFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return _config.IndexableExtensions.Contains(extension);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
