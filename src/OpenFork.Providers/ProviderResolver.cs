using OpenFork.Core.Abstractions;
using OpenFork.Core.Config;

namespace OpenFork.Providers;

public class ProviderResolver : IProviderResolver
{
    private readonly Dictionary<string, IChatProvider> _providers;
    private readonly AppSettings _settings;

    public ProviderResolver(AppSettings settings, IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _providers = new Dictionary<string, IChatProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in settings.OpenAiCompatible)
        {
            var key = entry.Key;
            var config = entry.Value;
            var httpClient = httpClientFactory.CreateClient($"openai:{key}");
            _providers[key] = new OpenAiCompatibleProviderClient(httpClient, config);
        }
    }

    public IChatProvider Resolve(string providerKey)
    {
        if (_providers.Count == 0)
        {
            throw new InvalidOperationException("No providers configured. Check config/appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            if (_providers.Count == 1)
            {
                return _providers.Values.First();
            }
        }

        if (_providers.TryGetValue(providerKey, out var provider))
        {
            return provider;
        }

        var available = string.Join(", ", _providers.Keys.OrderBy(k => k));
        throw new InvalidOperationException($"Provider not found: {providerKey}. Available: {available}");
    }

    public ModelInfo? GetModelInfo(string providerKey, string model)
    {
        if (!_settings.OpenAiCompatible.TryGetValue(providerKey, out var provider))
            return null;

        return provider.AvailableModels.FirstOrDefault(m =>
            string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase));
    }
}
