---
task: Your task description here
test_command: "npm test"
---

# Task

Files to Modify

1. OpenFork.Cli.csproj
   Remove: Spectre.Console (0.49.1), Spectre.Console.Cli (0.49.1)
   Add: Terminal.Gui (2.0.0-alpha.)
2. Program.cs
   Remove Console.CancelKeyPress handler
   Initialize Terminal.Gui Application
   Wrap ConsoleApp.RunAsync() in Terminal.Gui context
3. PromptStyles.cs (Theme, Icons, UI Helpers)
   Keep Theme colors (convert to Terminal.Gui Color)
   Keep Icons constants
   Replace Prompts with Terminal.Gui Dialog helpers
   Replace Panels with Terminal.Gui FrameView helpers
   Replace Tables with Terminal.Gui TableView helpers
   Replace StatusSpinner with Terminal.Gui ProgressDialog
   Replace MultilineInput with Terminal.Gui TextView dialogs
4. SpectreConsoleFileBrowser.cs
   Replace custom browser with Terminal.Gui OpenDialog/SaveDialog
   Simplify to wrapper around native dialogs
5. ConsoleApp.cs
   Replace AnsiConsole operations with Terminal.Gui Window
   Convert menu loop to Terminal.Gui menu bar or button navigation
   Replace SelectionPrompt with Terminal.Gui ListView or RadioGroup
6. ConsoleApp.Helpers.cs
   Replace FigletText header with styled Label
   Replace context Grid/Table with Terminal.Gui FrameView containing Labels
   Replace StatusSpinner.RunAsync with background tasks + ProgressDialog
   Refactor provider/model selection to use Terminal.Gui dialogs
   Replace directory browser calls with new Terminal.Gui implementation
7. ConsoleApp.Projects.cs
   Replace AnsiConsole.Clear() with Application.Top.RemoveAll()
   Replace Tables.Create() with TableView or ListView
   Replace SelectionPrompt with ListView selection
   Replace detail panels with FrameView
8. ConsoleApp.Sessions.cs
   Same patterns as Projects screen
   Replace tables, prompts, panels with Terminal.Gui equivalents
9. ConsoleApp.Agents.cs
   Same patterns as Projects screen
   Agent creation/editing forms using Terminal.Gui TextField, TextView, ComboBox
10. ConsoleApp.Pipelines.cs
    Same patterns as Projects screen
    Step configuration using Terminal.Gui forms
11. ConsoleApp.Chat.cs (Most Complex)
    Replace streaming AnsiConsole.Live() with TextView + periodic updates
    Replace Panel/Grid layout with Terminal.Gui FrameView layouts
    Replace side panels (files, todos) with separate FrameView views
    Replace TextPrompt with TextField
    Replace question dialogs with Terminal.Gui Dialog + form controls
    Replace ESC key handler with Terminal.Gui key bindings
    Keep file tracking logic, just update rendering
12. ToolOutputRenderer.cs
    Replace IRenderable return type with Terminal.Gui View
    Convert all tool output panels to FrameView or TextView
    Replace Table with TableView or formatted TextView
    Replace Tree with TreeView
    Replace Panel with FrameView
    Architecture Changes
    From Spectre.Console (Console-based TUI):
    Render-per-frame model
    AnsiConsole static API
    Prompt-based synchronous interaction
    Custom rendering with markup
    To Terminal.Gui (Event-driven TUI):
    View hierarchy with layout engine
    Application instance with Top-level container
    Async event-driven interaction
    Native controls with data binding
    Key Terminal.Gui Components
    Application - App lifecycle
    Window - Main container with border
    FrameView - Panel/group container
    Label - Text display
    TextField - Single-line input
    TextView - Multi-line text display/editing
    Button - Clickable buttons
    ListView - Selectable list
    TableView - Table with columns/rows
    TreeView - Hierarchical tree
    Dialog - Modal dialogs
    OpenDialog/SaveDialog - File selection
    MenuBar - Application menu
    StatusBar - Bottom status bar
    ProgressDialog - Progress indicator

There are docs for each file in refactoring folder
