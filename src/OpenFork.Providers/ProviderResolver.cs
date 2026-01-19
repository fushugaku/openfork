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

            // Multiple providers exist but no key specified
            var available = string.Join(", ", _providers.Keys.OrderBy(k => k));
            throw new InvalidOperationException($"No provider key specified. Available providers: {available}. Please specify a provider key.");
        }

        if (_providers.TryGetValue(providerKey, out var provider))
        {
            return provider;
        }

        var availableKeys = string.Join(", ", _providers.Keys.OrderBy(k => k));
        throw new InvalidOperationException($"Provider not found: '{providerKey}'. Available: {availableKeys}");
    }

    public ModelInfo? GetModelInfo(string providerKey, string model)
    {
        if (!_settings.OpenAiCompatible.TryGetValue(providerKey, out var provider))
            return null;

        return provider.AvailableModels.FirstOrDefault(m =>
            string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase));
    }

    public (IChatProvider Provider, string ProviderKey, ModelInfo Model)? ResolveByModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return null;

        // Search all providers for a model with the given name
        foreach (var (providerKey, config) in _settings.OpenAiCompatible)
        {
            var model = config.AvailableModels.FirstOrDefault(m =>
                string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase));

            if (model != null && _providers.TryGetValue(providerKey, out var provider))
            {
                return (provider, providerKey, model);
            }
        }

        return null;
    }
}
