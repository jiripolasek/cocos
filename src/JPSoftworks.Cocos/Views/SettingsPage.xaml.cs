using System.Diagnostics;
using JPSoftworks.Cocos.Services.Settings;
using JPSoftworks.Cocos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JPSoftworks.Cocos.Views;

public sealed partial class SettingsPage : Page
{
    private MainViewModel? _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        if (app?.NoteManager is null)
        {
            return;
        }

        var settingsService = app.Services.GetRequiredService<ISettingsService>();
        this._viewModel = new MainViewModel(app.NoteManager, app, settingsService);
        this.DataContext = this._viewModel;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        this._viewModel?.Dispose();
        this._viewModel = null;
        this.DataContext = null;
    }

    private void OnOpenLogsClicked(object sender, RoutedEventArgs e)
    {
        var logDirectory = App.GetLogDirectory();
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = logDirectory,
            UseShellExecute = true
        });
    }
}
