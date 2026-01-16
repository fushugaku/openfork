using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenFork.Search.Config;

namespace OpenFork.Search.Services;

public class EmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly SearchConfig _config;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(HttpClient httpClient, SearchConfig config, ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<float[]?> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embeddings = await GetEmbeddingsAsync(new[] { text }, cancellationToken);
        return embeddings?.FirstOrDefault();
    }

    public async Task<List<float[]>?> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
            return new List<float[]>();

        var results = new List<float[]>();
        
        foreach (var text in textList)
        {
            var request = new OllamaEmbedRequest
            {
                Model = _config.EmbeddingModel,
                Input = text
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_config.OllamaUrl.TrimEnd('/')}/api/embed",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Embedding request failed with status {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken);
            if (result?.Embeddings == null || result.Embeddings.Count == 0)
            {
                _logger.LogWarning("No embeddings returned from Ollama");
                return null;
            }

            results.Add(result.Embeddings[0]);
        }

        return results;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_config.OllamaUrl.TrimEnd('/')}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;
    }

    private class OllamaEmbedResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}
