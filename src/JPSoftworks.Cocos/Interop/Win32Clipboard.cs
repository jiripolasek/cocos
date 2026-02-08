using System.Runtime.InteropServices;

namespace JPSoftworks.Cocos.Interop;

/// <summary>
/// Win32 clipboard helper that bypasses WinUI's Clipboard API activation requirements.
/// Uses raw Win32 OpenClipboard/SetClipboardData/GetClipboardData which work without
/// "user activation" state, avoiding COM clipboard access errors in background scenarios.
/// </summary>
internal static class Win32Clipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    /// <summary>
    /// Gets the current clipboard text using Win32 APIs. Returns null if no text is available.
    /// </summary>
    public static string? GetText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
        {
            return null;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var hData = GetClipboardData(CF_UNICODETEXT);
            if (hData == IntPtr.Zero)
            {
                return null;
            }

            var pData = GlobalLock(hData);
            if (pData == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pData);
            }
            finally
            {
                GlobalUnlock(hData);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Sets the clipboard text using Win32 APIs. Returns true on success.
    /// </summary>
    public static bool SetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            EmptyClipboard();

            var chars = text.Length + 1; // include null terminator
            var bytes = (UIntPtr)(chars * 2); // UTF-16
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, bytes);
            if (hGlobal == IntPtr.Zero)
            {
                return false;
            }

            var pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                // Write null terminator
                Marshal.WriteInt16(pGlobal, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            return SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Clears the clipboard using Win32 APIs. Returns true on success.
    /// </summary>
    public static bool Clear()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            return EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Returns true if the clipboard currently contains text.
    /// </summary>
    public static bool HasText() => IsClipboardFormatAvailable(CF_UNICODETEXT);
}
