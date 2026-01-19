using Microsoft.Extensions.Logging;
using OpenFork.Search.Config;
using OpenFork.Search.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace OpenFork.Search.Services;

public class VectorStoreService : IDisposable
{
  private readonly SearchConfig _config;
  private readonly ILogger<VectorStoreService> _logger;
  private QdrantClient? _client;
  private bool _disposed;

  private const string CollectionPrefix = "openfork_project_";

  public VectorStoreService(SearchConfig config, ILogger<VectorStoreService> logger)
  {
    _config = config;
    _logger = logger;
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (_disposed) return;

    if (disposing)
    {
      _client?.Dispose();
      _client = null;
    }

    _disposed = true;
  }

  private QdrantClient GetClient()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    return _client ??= new QdrantClient(_config.QdrantHost, _config.QdrantPort);
  }

  public async Task<bool> EnsureCollectionAsync(long projectId, CancellationToken cancellationToken = default)
  {
    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      var collections = await client.ListCollectionsAsync(cancellationToken);
      if (collections.Any(c => c == collectionName))
        return true;

      await client.CreateCollectionAsync(
          collectionName,
          new VectorParams
          {
            Size = (ulong)_config.EmbeddingDimension,
            Distance = Distance.Cosine
          },
          cancellationToken: cancellationToken);

      _logger.LogInformation("Created Qdrant collection {CollectionName}", collectionName);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to ensure Qdrant collection for project {ProjectId}. Semantic search disabled.", projectId);
      return false;
    }
  }

  public async Task UpsertChunksAsync(long projectId, List<(FileChunk Chunk, float[] Embedding)> chunks, CancellationToken cancellationToken = default)
  {
    if (chunks.Count == 0)
      return;

    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      var points = chunks.Select(c => new PointStruct
      {
        Id = new PointId { Uuid = c.Chunk.Id },
        Vectors = c.Embedding,
        Payload =
              {
                  ["file_path"] = c.Chunk.FilePath,
                  ["relative_path"] = c.Chunk.RelativePath,
                  ["content"] = c.Chunk.Content,
                  ["start_line"] = c.Chunk.StartLine,
                  ["end_line"] = c.Chunk.EndLine,
                  ["chunk_index"] = c.Chunk.ChunkIndex,
                  ["file_hash"] = c.Chunk.FileHash,
                  ["last_modified"] = c.Chunk.LastModified.ToUnixTimeSeconds()
              }
      }).ToList();

      await client.UpsertAsync(collectionName, points, cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to upsert chunks to Qdrant for project {ProjectId}.", projectId);
    }
  }

  public async Task DeleteByFilePathAsync(long projectId, string filePath, CancellationToken cancellationToken = default)
  {
    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      await client.DeleteAsync(
          collectionName,
          new Filter
          {
            Must =
              {
                      new Condition
                      {
                          Field = new FieldCondition
                          {
                              Key = "file_path",
                              Match = new Match { Keyword = filePath }
                          }
                      }
              }
          },
          cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to delete file {FilePath} from Qdrant for project {ProjectId}.", filePath, projectId);
    }
  }

  public async Task<List<SearchResult>> SearchAsync(long projectId, float[] queryEmbedding, int limit = 10, CancellationToken cancellationToken = default)
  {
    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      var searchResults = await client.SearchAsync(
          collectionName,
          queryEmbedding,
          limit: (ulong)limit,
          cancellationToken: cancellationToken);

      return searchResults
        .Where(r => r.Payload != null &&
                    r.Payload.ContainsKey("file_path") &&
                    r.Payload.ContainsKey("content"))
        .Select(r => new SearchResult
        {
          FilePath = r.Payload.TryGetValue("file_path", out var fp) ? fp.StringValue : string.Empty,
          RelativePath = r.Payload.TryGetValue("relative_path", out var rp) ? rp.StringValue : string.Empty,
          Content = r.Payload.TryGetValue("content", out var c) ? c.StringValue : string.Empty,
          StartLine = r.Payload.TryGetValue("start_line", out var sl) ? (int)sl.IntegerValue : 0,
          EndLine = r.Payload.TryGetValue("end_line", out var el) ? (int)el.IntegerValue : 0,
          Score = r.Score
        }).ToList();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to search Qdrant for project {ProjectId}. Returning empty results.", projectId);
      return new List<SearchResult>();
    }
  }

  public async Task<List<IndexedFile>> GetIndexedFilesAsync(long projectId, CancellationToken cancellationToken = default)
  {
    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      var collections = await client.ListCollectionsAsync(cancellationToken);
      if (!collections.Any(c => c == collectionName))
        return new List<IndexedFile>();

      var result = new Dictionary<string, IndexedFile>();
      PointId? nextOffset = null;

      do
      {
        var scrollResult = await client.ScrollAsync(
            collectionName,
            limit: 100,
            offset: nextOffset,
            payloadSelector: true,
            cancellationToken: cancellationToken);

        var points = scrollResult.Result.ToList();

        foreach (var point in points)
        {
          if (point.Payload == null || !point.Payload.TryGetValue("file_path", out var filePathValue))
            continue;

          var filePath = filePathValue.StringValue;
          if (string.IsNullOrEmpty(filePath))
            continue;

          if (!result.ContainsKey(filePath))
          {
            var fileHash = point.Payload.TryGetValue("file_hash", out var fh) ? fh.StringValue : string.Empty;
            var lastModified = point.Payload.TryGetValue("last_modified", out var lm)
              ? DateTimeOffset.FromUnixTimeSeconds((long)lm.IntegerValue)
              : DateTimeOffset.MinValue;

            result[filePath] = new IndexedFile
            {
              FilePath = filePath,
              FileHash = fileHash,
              LastModified = lastModified,
              ChunkCount = 0
            };
          }
          result[filePath].ChunkCount++;
        }

        var lastPoint = points.LastOrDefault();
        nextOffset = lastPoint?.Id;

        if (points.Count < 100)
          break;

      } while (nextOffset != null);

      return result.Values.ToList();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to get indexed files from Qdrant for project {ProjectId}. Returning empty list.", projectId);
      return new List<IndexedFile>();
    }
  }

  public async Task DeleteCollectionAsync(long projectId, CancellationToken cancellationToken = default)
  {
    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      var collections = await client.ListCollectionsAsync(cancellationToken);
      if (collections.Any(c => c == collectionName))
      {
        await client.DeleteCollectionAsync(collectionName, cancellationToken: cancellationToken);
        _logger.LogInformation("Deleted Qdrant collection {CollectionName}", collectionName);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to delete Qdrant collection for project {ProjectId}.", projectId);
    }
  }

  public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var client = GetClient();
      await client.ListCollectionsAsync(cancellationToken);
      return true;
    }
    catch
    {
      return false;
    }
  }

  public async Task<IndexStatus> GetIndexStatusAsync(long projectId, CancellationToken cancellationToken = default)
  {
    var status = new IndexStatus { ProjectId = projectId };

    try
    {
      var client = GetClient();
      var collectionName = GetCollectionName(projectId);

      var collections = await client.ListCollectionsAsync(cancellationToken);
      status.IsAvailable = true;

      if (!collections.Any(c => c == collectionName))
      {
        status.Exists = false;
        return status;
      }

      status.Exists = true;

      var collectionInfo = await client.GetCollectionInfoAsync(collectionName, cancellationToken);
      status.TotalChunks = (int)collectionInfo.PointsCount;

      var indexedFiles = await GetIndexedFilesAsync(projectId, cancellationToken);
      status.TotalFiles = indexedFiles.Count;
    }
    catch
    {
      status.IsAvailable = false;
    }

    return status;
  }

  private static string GetCollectionName(long projectId) => $"{CollectionPrefix}{projectId}";
}
