using Microsoft.Extensions.Logging;
using OpenFork.Search.Models;

namespace OpenFork.Search.Services;

public class ProjectIndexService
{
    private readonly FileIndexerService _fileIndexer;
    private readonly EmbeddingService _embeddingService;
    private readonly VectorStoreService _vectorStore;
    private readonly ILogger<ProjectIndexService> _logger;

    public ProjectIndexService(
        FileIndexerService fileIndexer,
        EmbeddingService embeddingService,
        VectorStoreService vectorStore,
        ILogger<ProjectIndexService> logger)
    {
        _fileIndexer = fileIndexer;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<IndexResult> IndexProjectAsync(
        long projectId,
        string projectRoot,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new IndexResult();

        if (!await _embeddingService.IsAvailableAsync(cancellationToken))
        {
            _logger.LogWarning("Ollama embedding service is not available");
            result.Error = "Ollama embedding service is not available. Please ensure Ollama is running.";
            return result;
        }

        if (!await _vectorStore.IsAvailableAsync(cancellationToken))
        {
            _logger.LogWarning("Qdrant vector store is not available");
            result.Error = "Qdrant vector store is not available. Please ensure Qdrant is running.";
            return result;
        }

        await _vectorStore.EnsureCollectionAsync(projectId, cancellationToken);

        var indexedFiles = await _vectorStore.GetIndexedFilesAsync(projectId, cancellationToken);
        var indexedFileMap = indexedFiles.ToDictionary(f => f.FilePath, f => f);

        var filesToIndex = _fileIndexer.GetFilesToIndex(projectRoot);
        var filesToProcess = new List<(string FilePath, FileAction Action)>();

        foreach (var filePath in filesToIndex)
        {
            var currentHash = _fileIndexer.ComputeFileHash(filePath);
            
            if (indexedFileMap.TryGetValue(filePath, out var indexed))
            {
                if (indexed.FileHash != currentHash)
                {
                    filesToProcess.Add((filePath, FileAction.Update));
                    result.UpdatedFiles++;
                }
                else
                {
                    result.SkippedFiles++;
                }
                indexedFileMap.Remove(filePath);
            }
            else
            {
                filesToProcess.Add((filePath, FileAction.Add));
                result.NewFiles++;
            }
        }

        foreach (var deletedFile in indexedFileMap.Keys)
        {
            await _vectorStore.DeleteByFilePathAsync(projectId, deletedFile, cancellationToken);
            result.DeletedFiles++;
        }

        var totalFiles = filesToProcess.Count;
        var processedFiles = 0;

        foreach (var (filePath, action) in filesToProcess)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                if (action == FileAction.Update)
                {
                    await _vectorStore.DeleteByFilePathAsync(projectId, filePath, cancellationToken);
                }

                var chunks = _fileIndexer.ChunkFile(filePath, projectRoot, projectId);
                if (chunks.Count == 0)
                    continue;

                var chunksWithEmbeddings = new List<(FileChunk Chunk, float[] Embedding)>();

                foreach (var chunk in chunks)
                {
                    var embedding = await _embeddingService.GetEmbeddingAsync(chunk.Content, cancellationToken);
                    if (embedding != null)
                    {
                        chunksWithEmbeddings.Add((chunk, embedding));
                        result.ChunksIndexed++;
                    }
                }

                if (chunksWithEmbeddings.Count > 0)
                {
                    await _vectorStore.UpsertChunksAsync(projectId, chunksWithEmbeddings, cancellationToken);
                }

                result.FilesIndexed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index file {FilePath}", filePath);
                result.FailedFiles++;
            }

            processedFiles++;
            progress?.Report(new IndexProgress
            {
                TotalFiles = totalFiles,
                ProcessedFiles = processedFiles,
                CurrentFile = Path.GetFileName(filePath)
            });
        }

        result.Success = true;
        return result;
    }

    public async Task<List<SearchResult>> SearchAsync(
        long projectId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var embedding = await _embeddingService.GetEmbeddingAsync(query, cancellationToken);
        if (embedding == null)
        {
            _logger.LogWarning("Failed to get embedding for query");
            return new List<SearchResult>();
        }

        return await _vectorStore.SearchAsync(projectId, embedding, limit, cancellationToken);
    }

    public async Task ReindexProjectAsync(
        long projectId,
        string projectRoot,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _vectorStore.DeleteCollectionAsync(projectId, cancellationToken);
        await IndexProjectAsync(projectId, projectRoot, progress, cancellationToken);
    }

    public Task<IndexStatus> GetIndexStatusAsync(long projectId, CancellationToken cancellationToken = default)
    {
        return _vectorStore.GetIndexStatusAsync(projectId, cancellationToken);
    }

    public async Task<bool> IsSearchAvailableAsync(CancellationToken cancellationToken = default)
    {
        var qdrantAvailable = await _vectorStore.IsAvailableAsync(cancellationToken);
        var ollamaAvailable = await _embeddingService.IsAvailableAsync(cancellationToken);
        return qdrantAvailable && ollamaAvailable;
    }

    private enum FileAction
    {
        Add,
        Update
    }
}

public class IndexResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int FilesIndexed { get; set; }
    public int ChunksIndexed { get; set; }
    public int NewFiles { get; set; }
    public int UpdatedFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public int FailedFiles { get; set; }
}

public class IndexProgress
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
}
