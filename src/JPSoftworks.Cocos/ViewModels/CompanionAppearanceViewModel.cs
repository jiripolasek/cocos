using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JPSoftworks.Cocos.Services.Companion;
using Microsoft.UI.Xaml.Media;

namespace JPSoftworks.Cocos.ViewModels;

internal sealed partial class CompanionAppearanceViewModel : ObservableObject
{
    private readonly ICompanionDataStore _dataStore;
    private readonly Guid? _sessionId;
    private bool _initialized;
    private CompanionAppearanceOptionViewModel? _selectedOption;

    public CompanionAppearanceViewModel(
        ICompanionAppearanceProvider provider,
        ICompanionDataStore dataStore,
        CompanionSession? session)
    {
        this._dataStore = dataStore;
        this._sessionId = session?.Id;
        foreach (var option in provider.Options)
        {
            this.Options.Add(new CompanionAppearanceOptionViewModel(option));
        }

        this.HasSession = session is not null;
        this.Title = string.IsNullOrWhiteSpace(session?.WindowTitle)
            ? "Companion appearance"
            : session.WindowTitle;
        this.Subtitle = session is null
            ? "Open a companion window to customize its appearance."
            : "Customize the emoji and accent color used for this companion.";

        if (session is not null)
        {
            var selected = this.Options.FirstOrDefault(o => o.Emoji == session.Emoji)
                ?? this.Options.FirstOrDefault();
            this.SetProperty(ref this._selectedOption, selected, nameof(this.SelectedOption));
        }

        this._initialized = true;
    }

    public ObservableCollection<CompanionAppearanceOptionViewModel> Options { get; } = new();

    public bool HasSession { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public CompanionAppearanceOptionViewModel? SelectedOption
    {
        get => this._selectedOption;
        set
        {
            if (this.SetProperty(ref this._selectedOption, value) && this._initialized && value is not null)
            {
                this.PersistSelection(value);
                this.AppearanceChanged?.Invoke(this, value.Option);
            }
        }
    }

    public event EventHandler<CompanionAppearanceOption>? AppearanceChanged;

    private void PersistSelection(CompanionAppearanceOptionViewModel option)
    {
        if (this._sessionId is null)
        {
            return;
        }

        this._dataStore.UpdateSessionAppearance(this._sessionId.Value, option.Emoji, option.AccentHex);
    }
}

internal sealed class CompanionAppearanceOptionViewModel
{
    public CompanionAppearanceOptionViewModel(CompanionAppearanceOption option)
    {
        this.Option = option;
        this.AccentBrush = new SolidColorBrush(option.AccentColor);
    }

    public CompanionAppearanceOption Option { get; }

    public string Emoji => this.Option.Emoji;

    public string AccentHex => this.Option.AccentHex;

    public SolidColorBrush AccentBrush { get; }
}
