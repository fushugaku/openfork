# OpenFork TUI Refactoring: Spectre.Console → Terminal.Gui

## Overview
This directory contains detailed refactoring guides for migrating OpenFork's TUI from Spectre.Console to Terminal.Gui v2.

## Why Terminal.Gui?
- **Better architecture** - Event-driven UI vs prompt-based loops
- **Native controls** - TableView, TreeView, ListView, FileDialog
- **Cross-platform** - Consistent experience on Windows, macOS, Linux
- **Active development** - v2 is modern, well-maintained
- **Better for complex UIs** - Streaming chat, side panels, real-time updates

## Refactoring Files

### 1. [OpenFork.Cli.csproj.md](OpenFork.Cli.csproj.md)
**Complexity:** Easy  
**Estimated Time:** 5 minutes  
Update package dependencies.

### 2. [Program.cs.md](Program.cs.md)
**Complexity:** Easy  
**Estimated Time:** 10 minutes  
Initialize Terminal.Gui application lifecycle.

### 3. [PromptStyles.cs.md](PromptStyles.cs.md)
**Complexity:** Medium  
**Estimated Time:** 2 hours  
Convert theme, dialogs, helpers to Terminal.Gui.

### 4. [SpectreConsoleFileBrowser.cs.md](SpectreConsoleFileBrowser.cs.md)
**Complexity:** Easy  
**Estimated Time:** 30 minutes  
Replace custom browser with native file dialogs.

### 5. [ConsoleApp.cs.md](ConsoleApp.cs.md)
**Complexity:** Medium  
**Estimated Time:** 2 hours  
Convert main loop to window-based UI with menu.

### 6. [ConsoleApp.Helpers.cs.md](ConsoleApp.Helpers.cs.md)
**Complexity:** Easy  
**Estimated Time:** 1 hour  
Update helper methods, remove rendering code.

### 7. [ConsoleApp.Projects.cs.md](ConsoleApp.Projects.cs.md)
**Complexity:** Medium  
**Estimated Time:** 2 hours  
Convert projects screen to ListView + dialogs.

### 8. [ConsoleApp.Sessions.cs.md](ConsoleApp.Sessions.cs.md)
**Complexity:** Easy  
**Estimated Time:** 1 hour  
Similar to Projects screen.

### 9. [ConsoleApp.Agents.cs.md](ConsoleApp.Agents.cs.md)
**Complexity:** Medium  
**Estimated Time:** 2 hours  
Agent CRUD with form dialogs.

### 10. [ConsoleApp.Pipelines.cs.md](ConsoleApp.Pipelines.cs.md)
**Complexity:** Medium  
**Estimated Time:** 2 hours  
Pipeline configuration with steps.

### 11. [ConsoleApp.Chat.cs.md](ConsoleApp.Chat.cs.md)
**Complexity:** Hard  
**Estimated Time:** 4 hours  
Streaming chat UI with side panels.

### 12. [ToolOutputRenderer.cs.md](ToolOutputRenderer.cs.md)
**Complexity:** Medium  
**Estimated Time:** 2 hours  
Convert tool outputs to plain text or View-based.

## Total Estimated Time
**~19 hours** of focused development

## Refactoring Order

### Phase 1: Foundation (3 hours)
1. OpenFork.Cli.csproj
2. PromptStyles.cs
3. SpectreConsoleFileBrowser.cs
4. Program.cs

### Phase 2: Core UI (4 hours)
5. ConsoleApp.cs
6. ConsoleApp.Helpers.cs

### Phase 3: Screens (6 hours)
7. ConsoleApp.Projects.cs
8. ConsoleApp.Sessions.cs
9. ConsoleApp.Agents.cs
10. ConsoleApp.Pipelines.cs

### Phase 4: Chat (6 hours)
11. ToolOutputRenderer.cs
12. ConsoleApp.Chat.cs

## Testing Strategy

After each phase:
1. **Unit test** - Verify builds without errors
2. **Integration test** - Run application, navigate UI
3. **Functional test** - Test all features in that phase
4. **Regression test** - Verify previous phases still work

## Key Architectural Changes

### From: Prompt-based Loop
```csharp
while (true)
{
    var choice = AnsiConsole.Prompt(...);
    switch (choice)
    {
        case "Projects": await ProjectsScreen(); break;
    }
}
```

### To: Event-driven Window
```csharp
var window = new Window("OpenFork");
var menu = new MenuBar(...);
window.Add(contextPanel, contentFrame);
Application.Run();
```

## Common Patterns

### ListView Selection
```csharp
var listView = new ListView() { ... };
listView.SetSource(items);
listView.OpenSelectedItem += (e) => HandleSelection();
```

### Dialogs
```csharp
var dialog = new Dialog("Title");
dialog.Add(label, textField);
dialog.AddButton(okButton);
Application.Run(dialog);
```

### Async Operations
```csharp
_ = Task.Run(async () =>
{
    var result = await LongOperation();
    Application.MainLoop.Invoke(() => UpdateUI(result));
});
```

## Terminal.Gui v2 Resources
- Docs: https://gui-cs.github.io/Terminal.Gui/
- GitHub: https://github.com/gui-cs/Terminal.Gui
- Examples: https://github.com/gui-cs/Terminal.Gui/tree/v2_develop/Examples

## Notes
- Terminal.Gui v2 is in alpha but API is stable
- Some v1 examples may not apply to v2
- Use `Application.MainLoop.Invoke()` for thread safety
- QuitKey defaults to Esc (configurable)
