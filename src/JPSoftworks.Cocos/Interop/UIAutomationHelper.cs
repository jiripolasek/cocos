using System.Runtime.InteropServices;

namespace JPSoftworks.Cocos.Interop;

/// <summary>
/// Lightweight UI Automation wrapper using raw COM interop.
/// Works with modern apps (new Notepad, UWP, WinUI) that don't respond to WM_GETTEXT/EM_GETSEL.
/// </summary>
internal static class UIAutomationHelper
{
    private static readonly Guid CLSID_CUIAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IID_IUIAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    // Pattern IDs
    private const int UIA_TextPatternId = 10014;
    private const int UIA_TextPattern2Id = 10024;
    private const int UIA_ValuePatternId = 10002;

    // Text attribute — we don't filter by attribute, just get the selection/full text.
    private static readonly Guid IID_IUIAutomationTextPattern = new("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A");
    private static readonly Guid IID_IUIAutomationValuePattern = new("A94CD8B1-0844-4CD6-9D2D-640537AB39E9");

    [ThreadStatic]
    private static IUIAutomation? t_automation;

    private static IUIAutomation GetAutomation()
    {
        if (t_automation is null)
        {
            var hr = Ole32.CoCreateInstance(
                CLSID_CUIAutomation,
                IntPtr.Zero,
                1 /* CLSCTX_INPROC_SERVER */,
                IID_IUIAutomation,
                out var obj);

            if (hr != 0 || obj is null)
            {
                throw new COMException("Failed to create CUIAutomation instance.", hr);
            }

            t_automation = (IUIAutomation)obj;
        }

        return t_automation;
    }

    /// <summary>
    /// Gets the currently selected text from the focused element of the given window using UI Automation.
    /// Returns empty string if no selection or UIA is not supported by the target.
    /// </summary>
    public static string GetSelectedText(IntPtr hwnd)
    {
        try
        {
            var automation = GetAutomation();
            var element = FindTextElementInWindow(automation, hwnd);
            if (element is null)
            {
                return string.Empty;
            }

            // Try TextPattern first
            element.GetCurrentPattern(UIA_TextPatternId, out var patternObj);
            if (patternObj is IUIAutomationTextPattern textPattern)
            {
                textPattern.GetSelection(out var ranges);
                if (ranges is not null)
                {
                    ranges.get_Length(out var count);
                    if (count > 0)
                    {
                        ranges.GetElement(0, out var range);
                        if (range is not null)
                        {
                            range.GetText(-1, out var text);
                            if (!string.IsNullOrEmpty(text))
                            {
                                return text;
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
        catch (InvalidCastException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the full text content of the focused element of the given window using UI Automation.
    /// Tries TextPattern first (full document text), then ValuePattern (single-line value).
    /// </summary>
    public static string GetFullText(IntPtr hwnd)
    {
        try
        {
            var automation = GetAutomation();
            var element = FindTextElementInWindow(automation, hwnd);
            if (element is null)
            {
                return string.Empty;
            }

            // Try TextPattern (multi-line edit controls, rich text, etc.)
            element.GetCurrentPattern(UIA_TextPatternId, out var textPatternObj);
            if (textPatternObj is IUIAutomationTextPattern textPattern)
            {
                textPattern.get_DocumentRange(out var docRange);
                if (docRange is not null)
                {
                    docRange.GetText(-1, out var text);
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }

            // Fallback to ValuePattern (single-line inputs, combo boxes, etc.)
            element.GetCurrentPattern(UIA_ValuePatternId, out var valuePatternObj);
            if (valuePatternObj is IUIAutomationValuePattern valuePattern)
            {
                valuePattern.get_CurrentValue(out var value);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
        catch (InvalidCastException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Finds the text-bearing element within the parent window's UIA subtree.
    /// We cannot use GetFocusedElement() because our companion window has focus, not the parent.
    /// Instead we get the parent window's UIA element and search its subtree for a text/value control.
    /// </summary>
    private static IUIAutomationElement? FindTextElementInWindow(IUIAutomation automation, IntPtr hwnd)
    {
        IUIAutomationElement? root;
        try
        {
            automation.ElementFromHandle(hwnd, out root);
        }
        catch (COMException)
        {
            return null;
        }

        if (root is null)
        {
            return null;
        }

        // First check if the root element itself supports TextPattern (e.g. single-document editors)
        root.GetCurrentPattern(UIA_TextPatternId, out var rootTextPattern);
        if (rootTextPattern is IUIAutomationTextPattern)
        {
            return root;
        }

        // Walk children looking for a focused or keyboard-focusable text element.
        // Use the GUITHREADINFO to find the focused child HWND within the parent's thread.
        var focusedChild = FindFocusedChildViaWin32(hwnd);
        if (focusedChild != IntPtr.Zero && focusedChild != hwnd)
        {
            try
            {
                automation.ElementFromHandle(focusedChild, out var childElement);
                if (childElement is not null)
                {
                    // Check if this element or its parent supports TextPattern
                    childElement.GetCurrentPattern(UIA_TextPatternId, out var childTextPattern);
                    if (childTextPattern is IUIAutomationTextPattern)
                    {
                        return childElement;
                    }

                    childElement.GetCurrentPattern(UIA_ValuePatternId, out var childValuePattern);
                    if (childValuePattern is IUIAutomationValuePattern)
                    {
                        return childElement;
                    }
                }
            }
            catch (COMException)
            {
                // Fall through to root
            }
        }

        // Last resort: check if root has ValuePattern
        root.GetCurrentPattern(UIA_ValuePatternId, out var rootValuePattern);
        if (rootValuePattern is IUIAutomationValuePattern)
        {
            return root;
        }

        return root;
    }

    /// <summary>
    /// Uses GetGUIThreadInfo to find the focused/caret HWND within the parent window's thread.
    /// This works even when our own app has foreground — it queries the parent's thread state.
    /// </summary>
    private static IntPtr FindFocusedChildViaWin32(IntPtr parentHwnd)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(parentHwnd, out _);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            return IntPtr.Zero;
        }

        // Prefer focus handle, then caret handle, then active handle
        if (info.hwndFocus != IntPtr.Zero)
        {
            return info.hwndFocus;
        }

        if (info.hwndCaret != IntPtr.Zero)
        {
            return info.hwndCaret;
        }

        return info.hwndActive != IntPtr.Zero ? info.hwndActive : IntPtr.Zero;
    }

    // ─── COM Interop Definitions ──────────────────────────────────────

    private static class Ole32
    {
        [DllImport("ole32.dll")]
        public static extern int CoCreateInstance(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        // IUIAutomation methods — we only need a few, but must declare vtable slots in order.
        // Slot 0: CompareElements
        int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);

        // Slot 1: CompareRuntimeIds
        int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2);

        // Slot 2: GetRootElement
        void GetRootElement(out IUIAutomationElement root);

        // Slot 3: ElementFromHandle
        void ElementFromHandle(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

        // Slot 4: ElementFromPoint
        void ElementFromPoint(tagPOINT pt, out IUIAutomationElement element);

        // Slot 5: GetFocusedElement
        void GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct tagPOINT
    {
        public int x;
        public int y;
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        // Slot 0-2: SetFocus, GetRuntimeId, FindFirst
        void SetFocus();
        void GetRuntimeId(out IntPtr runtimeId);
        void FindFirst(int scope, IntPtr condition, out IUIAutomationElement found);

        // Slot 3: FindAll
        void FindAll(int scope, IntPtr condition, out IntPtr found);

        // Slot 4: FindFirstBuildCache
        void FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);

        // Slot 5: FindAllBuildCache
        void FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);

        // Slot 6: BuildUpdatedCache
        void BuildUpdatedCache(IntPtr cacheRequest, out IntPtr updatedElement);

        // Slot 7: GetCurrentPropertyValue
        void GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object retVal);

        // Slot 8: GetCurrentPropertyValueEx
        void GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, [MarshalAs(UnmanagedType.Struct)] out object retVal);

        // Slot 9: GetCachedPropertyValue
        void GetCachedPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object retVal);

        // Slot 10: GetCachedPropertyValueEx
        void GetCachedPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, [MarshalAs(UnmanagedType.Struct)] out object retVal);

        // Slot 11: GetCurrentPatternAs
        void GetCurrentPatternAs(int patternId, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr patternObject);

        // Slot 12: GetCachedPatternAs
        void GetCachedPatternAs(int patternId, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr patternObject);

        // Slot 13: GetCurrentPattern
        void GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.Interface)] out object patternObject);

        // Slot 14: GetCachedPattern
        void GetCachedPattern(int patternId, [MarshalAs(UnmanagedType.Interface)] out object patternObject);

        // Slot 15: GetCachedParent
        void GetCachedParent(out IntPtr parent);

        // Slot 16: GetCachedChildren
        void GetCachedChildren(out IntPtr children);

        // Properties — slots 17+
        void get_CurrentProcessId(out int retVal);                         // 17
        void get_CurrentControlType(out int retVal);                       // 18
        void get_CurrentLocalizedControlType([MarshalAs(UnmanagedType.BStr)] out string retVal); // 19
        void get_CurrentName([MarshalAs(UnmanagedType.BStr)] out string retVal);                 // 20
        void get_CurrentAcceleratorKey([MarshalAs(UnmanagedType.BStr)] out string retVal);       // 21
        void get_CurrentAccessKey([MarshalAs(UnmanagedType.BStr)] out string retVal);            // 22
        void get_CurrentHasKeyboardFocus(out int retVal);                  // 23
        void get_CurrentIsKeyboardFocusable(out int retVal);               // 24
        void get_CurrentIsEnabled(out int retVal);                         // 25
        void get_CurrentAutomationId([MarshalAs(UnmanagedType.BStr)] out string retVal);         // 26
        void get_CurrentClassName([MarshalAs(UnmanagedType.BStr)] out string retVal);            // 27
        void get_CurrentHelpText([MarshalAs(UnmanagedType.BStr)] out string retVal);             // 28
        void get_CurrentCulture(out int retVal);                           // 29
        void get_CurrentIsControlElement(out int retVal);                  // 30
        void get_CurrentIsContentElement(out int retVal);                  // 31
        void get_CurrentIsPassword(out int retVal);                        // 32
        void get_CurrentNativeWindowHandle(out IntPtr retVal);             // 33
        void get_CurrentItemType([MarshalAs(UnmanagedType.BStr)] out string retVal);             // 34
    }

    [ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        // Slot 0: RangeFromPoint
        void RangeFromPoint(tagPOINT pt, out IUIAutomationTextRange range);

        // Slot 1: RangeFromChild
        void RangeFromChild(IUIAutomationElement child, out IUIAutomationTextRange range);

        // Slot 2: GetSelection
        void GetSelection(out IUIAutomationTextRangeArray ranges);

        // Slot 3: GetVisibleRanges
        void GetVisibleRanges(out IUIAutomationTextRangeArray ranges);

        // Slot 4: get_DocumentRange
        void get_DocumentRange(out IUIAutomationTextRange range);

        // Slot 5: get_SupportedTextSelection
        void get_SupportedTextSelection(out int supportedTextSelection);
    }

    [ComImport, Guid("A94CD8B1-0844-4CD6-9D2D-640537AB39E9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationValuePattern
    {
        // Slot 0: SetValue
        void SetValue([MarshalAs(UnmanagedType.BStr)] string val);

        // Slot 1: get_CurrentValue
        void get_CurrentValue([MarshalAs(UnmanagedType.BStr)] out string retVal);

        // Slot 2: get_CurrentIsReadOnly
        void get_CurrentIsReadOnly(out int retVal);
    }

    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        // Slot 0: Clone
        void Clone(out IUIAutomationTextRange clonedRange);

        // Slot 1: Compare
        void Compare(IUIAutomationTextRange range, out int areSame);

        // Slot 2: CompareEndpoints
        void CompareEndpoints(int srcEndPoint, IUIAutomationTextRange range, int targetEndPoint, out int compValue);

        // Slot 3: ExpandToEnclosingUnit
        void ExpandToEnclosingUnit(int textUnit);

        // Slot 4: FindAttribute
        void FindAttribute(int attr, object val, int backward, out IUIAutomationTextRange found);

        // Slot 5: FindText
        void FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase, out IUIAutomationTextRange found);

        // Slot 6: GetAttributeValue
        void GetAttributeValue(int attr, [MarshalAs(UnmanagedType.Struct)] out object value);

        // Slot 7: GetBoundingRectangles
        void GetBoundingRectangles(out IntPtr boundingRects);

        // Slot 8: GetEnclosingElement
        void GetEnclosingElement(out IUIAutomationElement enclosingElement);

        // Slot 9: GetText
        void GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text);

        // Slot 10: Move
        void Move(int unit, int count, out int moved);

        // Slot 11: MoveEndpointByUnit
        void MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);

        // Slot 12: MoveEndpointByRange
        void MoveEndpointByRange(int srcEndPoint, IUIAutomationTextRange range, int targetEndPoint);

        // Slot 13: Select
        void Select();

        // Slot 14: AddToSelection
        void AddToSelection();

        // Slot 15: RemoveFromSelection
        void RemoveFromSelection();

        // Slot 16: ScrollIntoView
        void ScrollIntoView(int alignToTop);

        // Slot 17: GetChildren
        void GetChildren(out IntPtr children);
    }

    [ComImport, Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        // Slot 0: get_Length
        void get_Length(out int length);

        // Slot 1: GetElement
        void GetElement(int index, out IUIAutomationTextRange element);
    }
}
