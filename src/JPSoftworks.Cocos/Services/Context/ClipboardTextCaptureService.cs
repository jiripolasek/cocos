using System.Runtime.InteropServices;
using Windows.System;
using JPSoftworks.Cocos.Interop;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Context;

/// <summary>
/// Last-resort text capture service: activates the parent window, simulates Ctrl+C,
/// reads the clipboard, and restores the original clipboard content.
/// Uses Win32 clipboard APIs to avoid WinUI activation requirements.
/// </summary>
internal interface IClipboardTextCaptureService
{
    /// <summary>
    /// Activates the parent window, sends Ctrl+C, reads the resulting clipboard text,
    /// then restores the previous clipboard content and refocuses the companion window.
    /// Returns empty string on failure.
    /// </summary>
    Task<string> CaptureSelectionViaClipboardAsync(IntPtr parentHwnd, IntPtr companionHwnd);
}

internal sealed class ClipboardTextCaptureService : IClipboardTextCaptureService
{
    private readonly ILogger<ClipboardTextCaptureService> _logger;

    public ClipboardTextCaptureService(ILogger<ClipboardTextCaptureService> logger)
    {
        this._logger = logger;
    }

    public async Task<string> CaptureSelectionViaClipboardAsync(IntPtr parentHwnd, IntPtr companionHwnd)
    {
        if (!NativeMethods.IsWindow(parentHwnd))
        {
            return string.Empty;
        }

        this._logger.LogDebug("Attempting clipboard capture from parent {Parent}.", parentHwnd);

        // 1. Save the current clipboard content
        string? previousClipboardText = null;
        bool hadPreviousContent = false;
        if (Win32Clipboard.HasText())
        {
            previousClipboardText = Win32Clipboard.GetText();
            hadPreviousContent = previousClipboardText is not null;
        }

        // 2. Clear the clipboard so we can detect if Ctrl+C actually copied something
        if (!Win32Clipboard.Clear())
        {
            this._logger.LogDebug("Failed to clear clipboard before capture.");
            return string.Empty;
        }

        // 3. Activate the parent window
        if (!await ActivateWindowAsync(parentHwnd))
        {
            this._logger.LogDebug("Failed to activate parent window {Parent}.", parentHwnd);
            RestoreClipboard(previousClipboardText, hadPreviousContent);
            return string.Empty;
        }

        // 4. Brief delay for the window to fully activate and process pending input
        await Task.Delay(150);

        // 5. Send Ctrl+C
        SendCtrlC();

        // 6. Wait for the clipboard to be populated
        await Task.Delay(200);

        // 7. Read the clipboard
        var capturedText = Win32Clipboard.GetText() ?? string.Empty;

        // 8. Restore the original clipboard content
        RestoreClipboard(previousClipboardText, hadPreviousContent);

        // 9. Refocus the companion window
        if (NativeMethods.IsWindow(companionHwnd))
        {
            NativeMethods.SetForegroundWindow(companionHwnd);
        }

        if (!string.IsNullOrEmpty(capturedText))
        {
            this._logger.LogDebug("Clipboard capture got {Length} chars from parent {Parent}.", capturedText.Length, parentHwnd);
        }
        else
        {
            this._logger.LogDebug("Clipboard capture returned empty for parent {Parent}.", parentHwnd);
        }

        return capturedText;
    }

    private static async Task<bool> ActivateWindowAsync(IntPtr hwnd)
    {
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;

        try
        {
            if (currentThread != targetThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }

            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.BringWindowToTop(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
        }

        // Wait for the window to actually become foreground
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 1000)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    private static void SendCtrlC()
    {
        var inputs = new[]
        {
            CreateKeyInput(VirtualKey.Control, false),
            CreateKeyInput((VirtualKey)0x43 /* C */, false),
            CreateKeyInput((VirtualKey)0x43 /* C */, true),
            CreateKeyInput(VirtualKey.Control, true)
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void RestoreClipboard(string? previousText, bool hadContent)
    {
        if (hadContent && previousText is not null)
        {
            Win32Clipboard.SetText(previousText);
        }
        else if (!hadContent)
        {
            Win32Clipboard.Clear();
        }
    }

    private static NativeMethods.INPUT CreateKeyInput(VirtualKey key, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)key,
                    wScan = 0,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }
}
