using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace JPSoftworks.Cocos;

public sealed partial class OobeWindow : Window
{
    public OobeWindow()
    {
        InitializeComponent();
        this.Title = "Welcome to Sticky Companion";
        this.AppWindow.Resize(new SizeInt32(640, 800));
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(OobeTitleBar);
        this.CenterOnScreen();
    }

    private void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app || app.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        app.ShowMainWindow();
        mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ShowSettingsPage);
    }

    private void CenterOnScreen()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var size = this.AppWindow.Size;
        var x = workArea.X + (workArea.Width - size.Width) / 2;
        var y = workArea.Y + (workArea.Height - size.Height) / 2;
        this.AppWindow.Move(new PointInt32(x, y));
    }
}
