namespace JPSoftworks.Cocos.Services.Chat;

internal interface IChatService
{
    string Provider { get; }

    Task<string> GetCompletionAsync(ChatRequest request, CancellationToken cancellationToken);
}
