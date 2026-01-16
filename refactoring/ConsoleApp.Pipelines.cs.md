# Refactoring: ConsoleApp.Pipelines.cs

## Overview
Convert Pipelines screen to Terminal.Gui with step configuration.

## Pattern
Similar to Agents but with additional step configuration dialog.

## Key Components
- `CreatePipelinesView()` - Main view with ListView
- `CreatePipelineDialog()` - Create pipeline (name, description)
- `ConfigurePipelineSteps()` - Multi-step form
- `ShowPipelineDetail()` - Show pipeline with steps table
- `EditPipelineDialog()` - Edit name/description

## Pipeline Steps Configuration
```csharp
private void ConfigurePipelineSteps(Pipeline pipeline)
{
    var agents = _agents.ListAsync().GetAwaiter().GetResult();
    
    // Dialog with:
    // - Number of steps input
    // - For each step:
    //   - Agent selection (ListView)
    //   - Handoff mode (RadioGroup: full/last)
    
    // Save steps: _pipelines.UpsertStepsAsync(pipeline.Id, steps);
}
```

## Detail View - Steps Table
```csharp
var table = TableHelpers.CreateTable();
TableHelpers.SetupColumns(table, "Order", "Agent", "Handoff");
foreach (var step in steps.OrderBy(s => s.OrderIndex))
{
    TableHelpers.AddRow(table, step.OrderIndex + 1, agentName, step.HandoffMode);
}
```

## Buttons
- New Pipeline
- Edit
- Configure Steps
- Set Active
- Delete
- Back
