using OpenFork.Core.Chat;
using OpenFork.Core.Config;

namespace OpenFork.Core.Abstractions;

public interface IChatProvider
{
    Task<IAsyncEnumerable<ChatStreamEvent>> StreamChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken);
    Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken);
}

public interface IProviderResolver
{
    IChatProvider Resolve(string providerKey);
    ModelInfo? GetModelInfo(string providerKey, string model);

    /// <summary>
    /// Resolves provider and model info by model name.
    /// Searches all configured providers for a model with the given name.
    /// </summary>
    (IChatProvider Provider, string ProviderKey, ModelInfo Model)? ResolveByModel(string modelName);
}
