using JPSoftworks.Cocos.Services.Companion;
using JPSoftworks.Cocos.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JPSoftworks.Cocos.Views;

public sealed partial class CompanionAppearancePage : Page
{
    private CompanionAppearanceViewModel? _viewModel;

    public CompanionAppearancePage()
    {
        InitializeComponent();
        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;
    }

    internal CompanionAppearancePage(CompanionAppearanceViewModel viewModel)
    {
        InitializeComponent();
        this._viewModel = viewModel;
        this.DataContext = this._viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (this._viewModel is not null)
        {
            return;
        }

        var app = Application.Current as App;
        if (app?.Services is null)
        {
            return;
        }

        var dataStore = app.Services.GetRequiredService<ICompanionDataStore>();
        var provider = app.Services.GetRequiredService<ICompanionAppearanceProvider>();
        var session = dataStore.GetLatestSession();
        this._viewModel = new CompanionAppearanceViewModel(provider, dataStore, session);
        this.DataContext = this._viewModel;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (this.DataContext == this._viewModel)
        {
            this.DataContext = null;
        }
    }
}
