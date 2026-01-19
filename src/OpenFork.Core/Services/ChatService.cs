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
    private readonly AgentService _agents;
    private readonly IPipelineRepository _pipelines;
    private readonly IProjectRepository _projects;
    private readonly IProviderResolver _providers;
    private readonly ToolRegistry _tools;
    private readonly ILogger<ChatService> _logger;
    private IHistoryProvider? _historyProvider;

    public ChatService(
        IMessageRepository messages,
        AgentService agents,
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
        _logger.LogInformation("Session ProjectId: {ProjectId}, Project found: {Found}, RootPath: {RootPath}",
            session.ProjectId, project != null, project?.RootPath ?? "(null)");
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

            // Retry loop for provider errors
            var retryAttempt = 0;
            var deltaContentBuilder = new System.Text.StringBuilder();
            var outputBuilder = new System.Text.StringBuilder(output);
            var toolCalls = new List<ToolCall>();
            var toolCallBuffers = new Dictionary<int, ToolCall>();
            var streamSucceeded = false;
            string? finishReason = null;

            while (!streamSucceeded && retryAttempt <= RetryHelper.MaxRetries)
            {
                try
                {
                    if (retryAttempt > 0)
                    {
                        var delay = RetryHelper.GetDelayMs(retryAttempt);
                        _logger.LogWarning("Retrying request (attempt {Attempt}/{Max}) after {Delay}ms...",
                            retryAttempt, RetryHelper.MaxRetries, delay);

                        if (onUpdate != null)
                            await onUpdate(new AgentStreamUpdate(agent.Name, $"\n⏳ Retrying ({retryAttempt}/{RetryHelper.MaxRetries})...\n", false));

                        await Task.Delay(delay, cancellationToken);

                        // Clear buffers for retry
                        deltaContentBuilder.Clear();
                        outputBuilder.Clear();
                        outputBuilder.Append(output);
                        toolCallBuffers.Clear();
                    }

                    var stream = await provider.StreamChatAsync(request, cancellationToken);

                    await foreach (var chunk in stream.WithCancellation(cancellationToken))
                    {
                        if (!string.IsNullOrEmpty(chunk.DeltaContent))
                        {
                            deltaContentBuilder.Append(chunk.DeltaContent);
                            outputBuilder.Append(chunk.DeltaContent);

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

                        // Capture finish reason if available
                        if (!string.IsNullOrEmpty(chunk.FinishReason))
                            finishReason = chunk.FinishReason;
                    }

                    streamSucceeded = true;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    retryAttempt++;

                    if (RetryHelper.IsRetryable(ex) && retryAttempt <= RetryHelper.MaxRetries)
                    {
                        var reason = RetryHelper.GetRetryReason(ex);
                        _logger.LogWarning(ex, "Retryable error: {Reason}. Will retry...", reason);
                        continue;
                    }

                    _logger.LogError(ex, "Non-retryable error or max retries exceeded");
                    throw;
                }
            }

            iteration++;

            // Check if response was cut off
            var deltaContent = deltaContentBuilder.ToString();
            if (RetryHelper.IsResponseIncomplete(finishReason, deltaContent))
            {
                _logger.LogWarning("Response appears incomplete (finishReason={FinishReason}), will continue...", finishReason);

                if (onUpdate != null)
                    await onUpdate(new AgentStreamUpdate(agent.Name, "\n⚠️ Response was truncated, continuing...\n", false));

                // Add the partial response and ask model to continue
                chatMessages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = deltaContent
                });
                chatMessages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = "Your response was cut off. Please continue from where you left off."
                });
                continue;
            }

            toolCalls = toolCallBuffers.Values.ToList();
            output = outputBuilder.ToString();

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

                _logger.LogInformation("Agent {AgentName} completed normally after {Iterations} iterations (no more tool calls)",
                    agent.Name, iteration);
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
                _logger.LogInformation("Tool call: {Tool}\nArgs: {Args}",
                    toolCall.Function.Name, toolCall.Function.Arguments);

                var result = await _tools.ExecuteAsync(toolCall.Function.Name, toolCall.Function.Arguments, context);

                // Log result
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

                // Notify UI via single callback - onToolExecution handles all display
                if (onToolExecution != null)
                    await onToolExecution(new ToolExecutionUpdate(toolCall.Function.Name, toolCall.Function.Arguments, result.Output, result.Success));

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

    /// <summary>
    /// Runs a subagent with the full agentic loop in isolation.
    /// Uses the Agent domain model with tool filtering.
    /// </summary>
    public async Task<SubagentResult> RunSubagentAsync(
        SubagentRequest request,
        CancellationToken cancellationToken)
    {
        var agent = request.Agent;
        var modelInfo = _providers.GetModelInfo(agent.ProviderId, agent.ModelId);
        var maxTokens = modelInfo?.MaxTokens ?? 128000;

        // Build initial messages
        var chatMessages = new List<ChatMessage>
        {
            new() { Role = "system", Content = agent.SystemPrompt },
            new() { Role = "user", Content = request.Prompt }
        };

        // Get filtered tools based on agent configuration
        var toolDefinitions = _tools.GetFilteredToolDefinitions(agent.Tools);

        var context = new ToolContext
        {
            WorkingDirectory = request.WorkingDirectory,
            FileChangeTracker = null,
            TodoTracker = null,
            AskUserAsync = null,
            GetDiagnosticsAsync = null
        };

        var outputBuilder = new System.Text.StringBuilder();
        var maxIterations = Math.Min(request.MaxIterations, agent.MaxIterations);
        var iteration = 0;
        var tokenThreshold = (int)(maxTokens * 0.85);

        _logger.LogInformation(
            "Starting subagent {AgentName} (slug={Slug}) with maxIterations={MaxIterations}, tools={ToolCount}",
            agent.Name, agent.Slug, maxIterations, toolDefinitions.Count);

        try
        {
            while (iteration < maxIterations)
            {
                // Check token usage and compact if needed
                var estimatedTokens = EstimateTokens(chatMessages);
                if (estimatedTokens > tokenThreshold)
                {
                    _logger.LogInformation(
                        "Subagent token usage ({Tokens}) exceeds threshold ({Threshold}), compacting",
                        estimatedTokens, tokenThreshold);

                    if (request.OnUpdate != null)
                        await request.OnUpdate(new AgentStreamUpdate(agent.Name, "\n📦 Compacting context...\n", false));

                    // Create a temporary AgentProfile for compaction
                    var tempProfile = new AgentProfile
                    {
                        Name = agent.Name,
                        SystemPrompt = agent.SystemPrompt,
                        ProviderKey = agent.ProviderId,
                        Model = agent.ModelId,
                        MaxIterations = maxIterations
                    };
                    chatMessages = await CompactMessagesAsync(chatMessages, tempProfile, maxTokens, cancellationToken);
                }

                var chatRequest = new ChatCompletionRequest
                {
                    Model = agent.ModelId,
                    Messages = chatMessages,
                    Tools = toolDefinitions,
                    Stream = true
                };

                var provider = _providers.Resolve(agent.ProviderId);
                var deltaContentBuilder = new System.Text.StringBuilder();
                var toolCalls = new List<ToolCall>();
                var toolCallBuffers = new Dictionary<int, ToolCall>();
                string? finishReason = null;

                // Retry loop
                var retryAttempt = 0;
                var streamSucceeded = false;

                while (!streamSucceeded && retryAttempt <= RetryHelper.MaxRetries)
                {
                    try
                    {
                        if (retryAttempt > 0)
                        {
                            var delay = RetryHelper.GetDelayMs(retryAttempt);
                            _logger.LogWarning(
                                "Subagent retrying (attempt {Attempt}/{Max}) after {Delay}ms...",
                                retryAttempt, RetryHelper.MaxRetries, delay);

                            if (request.OnUpdate != null)
                                await request.OnUpdate(new AgentStreamUpdate(
                                    agent.Name, $"\n⏳ Retrying ({retryAttempt}/{RetryHelper.MaxRetries})...\n", false));

                            await Task.Delay(delay, cancellationToken);
                            deltaContentBuilder.Clear();
                            toolCallBuffers.Clear();
                        }

                        var stream = await provider.StreamChatAsync(chatRequest, cancellationToken);

                        await foreach (var chunk in stream.WithCancellation(cancellationToken))
                        {
                            if (!string.IsNullOrEmpty(chunk.DeltaContent))
                            {
                                deltaContentBuilder.Append(chunk.DeltaContent);
                                outputBuilder.Append(chunk.DeltaContent);

                                if (request.OnUpdate != null)
                                    await request.OnUpdate(new AgentStreamUpdate(
                                        agent.Name, chunk.DeltaContent, false));
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

                            if (!string.IsNullOrEmpty(chunk.FinishReason))
                                finishReason = chunk.FinishReason;
                        }

                        streamSucceeded = true;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        retryAttempt++;

                        if (RetryHelper.IsRetryable(ex) && retryAttempt <= RetryHelper.MaxRetries)
                        {
                            _logger.LogWarning(ex, "Subagent retryable error: {Reason}", RetryHelper.GetRetryReason(ex));
                            continue;
                        }

                        _logger.LogError(ex, "Subagent non-retryable error or max retries exceeded");
                        throw;
                    }
                }

                iteration++;

                // Check for truncated response
                var deltaContent = deltaContentBuilder.ToString();
                if (RetryHelper.IsResponseIncomplete(finishReason, deltaContent))
                {
                    _logger.LogWarning("Subagent response truncated (finishReason={FinishReason})", finishReason);

                    if (request.OnUpdate != null)
                        await request.OnUpdate(new AgentStreamUpdate(
                            agent.Name, "\n⚠️ Response truncated, continuing...\n", false));

                    chatMessages.Add(new ChatMessage { Role = "assistant", Content = deltaContent });
                    chatMessages.Add(new ChatMessage
                    {
                        Role = "user",
                        Content = "Your response was cut off. Please continue from where you left off."
                    });
                    continue;
                }

                toolCalls = toolCallBuffers.Values.ToList();

                // No tool calls means completion
                if (toolCalls.Count == 0)
                {
                    if (request.OnUpdate != null)
                        await request.OnUpdate(new AgentStreamUpdate(agent.Name, string.Empty, true));

                    _logger.LogInformation(
                        "Subagent {AgentName} completed after {Iterations} iterations",
                        agent.Name, iteration);

                    return new SubagentResult(
                        Success: true,
                        Output: outputBuilder.ToString(),
                        IterationsUsed: iteration);
                }

                // Add assistant message with tool calls
                chatMessages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = deltaContent.Length > 0 ? deltaContent : null,
                    ToolCalls = toolCalls
                });

                // Execute tool calls
                foreach (var toolCall in toolCalls)
                {
                    _logger.LogInformation(
                        "Subagent tool call: {Tool}\nArgs: {Args}",
                        toolCall.Function.Name, toolCall.Function.Arguments);

                    var result = await _tools.ExecuteAsync(toolCall.Function.Name, toolCall.Function.Arguments, context);

                    if (result.Success)
                    {
                        _logger.LogInformation("Subagent tool {Tool} succeeded", toolCall.Function.Name);
                    }
                    else
                    {
                        _logger.LogError("Subagent tool {Tool} failed: {Output}", toolCall.Function.Name, result.Output);
                    }

                    if (request.OnToolExecution != null)
                        await request.OnToolExecution(new ToolExecutionUpdate(
                            toolCall.Function.Name, toolCall.Function.Arguments, result.Output, result.Success));

                    var truncatedOutput = TruncateToolOutput(toolCall.Function.Name, result.Output);

                    chatMessages.Add(new ChatMessage
                    {
                        Role = "tool",
                        ToolCallId = toolCall.Id,
                        Content = truncatedOutput
                    });
                }
            }

            // Reached max iterations
            _logger.LogWarning(
                "Subagent {AgentName} reached max iterations ({MaxIterations}) without completing",
                agent.Name, maxIterations);

            if (request.OnUpdate != null)
            {
                await request.OnUpdate(new AgentStreamUpdate(
                    agent.Name, $"\n\n⚠️ Max iterations ({maxIterations}) reached.", false));
                await request.OnUpdate(new AgentStreamUpdate(agent.Name, string.Empty, true));
            }

            return new SubagentResult(
                Success: true,
                Output: outputBuilder.ToString(),
                IterationsUsed: iteration);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Subagent {AgentName} cancelled", agent.Name);
            return new SubagentResult(
                Success: false,
                Output: outputBuilder.ToString(),
                IterationsUsed: iteration,
                Error: "Operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subagent {AgentName} failed with error", agent.Name);
            return new SubagentResult(
                Success: false,
                Output: outputBuilder.ToString(),
                IterationsUsed: iteration,
                Error: ex.Message);
        }
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
        var systemMessage = messages.FirstOrDefault(m => m.Role == "system");
        var targetTokens = (int)(maxTokens * 0.5); // Target 50% capacity after compaction

        // Phase 1: Prune old tool outputs (keep structure, reduce content)
        var prunedMessages = PruneToolOutputs(messages, targetTokens);
        var currentTokens = EstimateTokens(prunedMessages);

        if (currentTokens <= targetTokens)
        {
            _logger.LogInformation("Pruning sufficient: {Before} -> {After} tokens", EstimateTokens(messages), currentTokens);
            return prunedMessages;
        }

        // Phase 2: Generate summary of older messages using LLM
        _logger.LogInformation("Pruning insufficient ({Tokens} > {Target}), generating summary...", currentTokens, targetTokens);

        var recentMessages = new List<ChatMessage>();
        var olderMessages = new List<ChatMessage>();
        var recentTokens = 0;
        var protectedTokens = (int)(targetTokens * 0.6); // Keep 60% of target for recent messages

        // Keep recent messages
        for (var i = prunedMessages.Count - 1; i >= 0; i--)
        {
            var msg = prunedMessages[i];
            if (msg.Role == "system") continue;

            var msgTokens = EstimateMessageTokens(msg);
            if (recentTokens + msgTokens > protectedTokens && recentMessages.Count > 2)
            {
                olderMessages = prunedMessages.Skip(systemMessage != null ? 1 : 0).Take(i + 1).ToList();
                break;
            }

            recentMessages.Insert(0, msg);
            recentTokens += msgTokens;
        }

        // Generate summary if we have older messages to summarize
        string? summary = null;
        if (olderMessages.Count > 0)
        {
            summary = await GenerateSummaryAsync(olderMessages, agent, cancellationToken);
        }

        // Build result
        var result = new List<ChatMessage>();
        if (systemMessage != null)
            result.Add(systemMessage);

        if (!string.IsNullOrEmpty(summary))
        {
            result.Add(new ChatMessage
            {
                Role = "system",
                Content = $"[CONVERSATION SUMMARY - {olderMessages.Count} messages compacted]\n{summary}\n[END SUMMARY - Continue from recent context below]"
            });
        }

        result.AddRange(recentMessages);

        _logger.LogInformation("Compacted {OlderCount} messages into summary, keeping {RecentCount} recent (~{Tokens} tokens)",
            olderMessages.Count, recentMessages.Count, EstimateTokens(result));

        return result;
    }

    private List<ChatMessage> PruneToolOutputs(List<ChatMessage> messages, int targetTokens)
    {
        const int maxToolOutputLength = 2000; // Limit tool outputs to ~500 tokens
        const int minMessagesToKeep = 10;

        var result = new List<ChatMessage>();
        var totalMessages = messages.Count;

        for (var i = 0; i < totalMessages; i++)
        {
            var msg = messages[i];
            var isRecent = i >= totalMessages - minMessagesToKeep;

            if (msg.Role == "tool" && !isRecent && !string.IsNullOrEmpty(msg.Content) && msg.Content.Length > maxToolOutputLength)
            {
                // Prune old tool outputs
                result.Add(new ChatMessage
                {
                    Role = msg.Role,
                    ToolCallId = msg.ToolCallId,
                    Content = msg.Content[..maxToolOutputLength] + "\n... [output truncated for context efficiency]"
                });
            }
            else
            {
                result.Add(msg);
            }
        }

        return result;
    }

    private async Task<string> GenerateSummaryAsync(List<ChatMessage> messages, AgentProfile agent, CancellationToken cancellationToken)
    {
        try
        {
            var conversationText = new System.Text.StringBuilder();
            foreach (var msg in messages.Take(50)) // Limit messages to summarize
            {
                var role = msg.Role.ToUpperInvariant();
                var content = msg.Content?.Length > 500 ? msg.Content[..500] + "..." : msg.Content;
                conversationText.AppendLine($"{role}: {content}");
            }

            var summaryPrompt = $@"Provide a concise summary of this conversation for context continuity. Focus on:
- What tasks were requested and completed
- Key files that were read, created, or modified
- Important decisions or findings
- Current state and what needs to be done next

Conversation to summarize:
{conversationText}

Summary:";

            var request = new ChatCompletionRequest
            {
                Model = agent.Model,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = "You are a helpful assistant that creates concise conversation summaries. Be brief but include all important context." },
                    new() { Role = "user", Content = summaryPrompt }
                },
                Stream = false
            };

            var provider = _providers.Resolve(agent.ProviderKey);
            var response = await provider.ChatAsync(request, cancellationToken);

            if (response?.Choices == null || response.Choices.Count == 0)
                return "[Summary generation failed]";

            return response.Choices[0].Message?.Content ?? "[Summary generation failed]";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate summary, using fallback");
            return $"[{messages.Count} earlier messages - summary unavailable]";
        }
    }

    private static int EstimateMessageTokens(ChatMessage msg)
    {
        var tokens = (msg.Content?.Length ?? 0) / 4;
        if (msg.ToolCalls != null)
        {
            foreach (var tc in msg.ToolCalls)
                tokens += (tc.Function.Name.Length + tc.Function.Arguments.Length) / 4;
        }
        return tokens;
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

public record ToolExecutionUpdate(string ToolName, string Input, string Output, bool Success);

/// <summary>
/// Request for running a subagent in isolation.
/// </summary>
public class SubagentRequest
{
    public required Agent Agent { get; init; }
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public int MaxIterations { get; init; } = 10;
    public Func<AgentStreamUpdate, Task>? OnUpdate { get; init; }
    public Func<ToolExecutionUpdate, Task>? OnToolExecution { get; init; }
}

/// <summary>
/// Result from a subagent execution.
/// </summary>
public record SubagentResult(bool Success, string Output, int IterationsUsed, string? Error = null);

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
