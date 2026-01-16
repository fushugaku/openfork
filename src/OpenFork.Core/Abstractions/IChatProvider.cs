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
}
