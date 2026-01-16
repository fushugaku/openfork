# Refactoring: ConsoleApp.Chat.cs

## Overview
Most complex refactoring - streaming chat UI with side panels for files/todos.

## Layout
```
┌─────────────────────────────────┬──────────────┐
│                                 │  Todos (5)   │
│                                 │  ✓ Task 1    │
│    Chat History TextView        │  → Task 2    │
│    (readonly, scrollable)       │  ○ Task 3    │
│                                 │──────────────│
│                                 │  Files (3)   │
│                                 │  + file1.cs  │
│                                 │  ~ file2.cs  │
└─────────────────────────────────┴──────────────┘
│ Input: [text field]                    [Send] │
└────────────────────────────────────────────────┘
```

## Key Components
- `CreateChatView()` - Main chat UI
- `_chatHistoryView` - TextView for message history
- `_chatInputField` - TextField for input
- `_todosFrame` - FrameView for todos
- `_filesFrame` - FrameView for file changes
- `StreamChatAsync()` - Handle streaming responses

## Streaming Implementation
```csharp
private async Task StreamChatAsync(string input)
{
    await _chat.RunAsync(_activeSession!, input, cancellationToken,
        update =>
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendToHistory(update.Delta);
                _chatHistoryView.SetNeedsDisplay();
            });
            return Task.CompletedTask;
        },
        _fileChangeTracker,
        _todoTracker,
        AskUserQuestionsAsync,
        GetDiagnosticsAsync,
        toolExecution =>
        {
            Application.MainLoop.Invoke(() =>
            {
                AppendToolOutput(toolExecution);
            });
            return Task.CompletedTask;
        });
}
```

## Side Panels Update
```csharp
private void UpdateTodosPanel()
{
    _todosFrame.RemoveAll();
    foreach (var todo in _todoTracker.Items)
    {
        var icon = todo.Status == "completed" ? "✓" : "○";
        var label = new Label($"{icon} {todo.Content}") { Y = Pos.Bottom(_todosFrame) };
        _todosFrame.Add(label);
    }
}
```

## Question Handling
Keep `AskUserQuestionsAsync()` logic but use Terminal.Gui dialogs instead of Spectre prompts.

## Diagnostics
Keep `GetDiagnosticsAsync()` unchanged.

## Key Changes
- AnsiConsole.Live → periodic TextView updates
- Panel layouts → FrameView layouts
- Escape key → handled by Terminal.Gui QuitKey
- Tool outputs → append to TextView with formatting
