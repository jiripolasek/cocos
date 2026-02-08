using JPSoftworks.Cocos.Services.Companion;

namespace JPSoftworks.Cocos.Services.Settings;

internal interface ISettingsService
{
    AppSettings Settings { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    void UpdateSingleWindowMode(bool enabled);

    void UpdateCornerPreference(CompanionCornerPreference preference);

    void UpdateChatProvider(string provider);

    void UpdateOllamaEndpoint(string endpoint);

    void UpdateOllamaModel(string model);

    void UpdateOpenAiEndpoint(string endpoint);

    void UpdateOpenAiModel(string model);

    void UpdateOpenAiApiKey(string apiKey);

    void UpdateSystemPrompt(string prompt);
}
