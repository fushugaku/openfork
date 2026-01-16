namespace OpenFork.Core.Domain;

public class AgentProfile
{
  public long Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public string SystemPrompt { get; set; } = string.Empty;
  public string ProviderKey { get; set; } = string.Empty;
  public string Model { get; set; } = string.Empty;
  public int MaxIterations { get; set; } = 0;
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
