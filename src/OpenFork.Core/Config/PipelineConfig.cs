namespace OpenFork.Core.Config;

public class PipelineConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<PipelineStepConfig> Steps { get; set; } = new();
}

public class PipelineStepConfig
{
    public string Agent { get; set; } = string.Empty;
    public string HandoffMode { get; set; } = "full";
}
