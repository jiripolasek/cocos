namespace JPSoftworks.Cocos.Services.Chat;

internal static class ChatProviders
{
    public const string Ollama = "Ollama";
    public const string OpenAI = "OpenAI";

    public static readonly IReadOnlyList<string> All = [Ollama, OpenAI];
}
