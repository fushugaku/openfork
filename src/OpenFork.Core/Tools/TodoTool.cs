using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class TodoTracker
{
  private List<TodoItem> _items = new();
  private readonly object _lock = new();

  public IReadOnlyList<TodoItem> Items
  {
    get
    {
      lock (_lock)
      {
        return _items.ToList();
      }
    }
  }

  public int PendingCount => _items.Count(x => x.Status != "completed");

  public void Update(List<TodoItem> todos)
  {
    lock (_lock)
    {
      _items = todos.ToList();
    }
  }

  public void Clear()
  {
    lock (_lock)
    {
      _items.Clear();
    }
  }
}

public class TodoItem
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("content")]
  public string Content { get; set; } = string.Empty;

  [JsonPropertyName("status")]
  public string Status { get; set; } = "pending";
}

public class TodoWriteTool : ITool
{
  public string Name => "todowrite";

  public string Description => PromptLoader.Load("todowrite",
      @"Use this tool to manage a structured task list for your current coding session.

### When to Use
- Complex multi-step tasks (3+ distinct steps)
- Non-trivial tasks requiring careful planning
- After receiving new instructions - capture requirements as todos
- After completing tasks - mark complete and add follow-ups

### When NOT to Use
- Single, straightforward tasks
- Trivial tasks with no organizational benefit
- Tasks completable in < 3 trivial steps

### Task States
- pending: Not yet started
- in_progress: Currently working on (only ONE at a time)
- completed: Finished successfully  
- cancelled: No longer needed");

  public object ParametersSchema => new
  {
    type = "object",
    properties = new
    {
      todos = new
      {
        type = "array",
        description = "The updated todo list (replaces existing list)",
        items = new
        {
          type = "object",
          properties = new
          {
            id = new
            {
              type = "string",
              description = "Unique identifier for the todo item"
            },
            content = new
            {
              type = "string",
              description = "The task description (max 100 chars)"
            },
            status = new
            {
              type = "string",
              description = "Task status",
              @enum = new[] { "pending", "in_progress", "completed", "cancelled" }
            }
          },
          required = new[] { "id", "content", "status" }
        }
      }
    },
    required = new[] { "todos" }
  };

  public Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
  {
    if (context.TodoTracker == null)
      return Task.FromResult(new ToolResult(false, "Todo tracking not available"));

    try
    {
      var args = JsonSerializer.Deserialize<TodoWriteArgs>(arguments, JsonHelper.Options);
      if (args?.Todos == null)
        return Task.FromResult(new ToolResult(false, "No todos provided"));

      var todos = args.Todos.Select(t => new TodoItem
      {
        Id = t.Id ?? Guid.NewGuid().ToString("N")[..8],
        Content = t.Content?.Length > 100 ? t.Content[..100] : t.Content ?? "",
        Status = t.Status ?? "pending"
      }).ToList();

      context.TodoTracker.Update(todos);

      var pendingCount = todos.Count(x => x.Status != "completed");
      var output = JsonSerializer.Serialize(todos, new JsonSerializerOptions { WriteIndented = true });

      return Task.FromResult(new ToolResult(true, $"{pendingCount} todos\n{output}"));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new ToolResult(false, $"Error: {ex.Message}"));
    }
  }

  private record TodoWriteArgs(
      [property: JsonPropertyName("todos")] List<TodoItemArg>? Todos
  );

  private record TodoItemArg(
      [property: JsonPropertyName("id")] string? Id,
      [property: JsonPropertyName("content")] string? Content,
      [property: JsonPropertyName("status")] string? Status
  );
}

public class TodoReadTool : ITool
{
  public string Name => "todoread";

  public string Description => PromptLoader.Load("todoread",
      "Use this tool to read your todo list to see current task status and plan next steps.");

  public object ParametersSchema => new
  {
    type = "object",
    properties = new { },
    required = Array.Empty<string>()
  };

  public Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
  {
    if (context.TodoTracker == null)
      return Task.FromResult(new ToolResult(false, "Todo tracking not available"));

    var items = context.TodoTracker.Items;
    if (items.Count == 0)
      return Task.FromResult(new ToolResult(true, "No todos"));

    var pendingCount = items.Count(x => x.Status != "completed");
    var output = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });

    return Task.FromResult(new ToolResult(true, $"{pendingCount} todos\n{output}"));
  }
}

internal static class JsonHelper
{
  public static readonly JsonSerializerOptions Options = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
