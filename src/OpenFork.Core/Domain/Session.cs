namespace OpenFork.Core.Domain;

public class Session
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? ActiveAgentId { get; set; }
    public long? ActivePipelineId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
