using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Chat;
using OpenFork.Core.Domain;
using OpenFork.Core.Tools;

namespace OpenFork.Core.Services;

public interface IHistoryProvider
{
    Task StoreMessageAsync(long sessionId, long messageId, string role, string content, DateTimeOffset createdAt, CancellationToken ct = default);
    Task<List<HistoryEntry>> GetContextAsync(long sessionId, string currentQuery, int maxTokens, string providerKey, string model, CancellationToken ct = default);
}

public class HistoryEntry
{
    public long MessageId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ChatService
{
    private readonly IMessageRepository _messages;
    private readonly IAgentRepository _agents;
    private readonly IPipelineRepository _pipelines;
    private readonly IProjectRepository _projects;
    private readonly IProviderResolver _providers;
    private readonly ToolRegistry _tools;
    private readonly ILogger<ChatService> _logger;
    private IHistoryProvider? _historyProvider;

    public ChatService(
        IMessageRepository messages,
        IAgentRepository agents,
        IPipelineRepository pipelines,
        IProjectRepository projects,
        IProviderResolver providers,
        ToolRegistry tools,
        ILogger<ChatService> logger)
    {
        _messages = messages;
        _agents = agents;
        _pipelines = pipelines;
        _projects = projects;
        _providers = providers;
        _tools = tools;
        _logger = logger;
    }

    public void SetHistoryProvider(IHistoryProvider provider)
    {
        _historyProvider = provider;
    }

    public async Task<List<Message>> ListMessagesAsync(long sessionId)
    {
        return await _messages.ListBySessionAsync(sessionId);
    }

    public async Task<List<AgentRunResult>> RunAsync(
        Session session, 
        string userInput, 
        CancellationToken cancellationToken, 
        Func<AgentStreamUpdate, Task>? onUpdate = null, 
        FileChangeTracker? fileChangeTracker = null, 
        TodoTracker? todoTracker = null,
        Func<QuestionRequest, Task<List<QuestionAnswer>>>? askUserAsync = null,
        Func<string[], Task<List<Diagnostic>>>? getDiagnosticsAsync = null,
        Func<ToolExecutionUpdate, Task>? onToolExecution = null)
    {
        var userMessage = new Message
        {
            SessionId = session.Id,
            Role = "user",
            Content = userInput,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _messages.AddAsync(userMessage);

        if (_historyProvider != null)
        {
            await _historyProvider.StoreMessageAsync(
                session.Id, userMessage.Id, userMessage.Role,
                userMessage.Content, userMessage.CreatedAt, cancellationToken);
        }

        var project = await _projects.GetAsync(session.ProjectId);
        var workingDir = project?.RootPath ?? Environment.CurrentDirectory;
        _logger.LogInformation("Tool context working directory: {WorkingDir}", workingDir);

        PromptLoader.SetWorkingDirectory(workingDir);

        var context = new ToolContext
        {
            WorkingDirectory = workingDir,
            FileChangeTracker = fileChangeTracker,
            TodoTracker = todoTracker,
            AskUserAsync = askUserAsync,
            GetDiagnosticsAsync = getDiagnosticsAsync
        };

        if (session.ActivePipelineId.HasValue)
        {
            return await RunPipelineAsync(session, userInput, session.ActivePipelineId.Value, context, cancellationToken, onUpdate, onToolExecution);
        }

        if (session.ActiveAgentId.HasValue)
        {
            var agent = await _agents.GetAsync(session.ActiveAgentId.Value);
            if (agent != null)
            {
                _logger.LogInformation("Loaded agent {AgentName} (ID={AgentId}) with MaxIterations={MaxIterations}", agent.Name, agent.Id, agent.MaxIterations);
                var result = await RunAgentAsync(session, userInput, agent, null, context, cancellationToken, onUpdate, onToolExecution);
                return new List<AgentRunResult> { result };
            }
        }

        return new List<AgentRunResult>();
    }

    private async Task<List<AgentRunResult>> RunPipelineAsync(
        Session session, 
        string userInput, 
        long pipelineId, 
        ToolContext context, 
        CancellationToken cancellationToken, 
        Func<AgentStreamUpdate, Task>? onUpdate,
        Func<ToolExecutionUpdate, Task>? onToolExecution)
    {
        var steps = await _pipelines.ListStepsAsync(pipelineId);
        var results = new List<AgentRunResult>();
        string? lastOutput = null;

        foreach (var step in steps.OrderBy(s => s.OrderIndex))
        {
            var agent = await _agents.GetAsync(step.AgentId);
            if (agent == null) continue;

            _logger.LogInformation("Loaded agent {AgentName} (ID={AgentId}) with MaxIterations={MaxIterations}", agent.Name, agent.Id, agent.MaxIterations);
            var result = await RunAgentAsync(session, userInput, agent, step, context, cancellationToken, onUpdate, onToolExecution, lastOutput);
            lastOutput = result.Output;
            results.Add(result);
        }

        return results;
    }

    private async Task<AgentRunResult> RunAgentAsync(
        Session session,
        string userInput,
        AgentProfile agent,
        PipelineStep? step,
        ToolContext context,
        CancellationToken cancellationToken,
        Func<AgentStreamUpdate, Task>? onUpdate,
        Func<ToolExecutionUpdate, Task>? onToolExecution,
        string? priorOutput = null)
    {
        var modelInfo = _providers.GetModelInfo(agent.ProviderKey, agent.Model);
        var maxTokens = modelInfo?.MaxTokens ?? 128000;

        List<ChatMessage> chatMessages;

        if (_historyProvider != null && step?.HandoffMode != "last")
        {
            var historyEntries = await _historyProvider.GetContextAsync(
                session.Id, userInput, maxTokens, agent.ProviderKey, agent.Model, cancellationToken);

            chatMessages = BuildMessagesFromHistory(agent, userInput, historyEntries, priorOutput);
        }
        else
        {
            var allMessages = await _messages.ListBySessionAsync(session.Id);
            chatMessages = BuildMessages(agent, userInput, allMessages, step?.HandoffMode, priorOutput);
        }

        var output = "";
        var maxIterations = agent.MaxIterations;
        var iteration = 0;
        var tokenThreshold = (int)(maxTokens * 0.85); // Compact at 85% capacity

        _logger.LogInformation("Starting agent loop for {AgentName} with maxIterations={MaxIterations} (0=unlimited)", agent.Name, maxIterations);

        while (maxIterations == 0 || iteration < maxIterations)
        {
            // Check token usage and compact if needed
            var estimatedTokens = EstimateTokens(chatMessages);
            if (estimatedTokens > tokenThreshold)
            {
                _logger.LogInformation("Token usage ({Tokens}) exceeds threshold ({Threshold}), compacting history", 
                    estimatedTokens, tokenThreshold);
                
                if (onUpdate != null)
                    await onUpdate(new AgentStreamUpdate(agent.Name, "\n📦 Compacting context...\n", false));

                chatMessages = await CompactMessagesAsync(chatMessages, agent, maxTokens, cancellationToken);
            }

            var request = new ChatCompletionRequest
            {
                Model = agent.Model,
                Messages = chatMessages,
                Tools = _tools.GetToolDefinitions(),
                Stream = true
            };

            var provider = _providers.Resolve(agent.ProviderKey);
            var stream = await provider.StreamChatAsync(request, cancellationToken);
            iteration++;

            var deltaContent = "";
            var toolCalls = new List<ToolCall>();
            var toolCallBuffers = new Dictionary<int, ToolCall>();

            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.DeltaContent))
                {
                    deltaContent += chunk.DeltaContent;
                    output += chunk.DeltaContent;

                    if (onUpdate != null)
                        await onUpdate(new AgentStreamUpdate(agent.Name, chunk.DeltaContent, false));
                }

                if (chunk.DeltaToolCalls != null)
                {
                    foreach (var tc in chunk.DeltaToolCalls)
                    {
                        if (!string.IsNullOrEmpty(tc.Id))
                        {
                            var idx = toolCallBuffers.Count;
                            toolCallBuffers[idx] = new ToolCall
                            {
                                Id = tc.Id,
                                Type = tc.Type,
                                Function = new ToolFunction
                                {
                                    Name = tc.Function.Name,
                                    Arguments = tc.Function.Arguments
                                }
                            };
                        }
                        else if (toolCallBuffers.Count > 0)
                        {
                            var lastIdx = toolCallBuffers.Count - 1;
                            toolCallBuffers[lastIdx].Function.Arguments += tc.Function.Arguments;
                        }
                    }
                }
            }

            toolCalls = toolCallBuffers.Values.ToList();

            if (toolCalls.Count == 0)
            {
                if (onUpdate != null)
                    await onUpdate(new AgentStreamUpdate(agent.Name, string.Empty, true));

                var assistantMessage = new Message
                {
                    SessionId = session.Id,
                    AgentId = agent.Id,
                    PipelineStepId = step?.Id,
                    Role = "assistant",
                    Content = output,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await _messages.AddAsync(assistantMessage);

                if (_historyProvider != null)
                {
                    await _historyProvider.StoreMessageAsync(
                        session.Id, assistantMessage.Id, assistantMessage.Role,
                        assistantMessage.Content, assistantMessage.CreatedAt, cancellationToken);
                }

                return new AgentRunResult(agent.Name, output);
            }

            chatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = deltaContent.Length > 0 ? deltaContent : null,
                ToolCalls = toolCalls
            });

            foreach (var toolCall in toolCalls)
            {
                _logger.LogInformation("Tool call: {Tool} with args: {Args}",
                    toolCall.Function.Name, toolCall.Function.Arguments);

                // Only send status updates for non-read tools (read tools are rendered as grouped panels)
                var shouldShowStatus = toolCall.Function.Name != "read";

                if (shouldShowStatus && onUpdate != null)
                    await onUpdate(new AgentStreamUpdate(agent.Name, $"\n⚡ {toolCall.Function.Name}", false));

                _logger.LogInformation("Tool call: {Tool}\nArgs: {Args}",
                    toolCall.Function.Name, toolCall.Function.Arguments);

                var result = await _tools.ExecuteAsync(toolCall.Function.Name, toolCall.Function.Arguments, context);

                if (onToolExecution != null)
                    await onToolExecution(new ToolExecutionUpdate(toolCall.Function.Name, result.Output, result.Success));

                if (shouldShowStatus)
                {
                    if (result.Success)
                    {
                        _logger.LogInformation("Tool {Tool} succeeded: {Output}",
                            toolCall.Function.Name, result.Output.Truncate(500));
                        if (onUpdate != null)
                            await onUpdate(new AgentStreamUpdate(agent.Name, " ✓\n", false));
                    }
                    else
                    {
                        _logger.LogError("Tool {Tool} failed: {Output}",
                            toolCall.Function.Name, result.Output);
                        if (onUpdate != null)
                            await onUpdate(new AgentStreamUpdate(agent.Name, $" ✗ {result.Output.Truncate(100)}\n", false));
                    }
                }
                else
                {
                    // Still log for non-status tools
                    if (result.Success)
                    {
                        _logger.LogInformation("Tool {Tool} succeeded: {Output}",
                            toolCall.Function.Name, result.Output.Truncate(500));
                    }
                    else
                    {
                        _logger.LogError("Tool {Tool} failed: {Output}",
                            toolCall.Function.Name, result.Output);
                    }
                }

                // Truncate very long tool outputs to keep context manageable
                var truncatedOutput = TruncateToolOutput(toolCall.Function.Name, result.Output);
                
                chatMessages.Add(new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = truncatedOutput
                });
            }
        }

        if (maxIterations > 0)
        {
            _logger.LogWarning("Agent {AgentName} reached max iterations ({MaxIterations}) without completing", agent.Name, maxIterations);
            
            if (onUpdate != null)
            {
                await onUpdate(new AgentStreamUpdate(agent.Name, $"\n\n⚠️ Max iterations ({maxIterations}) reached. Task may be incomplete.", false));
                await onUpdate(new AgentStreamUpdate(agent.Name, string.Empty, true));
            }
        }

        return new AgentRunResult(agent.Name, output);
    }

    private static string TruncateToolOutput(string toolName, string output)
    {
        // Define max lengths per tool type
        var maxLength = toolName switch
        {
            "read" => 4000,           // File content - keep reasonable amount
            "bash" => 2000,           // Command output - often verbose
            "diagnostics" => 1500,    // Build errors - can be very verbose
            "grep" => 3000,           // Search results
            "glob" => 2000,           // File lists
            "list" => 2000,           // Directory trees
            "webfetch" => 3000,       // Web content
            "websearch" => 2000,      // Search results
            "codesearch" => 4000,     // Code documentation
            "search_project" => 3000, // Semantic search
            "lsp" => 2000,            // LSP results
            _ => 5000                 // Default max
        };

        if (output.Length <= maxLength)
            return output;

        // Truncate and add indicator
        var truncated = output[..maxLength];
        var lines = truncated.Split('\n');
        var removedChars = output.Length - maxLength;
        var estimatedRemovedLines = (output.Length - maxLength) / 80; // Estimate lines removed
        
        return truncated + $"\n\n... (truncated {removedChars} characters, ~{estimatedRemovedLines} lines for context efficiency)";
    }

    private static int EstimateTokens(List<ChatMessage> messages)
    {
        // Rough estimate: ~4 characters per token for English text
        var totalChars = 0;
        foreach (var msg in messages)
        {
            totalChars += msg.Content?.Length ?? 0;
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                {
                    totalChars += tc.Function.Name.Length;
                    totalChars += tc.Function.Arguments.Length;
                }
            }
        }
        return totalChars / 4;
    }

    private async Task<List<ChatMessage>> CompactMessagesAsync(
        List<ChatMessage> messages, 
        AgentProfile agent, 
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // Keep system message and last N messages, summarize the rest
        var systemMessage = messages.FirstOrDefault(m => m.Role == "system");
        var targetTokens = maxTokens / 2; // Target 50% capacity after compaction
        
        // Start from end and work backwards, keeping messages until we hit target
        var keptMessages = new List<ChatMessage>();
        var tokensUsed = 0;
        
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role == "system") continue;
            
            var msgTokens = (msg.Content?.Length ?? 0) / 4;
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                    msgTokens += (tc.Function.Name.Length + tc.Function.Arguments.Length) / 4;
            }
            
            if (tokensUsed + msgTokens > targetTokens && keptMessages.Count > 4)
                break;
                
            keptMessages.Insert(0, msg);
            tokensUsed += msgTokens;
        }
        
        // Build summary of discarded messages
        var discardedCount = messages.Count - keptMessages.Count - (systemMessage != null ? 1 : 0);
        
        var result = new List<ChatMessage>();
        if (systemMessage != null)
            result.Add(systemMessage);
            
        if (discardedCount > 0)
        {
            result.Add(new ChatMessage
            {
                Role = "system",
                Content = $"[Context compacted: {discardedCount} earlier messages summarized to save tokens. Continue from recent context.]"
            });
        }
        
        result.AddRange(keptMessages);
        
        _logger.LogInformation("Compacted {Discarded} messages, keeping {Kept} messages (~{Tokens} tokens)",
            discardedCount, keptMessages.Count, tokensUsed);
        
        return result;
    }

    private static List<ChatMessage> BuildMessagesFromHistory(AgentProfile agent, string userInput, List<HistoryEntry> history, string? priorOutput)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = agent.SystemPrompt }
        };

        foreach (var entry in history)
        {
            messages.Add(new ChatMessage
            {
                Role = entry.Role,
                Content = entry.Content
            });
        }

        if (!string.IsNullOrEmpty(priorOutput))
        {
            messages.Add(new ChatMessage { Role = "assistant", Content = priorOutput });
        }

        if (history.LastOrDefault()?.Role != "user")
        {
            messages.Add(new ChatMessage { Role = "user", Content = userInput });
        }

        return messages;
    }

    private static List<ChatMessage> BuildMessages(AgentProfile agent, string userInput, List<Message> history, string? handoffMode, string? priorOutput)
    {
        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = agent.SystemPrompt
            }
        };

        if (string.Equals(handoffMode, "last", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(new ChatMessage { Role = "user", Content = userInput });
            if (!string.IsNullOrEmpty(priorOutput))
                messages.Add(new ChatMessage { Role = "assistant", Content = priorOutput });
            return messages;
        }

        foreach (var message in history)
        {
            messages.Add(new ChatMessage
            {
                Role = message.Role,
                Content = message.Content
            });
        }

        if (history.LastOrDefault()?.Role != "user")
            messages.Add(new ChatMessage { Role = "user", Content = userInput });

        return messages;
    }
}

public record AgentRunResult(string AgentName, string Output);

public record AgentStreamUpdate(string AgentName, string Delta, bool IsDone);

public record ToolExecutionUpdate(string ToolName, string Output, bool Success);

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
