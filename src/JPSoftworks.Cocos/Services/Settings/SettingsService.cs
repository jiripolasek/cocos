using JPSoftworks.Cocos.Services.Companion;

namespace JPSoftworks.Cocos.Services.Settings;

internal sealed class SettingsService : ISettingsService
{
    private readonly ISettingsStore _store;
    private AppSettings _settings;

    public SettingsService(ISettingsStore store)
    {
        this._store = store;
        var loaded = this._store.Load();
        this._settings = NormalizeSettings(loaded);
        if (!Equals(this._settings, loaded))
        {
            this._store.Save(this._settings);
        }
    }

    public AppSettings Settings => this._settings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public void UpdateSingleWindowMode(bool enabled)
    {
        this.UpdateSettings(this._settings with { SingleWindowMode = enabled });
    }

    public void UpdateCornerPreference(CompanionCornerPreference preference)
    {
        var legacyRounded = preference != CompanionCornerPreference.DoNotRound;
        this.UpdateSettings(this._settings with
        {
            CornerPreference = preference,
            UseRoundedCorners = legacyRounded
        });
    }

    public void UpdateEscapeBehavior(EscapeKeyBehavior behavior)
    {
        this.UpdateSettings(this._settings with { EscapeBehavior = behavior });
    }

    public void UpdateChatProvider(string provider)
    {
        this.UpdateSettings(this._settings with { ChatProvider = provider });
    }

    public void UpdateOllamaEndpoint(string endpoint)
    {
        this.UpdateSettings(this._settings with { OllamaEndpoint = endpoint });
    }

    public void UpdateOllamaModel(string model)
    {
        this.UpdateSettings(this._settings with { OllamaModel = model });
    }

    public void UpdateOpenAiEndpoint(string endpoint)
    {
        this.UpdateSettings(this._settings with { OpenAiEndpoint = endpoint });
    }

    public void UpdateOpenAiModel(string model)
    {
        this.UpdateSettings(this._settings with { OpenAiModel = model });
    }

    public void UpdateOpenAiApiKey(string apiKey)
    {
        this.UpdateSettings(this._settings with { OpenAiApiKey = apiKey });
    }

    public void UpdateSystemPrompt(string prompt)
    {
        this.UpdateSettings(this._settings with { SystemPrompt = prompt });
    }

    private void UpdateSettings(AppSettings updated)
    {
        if (this._settings == updated)
        {
            return;
        }

        this._settings = updated;
        this._store.Save(this._settings);
        this.SettingsChanged?.Invoke(this, this._settings);
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        if (!settings.UseRoundedCorners && settings.CornerPreference == CompanionCornerPreference.Round)
        {
            return settings with { CornerPreference = CompanionCornerPreference.DoNotRound };
        }

        return settings;
    }
}
