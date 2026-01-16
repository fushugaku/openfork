# Refactoring: ConsoleApp.Agents.cs

## Overview
Convert Agents screen to Terminal.Gui with ListView and form dialogs.

## Pattern
Same as Projects/Sessions - ListView + Buttons + Dialogs

## Key Components
- `CreateAgentsView()` - Main view with ListView
- `CreateAgentDialog()` - Create new agent
- `EditAgentDialog()` - Edit existing agent
- `ShowAgentDetail()` - Detail view
- `DeleteAgent()` - Delete with confirmation

## ListView Items Format
```csharp
var items = agents.Select(a =>
{
    var isActive = _activeSession?.ActiveAgentId == a.Id;
    var marker = isActive ? $"{Icons.Check} " : "  ";
    return $"{marker}{Icons.Agent} {a.Name} - {a.ProviderKey}/{a.Model}";
}).ToList();
```

## Agent Form Fields
- Name (TextField)
- Provider (Selection from available providers)
- Model (Selection from provider models)
- System Prompt (TextView - multiline)

## Buttons
- New Agent
- Edit
- Set Active
- Delete
- Back

## Migration Notes
- Replace `MultilineInput` with `TextViewHelpers.PromptMultiline()`
- Use `SelectProviderKey()` and `SelectModel()` from Helpers
- Update `_activeSession.ActiveAgentId` when setting active
- Clear `_activeSession.ActivePipelineId` when activating agent
