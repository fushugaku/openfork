using Microsoft.Extensions.Logging;
using OpenFork.Search.Config;
using OpenFork.Search.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace OpenFork.Search.Services;

public class VectorStoreService
{
  private readonly SearchConfig _config;
  private readonly ILogger<VectorStoreService> _logger;
  private QdrantClient? _client;

  private const string CollectionPrefix = "openfork_project_";

  public VectorStoreService(SearchConfig config, ILogger<VectorStoreService> logger)
  {
    _config = config;
    _logger = logger;
  }

  private QdrantClient GetClient()
  {
    return _client ??= new QdrantClient(_config.QdrantHost, _config.QdrantPort);
  }

  public async Task EnsureCollectionAsync(long projectId, CancellationToken cancellationToken = default)
  {
    var client = GetClient();
    var collectionName = GetCollectionName(projectId);

    var collections = await client.ListCollectionsAsync(cancellationToken);
    if (collections.Any(c => c == collectionName))
      return;

    await client.CreateCollectionAsync(
        collectionName,
        new VectorParams
        {
          Size = (ulong)_config.EmbeddingDimension,
          Distance = Distance.Cosine
        },
        cancellationToken: cancellationToken);

    _logger.LogInformation("Created Qdrant collection {CollectionName}", collectionName);
  }

  public async Task UpsertChunksAsync(long projectId, List<(FileChunk Chunk, float[] Embedding)> chunks, CancellationToken cancellationToken = default)
  {
    if (chunks.Count == 0)
      return;

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

  public async Task DeleteByFilePathAsync(long projectId, string filePath, CancellationToken cancellationToken = default)
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

  public async Task<List<SearchResult>> SearchAsync(long projectId, float[] queryEmbedding, int limit = 10, CancellationToken cancellationToken = default)
  {
    var client = GetClient();
    var collectionName = GetCollectionName(projectId);

    var searchResults = await client.SearchAsync(
        collectionName,
        queryEmbedding,
        limit: (ulong)limit,
        cancellationToken: cancellationToken);

    return searchResults.Select(r => new SearchResult
    {
      FilePath = r.Payload["file_path"].StringValue,
      RelativePath = r.Payload["relative_path"].StringValue,
      Content = r.Payload["content"].StringValue,
      StartLine = (int)r.Payload["start_line"].IntegerValue,
      EndLine = (int)r.Payload["end_line"].IntegerValue,
      Score = r.Score
    }).ToList();
  }

  public async Task<List<IndexedFile>> GetIndexedFilesAsync(long projectId, CancellationToken cancellationToken = default)
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
        var filePath = point.Payload["file_path"].StringValue;
        if (!result.ContainsKey(filePath))
        {
          result[filePath] = new IndexedFile
          {
            FilePath = filePath,
            FileHash = point.Payload["file_hash"].StringValue,
            LastModified = DateTimeOffset.FromUnixTimeSeconds((long)point.Payload["last_modified"].IntegerValue),
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

  public async Task DeleteCollectionAsync(long projectId, CancellationToken cancellationToken = default)
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
