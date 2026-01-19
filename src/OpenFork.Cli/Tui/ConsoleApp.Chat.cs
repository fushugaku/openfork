using OpenFork.Core.Domain;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using Terminal.Gui;
using Microsoft.Extensions.Logging;

namespace OpenFork.Cli.Tui;

public delegate Task<List<QuestionAnswer>> AskUserDelegate(QuestionRequest request);
public delegate Task<List<Diagnostic>> GetDiagnosticsDelegate(string[] files);

public partial class ConsoleApp
{
  private FileChangeTracker _fileChangeTracker = new();
  private TodoTracker _todoTracker = new();
  private ToolHistoryTracker _toolHistory = new();
  private SubagentTracker _subagentTracker = new();

  // Cache of available agents for Tab switching
  private List<AgentProfile> _cachedAgents = new();

  // Reference to current chat MarkdownView for global mouse wheel handling
  private MarkdownView? _activeChatView;

  /// <summary>Tracks recent tool executions for display in side panel</summary>
  private class ToolHistoryTracker
  {
    public List<(string Name, bool Success, DateTimeOffset Time)> Items { get; } = new();
    private const int MaxItems = 8;

    public void Add(string name, bool success)
    {
      Items.Insert(0, (name, success, DateTimeOffset.Now));
      if (Items.Count > MaxItems)
        Items.RemoveAt(Items.Count - 1);
    }

    public void Clear() => Items.Clear();
  }

  /// <summary>Tracks active subagent executions for display in side panel</summary>
  private class SubagentTracker
  {
    public List<SubagentInfo> Active { get; } = new();
    public List<SubagentInfo> Recent { get; } = new();
    private const int MaxRecent = 4;

    public void Add(Guid id, string agentSlug, string description)
    {
      Active.Add(new SubagentInfo
      {
        Id = id,
        AgentSlug = agentSlug,
        Description = description,
        Status = SubSessionStatus.Running,
        StartedAt = DateTimeOffset.Now
      });
    }

    public void UpdateStatus(Guid id, SubSessionStatus status)
    {
      var item = Active.FirstOrDefault(x => x.Id == id);
      if (item != null)
      {
        item.Status = status;
      }
    }

    public void Complete(Guid id, bool success, TimeSpan duration, string? error = null)
    {
      var item = Active.FirstOrDefault(x => x.Id == id);
      if (item != null)
      {
        item.Status = success ? SubSessionStatus.Completed : SubSessionStatus.Failed;
        item.Duration = duration;
        item.Error = error;
        item.CompletedAt = DateTimeOffset.Now;

        Active.Remove(item);
        Recent.Insert(0, item);
        if (Recent.Count > MaxRecent)
          Recent.RemoveAt(Recent.Count - 1);
      }
    }

    public void Remove(Guid id)
    {
      var item = Active.FirstOrDefault(x => x.Id == id);
      if (item != null)
      {
        Active.Remove(item);
      }
    }

    public void Clear()
    {
      Active.Clear();
      Recent.Clear();
    }
  }

  /// <summary>Information about a subagent execution</summary>
  private class SubagentInfo
  {
    public Guid Id { get; set; }
    public string AgentSlug { get; set; } = "";
    public string Description { get; set; } = "";
    public SubSessionStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? Error { get; set; }

    public string GetIcon() => AgentSlug.ToLowerInvariant() switch
    {
      "explore" or "explorer" => "🔭",
      "researcher" or "research" => "📚",
      "planner" or "planner-sub" => "📝",
      "general" => "🤖",
      "coder" => "💻",
      "tester" => "🧪",
      "reviewer" => "👀",
      _ => "🤖"
    };

    public string GetStatusIcon() => Status switch
    {
      SubSessionStatus.Pending => "⏳",
      SubSessionStatus.Queued => "📋",
      SubSessionStatus.Running => "🔄",
      SubSessionStatus.Completed => "✅",
      SubSessionStatus.Failed => "❌",
      SubSessionStatus.Cancelled => "🚫",
      _ => "•"
    };
  }

  private View CreateChatView(List<Message>? preloadedHistory = null)
  {
    var container = new View()
    {
      X = 0,
      Y = 0,
      Width = Dim.Fill(),
      Height = Dim.Fill(),
      ColorScheme = Theme.Schemes.Base
    };

    // Load agents and auto-select first if none selected
    _cachedAgents = _agents.ListAsync().GetAwaiter().GetResult();
    EnsureAgentSelected();

    // Use preloaded history or empty list
    var history = preloadedHistory ?? new List<Message>();

    // MarkdownView for chat history (supports colored markdown rendering like Glow)
    var chatContent = new MarkdownView()
    {
      X = 0,
      Y = 0,
      Width = Dim.Fill(),
      Height = Dim.Fill() - 2,  // Leave room for input + status
      CanFocus = true
    };

    // Store reference for global mouse wheel handling
    _activeChatView = chatContent;

    // Track if user has manually scrolled (to prevent auto-scroll override)
    var userScrolled = false;
    var lastContentLength = 0;
    var lastKnownWidth = 80; // Default until layout is known
    var needsInitialRebuild = true; // Rebuild once when actual width is known

    // Build initial history text with default width (will be rebuilt on LayoutComplete)
    var historyText = BuildHistoryText(history, lastKnownWidth);
    chatContent.Text = historyText;

    // Scroll to bottom of MarkdownView
    void ScrollToBottom()
    {
      chatContent.ScrollToEnd();
    }

    // Update and scroll if new content added and user hasn't scrolled
    void UpdateContentAndScroll()
    {
      var text = chatContent.Text ?? "";
      var contentChanged = text.Length != lastContentLength;
      lastContentLength = text.Length;

      if (contentChanged && !userScrolled)
      {
        ScrollToBottom();
      }
    }

    // Reset user scroll flag when new content arrives (auto-scroll resumes)
    void ScrollToBottomOnNewContent()
    {
      userScrolled = false;
      ScrollToBottom();
    }

    chatContent.LayoutComplete += (e) =>
    {
      var newWidth = chatContent.Frame.Width - 2;  // Full width with minimal margin
      if (newWidth > 40 && newWidth != lastKnownWidth)
      {
        lastKnownWidth = newWidth;
        // Rebuild content with correct width on first layout (initial load)
        if (needsInitialRebuild)
        {
          needsInitialRebuild = false;
          chatContent.Text = BuildHistoryText(history, newWidth);
        }
      }
      UpdateContentAndScroll();
    };

    // Input area (second to last line)
    var inputFrame = new View()
    {
      X = 0,
      Y = Pos.AnchorEnd(2),
      Width = Dim.Fill(),
      Height = 1,
      ColorScheme = Theme.Schemes.Base,
      CanFocus = false
    };

    var inputPrompt = new Label("› ")
    {
      X = 0,
      Y = 0,
      ColorScheme = Theme.Schemes.Base
    };

    var inputField = new TextField("")
    {
      X = Pos.Right(inputPrompt),
      Y = 0,
      Width = Dim.Fill() - 22,
      ColorScheme = Theme.Schemes.Input,
      CanFocus = true
    };

    var sendButton = new Button("📤 _Send")
    {
      X = Pos.Right(inputField) + 1,
      Y = 0,
      ColorScheme = Theme.Schemes.Button,
      CanFocus = false  // Keep focus on input
    };

    var backButton = new Button("⬅️ _Back")
    {
      X = Pos.Right(sendButton) + 1,
      Y = 0,
      ColorScheme = Theme.Schemes.Button,
      CanFocus = false  // Keep focus on input
    };

    // Status bar at very bottom
    var statusLine = new Label("")
    {
      X = 0,
      Y = Pos.AnchorEnd(1),
      Width = Dim.Fill(),
      Height = 1,
      ColorScheme = Theme.Schemes.Muted
    };
    UpdateStatusLine(statusLine);

    // Check if message is a slash command
    bool IsSlashCommand(string message) => message.TrimStart().StartsWith('/');

    // Handle slash command execution
    void HandleSlashCommand(string command)
    {
      _logger.LogInformation("Handling slash command: {Command}", command);

      var trimmedCommand = command.TrimStart().TrimStart('/').Trim();
      var parts = trimmedCommand.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
      var toolName = parts.Length > 0 ? parts[0] : "";

      // Get pipeline tools
      var pipelineTools = _toolRegistry.GetPipelineTools().ToList();

      // If just "/" or no tool name, show autocomplete
      if (string.IsNullOrWhiteSpace(toolName))
      {
        ShowToolAutocomplete(inputField);
        return;
      }

      // Find matching tool
      var matchingTools = pipelineTools
          .Where(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
          .ToList();

      if (matchingTools.Count == 0)
      {
        // Try prefix match
        var prefixMatches = pipelineTools
            .Where(t => t.Name.StartsWith(toolName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (prefixMatches.Count == 0)
        {
          FrameHelpers.ShowError($"Unknown pipeline tool: /{toolName}\n\nType '/' and press Tab to see available tools.");
          return;
        }
        else if (prefixMatches.Count == 1)
        {
          // Single prefix match - use it
          matchingTools = prefixMatches;
        }
        else
        {
          // Multiple matches - show autocomplete
          var selectedName = DialogHelpers.PromptToolAutocomplete(
              prefixMatches.Select(t => (t.Name, t.Description)));

          if (selectedName == null)
            return;

          matchingTools = pipelineTools.Where(t => t.Name == selectedName).ToList();
        }
      }

      var tool = matchingTools[0];

      // Get parameter information
      var (requiredParams, paramDescriptions) = tool.GetParameterInfo();

      // If no parameters, execute directly
      if (paramDescriptions.Count == 0)
      {
        ExecuteSlashCommand(tool, new Dictionary<string, string>(), chatContent, statusLine, ScrollToBottomOnNewContent);
        inputField.Text = "";
        return;
      }

      // Show interactive parameter dialog
      var parameters = DialogHelpers.PromptPipelineToolParameters(
          tool.Name,
          tool.Description,
          requiredParams,
          paramDescriptions);

      if (parameters == null)
      {
        _logger.LogInformation("User cancelled parameter input");
        return;
      }

      // Execute the tool
      inputField.Text = "";
      ExecuteSlashCommand(tool, parameters, chatContent, statusLine, ScrollToBottomOnNewContent);
    }

    // Execute slash command (pipeline tool) with provided parameters
    void ExecuteSlashCommand(
        PipelineTool tool,
        Dictionary<string, string> parameters,
        MarkdownView chatContent,
        Label statusLine,
        Action onScrollUpdate)
    {
      _logger.LogInformation("Executing slash command: /{ToolName} with {ParamCount} parameters",
          tool.Name, parameters.Count);

      // Build display text
      var paramDisplay = parameters.Count > 0
          ? string.Join(", ", parameters.Select(p => $"{p.Key}=\"{p.Value}\""))
          : "(no parameters)";

      // Append user command message - use responsive width from frame (full width minus small margin)
      var frameWidth = chatContent.Frame.Width > 0 ? chatContent.Frame.Width : 120;
      var boxWidth = Math.Max(40, frameWidth - 2);
      var currentText = chatContent.Text ?? "";
      var userMessage = FormatMessageBox(Icons.User, "You", $"/{tool.Name} {paramDisplay}", boxWidth);

      // Create assistant header
      var assistantHeader = $" {Icons.Tool} Pipeline: {tool.Name} ";
      var headerPadding = Math.Max(1, boxWidth - assistantHeader.Length);
      var boxHeader = $"─{assistantHeader}{new string('─', headerPadding)}";
      var boxFooter = new string('─', boxWidth);

      chatContent.Text = currentText + "\n" + userMessage + "\n\n" + boxHeader + "\n  Running pipeline...\n";
      onScrollUpdate();

      var baseText = currentText + "\n" + userMessage + "\n\n" + boxHeader + "\n  ";

      // Execute in background
      _ = Task.Run(async () =>
      {
        try
        {
          // Create JSON arguments
          var jsonArgs = System.Text.Json.JsonSerializer.Serialize(parameters);

          // Create tool context
          var context = new ToolContext
          {
              SessionId = _activeSession!.Id,
              WorkingDirectory = _activeProject?.RootPath ?? Environment.CurrentDirectory
          };

          var result = await tool.ExecuteAsync(jsonArgs, context);

          // Track tool execution
          _toolHistory.Add(tool.Name, result.Success);

          Application.MainLoop.Invoke(() =>
          {
            var status = result.Success ? "✓ Success" : "✗ Failed";
            var output = result.Output.Replace("\n", "\n  ");
            chatContent.Text = baseText + $"{status}\n  {output}\n" + boxFooter + "\n";
            onScrollUpdate();
            UpdateStatusLine(statusLine);
          });
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Slash command execution failed");
          _toolHistory.Add(tool.Name, false);

          Application.MainLoop.Invoke(() =>
          {
            chatContent.Text = baseText + $"✗ Error: {ex.Message}\n" + boxFooter + "\n";
            onScrollUpdate();
            UpdateStatusLine(statusLine);
          });
        }
        finally
        {
          Application.MainLoop.Invoke(() => inputField.SetFocus());
        }
      });
    }

    // Show tool autocomplete
    void ShowToolAutocomplete(TextField inputField)
    {
      var pipelineTools = _toolRegistry.GetPipelineTools().ToList();

      if (pipelineTools.Count == 0)
      {
        FrameHelpers.ShowInfo("No pipeline tools available.\n\nPipeline tools are loaded from *.tool.json files in the tools directory.");
        return;
      }

      var selectedName = DialogHelpers.PromptToolAutocomplete(
          pipelineTools.Select(t => (t.Name, t.Description)));

      if (selectedName != null)
      {
        inputField.Text = $"/{selectedName}";
        inputField.CursorPosition = inputField.Text.Length;
      }
    }

    // Send action - shared between button and Enter key
    void DoSend()
    {
      _logger.LogInformation("DoSend called");

      var message = inputField.Text?.ToString() ?? "";
      _logger.LogInformation("Message text: '{Message}' (length={Length})", message, message.Length);

      if (string.IsNullOrWhiteSpace(message))
      {
        _logger.LogWarning("Message is empty or whitespace, returning");
        return;
      }

      // Check if this is a slash command
      if (IsSlashCommand(message))
      {
        HandleSlashCommand(message);
        return;
      }

      // Check if agent or pipeline is configured
      if (_activeSession?.ActiveAgentId == null && _activeSession?.ActivePipelineId == null)
      {
        _logger.LogWarning("No agent or pipeline configured for session");
        FrameHelpers.ShowError("No agent configured. Please go to Agents (F4) and set an active agent for this session.");
        return;
      }

      _logger.LogInformation("Clearing input field");
      inputField.Text = "";

      _logger.LogInformation("Starting background task for SendMessageAsync");

      // Run async operation on background thread, update UI via MainLoop
      _ = Task.Run(async () =>
      {
        _logger.LogInformation("Background task started, calling SendMessageAsync");
        try
        {
          await SendMessageAsync(message, chatContent, statusLine, ScrollToBottomOnNewContent);
          _logger.LogInformation("SendMessageAsync completed successfully");
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "SendMessageAsync failed with error: {Error}", ex.Message);
          Application.MainLoop.Invoke(() =>
          {
            FrameHelpers.ShowError($"Error: {ex.Message}");
          });
        }
        finally
        {
          _logger.LogInformation("Refocusing input field");
          Application.MainLoop.Invoke(() => inputField.SetFocus());
        }
      });

      _logger.LogInformation("DoSend completed (background task running)");
    }

    sendButton.Clicked += () => DoSend();
    backButton.Clicked += () => ShowMainMenu();

    // Handle Enter key in input field and shortcuts
    inputField.KeyPress += (e) =>
    {
      _logger.LogDebug("KeyPress received: {Key}", e.KeyEvent.Key);

      if (e.KeyEvent.Key == Key.Enter || (e.KeyEvent.Key == (Key.CtrlMask | Key.s)))
      {
        _logger.LogInformation("Enter/Ctrl+S pressed, calling DoSend");
        DoSend();
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.Tab)
      {
        var currentText = inputField.Text?.ToString() ?? "";

        // If input starts with '/', show tool autocomplete
        if (currentText.TrimStart().StartsWith('/'))
        {
          var prefix = currentText.TrimStart().TrimStart('/');
          var pipelineTools = _toolRegistry.GetPipelineTools().ToList();
          var matches = pipelineTools
              .Where(t => string.IsNullOrEmpty(prefix) ||
                         t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
              .ToList();

          if (matches.Count == 1)
          {
            // Single match - complete it
            inputField.Text = $"/{matches[0].Name}";
            inputField.CursorPosition = inputField.Text.Length;
          }
          else if (matches.Count > 1)
          {
            // Multiple matches - show autocomplete dialog
            var selectedName = DialogHelpers.PromptToolAutocomplete(
                matches.Select(t => (t.Name, t.Description)));

            if (selectedName != null)
            {
              inputField.Text = $"/{selectedName}";
              inputField.CursorPosition = inputField.Text.Length;
            }
          }
          else
          {
            // No matches - show all tools
            ShowToolAutocomplete(inputField);
          }

          e.Handled = true;
        }
        else
        {
          // Tab - switch to next agent
          SwitchAgent(forward: true);
          UpdateStatusLine(statusLine);
          e.Handled = true;
        }
      }
      else if (e.KeyEvent.Key == Key.BackTab)
      {
        // Shift+Tab - switch to previous agent
        SwitchAgent(forward: false);
        UpdateStatusLine(statusLine);
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.Esc)
      {
        _logger.LogInformation("Esc pressed, going to main menu");
        ShowMainMenu();
        e.Handled = true;
      }
      // Scroll chat from input field using MarkdownView
      else if (e.KeyEvent.Key == Key.PageUp || e.KeyEvent.Key == (Key.CtrlMask | Key.u))
      {
        userScrolled = true;
        chatContent.TopRow -= 10;
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.PageDown || e.KeyEvent.Key == (Key.CtrlMask | Key.d))
      {
        userScrolled = true;
        chatContent.TopRow += 10;
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.Home || e.KeyEvent.Key == (Key.CtrlMask | Key.Home))
      {
        userScrolled = true;
        chatContent.TopRow = 0;
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.End || e.KeyEvent.Key == (Key.CtrlMask | Key.End))
      {
        ScrollToBottomOnNewContent();
        e.Handled = true;
      }
    };

    inputFrame.Add(inputPrompt, inputField, sendButton, backButton);
    container.Add(chatContent, inputFrame, statusLine);

    // Focus management
    container.Enter += (e) => inputField.SetFocus();
    inputField.SetFocus();

    return container;
  }

  private void EnsureAgentSelected()
  {
    if (_activeSession == null) return;

    // If no agent selected and we have agents, select the first one
    if (_activeSession.ActiveAgentId == null && _activeSession.ActivePipelineId == null && _cachedAgents.Count > 0)
    {
      var firstAgent = _cachedAgents[0];
      _activeSession.ActiveAgentId = firstAgent.Id;
      _ = _sessions.UpsertAsync(_activeSession);
      _logger.LogInformation("Auto-selected first agent: {AgentName} (ID={AgentId})", firstAgent.Name, firstAgent.Id);
    }
  }

  private void SwitchAgent(bool forward)
  {
    if (_activeSession == null || _cachedAgents.Count == 0) return;

    // Find current agent index
    var currentIndex = 0;
    if (_activeSession.ActiveAgentId != null)
    {
      currentIndex = _cachedAgents.FindIndex(a => a.Id == _activeSession.ActiveAgentId);
      if (currentIndex < 0) currentIndex = 0;
    }

    // Calculate new index
    int newIndex;
    if (forward)
    {
      newIndex = (currentIndex + 1) % _cachedAgents.Count;
    }
    else
    {
      newIndex = (currentIndex - 1 + _cachedAgents.Count) % _cachedAgents.Count;
    }

    // Switch to new agent
    var newAgent = _cachedAgents[newIndex];
    _activeSession.ActiveAgentId = newAgent.Id;
    _activeSession.ActivePipelineId = null; // Clear pipeline when switching agents
    _ = _sessions.UpsertAsync(_activeSession);

    _logger.LogInformation("Switched to agent: {AgentName} (ID={AgentId})", newAgent.Name, newAgent.Id);
    UpdateContextDisplay();
  }

  private string BuildHistoryText(List<Message> history, int boxWidth = 80)
  {
    // Ensure minimum width
    boxWidth = Math.Max(40, boxWidth);
    var separator = new string('─', boxWidth);

    if (history.Count == 0)
      return $@"
{separator}
  🎉 Welcome to OpenFork Chat!

  Type a message below and press Enter to start.

  ⚡ Pipeline Tools (slash commands):
    /              → List available pipeline tools
    /toolname      → Run a pipeline tool
    /tool + Tab    → Autocomplete tool name

  🖱️  Mouse: Scroll with mouse wheel or trackpad

  ⌨️  Keyboard shortcuts:
    Enter         → Send message
    Tab/Shift+Tab → Switch agent (or autocomplete tools)
    PgUp/PgDn     → Scroll chat (Ctrl+U/D also works)
    Home/End      → Jump to top/bottom
    Esc           → Back to main menu
{separator}
";

    var text = new System.Text.StringBuilder();
    foreach (var msg in history.TakeLast(20))
    {
      var (icon, roleLabel) = msg.Role switch
      {
        "user" => (Icons.User, "You"),
        "assistant" => (Icons.Assistant, "Assistant"),
        "system" => (Icons.System, "System"),
        _ => ("•", msg.Role)
      };

      // MarkdownView handles markdown rendering natively with colors
      text.AppendLine(FormatMessageBox(icon, roleLabel, msg.Content, boxWidth));
      text.AppendLine();
    }

    return text.ToString();
  }

  private static string FormatMessageBox(string icon, string roleLabel, string content, int boxWidth = 80)
  {
    // Ensure minimum width for readability
    boxWidth = Math.Max(40, boxWidth);
    var sb = new System.Text.StringBuilder();

    // Header with top border
    var header = $" {icon} {roleLabel} ";
    var headerPadding = Math.Max(1, boxWidth - header.Length);
    sb.AppendLine($"─{header}{new string('─', headerPadding)}");

    // Content lines (no side borders) with word-boundary wrapping
    var lines = content.Split('\n');
    foreach (var line in lines)
    {
      if (string.IsNullOrEmpty(line))
      {
        sb.AppendLine();
        continue;
      }

      // Word-wrap long lines (break at word boundaries)
      foreach (var wrappedLine in WrapTextAtWords(line, boxWidth))
      {
        sb.AppendLine($"  {wrappedLine}");
      }
    }

    // Footer
    sb.Append($"{new string('─', boxWidth)}");

    return sb.ToString();
  }

  /// <summary>
  /// Wraps text at word boundaries, similar to CSS word-break: break-word
  /// </summary>
  private static IEnumerable<string> WrapTextAtWords(string text, int maxWidth)
  {
    if (string.IsNullOrEmpty(text) || text.Length <= maxWidth)
    {
      yield return text;
      yield break;
    }

    var words = text.Split(' ');
    var currentLine = new System.Text.StringBuilder();

    foreach (var word in words)
    {
      // If the word itself is longer than maxWidth, break it
      if (word.Length > maxWidth)
      {
        // Flush current line first
        if (currentLine.Length > 0)
        {
          yield return currentLine.ToString().TrimEnd();
          currentLine.Clear();
        }

        // Break the long word into chunks
        var remaining = word;
        while (remaining.Length > maxWidth)
        {
          yield return remaining[..maxWidth];
          remaining = remaining[maxWidth..];
        }
        if (remaining.Length > 0)
        {
          currentLine.Append(remaining);
        }
      }
      else if (currentLine.Length + word.Length + 1 > maxWidth)
      {
        // Word doesn't fit on current line, start new line
        if (currentLine.Length > 0)
        {
          yield return currentLine.ToString().TrimEnd();
          currentLine.Clear();
        }
        currentLine.Append(word);
      }
      else
      {
        // Word fits, add it
        if (currentLine.Length > 0)
          currentLine.Append(' ');
        currentLine.Append(word);
      }
    }

    // Flush remaining
    if (currentLine.Length > 0)
    {
      yield return currentLine.ToString().TrimEnd();
    }
  }

  private void UpdateSidePanel(FrameView sidePanel)
  {
    sidePanel.RemoveAll();

    var y = 0;

    // Agent section (most important - at top)
    var agentInfo = GetCurrentAgentInfo();
    var agentLabel = ThemeHelper.CreateAccentLabel($"🤖 {agentInfo.name}");
    agentLabel.X = 0;
    agentLabel.Y = y++;
    sidePanel.Add(agentLabel);

    var modelLabel = new Label($"   {agentInfo.model}")
    {
      X = 0,
      Y = y++,
      ColorScheme = Theme.Schemes.Muted
    };
    sidePanel.Add(modelLabel);

    if (_cachedAgents.Count > 1)
    {
      var switchHint = new Label($"   Tab: {_cachedAgents.Count} agents")
      {
        X = 0,
        Y = y++,
        ColorScheme = Theme.Schemes.Muted
      };
      sidePanel.Add(switchHint);
    }

    // Separator
    AddSeparator(sidePanel, ref y);

    // Active subagents section (new - most important during execution)
    if (_subagentTracker.Active.Count > 0 || _subagentTracker.Recent.Count > 0)
    {
      var subagentHeader = ThemeHelper.CreateAccentLabel("🤖 Subagents:");
      subagentHeader.X = 0;
      subagentHeader.Y = y++;
      sidePanel.Add(subagentHeader);

      // Show active subagents with spinning indicator
      foreach (var sub in _subagentTracker.Active.Take(3))
      {
        var elapsed = DateTimeOffset.Now - sub.StartedAt;
        var statusText = sub.Status == SubSessionStatus.Queued ? "queued" : $"{elapsed.TotalSeconds:F0}s";
        var display = sub.Description.Length > 14 ? sub.Description[..14] + ".." : sub.Description;
        var subLabel = ThemeHelper.CreateLabel($" {sub.GetStatusIcon()} {sub.GetIcon()} {display} ({statusText})", Theme.WarningAttr);
        subLabel.X = 0;
        subLabel.Y = y++;
        sidePanel.Add(subLabel);
      }

      // Show recent completed subagents
      foreach (var sub in _subagentTracker.Recent.Take(2))
      {
        var durationText = sub.Duration.HasValue ? $"{sub.Duration.Value.TotalSeconds:F1}s" : "";
        var display = sub.Description.Length > 14 ? sub.Description[..14] + ".." : sub.Description;
        var color = sub.Status == SubSessionStatus.Completed ? Theme.SuccessAttr : Theme.ErrorAttr;
        var subLabel = ThemeHelper.CreateLabel($" {sub.GetStatusIcon()} {sub.GetIcon()} {display} {durationText}", color);
        subLabel.X = 0;
        subLabel.Y = y++;
        sidePanel.Add(subLabel);
      }

      // Show summary if more
      var totalActive = _subagentTracker.Active.Count;
      var totalRecent = _subagentTracker.Recent.Count;
      if (totalActive > 3)
      {
        var moreLabel = new Label($"   +{totalActive - 3} running...")
        {
          X = 0,
          Y = y++,
          ColorScheme = Theme.Schemes.Muted
        };
        sidePanel.Add(moreLabel);
      }

      // Separator
      AddSeparator(sidePanel, ref y);
    }

    // Tool history section
    var toolsHeader = ThemeHelper.CreateAccentLabel("⚡ Tools:");
    toolsHeader.X = 0;
    toolsHeader.Y = y++;
    sidePanel.Add(toolsHeader);

    if (_toolHistory.Items.Count > 0)
    {
      foreach (var tool in _toolHistory.Items.Take(5))
      {
        var icon = tool.Success ? "✓" : "✗";
        var color = tool.Success ? Theme.SuccessAttr : Theme.ErrorAttr;
        var name = tool.Name.Length > 18 ? tool.Name[..18] + ".." : tool.Name;
        var toolLabel = ThemeHelper.CreateLabel($" {icon} {name}", color);
        toolLabel.X = 0;
        toolLabel.Y = y++;
        sidePanel.Add(toolLabel);
      }
    }
    else
    {
      var noToolsLabel = new Label("   waiting...")
      {
        X = 0,
        Y = y++,
        ColorScheme = Theme.Schemes.Muted
      };
      sidePanel.Add(noToolsLabel);
    }

    // Separator
    AddSeparator(sidePanel, ref y);

    // Tasks section (compact)
    var todosHeader = ThemeHelper.CreateAccentLabel("📋 Tasks:");
    todosHeader.X = 0;
    todosHeader.Y = y++;
    sidePanel.Add(todosHeader);

    if (_todoTracker.Items.Count > 0)
    {
      var inProgress = _todoTracker.Items.Where(t => t.Status == "in_progress").Take(2);
      var pending = _todoTracker.Items.Where(t => t.Status == "pending").Take(2);

      foreach (var todo in inProgress.Concat(pending).Take(4))
      {
        var icon = todo.Status == "in_progress" ? "🔄" : "⏳";
        var color = todo.Status == "in_progress" ? Theme.WarningAttr : Theme.Muted;
        var content = todo.Content.Length > 18 ? todo.Content[..18] + ".." : todo.Content;
        var todoLabel = ThemeHelper.CreateLabel($" {icon} {content}", color);
        todoLabel.X = 0;
        todoLabel.Y = y++;
        sidePanel.Add(todoLabel);
      }

      var completed = _todoTracker.Items.Count(t => t.Status == "completed");
      var total = _todoTracker.Items.Count;
      if (completed > 0)
      {
        var statsLabel = new Label($"   ✅ {completed}/{total} done")
        {
          X = 0,
          Y = y++,
          ColorScheme = Theme.Schemes.Muted
        };
        sidePanel.Add(statsLabel);
      }
    }
    else
    {
      var noTodosLabel = new Label("   no tasks")
      {
        X = 0,
        Y = y++,
        ColorScheme = Theme.Schemes.Muted
      };
      sidePanel.Add(noTodosLabel);
    }

    // Separator
    AddSeparator(sidePanel, ref y);

    // Files section (compact)
    var filesHeader = ThemeHelper.CreateAccentLabel("📁 Files:");
    filesHeader.X = 0;
    filesHeader.Y = y++;
    sidePanel.Add(filesHeader);

    if (_fileChangeTracker.Changes.Count > 0)
    {
      foreach (var change in _fileChangeTracker.Changes.Take(4))
      {
        var icon = change.IsNew ? "+" : "~";
        var color = change.IsNew ? Theme.SuccessAttr : Theme.WarningAttr;
        var path = System.IO.Path.GetFileName(change.FilePath);
        if (path.Length > 20) path = path[..20] + "..";
        var fileLabel = ThemeHelper.CreateLabel($" {icon} {path}", color);
        fileLabel.X = 0;
        fileLabel.Y = y++;
        sidePanel.Add(fileLabel);
      }

      if (_fileChangeTracker.Changes.Count > 4)
      {
        var moreLabel = new Label($"   +{_fileChangeTracker.Changes.Count - 4} more")
        {
          X = 0,
          Y = y++,
          ColorScheme = Theme.Schemes.Muted
        };
        sidePanel.Add(moreLabel);
      }
    }
    else
    {
      var noFilesLabel = new Label("   no changes")
      {
        X = 0,
        Y = y++,
        ColorScheme = Theme.Schemes.Muted
      };
      sidePanel.Add(noFilesLabel);
    }

    // Project at bottom (context info)
    AddSeparator(sidePanel, ref y);
    var projectName = _activeProject?.Name ?? "None";
    var shortName = projectName.Length > 20 ? projectName[..20] + ".." : projectName;
    var projectLabel = new Label($"📦 {shortName}")
    {
      X = 0,
      Y = y,
      ColorScheme = Theme.Schemes.Muted
    };
    sidePanel.Add(projectLabel);
  }

  private static void AddSeparator(FrameView panel, ref int y)
  {
    var sep = new Label("─────────────────────")
    {
      X = 0,
      Y = y++,
      ColorScheme = Theme.Schemes.Muted
    };
    panel.Add(sep);
  }

  private (string name, string model) GetCurrentAgentInfo()
  {
    if (_activeSession?.ActiveAgentId != null)
    {
      var agent = _agents.GetAsync(_activeSession.ActiveAgentId.Value).GetAwaiter().GetResult();
      if (agent != null)
        return (agent.Name, agent.Model);
    }

    if (_activeSession?.ActivePipelineId != null)
    {
      return ("Pipeline", "Multi-agent");
    }

    return ("Default", _settings.DefaultModel ?? "N/A");
  }

  private void UpdateStatusLine(Label statusLine)
  {
    var agentInfo = GetCurrentAgentInfo();
    var parts = new List<string>();

    // Agent info (ASCII safe)
    parts.Add($"[{agentInfo.name}] {agentInfo.model}");

    // Active subagents (show prominently when active)
    if (_subagentTracker.Active.Count > 0)
    {
      parts.Add($"🤖 Subagents:{_subagentTracker.Active.Count}active");
    }

    // Tools status
    if (_toolHistory.Items.Count > 0)
    {
      var success = _toolHistory.Items.Count(t => t.Success);
      var total = _toolHistory.Items.Count;
      parts.Add($"Tools:{success}/{total}");
    }

    // Tasks status
    if (_todoTracker.Items.Count > 0)
    {
      var done = _todoTracker.Items.Count(t => t.Status == "completed");
      var total = _todoTracker.Items.Count;
      parts.Add($"Tasks:{done}/{total}");
    }

    // Files status
    if (_fileChangeTracker.Changes.Count > 0)
    {
      parts.Add($"Files:{_fileChangeTracker.Changes.Count}");
    }

    // Agent switch hint
    if (_cachedAgents.Count > 1)
    {
      parts.Add($"Tab:{_cachedAgents.Count}agents");
    }

    // Scroll hint
    parts.Add("PgUp/Dn:scroll");

    statusLine.Text = string.Join(" | ", parts);
  }

  private static string FormatToolExecution(OpenFork.Core.Services.ToolExecutionUpdate tool)
  {
    var sb = new System.Text.StringBuilder();
    var status = tool.Success ? "✓" : "✗";
    var inputSummary = GetInputSummary(tool.ToolName, tool.Input);
    var outputSummary = GetOutputSummary(tool.ToolName, tool.Output, tool.Success);

    sb.AppendLine();

    // Special formatting for task tool (subagent spawning)
    if (tool.ToolName.Equals("task", StringComparison.OrdinalIgnoreCase))
    {
      var isBackground = tool.Output.Contains("launched in background", StringComparison.OrdinalIgnoreCase);
      var icon = isBackground ? "🚀" : (tool.Success ? "🤖✓" : "🤖✗");

      sb.AppendLine($"  {icon} [SUBAGENT] {inputSummary}");

      if (!tool.Success)
      {
        sb.AppendLine($"      ❌ {TruncateText(tool.Output, 60)}");
      }
      else if (isBackground)
      {
        sb.Append($"      ⏳ Running in background...");
      }
      else
      {
        // Show brief result for completed subagent
        var resultPreview = GetSubagentResultPreview(tool.Output);
        if (!string.IsNullOrEmpty(resultPreview))
        {
          foreach (var line in resultPreview.Split('\n').Take(3))
          {
            sb.AppendLine($"      {TruncateText(line, 55)}");
          }
        }
      }

      return sb.ToString();
    }

    // Compact single-line format for simple tools
    if (string.IsNullOrEmpty(outputSummary) || outputSummary.Length <= 50)
    {
      var line = $"  ⚡ [{tool.ToolName}] {inputSummary}";
      if (!string.IsNullOrEmpty(outputSummary))
        line += $" → {outputSummary}";
      line += $" {status}";
      sb.Append(line);
    }
    else
    {
      // Multi-line format for tools with longer output
      sb.AppendLine($"  ⚡ [{tool.ToolName}] {inputSummary} {status}");
      foreach (var line in outputSummary.Split('\n').Take(4))
      {
        var trimmedLine = line.Length > 55 ? line[..55] + "..." : line;
        sb.AppendLine($"      {trimmedLine}");
      }
      if (outputSummary.Split('\n').Length > 4)
        sb.Append($"      ...(truncated)");
    }

    return sb.ToString();
  }

  private static string GetSubagentResultPreview(string output)
  {
    // Try to extract the Result section from subagent output
    var resultStart = output.IndexOf("**Result:**", StringComparison.OrdinalIgnoreCase);
    if (resultStart >= 0)
    {
      var resultContent = output[(resultStart + 11)..].Trim();
      // Take first few lines as preview
      var lines = resultContent.Split('\n').Take(3);
      return string.Join("\n", lines.Select(l => l.Trim()));
    }

    // Fallback: just take last non-empty portion
    var allLines = output.Split('\n')
        .Select(l => l.Trim())
        .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("##") && !l.StartsWith("**"))
        .TakeLast(2);
    return string.Join("\n", allLines);
  }

  private static string GetInputSummary(string toolName, string input)
  {
    if (string.IsNullOrWhiteSpace(input))
      return "(no args)";

    try
    {
      var json = System.Text.Json.JsonDocument.Parse(input);
      var root = json.RootElement;

      // Special handling for task tool
      if (toolName.Equals("task", StringComparison.OrdinalIgnoreCase))
      {
        return GetTaskToolInputSummary(root);
      }

      // Format all parameters as key=value pairs
      return FormatAllParameters(root);
    }
    catch
    {
      return TruncateText(input, 50);
    }
  }

  /// <summary>
  /// Formats all JSON parameters as key=value pairs for tool display
  /// </summary>
  private static string FormatAllParameters(System.Text.Json.JsonElement root)
  {
    if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
      return root.ToString() ?? "";

    var parts = new List<string>();
    foreach (var prop in root.EnumerateObject())
    {
      var value = prop.Value.ValueKind switch
      {
        System.Text.Json.JsonValueKind.String => TruncateText(prop.Value.GetString() ?? "", 40),
        System.Text.Json.JsonValueKind.Number => prop.Value.ToString(),
        System.Text.Json.JsonValueKind.True => "true",
        System.Text.Json.JsonValueKind.False => "false",
        System.Text.Json.JsonValueKind.Null => "null",
        System.Text.Json.JsonValueKind.Array => $"[{prop.Value.GetArrayLength()} items]",
        System.Text.Json.JsonValueKind.Object => "{...}",
        _ => prop.Value.ToString()
      };

      parts.Add($"{prop.Name}={value}");
    }

    return parts.Count > 0 ? string.Join(", ", parts) : "(no args)";
  }

  private static string GetTaskToolInputSummary(System.Text.Json.JsonElement root)
  {
    var agentType = root.TryGetProperty("subagent_type", out var st) ? st.GetString() : null;
    var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null;
    var runInBackground = root.TryGetProperty("run_in_background", out var bg) && bg.GetBoolean();

    var icon = (agentType?.ToLowerInvariant()) switch
    {
      "explore" or "explorer" => "🔭",
      "researcher" or "research" => "📚",
      "planner" or "planner-sub" => "📝",
      "general" => "🤖",
      "coder" => "💻",
      "tester" => "🧪",
      "reviewer" => "👀",
      _ => "🤖"
    };

    var parts = new List<string>();
    if (!string.IsNullOrEmpty(agentType))
      parts.Add($"{icon} {agentType}");
    if (!string.IsNullOrEmpty(description))
      parts.Add($"\"{TruncateText(description, 25)}\"");
    if (runInBackground)
      parts.Add("(bg)");

    return parts.Count > 0 ? string.Join(" ", parts) : "(subagent)";
  }

  private static string GetOutputSummary(string toolName, string output, bool success)
  {
    if (!success)
      return $"ERROR: {TruncateText(output, 80)}";

    var lowerName = toolName.ToLowerInvariant();

    if (lowerName == "read")
      return ""; // Don't show read content, just the file path in input

    if (lowerName == "bash")
    {
      var trimmed = output.Trim();
      return string.IsNullOrEmpty(trimmed) ? "" : TruncateText(trimmed, 150);
    }

    if (lowerName == "edit" || lowerName == "multiedit")
      return "file updated";

    if (lowerName == "write")
      return "file written";

    if (lowerName == "glob")
    {
      var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      return files.Length > 0 ? $"{files.Length} files found" : "no files found";
    }

    if (lowerName == "grep")
    {
      var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      return lines.Length > 0 ? $"{lines.Length} matches" : "no matches";
    }

    if (lowerName == "list")
    {
      var items = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      return items.Length > 0 ? $"{items.Length} items" : "empty directory";
    }

    if (lowerName == "todowrite")
      return "tasks updated";

    var trimmedOutput = output.Trim();
    return string.IsNullOrEmpty(trimmedOutput) ? "" : TruncateText(trimmedOutput, 100);
  }

  private static string TruncateText(string text, int maxLength)
  {
    if (string.IsNullOrEmpty(text)) return "";
    // Remove newlines for single-line display
    var singleLine = text.Replace("\n", " ").Replace("\r", "").Trim();
    return singleLine.Length > maxLength ? singleLine[..maxLength] + "..." : singleLine;
  }

  private async Task SendMessageAsync(string input, MarkdownView chatContent, Label statusLine, Action onScrollUpdate)
  {
    _logger.LogInformation("SendMessageAsync called with input: '{Input}'", input);
    _logger.LogInformation("Active session: {SessionId}, {SessionName}, ProjectId={ProjectId}",
        _activeSession?.Id, _activeSession?.Name, _activeSession?.ProjectId);
    _logger.LogInformation("Active project: {ProjectId}, {ProjectName}, RootPath={RootPath}",
        _activeProject?.Id, _activeProject?.Name, _activeProject?.RootPath);

    // Append user message with closed box - use responsive width from frame (full width minus small margin)
    var frameWidth = chatContent.Frame.Width > 0 ? chatContent.Frame.Width : 120;
    var boxWidth = Math.Max(40, frameWidth - 2);
    var currentText = chatContent.Text ?? "";
    var userMessage = FormatMessageBox(Icons.User, "You", input, boxWidth);

    // Create consistent assistant header (no side borders)
    var assistantHeader = $" {Icons.Assistant} Assistant ";
    var headerPadding = Math.Max(1, boxWidth - assistantHeader.Length);
    var boxHeader = $"─{assistantHeader}{new string('─', headerPadding)}";
    var boxFooter = new string('─', boxWidth);

    Application.MainLoop.Invoke(() =>
    {
      chatContent.Text = currentText + "\n" + userMessage + "\n\n" + boxHeader + "\n  ";
      onScrollUpdate();
    });

    var baseText = currentText + "\n" + userMessage + "\n\n" + boxHeader + "\n  ";

    try
    {
      // Single combined output that maintains correct ordering of text and tools
      var combinedOutput = new System.Text.StringBuilder();

      _logger.LogInformation("Calling _chat.RunAsync...");

      await _chat.RunAsync(_activeSession!, input, CancellationToken.None,
          update =>
          {
            if (!update.IsDone)
            {
              // Append text delta inline
              combinedOutput.Append(update.Delta);
              // MarkdownView handles rendering with colors natively
              var formattedOutput = combinedOutput.ToString().Replace("\n", "\n  ");
              Application.MainLoop.Invoke(() =>
                    {
                      chatContent.Text = baseText + formattedOutput;
                      onScrollUpdate();
                      chatContent.SetNeedsDisplay();
                    });
            }
            return Task.CompletedTask;
          },
          _fileChangeTracker,
          _todoTracker,
          AskUserQuestionsAsync,
          GetDiagnosticsAsync,
          toolExecution =>
          {
            // Track tool execution for status line
            _toolHistory.Add(toolExecution.ToolName, toolExecution.Success);

            // Append tool output inline (in correct order with text)
            var toolLine = FormatToolExecution(toolExecution);
            combinedOutput.Append(toolLine);

            // MarkdownView handles rendering with colors natively
            var formattedOutput = combinedOutput.ToString().Replace("\n", "\n  ");
            Application.MainLoop.Invoke(() =>
                  {
                    chatContent.Text = baseText + formattedOutput;
                    onScrollUpdate();
                    chatContent.SetNeedsDisplay();
                    UpdateStatusLine(statusLine);
                    statusLine.SetNeedsDisplay();
                  });

            return Task.CompletedTask;
          });

      _logger.LogInformation("_chat.RunAsync completed");

      // Final update - close the message box
      Application.MainLoop.Invoke(() =>
      {
        _logger.LogInformation("Final update - closing message box");
        // MarkdownView handles rendering with colors natively
        var formattedOutput = combinedOutput.ToString().Replace("\n", "\n  ");
        chatContent.Text = baseText + formattedOutput + "\n" + boxFooter + "\n";
        onScrollUpdate();
        chatContent.SetNeedsDisplay();
        UpdateStatusLine(statusLine);
        statusLine.SetNeedsDisplay();
      });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "SendMessageAsync caught exception: {Message}", ex.Message);
      Application.MainLoop.Invoke(() =>
      {
        chatContent.Text = (chatContent.Text ?? "") + $"\n  {Icons.Cross} Error: {ex.Message}\n" + boxFooter + "\n";
        chatContent.SetNeedsDisplay();
      });
    }
  }

  private Task<List<QuestionAnswer>> AskUserQuestionsAsync(QuestionRequest request)
  {
    var answers = new List<QuestionAnswer>();

    Application.MainLoop.Invoke(() =>
    {
      foreach (var question in request.Questions)
      {
        var answer = new QuestionAnswer { QuestionText = question.Text };

        if (question.Options.Count == 0)
        {
          var response = DialogHelpers.PromptText("Agent Question", question.Text, "", required: false);
          if (!string.IsNullOrWhiteSpace(response))
            answer.Answers.Add(response);
        }
        else
        {
          var selected = DialogHelpers.PromptSelection(
                  "Agent Question",
                  question.Options,
                  o => o);

          if (selected != null)
            answer.Answers.Add(selected);
        }

        answers.Add(answer);
      }
    });

    return Task.FromResult(answers);
  }

  private Task<List<Diagnostic>> GetDiagnosticsAsync(string[] files)
  {
    var diagnostics = new List<Diagnostic>();

    try
    {
      var workingDir = _activeProject?.RootPath ?? Environment.CurrentDirectory;
      var projectFile = Directory.GetFiles(workingDir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
                     ?? Directory.GetFiles(workingDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();

      if (projectFile == null)
        return Task.FromResult(diagnostics);

      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName = "dotnet",
        Arguments = $"build \"{projectFile}\" --no-restore --verbosity minimal --nologo",
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

      var combinedOutput = output + "\n" + errorOutput;
      var pattern = new System.Text.RegularExpressions.Regex(
          @"^(.+?)\((\d+),(\d+)\):\s*(error|warning)\s+(\w+):\s*(.+)$",
          System.Text.RegularExpressions.RegexOptions.Multiline);

      foreach (System.Text.RegularExpressions.Match match in pattern.Matches(combinedOutput))
      {
        var filePath = match.Groups[1].Value.Trim();

        if (files.Length > 0 && !files.Any(f =>
            filePath.Equals(f, StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(System.IO.Path.GetFileName(f), StringComparison.OrdinalIgnoreCase)))
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
      // Silently fail
    }

    return Task.FromResult(diagnostics);
  }
}
