using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Chat;
using OpenFork.Core.Config;

namespace OpenFork.Providers;

public class OpenAiCompatibleProviderClient : IChatProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProvider _config;

    public OpenAiCompatibleProviderClient(HttpClient httpClient, OpenAiCompatibleProvider config)
    {
        _httpClient = httpClient;
        _config = config;

        if (string.IsNullOrWhiteSpace(_config.ApiUrl))
            throw new ArgumentException("ApiUrl is required for OpenAI-compatible provider", nameof(config));
    }

    public Task<IAsyncEnumerable<ChatStreamEvent>> StreamChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(StreamAsync(request, cancellationToken));
    }

    public async Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_config.ApiUrl.TrimEnd('/')}/chat/completions");

        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var payload = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                name = m.Name,
                tool_calls = m.ToolCalls?.Select(tc => new
                {
                    id = tc.Id,
                    type = tc.Type,
                    function = new
                    {
                        name = tc.Function.Name,
                        arguments = tc.Function.Arguments
                    }
                }),
                tool_call_id = m.ToolCallId
            }),
            stream = false,
            tools = request.Tools?.Select(t => new
            {
                type = t.Type,
                function = new
                {
                    name = t.Function.Name,
                    description = t.Function.Description,
                    parameters = t.Function.Parameters
                }
            })
        };

        var json = JsonSerializer.Serialize(payload);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentProp) ? contentProp.GetString() : null;

        return new ChatCompletionResponse
        {
            Choices = new List<ChatChoice>
            {
                new()
                {
                    Message = new ChatMessage
                    {
                        Role = message.GetProperty("role").GetString() ?? "assistant",
                        Content = content
                    }
                }
            }
        };
    }

    private async IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_config.ApiUrl.TrimEnd('/')}/chat/completions");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var payload = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                name = m.Name,
                tool_calls = m.ToolCalls?.Select(tc => new
                {
                    id = tc.Id,
                    type = tc.Type,
                    function = new
                    {
                        name = tc.Function.Name,
                        arguments = tc.Function.Arguments
                    }
                }),
                tool_call_id = m.ToolCallId
            }),
            stream = true,
            tools = request.Tools?.Select(t => new
            {
                type = t.Type,
                function = new
                {
                    name = t.Function.Name,
                    description = t.Function.Description,
                    parameters = t.Function.Parameters
                }
            })
        };

        var json = JsonSerializer.Serialize(payload);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line.Substring(6).Trim();
            if (data == "[DONE]")
            {
                yield return new ChatStreamEvent { IsDone = true };
                yield break;
            }

            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            var delta = choice.GetProperty("delta");
            var content = delta.TryGetProperty("content", out var contentProp) ? contentProp.GetString() : null;
            var finishReason = choice.TryGetProperty("finish_reason", out var finishProp) && finishProp.ValueKind != JsonValueKind.Null
                ? finishProp.GetString() : null;
            List<ToolCall>? toolCalls = null;

            if (delta.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                toolCalls = new List<ToolCall>();
                foreach (var toolCallElement in toolCallsElement.EnumerateArray())
                {
                    var id = toolCallElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var type = toolCallElement.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "function";

                    string? name = null;
                    string? arguments = null;

                    if (toolCallElement.TryGetProperty("function", out var functionElement))
                    {
                        name = functionElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                        arguments = functionElement.TryGetProperty("arguments", out var argsProp) ? argsProp.GetString() : null;
                    }

                    toolCalls.Add(new ToolCall
                    {
                        Id = id ?? string.Empty,
                        Type = type ?? "function",
                        Function = new ToolFunction
                        {
                            Name = name ?? string.Empty,
                            Arguments = arguments ?? string.Empty
                        }
                    });
                }
            }

            yield return new ChatStreamEvent
            {
                DeltaContent = content,
                DeltaToolCalls = toolCalls,
                FinishReason = finishReason
            };
        }
    }

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            return _config.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(_config.ApiKeyEnv))
        {
            return Environment.GetEnvironmentVariable(_config.ApiKeyEnv);
        }

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }
}
