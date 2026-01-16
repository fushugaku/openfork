namespace OpenFork.Search.Models;

public class FileChunk
{
    public string Id { get; set; } = string.Empty;
    public long ProjectId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int ChunkIndex { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }
}

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public float Score { get; set; }
}

public class IndexedFile
{
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }
    public int ChunkCount { get; set; }
}

public class IndexStatus
{
    public long ProjectId { get; set; }
    public bool IsAvailable { get; set; }
    public bool Exists { get; set; }
    public int TotalFiles { get; set; }
    public int TotalChunks { get; set; }
}
