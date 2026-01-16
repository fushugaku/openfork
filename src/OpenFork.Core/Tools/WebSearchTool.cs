using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class WebSearchTool : ITool
{
    private const string BaseUrl = "https://mcp.exa.ai";
    private const string SearchEndpoint = "/mcp";
    private const int DefaultNumResults = 8;
    private const int TimeoutMs = 25000;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(TimeoutMs)
    };

    public string Name => "websearch";

    public string Description => PromptLoader.Load("websearch",
        "Search the web using Exa AI for real-time information and current events.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "Websearch query"
            },
            numResults = new
            {
                type = "integer",
                description = "Number of search results to return (default: 8)"
            },
            livecrawl = new
            {
                type = "string",
                @enum = new[] { "fallback", "preferred" },
                description = "Live crawl mode - 'fallback': use live crawling as backup, 'preferred': prioritize live crawling"
            },
            type = new
            {
                type = "string",
                @enum = new[] { "auto", "fast", "deep" },
                description = "Search type - 'auto': balanced (default), 'fast': quick results, 'deep': comprehensive"
            },
            contextMaxCharacters = new
            {
                type = "integer",
                description = "Maximum characters for context string (default: 10000)"
            }
        },
        required = new[] { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<WebSearchArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.Query))
                return new ToolResult(false, "Missing required parameter: query");

            var searchRequest = new McpSearchRequest
            {
                JsonRpc = "2.0",
                Id = 1,
                Method = "tools/call",
                Params = new McpSearchParams
                {
                    Name = "web_search_exa",
                    Arguments = new McpSearchArguments
                    {
                        Query = args.Query,
                        Type = args.Type ?? "auto",
                        NumResults = args.NumResults ?? DefaultNumResults,
                        Livecrawl = args.Livecrawl ?? "fallback",
                        ContextMaxCharacters = args.ContextMaxCharacters
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
                return new ToolResult(false, $"Search error ({(int)response.StatusCode}): {errorText}");
            }

            var responseText = await response.Content.ReadAsStringAsync(cts.Token);

            var lines = responseText.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<McpSearchResponse>(line[6..], new JsonSerializerOptions
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

            return new ToolResult(true, "No search results found. Please try a different query.");
        }
        catch (TaskCanceledException)
        {
            return new ToolResult(false, "Search request timed out");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error performing search: {ex.Message}");
        }
    }

    private record WebSearchArgs(
        [property: JsonPropertyName("query")] string? Query,
        [property: JsonPropertyName("numResults")] int? NumResults,
        [property: JsonPropertyName("livecrawl")] string? Livecrawl,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("contextMaxCharacters")] int? ContextMaxCharacters
    );

    private class McpSearchRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        public McpSearchParams? Params { get; set; }
    }

    private class McpSearchParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("arguments")]
        public McpSearchArguments? Arguments { get; set; }
    }

    private class McpSearchArguments
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "auto";

        [JsonPropertyName("numResults")]
        public int NumResults { get; set; } = 8;

        [JsonPropertyName("livecrawl")]
        public string Livecrawl { get; set; } = "fallback";

        [JsonPropertyName("contextMaxCharacters")]
        public int? ContextMaxCharacters { get; set; }
    }

    private class McpSearchResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string? JsonRpc { get; set; }

        [JsonPropertyName("result")]
        public McpSearchResult? Result { get; set; }
    }

    private class McpSearchResult
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
