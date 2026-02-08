using System.Runtime.InteropServices;
using System.Text;
using JPSoftworks.Cocos.Interop;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Context;

internal interface IParentWindowTextReader
{
    string GetFocusedInputText(IntPtr parentHwnd);

    string GetFocusedSelectionText(IntPtr parentHwnd);
}

internal sealed class ParentWindowTextReader : IParentWindowTextReader
{
    private readonly ILogger<ParentWindowTextReader> _logger;

    public ParentWindowTextReader(ILogger<ParentWindowTextReader> logger)
    {
        this._logger = logger;
    }

    public string GetFocusedInputText(IntPtr parentHwnd)
    {
        // Try Win32 WM_GETTEXT first (fast, works with classic controls)
        var focusHandle = this.GetFocusedHandle(parentHwnd);
        if (focusHandle != IntPtr.Zero)
        {
            var text = GetControlText(focusHandle);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        // Fallback to UI Automation (works with modern apps like new Notepad, UWP, WinUI)
        this._logger.LogDebug("Win32 text retrieval returned empty, trying UI Automation for parent {Parent}.", parentHwnd);
        return UIAutomationHelper.GetFullText(parentHwnd);
    }

    public string GetFocusedSelectionText(IntPtr parentHwnd)
    {
        // Try Win32 EM_GETSEL first (fast, works with classic controls)
        var focusHandle = this.GetFocusedHandle(parentHwnd);
        if (focusHandle != IntPtr.Zero)
        {
            if (TryGetSelectionRange(focusHandle, out var start, out var end))
            {
                var text = GetControlText(focusHandle);
                if (!string.IsNullOrEmpty(text))
                {
                    start = Math.Clamp(start, 0, text.Length);
                    end = Math.Clamp(end, start, text.Length);
                    if (end > start)
                    {
                        return text.Substring(start, end - start);
                    }
                }
            }
        }

        // Fallback to UI Automation (works with modern apps like new Notepad, UWP, WinUI)
        this._logger.LogDebug("Win32 selection retrieval returned empty, trying UI Automation for parent {Parent}.", parentHwnd);
        return UIAutomationHelper.GetSelectedText(parentHwnd);
    }

    private IntPtr GetFocusedHandle(IntPtr parentHwnd)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(parentHwnd, out _);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            this._logger.LogWarning("GetGUIThreadInfo failed for parent {Parent}.", parentHwnd);
        }
        else
        {
            var guiFocus = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : info.hwndActive;
            if (guiFocus == IntPtr.Zero && info.hwndCaret != IntPtr.Zero)
            {
                guiFocus = info.hwndCaret;
            }
            if (guiFocus != IntPtr.Zero && NativeMethods.IsWindow(guiFocus))
            {
                return guiFocus;
            }
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        if (NativeMethods.AttachThreadInput(currentThread, threadId, true))
        {
            try
            {
                var focusHandle = NativeMethods.GetFocus();
                if (focusHandle != IntPtr.Zero && NativeMethods.IsWindow(focusHandle))
                {
                    return focusHandle;
                }
            }
            finally
            {
                NativeMethods.AttachThreadInput(currentThread, threadId, false);
            }
        }

        this._logger.LogDebug("No focused control found for parent {Parent}.", parentHwnd);
        return IntPtr.Zero;
    }

    private static string GetControlText(IntPtr hwnd)
    {
        var length = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETTEXT, (IntPtr)builder.Capacity, builder);
        return builder.ToString();
    }

    private static bool TryGetSelectionRange(IntPtr hwnd, out int start, out int end)
    {
        start = 0;
        end = 0;
        NativeMethods.SendMessage(hwnd, NativeMethods.EM_GETSEL, out start, out end);
        if (start == end)
        {
            return false;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        return true;
    }
}
