# OpenFork Gap Analysis: opencode Reference Implementation

## Executive Summary

This document provides a comprehensive analysis comparing OpenFork's current implementation against the opencode reference implementation. It identifies missing features, architectural gaps, and provides prioritized recommendations for bringing OpenFork to feature parity.

---

## Feature Comparison Matrix

| Feature Category | opencode | OpenFork | Gap Level |
|-----------------|----------|----------|-----------|
| **Agent System** | 5 agent types + hidden | Config-based agents | HIGH |
| **Subagent System** | Task tool spawns children | Not implemented | CRITICAL |
| **Token Management** | 3-layer strategy | Single compaction at 85% | HIGH |
| **Permission System** | Full ruleset (allow/deny/ask) | None | CRITICAL |
| **Message Parts** | 11 typed parts | Simple message model | HIGH |
| **Session Schema** | Rich (summary, share, revert) | Basic CRUD | MEDIUM |
| **Event Bus** | Cross-module pub/sub | None | MEDIUM |
| **MCP Integration** | Full stdio/HTTP support | None | HIGH |
| **Custom Tools** | .opencode/tool/*.{js,ts} | None | MEDIUM |
| **Hooks System** | Pre/post execution | None | MEDIUM |
| **Cloud Storage** | S3/R2/Cloudflare | Local only | LOW |
| **Tool Output Spill** | Disk spillover | Memory only | MEDIUM |

---

## Critical Gaps (Must Implement)

### 1. Subagent System (CRITICAL)
**Current State**: OpenFork has no concept of spawning child agents/sessions.

**opencode Implementation**:
- `task` tool spawns independent child sessions
- Parent-child relationship via `parentID`
- Event-based communication (PartUpdated subscriptions)
- Permission inheritance (parent + agent merged)
- Recursion prevention

**Impact**: Without subagents, OpenFork cannot delegate complex tasks to specialized agents, severely limiting agentic capabilities.

**Effort**: ~2-3 weeks

---

### 2. Permission System (CRITICAL)
**Current State**: No permission model; all tools are implicitly allowed.

**opencode Implementation**:
- Per-agent permission rulesets
- Actions: `allow`, `deny`, `ask` (user confirmation)
- Pattern matching for resources (file paths, commands)
- Last-matching-rule wins evaluation
- Default action: `ask`

**Impact**: Security vulnerability - agents can execute any operation without user consent.

**Effort**: ~1-2 weeks

---

## High Priority Gaps

### 3. Token Management (HIGH)
**Current State**: Simple compaction at 85% capacity with tool output truncation.

**opencode Implementation**:
```
Layer 1: Tool Output Truncation
├── 2000 lines max
├── 50KB max size
└── Disk spillover for large outputs

Layer 2: Tool Output Pruning
├── Protect first 40K tokens
├── Minimum 20K token reduction
└── Progressive old output removal

Layer 3: Conversation Compaction
├── Summarization via compaction agent
├── Preserves key decisions/context
└── Stops loading at compaction boundary
```

**Key Constants**:
- `OUTPUT_TOKEN_MAX`: 32,000 tokens
- `PRUNE_PROTECT`: 40,000 tokens
- `PRUNE_MINIMUM`: 20,000 tokens

**Impact**: Without sophisticated token management, context window overflows cause lost context and degraded responses.

**Effort**: ~1-2 weeks

---

### 4. Agent Architecture (HIGH)
**Current State**: Flat config-based agents with uniform behavior.

**opencode Implementation**:
- **Primary Agents**: `build`, `plan` - main user-facing
- **Subagent Types**: `general`, `explore` - spawned by Task tool
- **Hidden Agents**: Internal system agents (compaction, etc.)
- **Per-Agent Properties**:
  - `modelID`, `providerID`
  - `permission` ruleset
  - `systemPrompt` templating
  - `tools` override list

**Impact**: Limited agent specialization and capability isolation.

**Effort**: ~1 week

---

### 5. Message Parts System (HIGH)
**Current State**: Simple `Message` with `Content` and `ToolCallsJson`.

**opencode Implementation**:
11 typed message parts:
1. `TextPart` - LLM text output
2. `ReasoningPart` - Chain-of-thought
3. `ToolPart` - Tool invocation with status (pending→running→completed/error)
4. `FilePart` - File attachment
5. `SnapshotPart` - State snapshot
6. `PatchPart` - Code diff/patch
7. `StepPart` - Agent step marker
8. `AgentPart` - Agent invocation
9. `RetryPart` - Retry marker
10. `CompactionPart` - Compaction boundary
11. `SubtaskPart` - Subagent task

**Benefits**:
- Granular UI rendering
- State machine for tool execution
- Compaction boundary detection
- Rich audit trail

**Impact**: Limited observability and UI rendering capabilities.

**Effort**: ~2 weeks

---

### 6. MCP Integration (HIGH)
**Current State**: No Model Context Protocol support.

**opencode Implementation**:
- MCP server discovery and connection
- Stdio and HTTP/SSE transports
- Tool naming: `mcp__{server}__{tool}`
- OAuth authentication flow
- Server lifecycle management

**Impact**: Cannot integrate with ecosystem MCP servers for extended capabilities.

**Effort**: ~2-3 weeks

---

## Medium Priority Gaps

### 7. Event Bus System (MEDIUM)
**Current State**: Direct method calls between components.

**opencode Implementation**:
- Cross-module pub/sub
- Typed event payloads
- Batched event flushing (16ms cycle ~60fps)
- PartUpdated, SessionUpdated, etc.

**Impact**: Tight coupling between components, difficult to extend.

**Effort**: ~1 week

---

### 8. Hooks System (MEDIUM)
**Current State**: No pre/post execution hooks.

**opencode Implementation**:
- Pre-execution hooks (validation, logging)
- Post-execution hooks (metrics, learning)
- Pluggable hook registry
- Per-tool hook configuration

**Impact**: Limited extensibility and observability.

**Effort**: ~1 week

---

### 9. Custom Tool Loading (MEDIUM)
**Current State**: All tools are hardcoded in ToolRegistry.

**opencode Implementation**:
- Load from `.opencode/tool/*.{js,ts}`
- Runtime tool registration
- Tool validation
- Hot reload support

**Impact**: Users cannot extend tool capabilities without code changes.

**Effort**: ~1 week

---

### 10. Session Schema Enhancement (MEDIUM)
**Current State**: Basic session with ID, Name, ProjectId, ActiveAgentId.

**opencode Implementation**:
```typescript
Session.Info {
  id, slug, projectID, directory, parentID
  title, version
  time: {created, updated, compacting?, archived?}
  summary: {additions, deletions, files, diffs[]}
  share: {url?}
  permission: Ruleset
  revert: RevertInfo
}
```

**Impact**: Limited session metadata and management capabilities.

**Effort**: ~3-5 days

---

### 11. Tool Output Disk Spillover (MEDIUM)
**Current State**: Large outputs truncated in memory.

**opencode Implementation**:
- Spill large outputs to disk files
- Reference by path in message
- Cleanup on session close
- Configurable spill threshold

**Impact**: Memory pressure with large tool outputs.

**Effort**: ~3 days

---

## Low Priority Gaps

### 12. Cloud Storage Adapters (LOW)
**Current State**: Local SQLite + Qdrant only.

**opencode Implementation**:
- S3 adapter
- Cloudflare R2 adapter
- Abstract storage interface

**Impact**: Cannot sync sessions across devices.

**Effort**: ~1 week (per adapter)

---

## Recommended Implementation Order

### Phase 1: Foundation (Weeks 1-3)
1. **Permission System** - Security foundation
2. **Message Parts** - Required for subagents
3. **Event Bus** - Required for subagent communication

### Phase 2: Core Features (Weeks 4-6)
4. **Subagent System** - Critical capability
5. **Token Management Enhancement** - Context handling
6. **Agent Architecture** - Specialization

### Phase 3: Integration (Weeks 7-9)
7. **MCP Integration** - Ecosystem connectivity
8. **Hooks System** - Extensibility
9. **Custom Tool Loading** - User extensions

### Phase 4: Polish (Weeks 10-12)
10. **Session Schema** - Enhanced metadata
11. **Tool Output Spillover** - Memory optimization
12. **Cloud Storage** - Optional sync

---

## Architecture Alignment Recommendations

### Data Flow Alignment
```
Current (OpenFork):
User → ChatService → Provider → Tool → Response → SQLite

Target (opencode-aligned):
User → Agent → Session → LLM → ToolPart → Permission → Hook → Tool → PartUpdated → UI
                  ↓
           EventBus (pub/sub)
                  ↓
         Storage (parts, spillover)
```

### Domain Model Alignment
```csharp
// Current
public class Message { Role, Content, ToolCallsJson }

// Target
public class Message { Role, AgentId, ParentId, ModelId, SystemPrompt }
public abstract class MessagePart { MessageId, Type, CreatedAt }
public class TextPart : MessagePart { Content }
public class ToolPart : MessagePart { Status, Input, Output, Attachments }
public class CompactionPart : MessagePart { Summary }
// ... etc
```

### Service Layer Alignment
```csharp
// Add new services
IEventBus          // Cross-module communication
IPermissionService // Ruleset evaluation
IHookService       // Pre/post execution hooks
ISubagentService   // Child session management
ICompactionService // Context reduction
IMcpService        // MCP server management
```

---

## Technical Debt to Address

1. **ToolCallsJson String** → Typed ToolPart collection
2. **Direct SQLite** → Repository pattern with caching
3. **Hardcoded Tools** → Dynamic tool registry
4. **Synchronous File I/O** → Async throughout
5. **Single Thread** → Event-driven architecture

---

## Success Metrics

| Metric | Current | Target |
|--------|---------|--------|
| Agent Types | 1 (config) | 5+ specialized |
| Permission Levels | 0 | 3 (allow/deny/ask) |
| Message Part Types | 1 | 11 |
| Token Management Layers | 1 | 3 |
| Tool Count | 15 | 15 + MCP + Custom |
| Subagent Support | No | Yes |
| Event-Driven | No | Yes |

---

## Next Steps

1. Review and approve implementation order
2. Begin Phase 1 with Permission System
3. Set up integration tests for each component
4. Plan for backward compatibility during migration
