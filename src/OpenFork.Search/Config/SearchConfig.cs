namespace OpenFork.Search.Config;

public class SearchConfig
{
    public string QdrantHost { get; set; } = "localhost";
    public int QdrantPort { get; set; } = 6334;
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int EmbeddingDimension { get; set; } = 768;
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public int MaxFileSize { get; set; } = 1024 * 1024;
    public List<string> IndexableExtensions { get; set; } = new()
    {
        ".cs", ".fs", ".vb",
        ".ts", ".tsx", ".js", ".jsx",
        ".py", ".rb", ".go", ".rs",
        ".java", ".kt", ".scala",
        ".c", ".cpp", ".h", ".hpp",
        ".html", ".css", ".scss", ".less",
        ".json", ".yaml", ".yml", ".xml", ".toml",
        ".md", ".txt", ".sql", ".sh", ".bash",
        ".dockerfile", ".makefile"
    };
}
