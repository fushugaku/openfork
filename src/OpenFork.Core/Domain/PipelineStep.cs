namespace OpenFork.Core.Domain;

public class PipelineStep
{
    public long Id { get; set; }
    public long PipelineId { get; set; }
    public int OrderIndex { get; set; }
    public long AgentId { get; set; }
    public string HandoffMode { get; set; } = "full";
}
