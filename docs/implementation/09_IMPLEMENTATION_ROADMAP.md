# OpenFork Implementation Roadmap

## Executive Summary

This roadmap outlines the plan for bringing OpenFork to feature parity with the opencode reference implementation. The implementation is organized into 4 phases spanning approximately 12 weeks.

---

## Phase Overview

| Phase | Focus | Duration | Priority |
|-------|-------|----------|----------|
| **Phase 1** | Foundation | Weeks 1-3 | CRITICAL |
| **Phase 2** | Core Features | Weeks 4-6 | HIGH |
| **Phase 3** | Integration | Weeks 7-9 | HIGH |
| **Phase 4** | Polish | Weeks 10-12 | MEDIUM |

---

## Phase 1: Foundation (Weeks 1-3)

### Week 1: Event Bus & Permission System

#### 1.1 Event Bus (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Events/IEvent.cs`
- `src/OpenFork.Core/Events/EventBase.cs`
- `src/OpenFork.Core/Events/IEventBus.cs`
- `src/OpenFork.Core/Events/InMemoryEventBus.cs`
- `src/OpenFork.Core/Events/SessionEvents.cs`
- `src/OpenFork.Core/Events/ToolEvents.cs`
- `src/OpenFork.Core/Events/MessageEvents.cs`

**Tasks:**
- [ ] Define core event interfaces
- [ ] Implement InMemoryEventBus with batching
- [ ] Create event categories (Session, Tool, Message, Error)
- [ ] Add DI registration
- [ ] Write unit tests

**Acceptance Criteria:**
- Events can be published and subscribed
- 16ms batching works correctly
- Filters and scoping work

#### 1.2 Permission System (Days 3-5)
**Files to Create:**
- `src/OpenFork.Core/Permissions/PermissionRule.cs`
- `src/OpenFork.Core/Permissions/PermissionRuleset.cs`
- `src/OpenFork.Core/Permissions/PermissionAction.cs`
- `src/OpenFork.Core/Services/IPermissionService.cs`
- `src/OpenFork.Core/Services/PermissionService.cs`
- `src/OpenFork.Core/Permissions/BuiltInRulesets.cs`
- `src/OpenFork.Storage/Repositories/PermissionRepository.cs`

**Tasks:**
- [ ] Define permission domain model
- [ ] Implement pattern matching logic
- [ ] Create built-in rulesets (Primary, Explorer, Planner, Researcher)
- [ ] Add user prompt integration
- [ ] Implement permission persistence
- [ ] Write unit tests

**Acceptance Criteria:**
- Pattern matching works correctly
- Last-match-wins evaluation
- User prompts display correctly
- Permissions persist across sessions

### Week 2: Message Parts System

#### 2.1 Domain Model (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Domain/Parts/MessagePart.cs`
- `src/OpenFork.Core/Domain/Parts/TextPart.cs`
- `src/OpenFork.Core/Domain/Parts/ToolPart.cs`
- `src/OpenFork.Core/Domain/Parts/ReasoningPart.cs`
- `src/OpenFork.Core/Domain/Parts/CompactionPart.cs`
- `src/OpenFork.Core/Domain/Parts/FilePart.cs`
- `src/OpenFork.Core/Domain/Parts/PatchPart.cs`
- `src/OpenFork.Core/Domain/Parts/SubtaskPart.cs`

**Tasks:**
- [ ] Define MessagePart base class
- [ ] Implement all 11 part types
- [ ] Create ToolPartStatus state machine
- [ ] Add serialization support

#### 2.2 Repository & Migration (Days 3-4)
**Files to Create:**
- `src/OpenFork.Storage/Repositories/MessagePartRepository.cs`
- `src/OpenFork.Storage/Migrations/AddMessageParts.sql`

**Tasks:**
- [ ] Create MessageParts table
- [ ] Implement polymorphic repository
- [ ] Create data migration script
- [ ] Test backward compatibility

#### 2.3 UI Rendering (Day 5)
**Files to Modify:**
- `src/OpenFork.Cli/Tui/ToolOutputRenderer.cs`
- `src/OpenFork.Cli/Tui/ConsoleApp.Chat.cs`

**Tasks:**
- [ ] Create part renderers
- [ ] Implement live status updates
- [ ] Add ToolPart spinner/progress

**Acceptance Criteria:**
- All part types serialize/deserialize correctly
- Tool execution shows live status
- Migration preserves existing data

### Week 3: Token Management Enhancement

#### 3.1 Layer 1 - Truncation (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Services/IOutputTruncationService.cs`
- `src/OpenFork.Core/Services/OutputTruncationService.cs`
- `src/OpenFork.Core/Constants/TokenConstants.cs`

**Tasks:**
- [ ] Implement per-tool truncation limits
- [ ] Add disk spillover for large outputs
- [ ] Create spill cleanup logic

#### 3.2 Layer 2 - Pruning (Days 3-4)
**Files to Create:**
- `src/OpenFork.Core/Services/IOutputPruningService.cs`
- `src/OpenFork.Core/Services/OutputPruningService.cs`

**Tasks:**
- [ ] Implement progressive pruning algorithm
- [ ] Add protection for recent content
- [ ] Preserve tool metadata when pruning

#### 3.3 Layer 3 - Compaction (Day 5)
**Files to Modify:**
- `src/OpenFork.Search/Services/HistoryCompactService.cs`

**Tasks:**
- [ ] Enhance compaction with boundary markers
- [ ] Implement message loading with compaction awareness
- [ ] Add compaction metrics

**Acceptance Criteria:**
- Large outputs truncate with spillover
- Pruning respects protection threshold
- Compaction creates proper summaries

---

## Phase 2: Core Features (Weeks 4-6)

### Week 4: Agent Architecture Enhancement

#### 4.1 Enhanced Agent Model (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Domain/Agent.cs` (replace AgentProfile)
- `src/OpenFork.Core/Domain/AgentCategory.cs`
- `src/OpenFork.Core/Domain/AgentExecutionMode.cs`
- `src/OpenFork.Core/Domain/ToolConfiguration.cs`
- `src/OpenFork.Core/Agents/BuiltInAgents.cs`

**Tasks:**
- [ ] Define enhanced Agent model
- [ ] Create agent categories (Primary, Subagent, Hidden)
- [ ] Implement tool filtering modes
- [ ] Define built-in agents

#### 4.2 Agent Registry (Days 3-4)
**Files to Create:**
- `src/OpenFork.Core/Services/IAgentRegistry.cs`
- `src/OpenFork.Core/Services/AgentRegistry.cs`
- `src/OpenFork.Storage/Repositories/AgentRepository.cs`

**Tasks:**
- [ ] Implement agent registry with caching
- [ ] Add custom agent persistence
- [ ] Create agent validation

#### 4.3 Agent Execution Service (Day 5)
**Files to Create:**
- `src/OpenFork.Core/Services/IAgentExecutionService.cs`
- `src/OpenFork.Core/Services/AgentExecutionService.cs`

**Tasks:**
- [ ] Implement execution modes
- [ ] Add system prompt templating
- [ ] Integrate with ChatService

**Acceptance Criteria:**
- Built-in agents work correctly
- Custom agents can be registered
- Execution modes behave differently

### Week 5: Subagent System

#### 5.1 Domain Model (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Domain/SubSession.cs`
- `src/OpenFork.Core/Domain/SubSessionStatus.cs`
- `src/OpenFork.Core/Domain/SubAgentConfig.cs`
- `src/OpenFork.Core/Events/SubSessionEvents.cs`

**Tasks:**
- [ ] Define SubSession model
- [ ] Create status state machine
- [ ] Define subagent event types

#### 5.2 Task Tool (Days 3-4)
**Files to Create:**
- `src/OpenFork.Core/Tools/TaskTool.cs`
- `src/OpenFork.Core/Services/ISubagentService.cs`
- `src/OpenFork.Core/Services/SubagentService.cs`

**Tasks:**
- [ ] Implement TaskTool
- [ ] Create SubagentService
- [ ] Add permission inheritance
- [ ] Implement recursion prevention

#### 5.3 Integration & Testing (Day 5)
**Tasks:**
- [ ] Integrate with ChatService
- [ ] Add event communication
- [ ] Write integration tests

**Acceptance Criteria:**
- Task tool spawns subagents
- Subagents respect tool restrictions
- Events flow to parent correctly

### Week 6: Hooks System

#### 6.1 Hook Framework (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Hooks/IHook.cs`
- `src/OpenFork.Core/Hooks/HookTrigger.cs`
- `src/OpenFork.Core/Hooks/HookContext.cs`
- `src/OpenFork.Core/Hooks/HookResult.cs`
- `src/OpenFork.Core/Services/IHookService.cs`
- `src/OpenFork.Core/Services/HookService.cs`

**Tasks:**
- [ ] Define hook interfaces
- [ ] Implement hook pipeline
- [ ] Add priority ordering

#### 6.2 Built-in Hooks (Days 3-4)
**Files to Create:**
- `src/OpenFork.Core/Hooks/BuiltIn/LoggingHook.cs`
- `src/OpenFork.Core/Hooks/BuiltIn/MetricsHook.cs`
- `src/OpenFork.Core/Hooks/BuiltIn/ValidationHook.cs`
- `src/OpenFork.Core/Hooks/BuiltIn/FileBackupHook.cs`

**Tasks:**
- [ ] Implement logging hook
- [ ] Implement metrics hook
- [ ] Implement validation hook
- [ ] Implement backup hook

#### 6.3 Custom Hooks (Day 5)
**Files to Create:**
- `src/OpenFork.Core/Hooks/CommandHook.cs`
- `src/OpenFork.Core/Hooks/WebhookHook.cs`
- `src/OpenFork.Core/Services/HookLoader.cs`

**Tasks:**
- [ ] Implement command hooks
- [ ] Implement webhook hooks
- [ ] Add project-level hook loading

**Acceptance Criteria:**
- Hooks execute in correct order
- Pre-hooks can cancel operations
- Custom hooks work from config

---

## Phase 3: Integration (Weeks 7-9)

### Week 7: MCP Integration

#### 7.1 Transport Layer (Days 1-2)
**Files to Create:**
- `src/OpenFork.Core/Mcp/IMcpTransport.cs`
- `src/OpenFork.Core/Mcp/Transport/StdioTransport.cs`
- `src/OpenFork.Core/Mcp/Transport/HttpTransport.cs`

**Tasks:**
- [ ] Implement stdio transport
- [ ] Implement HTTP transport
- [ ] Add connection management

#### 7.2 MCP Server Manager (Days 3-4)
**Files to Create:**
- `src/OpenFork.Core/Mcp/IMcpServerManager.cs`
- `src/OpenFork.Core/Mcp/McpServerManager.cs`
- `src/OpenFork.Core/Mcp/McpServerConfig.cs`
- `src/OpenFork.Core/Mcp/McpTool.cs`

**Tasks:**
- [ ] Implement server lifecycle management
- [ ] Add tool discovery
- [ ] Create configuration loading

#### 7.3 Tool Integration (Day 5)
**Files to Create:**
- `src/OpenFork.Core/Mcp/McpToolAdapter.cs`

**Tasks:**
- [ ] Create tool adapter
- [ ] Register MCP tools
- [ ] Test with real MCP server

**Acceptance Criteria:**
- Can connect to stdio MCP servers
- Tools appear in registry
- Tool calls work correctly

### Week 8: UI Enhancements

#### 8.1 Permission Prompts (Days 1-2)
**Files to Modify:**
- `src/OpenFork.Cli/Tui/ConsoleApp.Chat.cs`

**Files to Create:**
- `src/OpenFork.Cli/Tui/PermissionPromptDialog.cs`

**Tasks:**
- [ ] Create permission prompt UI
- [ ] Add remember option
- [ ] Show pending permissions

#### 8.2 Agent Switching (Days 3-4)
**Files to Modify:**
- `src/OpenFork.Cli/Tui/ConsoleApp.Agents.cs`

**Tasks:**
- [ ] Show agent categories
- [ ] Display agent capabilities
- [ ] Add agent configuration UI

#### 8.3 Session Enhancements (Day 5)
**Files to Modify:**
- `src/OpenFork.Cli/Tui/ConsoleApp.Sessions.cs`

**Tasks:**
- [ ] Show session summary
- [ ] Display token usage
- [ ] Add compaction indicators

**Acceptance Criteria:**
- Permission prompts work correctly
- Agent UI shows all info
- Sessions display rich metadata

### Week 9: Testing & Documentation

#### 9.1 Integration Tests (Days 1-3)
**Files to Create:**
- `tests/OpenFork.Tests/Integration/AgentExecutionTests.cs`
- `tests/OpenFork.Tests/Integration/SubagentTests.cs`
- `tests/OpenFork.Tests/Integration/PermissionTests.cs`
- `tests/OpenFork.Tests/Integration/McpTests.cs`

**Tasks:**
- [ ] Write end-to-end agent tests
- [ ] Test permission flows
- [ ] Test subagent scenarios
- [ ] Test MCP integration

#### 9.2 Performance Tests (Days 4-5)
**Files to Create:**
- `tests/OpenFork.Tests/Performance/TokenManagementBenchmarks.cs`
- `tests/OpenFork.Tests/Performance/EventBusBenchmarks.cs`

**Tasks:**
- [ ] Benchmark token management
- [ ] Benchmark event bus
- [ ] Profile memory usage
- [ ] Identify bottlenecks

**Acceptance Criteria:**
- 90%+ test coverage for new code
- No performance regressions
- Memory usage is reasonable

---

## Phase 4: Polish (Weeks 10-12)

### Week 10: Performance Optimization

**Tasks:**
- [ ] Optimize event batching
- [ ] Add message part caching
- [ ] Improve truncation performance
- [ ] Profile and optimize hot paths

### Week 11: Error Handling & Resilience

**Tasks:**
- [ ] Add circuit breakers for MCP
- [ ] Improve error messages
- [ ] Add recovery mechanisms
- [ ] Test failure scenarios

### Week 12: Documentation & Release

**Tasks:**
- [ ] Update README
- [ ] Create migration guide
- [ ] Write user documentation
- [ ] Prepare release notes
- [ ] Version bump

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking existing functionality | Medium | High | Comprehensive tests, backward compat |
| Performance degradation | Medium | Medium | Benchmarks, profiling |
| MCP complexity | High | Medium | Start with stdio, add HTTP later |
| Scope creep | Medium | Medium | Strict phase boundaries |

---

## Success Metrics

| Metric | Target |
|--------|--------|
| Test Coverage | 85%+ for new code |
| Permission Prompt Latency | <100ms |
| Event Bus Throughput | 10,000 events/sec |
| Token Management Overhead | <5% |
| MCP Tool Discovery | <5 seconds |

---

## Dependencies

### External Dependencies
- Terminal.Gui (UI framework)
- Spectre.Console (Rich console output)
- Dapper (Database access)
- System.Text.Json (Serialization)

### Internal Dependencies
```
Phase 1 (Foundation):
  EventBus ←── Permission System
  EventBus ←── Message Parts

Phase 2 (Core):
  Permission System ←── Agent Architecture
  Message Parts ←── Subagent System
  EventBus ←── Hooks System

Phase 3 (Integration):
  Subagent System ←── MCP Integration
  All Foundation ←── UI Enhancements
```

---

## Getting Started

### Prerequisites
```bash
# Ensure .NET 10 SDK installed
dotnet --version

# Restore dependencies
dotnet restore

# Run existing tests
dotnet test
```

### Development Workflow
1. Create feature branch from `master`
2. Implement feature following guide
3. Write tests
4. Update documentation
5. Create PR for review

### Code Style
- Follow existing C# conventions
- Use nullable reference types
- Add XML documentation for public APIs
- Keep files focused (<500 lines)

---

## Appendix: File Organization

```
src/
├── OpenFork.Cli/
│   └── Tui/
│       ├── Dialogs/
│       │   └── PermissionPromptDialog.cs
│       └── Renderers/
│           └── PartRenderer.cs
├── OpenFork.Core/
│   ├── Agents/
│   │   └── BuiltInAgents.cs
│   ├── Constants/
│   │   └── TokenConstants.cs
│   ├── Domain/
│   │   ├── Parts/
│   │   │   └── *.cs
│   │   └── Agent.cs
│   ├── Events/
│   │   └── *.cs
│   ├── Hooks/
│   │   ├── BuiltIn/
│   │   │   └── *.cs
│   │   └── *.cs
│   ├── Mcp/
│   │   ├── Transport/
│   │   │   └── *.cs
│   │   └── *.cs
│   ├── Permissions/
│   │   └── *.cs
│   └── Services/
│       └── *.cs
├── OpenFork.Storage/
│   ├── Migrations/
│   │   └── *.sql
│   └── Repositories/
│       └── *.cs
└── OpenFork.Search/
    └── Services/
        └── *.cs
```

---

## Conclusion

This roadmap provides a structured approach to implementing the missing features. Each phase builds on the previous, ensuring a stable foundation before adding more complex functionality.

Key principles:
1. **Foundation first** - Event bus and permissions enable everything else
2. **Test early** - Write tests as you implement
3. **Incremental delivery** - Each phase delivers usable functionality
4. **Backward compatibility** - Don't break existing users
