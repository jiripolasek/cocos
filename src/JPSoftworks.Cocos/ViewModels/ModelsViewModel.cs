using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JPSoftworks.Cocos.Services.Chat;
using JPSoftworks.Cocos.Services.Settings;

namespace JPSoftworks.Cocos.ViewModels;

internal sealed partial class ModelsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private ChatModelOption? _selectedDefaultModel;
    private string _ollamaEndpoint = string.Empty;
    private string _ollamaModel = string.Empty;
    private string _openAiEndpoint = string.Empty;
    private string _openAiModel = string.Empty;
    private string _openAiApiKey = string.Empty;
    private string _systemPrompt = string.Empty;

    public ModelsViewModel(ISettingsService settingsService)
    {
        this._settingsService = settingsService;
        this.ApplySettings(settingsService.Settings);
        this._settingsService.SettingsChanged += this.OnSettingsChanged;
    }

    public ObservableCollection<ChatModelOption> DefaultModelOptions { get; } = new();

    public ChatModelOption? SelectedDefaultModel
    {
        get => this._selectedDefaultModel;
        set
        {
            if (value is null)
            {
                return;
            }

            if (this._selectedDefaultModel == value)
            {
                return;
            }

            if (!string.Equals(this._settingsService.Settings.ChatProvider, value.Provider, StringComparison.OrdinalIgnoreCase))
            {
                this._settingsService.UpdateChatProvider(value.Provider);
            }
            this.SetProperty(ref this._selectedDefaultModel, value);
        }
    }

    public string OllamaEndpoint
    {
        get => this._ollamaEndpoint;
        set
        {
            if (this._ollamaEndpoint == value)
            {
                return;
            }

            this._settingsService.UpdateOllamaEndpoint(value);
            this.SetProperty(ref this._ollamaEndpoint, value);
        }
    }

    public string OllamaModel
    {
        get => this._ollamaModel;
        set
        {
            if (this._ollamaModel == value)
            {
                return;
            }

            this._settingsService.UpdateOllamaModel(value);
            this.SetProperty(ref this._ollamaModel, value);
        }
    }

    public string OpenAiEndpoint
    {
        get => this._openAiEndpoint;
        set
        {
            if (this._openAiEndpoint == value)
            {
                return;
            }

            this._settingsService.UpdateOpenAiEndpoint(value);
            this.SetProperty(ref this._openAiEndpoint, value);
        }
    }

    public string OpenAiModel
    {
        get => this._openAiModel;
        set
        {
            if (this._openAiModel == value)
            {
                return;
            }

            this._settingsService.UpdateOpenAiModel(value);
            this.SetProperty(ref this._openAiModel, value);
        }
    }

    public string OpenAiApiKey
    {
        get => this._openAiApiKey;
        set
        {
            if (this._openAiApiKey == value)
            {
                return;
            }

            this._settingsService.UpdateOpenAiApiKey(value);
            this.SetProperty(ref this._openAiApiKey, value);
        }
    }

    public string SystemPrompt
    {
        get => this._systemPrompt;
        set
        {
            if (this._systemPrompt == value)
            {
                return;
            }

            this._settingsService.UpdateSystemPrompt(value);
            this.SetProperty(ref this._systemPrompt, value);
        }
    }

    public void Dispose()
    {
        this._settingsService.SettingsChanged -= this.OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => this.ApplySettings(settings);

    private void ApplySettings(AppSettings settings)
    {
        this.UpdateModelOptions(settings);
        this.SetProperty(ref this._ollamaEndpoint, settings.OllamaEndpoint, nameof(this.OllamaEndpoint));
        this.SetProperty(ref this._ollamaModel, settings.OllamaModel, nameof(this.OllamaModel));
        this.SetProperty(ref this._openAiEndpoint, settings.OpenAiEndpoint, nameof(this.OpenAiEndpoint));
        this.SetProperty(ref this._openAiModel, settings.OpenAiModel, nameof(this.OpenAiModel));
        this.SetProperty(ref this._openAiApiKey, settings.OpenAiApiKey, nameof(this.OpenAiApiKey));
        this.SetProperty(ref this._systemPrompt, settings.SystemPrompt, nameof(this.SystemPrompt));
    }

    private void UpdateModelOptions(AppSettings settings)
    {
        this.DefaultModelOptions.Clear();
        this.DefaultModelOptions.Add(new ChatModelOption
        {
            Provider = ChatProviders.Ollama,
            Model = settings.OllamaModel,
            DisplayName = $"Ollama · {settings.OllamaModel}"
        });
        this.DefaultModelOptions.Add(new ChatModelOption
        {
            Provider = ChatProviders.OpenAI,
            Model = settings.OpenAiModel,
            DisplayName = $"OpenAI · {settings.OpenAiModel}"
        });

        var selected = this.DefaultModelOptions
            .FirstOrDefault(option => string.Equals(option.Provider, settings.ChatProvider, StringComparison.OrdinalIgnoreCase))
            ?? this.DefaultModelOptions.FirstOrDefault();

        if (selected is not null)
        {
            this.SetProperty(ref this._selectedDefaultModel, selected, nameof(this.SelectedDefaultModel));
        }
    }
}
