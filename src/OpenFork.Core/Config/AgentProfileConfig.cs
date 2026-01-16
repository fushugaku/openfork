namespace OpenFork.Core.Config;

public class AgentProfileConfig
{
  public string Name { get; set; } = string.Empty;
  public string SystemPrompt { get; set; } = string.Empty;
  public string? ProviderKey { get; set; }
  public string? Model { get; set; }
  public int MaxIterations { get; set; } = 0;
}
