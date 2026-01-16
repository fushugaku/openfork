using OpenFork.Core.Domain;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace OpenFork.Cli.Tui;

public delegate Task<List<QuestionAnswer>> AskUserDelegate(QuestionRequest request);
public delegate Task<List<Diagnostic>> GetDiagnosticsDelegate(string[] files);

public partial class ConsoleApp
{
  private FileChangeTracker _fileChangeTracker = new();
  private TodoTracker _todoTracker = new();

  private async Task ChatScreenAsync(CancellationToken cancellationToken)
  {
    if (_activeProject == null)
    {
      AnsiConsole.Clear();
      RenderHeader();
      AnsiConsole.Write(Panels.Error("Select a project first"));
      Pause();
      return;
    }

    if (_activeSession == null)
    {
      AnsiConsole.Clear();
      RenderHeader();
      RenderContext();
      await EnsureSessionSelectedAsync();
      if (_activeSession == null) return;
    }

    if (!await EnsureChatTargetSelectedAsync())
    {
      Pause();
      return;
    }

    _fileChangeTracker.Clear();
    _todoTracker.Clear();
    var history = await _chat.ListMessagesAsync(_activeSession.Id);

    while (!cancellationToken.IsCancellationRequested)
    {
      AnsiConsole.Clear();
      RenderHeader();
      RenderChatContext();
      RenderChatWithFilesPanel(history);

      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Rule($"[{Theme.Muted.ToMarkup()}]/exit to quit | /clear to reset tracking | /copy to copy message | ESC to stop agent[/]").LeftJustified().RuleStyle(Theme.MutedStyle));

      var input = AnsiConsole.Prompt(
          new TextPrompt<string>($"[{Theme.Accent.ToMarkup()}]>[/] ")
              .PromptStyle(Theme.PrimaryStyle)
              .AllowEmpty());

      // Sanitize input to remove control characters and keep only plain text
      input = SanitizeInput(input);

      if (string.IsNullOrWhiteSpace(input)) break;
      if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase) || input.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
      if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
      {
        _fileChangeTracker.Clear();
        _todoTracker.Clear();
        continue;
      }
      if (input.Equals("/copy", StringComparison.OrdinalIgnoreCase))
      {
        CopyMessageToClipboard(history);
        continue;
      }

      AnsiConsole.WriteLine();
      RenderUserMessage(input);

      var assistantResponse = await StreamChatResponseAsync(input, cancellationToken);

      history.Add(new Message { Role = "user", Content = input, SessionId = _activeSession.Id });
      if (!string.IsNullOrEmpty(assistantResponse))
      {
        history.Add(new Message { Role = "assistant", Content = assistantResponse, SessionId = _activeSession.Id });
      }
    }
  }

  private void RenderChatWithFilesPanel(List<Message> history)
  {
    var changes = _fileChangeTracker.Changes;
    var todos = _todoTracker.Items;
    var workingDir = _activeProject?.RootPath ?? "";

    if (changes.Count == 0 && todos.Count == 0)
    {
      RenderChatHistory(history);
      return;
    }

    var chatPanel = BuildChatHistoryPanel(history);
    var sidePanel = BuildSidePanel(changes, todos, workingDir);

    var grid = new Grid();
    grid.AddColumn(new GridColumn().NoWrap());
    grid.AddColumn(new GridColumn().Width(42));
    grid.AddRow(chatPanel, sidePanel);

    AnsiConsole.Write(grid);
  }

  private IRenderable BuildSidePanel(IReadOnlyList<FileChange> changes, IReadOnlyList<TodoItem> todos, string workingDir)
  {
    var panels = new List<IRenderable>();

    if (todos.Count > 0)
    {
      panels.Add(BuildTodosPanel(todos));
    }

    if (changes.Count > 0)
    {
      panels.Add(BuildFileChangesPanel(changes, workingDir));
    }

    return new Rows(panels);
  }

  private Panel BuildTodosPanel(IReadOnlyList<TodoItem> todos)
  {
    var table = new Table().Border(TableBorder.None).HideHeaders();
    table.AddColumn(new TableColumn("").NoWrap());

    foreach (var todo in todos)
    {
      var (icon, color) = todo.Status switch
      {
        "completed" => ("✓", "green"),
        "in_progress" => ("→", "yellow"),
        "cancelled" => ("✗", "dim"),
        _ => ("○", "white")
      };

      var content = todo.Content.Length > 35 ? todo.Content[..32] + "..." : todo.Content;
      table.AddRow(new Markup($"[{color}]{icon}[/] {Markup.Escape(content)}"));
    }

    var pending = todos.Count(t => t.Status == "pending");
    var inProgress = todos.Count(t => t.Status == "in_progress");
    var completed = todos.Count(t => t.Status == "completed");

    table.AddRow(new Rule().RuleStyle(Theme.MutedStyle));
    table.AddRow(new Markup($"[dim]{pending} pending, {inProgress} active, {completed} done[/]"));

    return new Panel(table)
        .Header($"[{Theme.Primary.ToMarkup()}]Todos ({todos.Count})[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Theme.Primary);
  }

  private Panel BuildChatHistoryPanel(List<Message> history)
  {
    var table = new Table().Border(TableBorder.None).HideHeaders().Expand();
    table.AddColumn(new TableColumn("").NoWrap());

    if (history.Count == 0)
    {
      table.AddRow(new Markup($"[{Theme.Muted.ToMarkup()}]No messages yet. Start the conversation![/]"));
    }
    else
    {
      foreach (var message in history.TakeLast(8))
      {
        var icon = message.Role switch
        {
          "user" => Icons.User,
          "assistant" => Icons.Assistant,
          "system" => Icons.System,
          _ => "•"
        };

        var color = message.Role switch
        {
          "user" => Theme.Accent,
          "assistant" => Theme.Success,
          "system" => Theme.Muted,
          _ => Theme.Primary
        };

        var truncated = message.Content.Length > 200
            ? message.Content[..200] + "..."
            : message.Content;

        table.AddRow(new Markup($"[{color.ToMarkup()}]{icon} {message.Role}[/]: {Markup.Escape(truncated)}"));
      }
    }

    return new Panel(table)
        .Header($"[{Theme.Primary.ToMarkup()}]Chat[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Theme.Secondary)
        .Expand();
  }

  private Panel BuildFileChangesPanel(IReadOnlyList<FileChange> changes, string workingDir)
  {
    var table = new Table().Border(TableBorder.None).HideHeaders();
    table.AddColumn(new TableColumn("").NoWrap());

    foreach (var change in changes)
    {
      var relativePath = change.RelativePath(workingDir);

      if (change.IsNew)
      {
        table.AddRow(new Markup($"[green]+ {Markup.Escape(relativePath)}[/] [dim]({change.NewLineCount})[/]"));
      }
      else
      {
        var addedStr = change.LinesAdded > 0 ? $"[green]+{change.LinesAdded}[/]" : "";
        var deletedStr = change.LinesDeleted > 0 ? $"[red]-{change.LinesDeleted}[/]" : "";
        var separator = change.LinesAdded > 0 && change.LinesDeleted > 0 ? " " : "";

        table.AddRow(new Markup($"[yellow]~ {Markup.Escape(relativePath)}[/] {addedStr}{separator}{deletedStr}"));
      }
    }

    var totalAdded = changes.Sum(c => c.LinesAdded);
    var totalDeleted = changes.Sum(c => c.LinesDeleted);
    var newFiles = changes.Count(c => c.IsNew);
    var editedFiles = changes.Count(c => !c.IsNew);

    table.AddRow(new Rule().RuleStyle(Theme.MutedStyle));
    table.AddRow(new Markup($"[dim]{newFiles} new, {editedFiles} edited[/]"));
    table.AddRow(new Markup($"[green]+{totalAdded}[/] [red]-{totalDeleted}[/] lines"));

    return new Panel(table)
        .Header($"[{Theme.Accent.ToMarkup()}]Files ({changes.Count})[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Theme.Accent);
  }

  private void RenderUserMessage(string content)
  {
    var panel = new Panel(new Markup(Markup.Escape(content)))
        .Header($"[{Theme.Accent.ToMarkup()}]{Icons.User} You[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Theme.Accent);
    AnsiConsole.Write(panel);
  }

  private async Task<string> StreamChatResponseAsync(string input, CancellationToken cancellationToken)
  {
    var responsePanel = new Panel(new Markup($"[{Theme.Muted.ToMarkup()}]...[/]"))
        .Header($"[{Theme.Primary.ToMarkup()}]{Icons.Assistant} Assistant[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Theme.Secondary);

    var buffers = new Dictionary<string, string>();
    var fullResponse = "";
    var toolOutputs = new List<IRenderable>();
    var pendingReads = new List<string>();

    // Create a linked cancellation token that can be cancelled by Escape key
    using var escapeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var escapeToken = escapeCts.Token;

    void FlushPendingReads()
    {
      if (pendingReads.Count > 0)
      {
        var rendered = ToolOutputRenderer.RenderGroupedReads(pendingReads);
        toolOutputs.Add(rendered);
        pendingReads.Clear();
      }
    }

    try
    {
      // Start keyboard monitoring task
      var keyboardMonitorTask = Task.Run(() =>
      {
        while (!escapeToken.IsCancellationRequested)
        {
          if (Console.KeyAvailable)
          {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape)
            {
              escapeCts.Cancel();
              break;
            }
          }
          Thread.Sleep(50);
        }
      }, escapeToken);

      await AnsiConsole.Live(responsePanel).StartAsync(async ctx =>
      {
        await _chat.RunAsync(_activeSession!, input, escapeToken,
          update =>
          {
            if (update.IsDone)
            {
              FlushPendingReads();

              var finalContent = BuildResponseContent(buffers, toolOutputs);
              responsePanel = new Panel(new Padder(finalContent, new Padding(1, 0)))
                        .Header($"[{Theme.Primary.ToMarkup()}]{Icons.Assistant} Assistant[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Theme.Success);

              ctx.UpdateTarget(responsePanel);
              return Task.CompletedTask;
            }

            if (!buffers.ContainsKey(update.AgentName))
              buffers[update.AgentName] = "";

            buffers[update.AgentName] += update.Delta;
            fullResponse = string.Join("\n\n", buffers.Values);

            var content = BuildResponseContent(buffers, toolOutputs);

            responsePanel = new Panel(new Padder(content, new Padding(1, 0)))
                      .Header($"[{Theme.Primary.ToMarkup()}]{Icons.Assistant} Assistant[/]")
                      .Border(BoxBorder.Rounded)
                      .BorderColor(Theme.Success);

            ctx.UpdateTarget(responsePanel);
            return Task.CompletedTask;
          },
          _fileChangeTracker,
          _todoTracker,
          AskUserQuestionsAsync,
          GetDiagnosticsAsync,
          toolExecution =>
          {
            if (toolExecution.ToolName == "read" && toolExecution.Success)
            {
              // Extract file path from output
              var match = System.Text.RegularExpressions.Regex.Match(toolExecution.Output, @"<file path=""(.+?)"">");
              if (match.Success)
              {
                pendingReads.Add(match.Groups[1].Value);
              }
            }
            else
            {
              // Flush any pending reads before rendering the non-read tool
              FlushPendingReads();

              var rendered = ToolOutputRenderer.Render(toolExecution.ToolName, toolExecution.Output, toolExecution.Success);
              toolOutputs.Add(rendered);
            }

            var content = BuildResponseContent(buffers, toolOutputs);

            responsePanel = new Panel(new Padder(content, new Padding(1, 0)))
                      .Header($"[{Theme.Primary.ToMarkup()}]{Icons.Assistant} Assistant[/]")
                      .Border(BoxBorder.Rounded)
                      .BorderColor(Theme.Success);

            ctx.UpdateTarget(responsePanel);
            return Task.CompletedTask;
          });
      });

      AnsiConsole.WriteLine();
      return fullResponse;
    }
    catch (OperationCanceledException)
    {
      // Agent was stopped by user (Escape key)
      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Panel(new Markup($"[{Theme.Warning.ToMarkup()}]Agent stopped by user[/]"))
          .Header($"[{Theme.Warning.ToMarkup()}]{Icons.Assistant} Assistant[/]")
          .Border(BoxBorder.Rounded)
          .BorderColor(Theme.Warning));
      AnsiConsole.WriteLine();
      return "";
    }
    catch (Exception ex)
    {
      AnsiConsole.Write(Panels.Error(ex.Message));
      return "";
    }
  }

  private IRenderable BuildResponseContent(Dictionary<string, string> buffers, List<IRenderable> toolOutputs)
  {
    var items = new List<IRenderable>();

    if (toolOutputs.Count > 0)
    {
      items.AddRange(toolOutputs);
    }

    if (buffers.Count > 0)
    {
      var textContent = buffers.Count == 1
                ? Markup.Escape(buffers.Values.First())
                : string.Join("\n\n", buffers.Select(kvp =>
                    $"[{Theme.Accent.ToMarkup()}]{Markup.Escape(kvp.Key)}[/]\n{Markup.Escape(kvp.Value)}"));

      if (!string.IsNullOrWhiteSpace(textContent))
      {
        items.Add(new Markup(textContent));
      }
    }

    if (items.Count == 0)
    {
      return new Markup($"[{Theme.Muted.ToMarkup()}]...[/]");
    }

    if (items.Count == 1)
    {
      return items[0];
    }

    return new Rows(items);
  }

  private void RenderChatContext()
  {
    var agentName = _activeSession?.ActiveAgentId.HasValue == true ? $"Agent #{_activeSession.ActiveAgentId}" : "None";
    var pipelineName = _activeSession?.ActivePipelineId.HasValue == true ? $"Pipeline #{_activeSession.ActivePipelineId}" : "None";

    var grid = new Grid()
        .AddColumn()
        .AddColumn()
        .AddColumn()
        .AddRow(
            $"[{Theme.Muted.ToMarkup()}]Project:[/] [{Theme.Primary.ToMarkup()}]{Markup.Escape(_activeProject?.Name ?? "None")}[/]",
            $"[{Theme.Muted.ToMarkup()}]Session:[/] [{Theme.Primary.ToMarkup()}]{Markup.Escape(_activeSession?.Name ?? "None")}[/]",
            _activeSession?.ActiveAgentId.HasValue == true
                ? $"[{Theme.Muted.ToMarkup()}]Agent:[/] [{Theme.Success.ToMarkup()}]{agentName}[/]"
                : $"[{Theme.Muted.ToMarkup()}]Pipeline:[/] [{Theme.Success.ToMarkup()}]{pipelineName}[/]"
        );

    AnsiConsole.Write(grid);
    AnsiConsole.WriteLine();
  }

  private void RenderChatHistory(List<Message> history)
  {
    if (history.Count == 0)
    {
      AnsiConsole.Write(Panels.Info("No messages yet. Start the conversation!"));
      return;
    }

    foreach (var message in history.TakeLast(10))
    {
      var icon = message.Role switch
      {
        "user" => Icons.User,
        "assistant" => Icons.Assistant,
        "system" => Icons.System,
        _ => "•"
      };

      var color = message.Role switch
      {
        "user" => Theme.Accent,
        "assistant" => Theme.Success,
        "system" => Theme.Muted,
        _ => Theme.Primary
      };

      var content = new Markup(Markup.Escape(message.Content));

      var panelContent = message.Role == "assistant"
          ? (IRenderable)new Padder(content, new Padding(1, 0))
          : content;

      var panel = new Panel(panelContent)
          .Header($"[{color.ToMarkup()}]{icon} {message.Role}[/]")
          .Border(BoxBorder.Rounded)
          .BorderColor(color)
          .Expand();

      AnsiConsole.Write(panel);
    }
  }

  private async Task<bool> EnsureChatTargetSelectedAsync()
  {
    if (_activeSession == null) return false;
    if (_activeSession.ActiveAgentId.HasValue || _activeSession.ActivePipelineId.HasValue) return true;

    var choice = AnsiConsole.Prompt(
        Prompts.Selection<string>("Choose chat target")
            .AddChoices($"{Icons.Agent} Agent", $"{Icons.Pipeline} Pipeline", $"{Icons.Back} Cancel"));

    if (choice.Contains("Cancel")) return false;

    if (choice.Contains("Agent"))
    {
      var agents = await StatusSpinner.RunAsync("Loading agents...", _agents.ListAsync);
      if (agents.Count == 0)
      {
        AnsiConsole.Write(Panels.Error("No agents available. Create one first."));
        return false;
      }

      var agent = AnsiConsole.Prompt(
          Prompts.Selection<AgentProfile>("Select agent")
              .UseConverter(a => $"{Icons.Agent} {a.Name} [{Theme.Muted.ToMarkup()}]{a.Model}[/]")
              .AddChoices(agents));

      _activeSession.ActiveAgentId = agent.Id;
      _activeSession.ActivePipelineId = null;
      _activeSession = await StatusSpinner.RunAsync("Saving...", () => _sessions.UpsertAsync(_activeSession));
      return true;
    }

    var pipelines = await StatusSpinner.RunAsync("Loading pipelines...", _pipelines.ListAsync);
    if (pipelines.Count == 0)
    {
      AnsiConsole.Write(Panels.Error("No pipelines available. Create one first."));
      return false;
    }

    var pipeline = AnsiConsole.Prompt(
        Prompts.Selection<Pipeline>("Select pipeline")
            .UseConverter(p => $"{Icons.Pipeline} {p.Name}")
            .AddChoices(pipelines));

    _activeSession.ActivePipelineId = pipeline.Id;
    _activeSession.ActiveAgentId = null;
    _activeSession = await StatusSpinner.RunAsync("Saving...", () => _sessions.UpsertAsync(_activeSession));
    return true;
  }

  private Task<List<QuestionAnswer>> AskUserQuestionsAsync(QuestionRequest request)
  {
    var answers = new List<QuestionAnswer>();

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[{Theme.Warning.ToMarkup()}]Agent has questions for you[/]").LeftJustified().RuleStyle(Theme.WarningStyle));
    AnsiConsole.WriteLine();

    foreach (var question in request.Questions)
    {
      var answer = new QuestionAnswer { QuestionText = question.Text };

      if (question.Options.Count == 0)
      {
        var response = AnsiConsole.Prompt(
            new TextPrompt<string>($"[{Theme.Primary.ToMarkup()}]{Markup.Escape(question.Text)}[/]")
                .PromptStyle(Theme.AccentStyle)
                .AllowEmpty());

        if (!string.IsNullOrWhiteSpace(response))
          answer.Answers.Add(response);
      }
      else if (question.AllowMultiple)
      {
        var selections = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title($"[{Theme.Primary.ToMarkup()}]{Markup.Escape(question.Text)}[/]")
                .PageSize(10)
                .InstructionsText($"[{Theme.Muted.ToMarkup()}](Space to toggle, Enter to confirm)[/]")
                .AddChoices(question.Options));

        answer.Answers.AddRange(selections);
      }
      else
      {
        var options = question.Options.ToList();
        if (question.AllowCustom)
          options.Add("[Type custom answer]");

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[{Theme.Primary.ToMarkup()}]{Markup.Escape(question.Text)}[/]")
                .PageSize(10)
                .AddChoices(options));

        if (selection == "[Type custom answer]")
        {
          var custom = AnsiConsole.Prompt(
              new TextPrompt<string>($"[{Theme.Muted.ToMarkup()}]Your answer:[/]")
                  .PromptStyle(Theme.AccentStyle));
          answer.Answers.Add(custom);
        }
        else
        {
          answer.Answers.Add(selection);
        }
      }

      answers.Add(answer);
    }

    AnsiConsole.Write(new Rule().RuleStyle(Theme.MutedStyle));
    AnsiConsole.WriteLine();

    return Task.FromResult(answers);
  }

  private Task<List<Diagnostic>> GetDiagnosticsAsync(string[] files)
  {
    // Basic implementation using dotnet build for .NET projects
    // This can be extended with proper LSP integration
    var diagnostics = new List<Diagnostic>();

    try
    {
      var workingDir = _activeProject?.RootPath ?? Environment.CurrentDirectory;

      // Try to find a .csproj or .sln file
      var projectFile = Directory.GetFiles(workingDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
                     ?? Directory.GetFiles(workingDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();

      if (projectFile == null)
        return Task.FromResult(diagnostics);

      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "dotnet",
        Arguments = $"build \"{projectFile}\" --no-restore --verbosity minimal --nologo --consoleLoggerParameters:NoSummary",
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = System.Diagnostics.Process.Start(psi);
      if (process == null)
        return Task.FromResult(diagnostics);

      var output = process.StandardOutput.ReadToEnd();
      var errorOutput = process.StandardError.ReadToEnd();
      process.WaitForExit(30000);

      // Parse MSBuild output format: file(line,col): error/warning CODE: message
      var combinedOutput = output + "\n" + errorOutput;

      // Filter out all the verbose project loading lines
      var relevantLines = combinedOutput.Split('\n')
          .Where(line => !line.Contains("{") && !line.Contains("}") &&
                        !line.Contains("CPU,ActiveCfg") &&
                        !line.Contains("CPU.Build.0") &&
                        !line.Contains("more lines") &&
                        !string.IsNullOrWhiteSpace(line))
          .ToList();

      var filteredOutput = string.Join("\n", relevantLines);

      var pattern = new System.Text.RegularExpressions.Regex(
          @"^(.+?)\((\d+),(\d+)\):\s*(error|warning)\s+(\w+):\s*(.+)$",
          System.Text.RegularExpressions.RegexOptions.Multiline);

      foreach (System.Text.RegularExpressions.Match match in pattern.Matches(filteredOutput))
      {
        var filePath = match.Groups[1].Value.Trim();

        // Filter by requested files if specified
        if (files.Length > 0 && !files.Any(f =>
            filePath.Equals(f, StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase)))
          continue;

        diagnostics.Add(new Diagnostic
        {
          FilePath = filePath,
          Line = int.TryParse(match.Groups[2].Value, out var line) ? line : 0,
          Column = int.TryParse(match.Groups[3].Value, out var col) ? col : 0,
          Severity = match.Groups[4].Value == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
          Code = match.Groups[5].Value,
          Message = match.Groups[6].Value.Trim()
        });
      }
    }
    catch
    {
      // Silently fail if diagnostics can't be retrieved
    }

    return Task.FromResult(diagnostics);
  }

  private void CopyMessageToClipboard(List<Message> history)
  {
    if (history.Count == 0)
    {
      AnsiConsole.Write(Panels.Info("No messages to copy"));
      Pause();
      return;
    }

    var messageChoices = history.TakeLast(10).Select((msg, idx) =>
    {
      var preview = msg.Content.Length > 60 ? msg.Content[..60] + "..." : msg.Content;
      var icon = msg.Role == "user" ? Icons.User : Icons.Assistant;
      return $"{icon} [{idx}] {msg.Role}: {preview}";
    }).ToList();

    messageChoices.Add($"{Icons.Back} Cancel");

    var choice = AnsiConsole.Prompt(
        Prompts.Selection<string>("Select message to copy")
            .AddChoices(messageChoices));

    if (choice.Contains("Cancel")) return;

    var indexMatch = System.Text.RegularExpressions.Regex.Match(choice, @"\[(\d+)\]");
    if (indexMatch.Success)
    {
      var index = int.Parse(indexMatch.Groups[1].Value);
      var recentMessages = history.TakeLast(10).ToList();
      if (index < recentMessages.Count)
      {
        var message = recentMessages[index];
        if (CopyToSystemClipboard(message.Content))
        {
          AnsiConsole.Write(Panels.Success("Message copied to clipboard"));
        }
        else
        {
          AnsiConsole.Write(Panels.Error("Failed to copy to clipboard"));
        }
        Pause();
      }
    }
  }

  private bool CopyToSystemClipboard(string text)
  {
    try
    {
      var psi = new System.Diagnostics.ProcessStartInfo
      {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      if (OperatingSystem.IsMacOS())
      {
        psi.FileName = "pbcopy";
      }
      else if (OperatingSystem.IsLinux())
      {
        // Try xclip first, fall back to xsel
        if (System.IO.File.Exists("/usr/bin/xclip") || System.IO.File.Exists("/bin/xclip"))
        {
          psi.FileName = "xclip";
          psi.Arguments = "-selection clipboard";
        }
        else if (System.IO.File.Exists("/usr/bin/xsel") || System.IO.File.Exists("/bin/xsel"))
        {
          psi.FileName = "xsel";
          psi.Arguments = "--clipboard --input";
        }
        else
        {
          return false;
        }
      }
      else if (OperatingSystem.IsWindows())
      {
        psi.FileName = "clip";
      }
      else
      {
        return false;
      }

      using var process = System.Diagnostics.Process.Start(psi);
      if (process == null) return false;

      process.StandardInput.Write(text);
      process.StandardInput.Close();
      process.WaitForExit(5000);

      return process.ExitCode == 0;
    }
    catch
    {
      return false;
    }
  }

  private static string SanitizeInput(string input)
  {
    if (string.IsNullOrEmpty(input)) return input;

    var result = new System.Text.StringBuilder(input.Length);
    foreach (var c in input)
    {
      // Keep printable characters, spaces, tabs, and newlines
      // Remove control characters and terminal escape sequences
      if (c >= 32 && c != 127 || c == '\n' || c == '\r' || c == '\t')
      {
        result.Append(c);
      }
    }

    return result.ToString();
  }
}
