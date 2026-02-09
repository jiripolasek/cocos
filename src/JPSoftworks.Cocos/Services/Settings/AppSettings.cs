using JPSoftworks.Cocos.Services.Chat;
using JPSoftworks.Cocos.Services.Companion;

namespace JPSoftworks.Cocos.Services.Settings;

internal sealed record AppSettings
{
    public bool SingleWindowMode { get; init; }

    public bool UseRoundedCorners { get; init; } = true;

    public CompanionCornerPreference CornerPreference { get; init; } = CompanionCornerPreference.Round;

    public EscapeKeyBehavior EscapeBehavior { get; init; } = EscapeKeyBehavior.HideWindow;

    public string ChatProvider { get; init; } = ChatProviders.Ollama;

    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    public string OllamaModel { get; init; } = "llama3.2";

    public string OpenAiEndpoint { get; init; } = "https://api.openai.com/v1/chat/completions";

    public string OpenAiModel { get; init; } = "gpt-4o-mini";

    public string OpenAiApiKey { get; init; } = string.Empty;

    public string SystemPrompt { get; init; } = "You are a helpful assistant.";
}
