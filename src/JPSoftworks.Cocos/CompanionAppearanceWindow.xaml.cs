using Windows.Graphics;
using JPSoftworks.Cocos.ViewModels;
using CompanionAppearancePage = JPSoftworks.Cocos.Views.CompanionAppearancePage;

namespace JPSoftworks.Cocos;

internal sealed partial class CompanionAppearanceWindow : Window
{
    internal CompanionAppearanceWindow(CompanionAppearanceViewModel viewModel)
    {
        InitializeComponent();
        this.Title = "Companion appearance";
        this.AppWindow.Resize(new SizeInt32(520, 640));
        ContentFrame.Content = new CompanionAppearancePage(viewModel);
    }
}
