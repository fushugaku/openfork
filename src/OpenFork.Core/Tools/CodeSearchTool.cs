using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

/// <summary>
/// Search external documentation, libraries, SDKs and APIs using Exa Code API.
/// Different from search_project which searches the local codebase.
/// </summary>
public class CodeSearchTool : ITool
{
    private const string BaseUrl = "https://mcp.exa.ai";
    private const string SearchEndpoint = "/mcp";
    private const int DefaultTokens = 5000;
    private const int MinTokens = 1000;
    private const int MaxTokens = 50000;
    private const int TimeoutMs = 30000;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(TimeoutMs)
    };

    public string Name => "codesearch";

    public string Description => PromptLoader.Load("codesearch",
        "Search and get relevant context for programming tasks using Exa Code API. Returns code examples, documentation, and API references for libraries and SDKs.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "Search query to find relevant context for APIs, Libraries, and SDKs. For example, 'React useState hook examples', 'C# HttpClient async patterns', '.NET dependency injection'"
            },
            tokensNum = new
            {
                type = "integer",
                description = "Number of tokens to return (1000-50000). Default is 5000. Use lower values for focused queries, higher for comprehensive documentation."
            }
        },
        required = new[] { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<CodeSearchArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.Query))
                return new ToolResult(false, "Missing required parameter: query");

            var tokensNum = args.TokensNum ?? DefaultTokens;
            tokensNum = Math.Clamp(tokensNum, MinTokens, MaxTokens);

            var searchRequest = new McpCodeRequest
            {
                JsonRpc = "2.0",
                Id = 1,
                Method = "tools/call",
                Params = new McpCodeParams
                {
                    Name = "get_code_context_exa",
                    Arguments = new McpCodeArguments
                    {
                        Query = args.Query,
                        TokensNum = tokensNum
                    }
                }
            };

            var json = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{SearchEndpoint}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Accept", "application/json, text/event-stream");

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(TimeoutMs));
            using var response = await HttpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cts.Token);
                return new ToolResult(false, $"Code search error ({(int)response.StatusCode}): {errorText}");
            }

            var responseText = await response.Content.ReadAsStringAsync(cts.Token);

            var lines = responseText.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<McpCodeResponse>(line[6..], new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (data?.Result?.Content != null && data.Result.Content.Count > 0)
                        {
                            return new ToolResult(true, data.Result.Content[0].Text ?? "No content");
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return new ToolResult(true, "No code snippets or documentation found. Try a different query or be more specific about the library or programming concept.");
        }
        catch (TaskCanceledException)
        {
            return new ToolResult(false, "Code search request timed out");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error performing code search: {ex.Message}");
        }
    }

    private record CodeSearchArgs(
        [property: JsonPropertyName("query")] string? Query,
        [property: JsonPropertyName("tokensNum")] int? TokensNum
    );

    private class McpCodeRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        public McpCodeParams? Params { get; set; }
    }

    private class McpCodeParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("arguments")]
        public McpCodeArguments? Arguments { get; set; }
    }

    private class McpCodeArguments
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("tokensNum")]
        public int TokensNum { get; set; } = 5000;
    }

    private class McpCodeResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string? JsonRpc { get; set; }

        [JsonPropertyName("result")]
        public McpCodeResult? Result { get; set; }
    }

    private class McpCodeResult
    {
        [JsonPropertyName("content")]
        public List<McpContent>? Content { get; set; }
    }

    private class McpContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
