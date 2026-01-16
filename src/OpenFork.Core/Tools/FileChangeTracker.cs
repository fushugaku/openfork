namespace OpenFork.Core.Tools;

public class FileChangeTracker
{
    private readonly List<FileChange> _changes = new();
    private readonly object _lock = new();

    public IReadOnlyList<FileChange> Changes
    {
        get
        {
            lock (_lock)
            {
                return _changes.ToList();
            }
        }
    }

    public void TrackChange(FileChange change)
    {
        lock (_lock)
        {
            var existing = _changes.FirstOrDefault(c => c.FilePath == change.FilePath);
            if (existing != null)
            {
                existing.LinesAdded += change.LinesAdded;
                existing.LinesDeleted += change.LinesDeleted;
                existing.NewLineCount = change.NewLineCount;
            }
            else
            {
                _changes.Add(change);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _changes.Clear();
        }
    }

    public static FileChange ComputeChange(string filePath, string? oldContent, string newContent)
    {
        var isNew = string.IsNullOrEmpty(oldContent);
        var newLines = newContent.Split('\n').Length;

        if (isNew)
        {
            return new FileChange
            {
                FilePath = filePath,
                IsNew = true,
                NewLineCount = newLines,
                LinesAdded = newLines,
                LinesDeleted = 0
            };
        }

        var oldLines = oldContent!.Split('\n');
        var newLinesArr = newContent.Split('\n');

        var (added, deleted) = ComputeLineDiff(oldLines, newLinesArr);

        return new FileChange
        {
            FilePath = filePath,
            IsNew = false,
            NewLineCount = newLines,
            LinesAdded = added,
            LinesDeleted = deleted
        };
    }

    private static (int added, int deleted) ComputeLineDiff(string[] oldLines, string[] newLines)
    {
        var oldSet = new HashSet<string>(oldLines);
        var newSet = new HashSet<string>(newLines);

        var added = newLines.Count(line => !oldSet.Contains(line));
        var deleted = oldLines.Count(line => !newSet.Contains(line));

        return (added, deleted);
    }
}

public class FileChange
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public int NewLineCount { get; set; }
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }

    public string RelativePath(string workingDir)
    {
        if (FilePath.StartsWith(workingDir, StringComparison.OrdinalIgnoreCase))
        {
            var rel = FilePath[(workingDir.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel;
        }
        return Path.GetFileName(FilePath);
    }
}
