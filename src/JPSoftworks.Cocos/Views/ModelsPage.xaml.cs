using JPSoftworks.Cocos.Services.Settings;
using JPSoftworks.Cocos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JPSoftworks.Cocos.Views;

public sealed partial class ModelsPage : Page
{
    private ModelsViewModel? _viewModel;

    public ModelsPage()
    {
        InitializeComponent();
        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        if (app?.Services is null)
        {
            return;
        }

        var settingsService = app.Services.GetRequiredService<ISettingsService>();
        this._viewModel = new ModelsViewModel(settingsService);
        this.DataContext = this._viewModel;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        this._viewModel?.Dispose();
        this._viewModel = null;
        this.DataContext = null;
    }
}
