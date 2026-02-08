using System.Runtime.InteropServices;
using JPSoftworks.Cocos.Interop;
using WinRT.Interop;

namespace JPSoftworks.Cocos.Services.HotKeys;

internal sealed class HotKeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly int _id;
    private readonly Action _onPressed;
    private readonly NativeMethods.WndProc _hookProc;
    private readonly IntPtr _prevWndProc;
    private bool _disposed;

    private HotKeyManager(IntPtr hwnd, uint modifiers, uint key, Action onPressed)
    {
        this._hwnd = hwnd;
        this._onPressed = onPressed;

        this._id = this.GetHashCode() & 0xFFFF;
        if (!NativeMethods.RegisterHotKey(this._hwnd, this._id, modifiers, key))
        {
            throw new InvalidOperationException("Failed to register global hotkey. It may already be in use.");
        }

        this._hookProc = this.HookWndProc;
        this._prevWndProc = NativeMethods.SetWindowLongPtr(this._hwnd, NativeMethods.GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(this._hookProc));
        if (this._prevWndProc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to subclass window for hotkey handling.");
        }
    }

    public static HotKeyManager Register(Window window, uint modifiers, uint key, Action onPressed)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        return new HotKeyManager(hwnd, modifiers, key, onPressed);
    }

    private IntPtr HookWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == this._id)
        {
            this._onPressed?.Invoke();
            return IntPtr.Zero;
        }

        return NativeMethods.CallWindowProc(this._prevWndProc, hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(this._hwnd, this._id);
        NativeMethods.SetWindowLongPtr(this._hwnd, NativeMethods.GWL_WNDPROC, this._prevWndProc);
        this._disposed = true;
    }
}
