using Windows.Graphics;

namespace JPSoftworks.Cocos;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pages;

    public MainWindow()
    {
        InitializeComponent();
        this.AppWindow.Resize(new SizeInt32(1200, 720));
        this._pages = new()
        {
            ["companions"] = typeof(CompanionsPage),
            ["appearance"] = typeof(CompanionAppearancePage),
            ["models"] = typeof(ModelsPage),
            ["transformations"] = typeof(TransformationsPage),
            ["scraps"] = typeof(ScrapsPage),
            ["apps"] = typeof(AppsPage),
            ["settings"] = typeof(SettingsPage)
        };
    }

    public void Initialize(string? arguments)
    {
        this.EnsureDefaultSelection();
    }

    private void OnNavViewLoaded(object sender, RoutedEventArgs e)
    {
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(MainTitleBar);
        this.EnsureDefaultSelection();
    }

    private void EnsureDefaultSelection()
    {
        if (RootNavView.SelectedItem is null)
        {
            RootNavView.SelectedItem = Enumerable.OfType<NavigationViewItem>(RootNavView.MenuItems).FirstOrDefault();
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            this.NavigateTo("settings");
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            this.NavigateTo(tag);
        }
    }

    private void NavigateTo(string key)
    {
        if (!this._pages.TryGetValue(key, out var pageType))
        {
            return;
        }

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    internal void ShowSettingsPage()
    {
        this.NavigateTo("settings");
        if (RootNavView is not null)
        {
            RootNavView.SelectedItem = RootNavView.SettingsItem;
        }
    }
}
