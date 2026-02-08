using Windows.Graphics;

namespace JPSoftworks.Cocos;

public sealed partial class OobeWindow : Window
{
    public OobeWindow()
    {
        InitializeComponent();
        this.Title = "Welcome to Sticky Companion";
        this.AppWindow.Resize(new SizeInt32(520, 620));
    }
}
