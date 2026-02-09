using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPSoftworks.Cocos.Services.Companion;
using JPSoftworks.Cocos.Services.Settings;

namespace JPSoftworks.Cocos.ViewModels;

internal sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly StickyNoteManager _noteManager;
    private readonly App _app;
    private readonly ISettingsService _settingsService;

    private int _activeNotes;
    private bool _singleWindowMode;
    private CornerPreferenceOption? _selectedCornerPreference;
    private EscapeBehaviorOption? _selectedEscapeBehavior;

    public IRelayCommand CreateNoteCommand { get; }
    public IRelayCommand ExitCommand { get; }

    public string ActiveNotesText => $"Active notes: {this.ActiveNotes}";
    public IReadOnlyList<CornerPreferenceOption> CornerPreferenceOptions { get; } =
    [
        new(CompanionCornerPreference.Default, "System default"),
        new(CompanionCornerPreference.DoNotRound, "No rounding"),
        new(CompanionCornerPreference.Round, "Round"),
        new(CompanionCornerPreference.RoundSmall, "Round small")
    ];
    public IReadOnlyList<EscapeBehaviorOption> EscapeBehaviorOptions { get; } =
    [
        new(EscapeKeyBehavior.HideWindow, "Hide companion window"),
        new(EscapeKeyBehavior.DismissWindow, "Dismiss companion window"),
        new(EscapeKeyBehavior.DoNothing, "Do nothing")
    ];

    public MainViewModel(StickyNoteManager noteManager, App app, ISettingsService settingsService)
    {
        this._noteManager = noteManager;
        this._app = app;
        this._settingsService = settingsService;

        this.CreateNoteCommand = new RelayCommand(this.CreateNote);
        this.ExitCommand = new RelayCommand(this.Exit);

        this._noteManager.ActiveNotesChanged += this.OnActiveNotesChanged;
        this.ActiveNotes = this._noteManager.ActiveCount;
        this._settingsService.SettingsChanged += this.OnSettingsChanged;
        this.ApplySettings(this._settingsService.Settings);
    }

    private void OnActiveNotesChanged(object? sender, int count)
    {
        this.ActiveNotes = count;
    }

    private void CreateNote() => this._noteManager.CreateStickyNoteForActiveWindow();

    private void Exit() => this._app.Shutdown();

    public void Dispose()
    {
        this._noteManager.ActiveNotesChanged -= this.OnActiveNotesChanged;
        this._settingsService.SettingsChanged -= this.OnSettingsChanged;
    }

    public int ActiveNotes
    {
        get => this._activeNotes;
        private set
        {
            if (this.SetProperty(ref this._activeNotes, value))
            {
                this.OnPropertyChanged(nameof(this.ActiveNotesText));
            }
        }
    }

    public bool SingleWindowMode
    {
        get => this._singleWindowMode;
        set
        {
            if (this._singleWindowMode == value)
            {
                return;
            }

            this._settingsService.UpdateSingleWindowMode(value);
            this._noteManager.SingleWindowMode = value;
            this.SetProperty(ref this._singleWindowMode, value);
        }
    }

    public CornerPreferenceOption? SelectedCornerPreference
    {
        get => this._selectedCornerPreference;
        set
        {
            if (value is null || this._selectedCornerPreference?.Value == value.Value)
            {
                return;
            }

            this._settingsService.UpdateCornerPreference(value.Value);
            this.SetProperty(ref this._selectedCornerPreference, value);
        }
    }

    public EscapeBehaviorOption? SelectedEscapeBehavior
    {
        get => this._selectedEscapeBehavior;
        set
        {
            if (value is null || this._selectedEscapeBehavior?.Value == value.Value)
            {
                return;
            }

            this._settingsService.UpdateEscapeBehavior(value.Value);
            this.SetProperty(ref this._selectedEscapeBehavior, value);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => this.ApplySettings(settings);

    private void ApplySettings(AppSettings settings)
    {
        this._noteManager.SingleWindowMode = settings.SingleWindowMode;
        this.SetProperty(ref this._singleWindowMode, settings.SingleWindowMode, nameof(this.SingleWindowMode));
        var option = this.CornerPreferenceOptions.FirstOrDefault(item => item.Value == settings.CornerPreference)
            ?? this.CornerPreferenceOptions.FirstOrDefault();
        this.SetProperty(ref this._selectedCornerPreference, option, nameof(this.SelectedCornerPreference));
        var escapeOption = this.EscapeBehaviorOptions.FirstOrDefault(item => item.Value == settings.EscapeBehavior)
            ?? this.EscapeBehaviorOptions.FirstOrDefault();
        this.SetProperty(ref this._selectedEscapeBehavior, escapeOption, nameof(this.SelectedEscapeBehavior));
    }
}

internal sealed record CornerPreferenceOption(CompanionCornerPreference Value, string DisplayName);

internal sealed record EscapeBehaviorOption(EscapeKeyBehavior Value, string DisplayName);
