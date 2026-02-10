using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.Storage;
using Windows.Storage.Pickers;
using CommunityToolkit.WinUI.Helpers;
using JPSoftworks.Cocos.Interop;
using JPSoftworks.Cocos.Services.Chat;
using JPSoftworks.Cocos.Services.Companion;
using JPSoftworks.Cocos.Services.Context;
using JPSoftworks.Cocos.Services.Settings;
using JPSoftworks.Cocos.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Interop;
using WinUIEx;
using ColorHelper = CommunityToolkit.WinUI.Helpers.ColorHelper;

namespace JPSoftworks.Cocos;

internal sealed partial class StickyNoteWindow : WindowEx, IDisposable
{

    private readonly IntPtr _parentHwnd;
    private readonly NativeMethods.RECT _initialParentRect;
    private readonly DispatcherTimer _timer = new();

    private IntPtr _noteHwnd;
    private bool _disposed;
    private string _emoji;
    private Color _baseAccent;
    private Color _accentColor;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private bool _followEnabled = true;
    private bool _suppressMoveEvents;
    private bool _toolStyleApplied;
    private bool _didInitialFocus;
    private bool _hiddenByParentMinimize;
    private bool _hiddenByUser;
    private bool _modelOverride;
    private bool _suppressModelSelectionChanged;
    private CompanionCornerPreference _cornerPreference = CompanionCornerPreference.Round;
    private readonly StickyNoteViewModel _viewModel = new();
    private readonly List<ChatContextItem> _contextItems = new();
    private readonly List<ChatContextItem> _manualContextItems = new();
    private readonly HashSet<string> _suppressedContextLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<StickyNoteWindow>? _logger;
    private readonly ISettingsService _settingsService;
    private readonly IReadOnlyList<IChatService> _chatServices;
    private readonly ICompanionDataStore _dataStore;
    private readonly IParentContextService _contextService;
    private readonly IParentWindowTextReader _textReader;
    private readonly IClipboardTextCaptureService _clipboardCapture;
    private readonly ICompanionAppearanceProvider _appearanceProvider;
    private readonly ParentWindowInfo _parentInfo;
    private CompanionSession _chatSession;
    private readonly CompanionSession _notesSession;
    private CancellationTokenSource? _promptCts;
    private CompanionAppearanceWindow? _appearanceWindow;
    private static readonly string[] _slashCommands = ["/command", "/save", "/note", "/ask"];
    private static readonly string[] _referenceTokens = ["#clipboard", "#selection", "#input"];

    public StickyNoteWindow(
        ParentWindowInfo parentInfo,
        NativeMethods.RECT parentRect,
        ILogger<StickyNoteWindow>? logger,
        ISettingsService settingsService,
        IReadOnlyList<IChatService> chatServices,
        ICompanionDataStore dataStore,
        IParentContextService contextService,
        IParentWindowTextReader textReader,
        IClipboardTextCaptureService clipboardCapture,
        ICompanionAppearanceProvider appearanceProvider,
        CompanionSession chatSession,
        CompanionSession notesSession,
        CompanionCornerPreference cornerPreference)
    {
        InitializeComponent();

        this._logger = logger;
        this._parentInfo = parentInfo;
        this._parentHwnd = parentInfo.Handle;
        this._initialParentRect = parentRect;
        this._cornerPreference = cornerPreference;
        this._settingsService = settingsService;
        this._chatServices = chatServices;
        this._dataStore = dataStore;
        this._contextService = contextService;
        this._textReader = textReader;
        this._clipboardCapture = clipboardCapture;
        this._appearanceProvider = appearanceProvider;
        this._chatSession = chatSession;
        this._notesSession = notesSession;

        var appearance = this.ResolveAppearance(chatSession);
        this._emoji = appearance.Emoji;
        this._baseAccent = appearance.AccentColor;
        this._accentColor = this.AdjustAccent(this._baseAccent);

        this.Title = "CoCo";

        RootGrid.DataContext = this._viewModel;
        this._viewModel.Emoji = this._emoji;
        this._viewModel.Title = this.Title;
        this._viewModel.IsFollowing = true;
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(CustomTitleBar);
        this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        this.HideSystemCaptionButtons();
        this.InitializeBackdrop();

        this._timer.Interval = TimeSpan.FromMilliseconds(200);
        this._timer.Tick += this.OnTick;
        this.Closed += this.OnClosed;
        this.AppWindow.Changed += this.OnAppWindowChanged;
        this._settingsService.SettingsChanged += this.OnSettingsChanged;

        this.InitializeWindowState();
        this.UpdateModelOptions(this._settingsService.Settings);
        this._viewModel.IsChatSaved = this._chatSession.IsSaved;
        if (this._chatSession.IsSaved)
        {
            this.LoadChatHistory();
        }

        this.LoadNotes();
        this.LoadContextAsync();
        this.EnsureHeroMessage(this.ResolveHeroAppLabel());

        this._viewModel.Messages.CollectionChanged += (_, _) => this.ScrollChatToBottom();
    }

    private void InitializeBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        this._backdropConfiguration = new SystemBackdropConfiguration { IsInputActive = true };

        this._acrylicController = new DesktopAcrylicController { TintOpacity = 0.75f, LuminosityOpacity = 0.85f };

        this._acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        this._acrylicController.SetSystemBackdropConfiguration(this._backdropConfiguration);

        this.Activated += this.OnWindowActivated;
        if (this.Content is FrameworkElement root)
        {
            root.ActualThemeChanged += this.OnActualThemeChanged;
        }

        this.UpdateBackdropTheme();
    }

    private void OnFollowToggleClicked(object sender, RoutedEventArgs e) => this.ToggleFollowMode();

    private void InitializeWindowState()
    {
        this.EnsureHandle();
        this.ApplyToolWindowStyle();
        this.ApplyCornerPreference(this._cornerPreference);
        this.AppWindow.Resize(new SizeInt32(360, 480));
        this.PositionRelativeTo(this._initialParentRect);
        NativeMethods.SetForegroundWindow(this._noteHwnd);
        this.FocusPrompt();
    }

    private void HideSystemCaptionButtons()
    {
        if (this.AppWindow?.TitleBar is AppWindowTitleBar titleBar)
        {
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Colors.Transparent;
            titleBar.ButtonPressedBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Transparent;
            titleBar.ExtendsContentIntoTitleBar = true;
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (this._backdropConfiguration is not null)
        {
            this._backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            this.LoadContextAsync();
            if (!this._didInitialFocus)
            {
                this._didInitialFocus = true;
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(50);
                    this.FocusActiveTab();
                });
            }
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        this.UpdateBackdropTheme();
    }

    private void UpdateBackdropTheme()
    {
        if (this._backdropConfiguration is null)
        {
            return;
        }

        var theme = this.Content is FrameworkElement root ? root.ActualTheme : ElementTheme.Default;
        this._backdropConfiguration.Theme = theme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };

        var appTheme = theme switch
        {
            ElementTheme.Dark => ApplicationTheme.Dark,
            ElementTheme.Light => ApplicationTheme.Light,
            _ => Application.Current.RequestedTheme
        };

        this._accentColor = this.AdjustAccent(this._baseAccent, appTheme);
        if (this._acrylicController is not null)
        {
            if (appTheme == ApplicationTheme.Dark)
            {
                this._acrylicController.TintColor = this._accentColor;
                this._acrylicController.TintOpacity = 0.75f;
                this._acrylicController.LuminosityOpacity = 0.75f;
            }
            else
            {
                this._acrylicController.TintColor = this._accentColor;
                this._acrylicController.TintOpacity = 0.45f;
                this._acrylicController.LuminosityOpacity = 0.85f;
            }
        }
    }

    private CompanionAppearanceOption ResolveAppearance(CompanionSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.Emoji)
            && CompanionAppearanceSerializer.TryParseHex(session.AccentColor, out var color))
        {
            return new CompanionAppearanceOption(session.Emoji, color);
        }

        if (!string.IsNullOrWhiteSpace(session.Emoji))
        {
            var match = this._appearanceProvider.FindByEmoji(session.Emoji);
            if (match is not null)
            {
                return match;
            }
        }

        return this._appearanceProvider.GetRandom();
    }

    private void ApplyAppearance(CompanionAppearanceOption option)
    {
        this._emoji = option.Emoji;
        this._baseAccent = option.AccentColor;
        this._viewModel.Emoji = this._emoji;
        this.UpdateBackdropTheme();
    }

    private void EnsureHandle()
    {
        if (this._noteHwnd == IntPtr.Zero)
        {
            this._noteHwnd = WindowNative.GetWindowHandle(this);
        }
    }

    private void ApplyToolWindowStyle()
    {
        if (this._toolStyleApplied || this._noteHwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(this._noteHwnd, NativeMethods.GWL_EXSTYLE);
        var newStyle = new IntPtr(style.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowLongPtr(this._noteHwnd, NativeMethods.GWL_EXSTYLE, newStyle);
        this._toolStyleApplied = true;
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        this.Dispose();
    }

    private void OnTick(object? sender, object e)
    {
        if (!NativeMethods.IsWindow(this._parentHwnd) || !NativeMethods.GetWindowRect(this._parentHwnd, out var rect))
        {
            this.Close();
            return;
        }

        if (NativeMethods.IsIconic(this._parentHwnd))
        {
            this.HideForParentMinimize();
            return;
        }

        if (this._hiddenByUser)
        {
            return;
        }

        this.ShowAfterParentRestore(rect);

        if (this._followEnabled)
        {
            this.PositionRelativeTo(rect);
        }
        else
        {
            this.UpdateZOrderOnly();
        }
    }

    private void PositionRelativeTo(NativeMethods.RECT parentRect)
    {
        this.EnsureHandle();

        var noteSize = this.AppWindow.Size;
        const int margin = 12;
        var workArea = this.GetWorkArea(parentRect);

        var hasRightSpace = parentRect.Right + margin + noteSize.Width <= workArea.Right;
        var targetX = hasRightSpace
            ? parentRect.Right + margin
            : parentRect.Right - noteSize.Width - margin;

        if (targetX < workArea.Left + margin)
        {
            targetX = workArea.Left + margin;
        }

        var targetY = parentRect.Top;
        targetY = Math.Max(workArea.Top + margin, targetY);
        if (targetY + noteSize.Height + margin > workArea.Bottom)
        {
            targetY = Math.Max(workArea.Top + margin, workArea.Bottom - noteSize.Height - margin);
        }

        var topMost = NativeMethods.GetForegroundWindow() == this._parentHwnd;

        var zOrder = topMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST;
        this._suppressMoveEvents = true;
        NativeMethods.SetWindowPos(
            this._noteHwnd,
            zOrder,
            targetX,
            targetY,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        this._suppressMoveEvents = false;
    }

    private void UpdateZOrderOnly()
    {
        var topMost = NativeMethods.GetForegroundWindow() == this._parentHwnd;
        var zOrder = topMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST;
        this._suppressMoveEvents = true;
        NativeMethods.SetWindowPos(
            this._noteHwnd,
            zOrder,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW);
        this._suppressMoveEvents = false;
    }

    public void StartTracking()
    {
        this._timer.Start();
    }

    public bool OwnsWindow(IntPtr window)
    {
        this.EnsureHandle();
        return this._noteHwnd == window;
    }

    internal IntPtr ParentHandle => this._parentHwnd;

    internal void ApplyTopMost(bool shouldBeTopMost, IntPtr anchorBelow)
    {
        this.EnsureHandle();
        if (!this.AppWindow.IsVisible)
        {
            return;
        }

        this._suppressMoveEvents = true;
        NativeMethods.SetWindowPos(
            this._noteHwnd,
            shouldBeTopMost
                ? NativeMethods.HWND_TOPMOST
                : (anchorBelow != IntPtr.Zero ? anchorBelow : NativeMethods.HWND_NOTOPMOST),
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW);
        this._suppressMoveEvents = false;
    }

    internal void ApplyCornerPreference(CompanionCornerPreference preference)
    {
        this._cornerPreference = preference;
        if (this._noteHwnd == IntPtr.Zero)
        {
            return;
        }

        var preferenceValue = preference switch
        {
            CompanionCornerPreference.Default => NativeMethods.DWMWCP_DEFAULT,
            CompanionCornerPreference.DoNotRound => NativeMethods.DWMWCP_DONOTROUND,
            CompanionCornerPreference.RoundSmall => NativeMethods.DWMWCP_ROUNDSMALL,
            _ => NativeMethods.DWMWCP_ROUND
        };
        var result = NativeMethods.DwmSetWindowAttribute(
            this._noteHwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preferenceValue,
            Marshal.SizeOf<int>());

        if (result != 0)
        {
            this._logger?.LogDebug("DwmSetWindowAttribute failed with {Result}.", result);
        }
    }

    public bool MatchesParent(IntPtr parent) => parent == this._parentHwnd;

    public void Summon(NativeMethods.RECT rect)
    {
        this.EnsureHandle();
        if (this._hiddenByUser || !this.AppWindow.IsVisible)
        {
            this._hiddenByUser = false;
            this._hiddenByParentMinimize = false;
            this.AppWindow.Show();
        }

        this.Activate();
        NativeMethods.SetForegroundWindow(this._noteHwnd);
        this.FocusPrompt();
        if (!this._timer.IsEnabled)
        {
            this._timer.Start();
        }

        if (this._followEnabled)
        {
            this.PositionRelativeTo(rect);
        }
        else
        {
            this.UpdateZOrderOnly();
        }
    }

    internal void ShowNotesTab()
    {
        if (MainTabs is null)
        {
            return;
        }

        MainTabs.SelectedIndex = 1;
        this.Activate();
        this.FocusNotesInput();
    }

    internal void ShowChatTab()
    {
        if (MainTabs is null)
        {
            return;
        }

        MainTabs.SelectedIndex = 0;
        this.Activate();
        this.FocusPrompt();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void OnShowSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowMainWindow();
        }
    }

    private void OnShowOobeClicked(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowOobeWindow();
        }
    }

    private void OnShowAppearanceClicked(object sender, RoutedEventArgs e)
    {
        if (this._appearanceWindow is null)
        {
            var viewModel = new CompanionAppearanceViewModel(this._appearanceProvider, this._dataStore, this._chatSession);
            viewModel.AppearanceChanged += (_, option) => this.ApplyAppearance(option);
            this._appearanceWindow = new CompanionAppearanceWindow(viewModel);
            this._appearanceWindow.Closed += (_, _) => this._appearanceWindow = null;
        }

        this._appearanceWindow.Activate();
    }

    private void OnSaveChatToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item)
        {
            return;
        }

        var isSaved = item.IsChecked;
        this._chatSession = this._dataStore.UpdateSessionSaved(this._chatSession.Id, isSaved);
        this._viewModel.IsChatSaved = isSaved;
    }

    private void OnClearChatClicked(object sender, RoutedEventArgs e)
    {
        this._dataStore.DeleteChatMessages(this._chatSession.Id);
        this._viewModel.Messages.Clear();
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this.Activated -= this.OnWindowActivated;
        if (this.Content is FrameworkElement root)
        {
            root.ActualThemeChanged -= this.OnActualThemeChanged;
        }

        this._settingsService.SettingsChanged -= this.OnSettingsChanged;
        this.AppWindow.Changed -= this.OnAppWindowChanged;
        this._acrylicController?.Dispose();
        this._acrylicController = null;
        this._backdropConfiguration = null;
        this._timer.Stop();
        this._timer.Tick -= this.OnTick;
        this._promptCts?.Cancel();
        this._promptCts?.Dispose();
        this._appearanceWindow?.Close();
        this._appearanceWindow = null;
        this._disposed = true;
    }

    private NativeMethods.RECT GetWorkArea(NativeMethods.RECT parentRect)
    {
        var work = new NativeMethods.RECT
        {
            Left = parentRect.Left - 200,
            Top = parentRect.Top - 200,
            Right = parentRect.Right + 200,
            Bottom = parentRect.Bottom + 200
        };

        var monitor = NativeMethods.MonitorFromWindow(this._parentHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return work;
        }

        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };

        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            work = info.rcWork;
        }

        return work;
    }

    private void ToggleFollowMode()
    {
        this._followEnabled = this._viewModel.IsFollowing;
        this.UpdateFollowToggleContent();
        if (this._followEnabled && NativeMethods.GetWindowRect(this._parentHwnd, out var rect))
        {
            this.PositionRelativeTo(rect);
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (this._suppressMoveEvents)
        {
            return;
        }

        if (args.DidPositionChange && this._followEnabled)
        {
            this._followEnabled = false;
            this.UpdateFollowToggleContent();
        }
    }

    private void UpdateFollowToggleContent()
    {
        if (this._viewModel.IsFollowing != this._followEnabled)
        {
            this._viewModel.IsFollowing = this._followEnabled;
        }
    }

    private Color AdjustAccent(Color baseColor, ApplicationTheme? themeOverride = null)
    {
        var result = baseColor;
        if (themeOverride == ApplicationTheme.Dark)
        {
            var hsv = result.ToHsv();
            hsv.V *= 0.5f;
            result = ColorHelper.FromHsv(hsv.H, hsv.S, hsv.V, hsv.A);
        }

        return result;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        this.FocusActiveTab();
        this.ScrollChatToBottom();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        this.FocusActiveTab();
    }

    private void OnPromptGotFocus(object sender, RoutedEventArgs e)
    {
        this.LoadContextAsync();
    }

    private void FocusPrompt()
    {
        if (PromptTextBox is null)
        {
            return;
        }

        PromptTextBox.Focus(FocusState.Programmatic);
    }

    private void FocusNotesInput()
    {
        if (NoteInputBox is null)
        {
            return;
        }

        NoteInputBox.Focus(FocusState.Programmatic);
    }

    private void FocusActiveTab()
    {
        if (MainTabs is null)
        {
            return;
        }

        if (MainTabs.SelectedIndex == 1)
        {
            this.FocusNotesInput();
            return;
        }

        this.FocusPrompt();
    }

    private void SwitchTab(int direction)
    {
        if (MainTabs is null)
        {
            return;
        }

        var count = MainTabs.Items.Count;
        if (count == 0)
        {
            return;
        }

        var index = MainTabs.SelectedIndex;
        if (index < 0)
        {
            index = 0;
        }

        var next = index + direction;
        if (next >= count)
        {
            next = 0;
        }
        else if (next < 0)
        {
            next = count - 1;
        }

        MainTabs.SelectedIndex = next;
        this.FocusActiveTab();
    }

    private bool TryHandleReservedShortcut(KeyRoutedEventArgs e)
    {
        // Prevent textboxes from handling Ctrl+Tab / Ctrl+Shift+Tab (tab switching)
        if (e.Key == VirtualKey.Tab && IsCtrlDown())
        {
            var direction = IsShiftDown() ? -1 : 1;
            this.SwitchTab(direction);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private async Task<bool> TryHandlePasteShortcutAsync(KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.V || !IsCtrlDown() || !IsShiftDown())
        {
            return false;
        }

        e.Handled = true;
        await this.CopyLastAssistantResultAsync().ConfigureAwait(true);
        await this.PasteToParentAsync().ConfigureAwait(true);
        return true;
    }

    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            this.HandleEscapeKey();
            e.Handled = true;
            return;
        }

        if (await this.TryHandlePasteShortcutAsync(e))
        {
            return;
        }

        if (this.TryHandleReservedShortcut(e))
        {
            return;
        }

        // Ctrl+Shift+P ... go back
        if (e.Key == VirtualKey.P)
        {
            var ctrl = IsCtrlDown();
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl && shift)
            {
                NativeMethods.SetForegroundWindow(this._parentHwnd);
                e.Handled = true;
            }

            return;
        }

        // Ctrl+Shift+C ... copy last
        if (e.Key == VirtualKey.C && IsCtrlDown() && IsShiftDown())
        {
            await this.CopyLastAssistantResultAsync();
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+V ... paste last
        if (e.Key == VirtualKey.B && IsCtrlDown() && IsShiftDown())
        {
            var last = this._viewModel.Messages.LastOrDefault(m => !m.IsUser && !m.IsHero);
            if (last is null)
            {
                return;
            }

            var text = ExtractResultSegment(last.Text);
            await this.TypeIntoParentAsync(text);
            e.Handled = true;
            return;
        }
    }

    private void HandleEscapeKey()
    {
        var behavior = this._settingsService.Settings.EscapeBehavior;
        switch (behavior)
        {
            case EscapeKeyBehavior.HideWindow:
                this._hiddenByUser = true;
                this._hiddenByParentMinimize = false;
                this.AppWindow.Hide();
                break;
            case EscapeKeyBehavior.DismissWindow:
                this.Close();
                break;
            case EscapeKeyBehavior.DoNothing:
            default:
                break;
        }
    }

    private void ScrollChatToBottom()
    {
        if (ChatScrollViewer is null || ChatItems is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ChatItems.UpdateLayout();
            ChatScrollViewer.UpdateLayout();

            if (ChatItems.Items.Count == 0)
            {
                return;
            }

            FrameworkElement? container = null;
            if (ChatItems.ItemsPanelRoot is Panel panel && panel.Children.Count > 0)
            {
                container = panel.Children[panel.Children.Count - 1] as FrameworkElement;
            }

            if (container is null)
            {
                ChatScrollViewer.ScrollToVerticalOffset(ChatScrollViewer.ScrollableHeight);
                return;
            }

            var point = container.TransformToVisual(ChatItems).TransformPoint(new Point(0, 0));
            var targetOffset = point.Y;
            if (targetOffset > ChatScrollViewer.ScrollableHeight)
            {
                targetOffset = ChatScrollViewer.ScrollableHeight;
            }
            else if (targetOffset < 0)
            {
                targetOffset = 0;
            }

            ChatScrollViewer.ScrollToVerticalOffset(targetOffset);
        });
    }

    private void OnPromptTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var text = sender.Text ?? string.Empty;
        var lastToken = GetLastToken(text);
        if (string.IsNullOrEmpty(lastToken))
        {
            sender.ItemsSource = null;
            return;
        }

        IEnumerable<string> matches = Enumerable.Empty<string>();
        if (lastToken.StartsWith("/"))
        {
            matches = _slashCommands.Where(c => c.StartsWith(lastToken, StringComparison.OrdinalIgnoreCase));
        }
        else if (lastToken.StartsWith("#"))
        {
            matches = _referenceTokens.Where(c => c.StartsWith(lastToken, StringComparison.OrdinalIgnoreCase));
        }

        var list = matches.ToList();
        sender.ItemsSource = list;
        sender.IsSuggestionListOpen = list.Count > 0;
    }

    private void OnPromptSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not string choice)
        {
            return;
        }

        var text = sender.Text ?? string.Empty;
        var parts = text.Split(' ', StringSplitOptions.None).ToList();
        var replaceIndex
            = parts.FindLastIndex(p => !string.IsNullOrWhiteSpace(p) && (p.StartsWith("/") || p.StartsWith("#")));
        if (parts.Count == 0)
        {
            sender.Text = choice;
            return;
        }

        if (replaceIndex >= 0)
        {
            parts[replaceIndex] = choice;
        }
        else
        {
            parts[^1] = choice;
        }

        sender.Text = string.Join(' ', parts);
    }

    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (this._viewModel.IsSending)
        {
            this.CancelActivePrompt();
            return;
        }

        _ = this.ProcessPromptAsync();
    }

    private async void OnAddContextClicked(object sender, RoutedEventArgs e)
    {
        this.EnsureHandle();
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, this._noteHwnd);

        IReadOnlyList<StorageFile>? files;
        try
        {
            files = await picker.PickMultipleFilesAsync();
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "Failed to open file picker for context attachments.");
            return;
        }

        if (files is null || files.Count == 0)
        {
            return;
        }

        foreach (var file in files)
        {
            var label = $"File: {file.Name}";
            var content = string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path;
            var item = new ChatContextItem(label, content);
            this._manualContextItems.Add(item);
            this._contextItems.Add(item);
            this._suppressedContextLabels.Remove(label);
        }

        this.RefreshContextItems();
    }

    private void OnRemoveContextClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ContextItemViewModel context)
        {
            return;
        }

        this._suppressedContextLabels.Add(context.Label);
        RemoveContextItemsByLabel(this._manualContextItems, context.Label);
        RemoveContextItemsByLabel(this._contextItems, context.Label);
        this.RefreshContextItems();
    }

    private async void NoteInputBox_OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            this.HandleEscapeKey();
            e.Handled = true;
            return;
        }

        if (await this.TryHandlePasteShortcutAsync(e))
        {
            return;
        }

        if (this.TryHandleReservedShortcut(e))
        {
            return;
        }
    }

    private async void OnPromptKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (this.TryHandleReservedShortcut(e))
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            if (this._viewModel.IsSending)
            {
                return;
            }

            if (IsCtrlDown())
            {
                e.Handled = true;
                await this.ProcessPromptAsync();
            }
        }
    }

    private async void OnPromptQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (IsCtrlDown() && !this._viewModel.IsSending)
        {
            await this.ProcessPromptAsync();
        }
    }

    private async Task ProcessPromptAsync()
    {
        if (PromptTextBox is null)
        {
            return;
        }

        if (this._viewModel.IsSending)
        {
            return;
        }

        var input = (PromptTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        this._viewModel.Messages.Add(new ChatMessage { Text = input, IsUser = true });
        this._dataStore.AddChatMessage(this._chatSession.Id, ChatMessageRole.User, input);
        try
        {
            var (expanded, error) = await this.TryExpandReferencesAsync(input);
            if (!string.IsNullOrWhiteSpace(error))
            {
                this.AddSystemMessage(error);
                return;
            }

            this._viewModel.IsSending = true;
            this._promptCts?.Dispose();
            this._promptCts = new CancellationTokenSource();
            var settings = this._settingsService.Settings;
            var provider = this.ResolveProvider(settings);
            var request = this.BuildChatRequest(settings, expanded, provider);
            var service = this.GetChatService(provider);
            var response = await service.GetCompletionAsync(request, this._promptCts.Token);
            this._viewModel.Messages.Add(new ChatMessage { Text = response, IsUser = false });
            this._dataStore.AddChatMessage(this._chatSession.Id, ChatMessageRole.Assistant, response);
            PromptTextBox.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
            const string message = "Canceled.";
            this._viewModel.Messages.Add(new ChatMessage { Text = message, IsUser = false });
            this._dataStore.AddChatMessage(this._chatSession.Id, ChatMessageRole.System, message);
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "Chat request failed.");
            var message = $"Error: {ex.Message}";
            this._viewModel.Messages.Add(new ChatMessage { Text = message, IsUser = false });
            this._dataStore.AddChatMessage(this._chatSession.Id, ChatMessageRole.System, message);
        }
        finally
        {
            this._viewModel.IsSending = false;
            this._promptCts?.Dispose();
            this._promptCts = null;
        }
    }

    private void CancelActivePrompt()
    {
        this._promptCts?.Cancel();
    }

    private void AddSystemMessage(string message)
    {
        this._viewModel.Messages.Add(new ChatMessage { Text = message, IsUser = false });
        this._dataStore.AddChatMessage(this._chatSession.Id, ChatMessageRole.System, message);
    }

    private void EnsureHeroMessage(string? appLabel)
    {
        var label = string.IsNullOrWhiteSpace(appLabel) ? "this app" : appLabel;
        var text = $"""
                    Hello, I'm your companion for **{label}**.

                    Use # to reference items from your current context, like #clipboard, #selection, or #input.
                    
                    Use / to enter commands, like /save to save our chat, or /note to jot down a quick note.
                    
                    Ctrl+Shift+V to paste the last response to the app, Ctrl+Shift+B to type it out, or Ctrl+Shift+C to copy it to clipboard.
                    """;
        var heroIndex = -1;
        for (var i = 0; i < this._viewModel.Messages.Count; i++)
        {
            if (this._viewModel.Messages[i].IsHero)
            {
                heroIndex = i;
                break;
            }
        }

        var heroMessage = new ChatMessage { Text = text, IsHero = true };
        if (heroIndex >= 0)
        {
            this._viewModel.Messages[heroIndex] = heroMessage;
        }
        else
        {
            this._viewModel.Messages.Insert(0, heroMessage);
        }
    }

    private string? ResolveHeroAppLabel()
    {
        var appItem = this._contextItems.FirstOrDefault(item =>
            string.Equals(item.Label, "App", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(appItem?.Content))
        {
            return appItem.Content;
        }

        if (!string.IsNullOrWhiteSpace(this._parentInfo.ProcessName))
        {
            return this._parentInfo.ProcessName;
        }

        return this._parentInfo.WindowTitle;
    }

    private string ResolveProvider(AppSettings settings)
    {
        var selected = this._viewModel.SelectedModel;
        if (selected is null || selected.IsDefault)
        {
            return settings.ChatProvider;
        }

        return selected.Provider;
    }

    private string ResolveModel(AppSettings settings, string provider)
    {
        var selected = this._viewModel.SelectedModel;
        if (selected is not null
            && !selected.IsDefault
            && string.Equals(selected.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            return selected.Model;
        }

        return string.Equals(provider, ChatProviders.OpenAI, StringComparison.OrdinalIgnoreCase)
            ? settings.OpenAiModel
            : settings.OllamaModel;
    }

    private ChatRequest BuildChatRequest(AppSettings settings, string prompt, string provider)
    {
        var contextItems = this._contextItems.Count > 0
            ? this._contextItems.ToList()
            : this._viewModel.ContextItems
                .Select(item => new ChatContextItem(item.Label, item.Content))
                .ToList();

        var model = this.ResolveModel(settings, provider);
        if (string.Equals(provider, ChatProviders.OpenAI, StringComparison.OrdinalIgnoreCase))
        {
            return new ChatRequest
            {
                Prompt = prompt,
                SystemPrompt = settings.SystemPrompt,
                Model = model,
                Endpoint = settings.OpenAiEndpoint,
                ApiKey = settings.OpenAiApiKey,
                ContextItems = contextItems
            };
        }

        return new ChatRequest
        {
            Prompt = prompt,
            SystemPrompt = settings.SystemPrompt,
            Model = model,
            Endpoint = settings.OllamaEndpoint,
            ContextItems = contextItems
        };
    }

    private IChatService GetChatService(string provider)
    {
        var service = this._chatServices.FirstOrDefault(s =>
            string.Equals(s.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            throw new InvalidOperationException($"Chat provider '{provider}' is not available.");
        }

        return service;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        this.UpdateModelOptions(settings);
    }

    private void UpdateModelOptions(AppSettings settings)
    {
        var previousSelection = this._viewModel.SelectedModel;
        var defaultProvider = settings.ChatProvider;
        var defaultModel = string.Equals(defaultProvider, ChatProviders.OpenAI, StringComparison.OrdinalIgnoreCase)
            ? settings.OpenAiModel
            : settings.OllamaModel;
        this._viewModel.ModelOptions.Clear();
        this._viewModel.ModelOptions.Add(new ChatModelOption
        {
            Provider = defaultProvider,
            Model = defaultModel,
            DisplayName = $"Default - {defaultProvider} · {defaultModel}",
            IsDefault = true
        });
        this._viewModel.ModelOptions.Add(new ChatModelOption
        {
            Provider = ChatProviders.Ollama,
            Model = settings.OllamaModel,
            DisplayName = $"Ollama · {settings.OllamaModel}"
        });
        this._viewModel.ModelOptions.Add(new ChatModelOption
        {
            Provider = ChatProviders.OpenAI,
            Model = settings.OpenAiModel,
            DisplayName = $"OpenAI · {settings.OpenAiModel}"
        });

        var selected = this._modelOverride && previousSelection is not null && !previousSelection.IsDefault
            ? this._viewModel.ModelOptions.FirstOrDefault(option =>
                !option.IsDefault
                && string.Equals(option.Provider, previousSelection.Provider, StringComparison.OrdinalIgnoreCase))
            : this._viewModel.ModelOptions.FirstOrDefault(option => option.IsDefault);

        selected ??= this._viewModel.ModelOptions.FirstOrDefault();

        if (selected is not null)
        {
            this._suppressModelSelectionChanged = true;
            this._viewModel.SelectedModel = selected;
            this._suppressModelSelectionChanged = false;
        }
    }

    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.SelectedItem is not ChatModelOption option)
        {
            return;
        }

        if (this._suppressModelSelectionChanged)
        {
            return;
        }

        this._modelOverride = !option.IsDefault;
    }

    private void LoadNotes()
    {
        var notes = this._dataStore.GetNotes(this._notesSession.Id);
        this._viewModel.Notes.Clear();
        foreach (var note in notes)
        {
            this._viewModel.Notes.Add(new NoteItemViewModel(note));
        }
    }

    private void LoadChatHistory()
    {
        var messages = this._dataStore.GetChatMessages(this._chatSession.Id);
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            this._viewModel.Messages.Add(new ChatMessage
            {
                Text = message.Content,
                IsUser = message.Role == ChatMessageRole.User
            });
        }
    }

    private void OnAddNoteClicked(object sender, RoutedEventArgs e)
    {
        this.AddNote();
    }

    private async void OnNoteInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (this.TryHandleReservedShortcut(e))
        {
            return;
        }

        if (e.Key != VirtualKey.Enter || !IsCtrlDown())
        {
            return;
        }

        e.Handled = true;
        this.AddNote();
    }

    private void AddNote()
    {
        if (NoteInputBox is null)
        {
            return;
        }

        var content = (NoteInputBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        var note = this._dataStore.AddNote(this._notesSession.Id, content);
        this._viewModel.Notes.Insert(0, new NoteItemViewModel(note));
        NoteInputBox.Text = string.Empty;
    }

    private void OnSaveMessageAsNoteClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message)
        {
            return;
        }

        var content = message.Text.Trim();
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        var note = this._dataStore.AddNote(this._notesSession.Id, content);
        this._viewModel.Notes.Insert(0, new NoteItemViewModel(note));
    }

    private void OnNoteFlagChanged(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItemViewModel note)
        {
            return;
        }

        this._dataStore.UpdateNoteFlags(note.Id, note.IsPinned, note.IsFavorite, note.IsFlagged);
    }

    private void OnDeleteNoteClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItemViewModel note)
        {
            return;
        }

        this._dataStore.DeleteNote(note.Id);
        this._viewModel.Notes.Remove(note);
    }

    private async void LoadContextAsync()
    {
        try
        {
            var contextItems = await this._contextService.GetContextAsync(this._parentInfo, CancellationToken.None)
                .ConfigureAwait(true);
            this._contextItems.Clear();
            foreach (var item in contextItems)
            {
                if (!this._suppressedContextLabels.Contains(item.Label))
                {
                    this._contextItems.Add(item);
                }
            }
            foreach (var item in this._manualContextItems)
            {
                if (!this._suppressedContextLabels.Contains(item.Label))
                {
                    this._contextItems.Add(item);
                }
            }
            this.RefreshContextItems();

            this.EnsureHeroMessage(this.ResolveHeroAppLabel());
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "Failed to load parent context.");
        }
    }

    private void RefreshContextItems()
    {
        this._viewModel.ContextItems.Clear();
        this._viewModel.SensitiveContextItems.Clear();
        this._viewModel.ContextPills.Clear();
        var summaryParts = new List<string>();
        var sensitivePills = new List<ContextItemViewModel>();
        string? appDisplayLabel = null;
        foreach (var item in this._contextItems)
        {
            var preview = BuildContextPreview(item.Content);
            var iconGlyph = ResolveContextIconGlyph(item.Label);
            var displayLabel = ResolveContextDisplayLabel(item.Label, preview);
            if (!string.IsNullOrWhiteSpace(displayLabel))
            {
                appDisplayLabel = displayLabel;
            }
            if (!string.IsNullOrWhiteSpace(preview))
            {
                this._viewModel.ContextItems.Add(new ContextItemViewModel(item.Label, preview, iconGlyph, displayLabel));
            }

            if (IsSensitiveContextLabel(item.Label))
            {
                var sensitiveItem = new ContextItemViewModel(item.Label, item.Content, iconGlyph, displayLabel);
                this._viewModel.SensitiveContextItems.Add(sensitiveItem);
                sensitivePills.Add(sensitiveItem);
                continue;
            }

            var summaryPreview = BuildSummaryPreview(item.Content);
            if (!string.IsNullOrWhiteSpace(summaryPreview))
            {
                summaryParts.Add($"{item.Label}: {summaryPreview}");
            }
        }

        this._viewModel.ContextSummary = summaryParts.Count > 0
            ? string.Join(" | ", summaryParts)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(this._viewModel.ContextSummary))
        {
            var summaryGlyph = !string.IsNullOrWhiteSpace(appDisplayLabel) ? "\uE737" : "\uE946";
            this._viewModel.ContextPills.Add(new ContextItemViewModel(string.Empty, this._viewModel.ContextSummary, summaryGlyph, appDisplayLabel));
        }

        foreach (var item in sensitivePills)
        {
            this._viewModel.ContextPills.Add(item);
        }
    }

    private static string? ResolveContextIconGlyph(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var normalized = label.Trim();
        if (string.Equals(normalized, "App", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE737";
        }

        if (string.Equals(normalized, "Explorer folder", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8B7";
        }

        if (string.Equals(normalized, "Selected files", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8C8";
        }

        return null;
    }

    private static string? ResolveContextDisplayLabel(string label, string preview)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(preview))
        {
            return null;
        }

        return string.Equals(label, "App", StringComparison.OrdinalIgnoreCase) ? preview : null;
    }

    private static void RemoveContextItemsByLabel(List<ChatContextItem> items, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(items[i].Label, label, StringComparison.OrdinalIgnoreCase))
            {
                items.RemoveAt(i);
            }
        }
    }

    private static string BuildContextPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        const int maxLength = 140;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized.Substring(0, maxLength) + "...";
    }

    private static string BuildSummaryPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        const int maxLength = 60;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized.Substring(0, maxLength) + "...";
    }

    private static bool IsSensitiveContextLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var normalized = label.Trim().ToLowerInvariant();
        return normalized is "selection" or "selection text" or "selected files" or "explorer folder" or "input" or "input text" or "clipboard"
            || normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("attachment:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("selection", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("selected files", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("explorer folder", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("clipboard", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("input text", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string Expanded, string? Error)> TryExpandReferencesAsync(string text)
    {
        var result = text;
        var missing = new List<string>();

        if (result.Contains("#clipboard", StringComparison.OrdinalIgnoreCase))
        {
            var clip = await TryGetClipboardTextAsync() ?? string.Empty;
            result = result.Replace("#clipboard", clip, StringComparison.OrdinalIgnoreCase);
        }

        if (result.Contains("#input", StringComparison.OrdinalIgnoreCase))
        {
            var parentInput = this._textReader.GetFocusedInputText(this._parentHwnd);
            if (string.IsNullOrWhiteSpace(parentInput))
            {
                parentInput = this.TryGetContextValue("Input") ?? string.Empty;
            }

            // Last resort: Ctrl+A → Ctrl+C clipboard heist
            if (string.IsNullOrWhiteSpace(parentInput))
            {
                parentInput = await this.CaptureAllTextViaClipboardAsync();
            }

            if (string.IsNullOrWhiteSpace(parentInput))
            {
                missing.Add("input text");
            }
            else
            {
                result = result.Replace("#input", parentInput, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (result.Contains("#selection", StringComparison.OrdinalIgnoreCase))
        {
            var selection = this._textReader.GetFocusedSelectionText(this._parentHwnd);
            if (string.IsNullOrWhiteSpace(selection))
            {
                selection = this.TryGetContextValue("Selection") ?? string.Empty;
            }

            // Last resort: Ctrl+C clipboard heist (captures whatever is selected in the parent)
            if (string.IsNullOrWhiteSpace(selection))
            {
                this.EnsureHandle();
                selection = await this._clipboardCapture.CaptureSelectionViaClipboardAsync(this._parentHwnd, this._noteHwnd);
            }

            if (string.IsNullOrWhiteSpace(selection))
            {
                missing.Add("selection");
            }
            else
            {
                result = result.Replace("#selection", selection, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (missing.Count > 0)
        {
            return (result, BuildMissingReferenceMessage(missing));
        }

        return (result, null);
    }

    private static string BuildMissingReferenceMessage(IReadOnlyList<string> missing)
    {
        if (missing.Count == 1)
        {
            return $"Unable to read {missing[0]} from the source window. Focus it and try again.";
        }

        if (missing.Count == 2)
        {
            return $"Unable to read {missing[0]} and {missing[1]} from the source window. Focus it and try again.";
        }

        var allButLast = string.Join(", ", missing.Take(missing.Count - 1));
        return $"Unable to read {allButLast}, and {missing[^1]} from the source window. Focus it and try again.";
    }

    private string? TryGetContextValue(string label)
    {
        var item = this._contextItems.FirstOrDefault(context =>
            string.Equals(context.Label, label, StringComparison.OrdinalIgnoreCase));
        return item?.Content;
    }

    private static Task<string?> TryGetClipboardTextAsync()
    {
        return Task.FromResult(Win32Clipboard.GetText());
    }

    /// <summary>
    /// Last-resort input capture: activates the parent, sends Ctrl+A → Ctrl+C, reads clipboard,
    /// then sends Right arrow (to deselect), restores clipboard, and refocuses companion.
    /// </summary>
    private async Task<string> CaptureAllTextViaClipboardAsync()
    {
        this.EnsureHandle();
        if (!NativeMethods.IsWindow(this._parentHwnd))
        {
            return string.Empty;
        }

        // Save clipboard
        string? previousClip = null;
        var hadContent = false;
        if (Win32Clipboard.HasText())
        {
            previousClip = Win32Clipboard.GetText();
            hadContent = previousClip is not null;
        }

        if (!Win32Clipboard.Clear())
        {
            return string.Empty;
        }

        // Activate parent
        if (!await this.WaitForParentActivationAsync())
        {
            this.RestoreClipboardAndRefocus(previousClip, hadContent);
            return string.Empty;
        }

        await Task.Delay(150);

        // Ctrl+A to select all, then Ctrl+C to copy
        SendKeyCombo(VirtualKey.Control, VirtualKey.A);
        await Task.Delay(100);
        SendKeyCombo(VirtualKey.Control, (VirtualKey)0x43 /* C */);
        await Task.Delay(200);

        // Deselect by pressing Right arrow (moves caret, clears selection)
        SendSingleKey(VirtualKey.Right);

        // Read clipboard
        var captured = Win32Clipboard.GetText() ?? string.Empty;

        this.RestoreClipboardAndRefocus(previousClip, hadContent);
        return captured;
    }

    private async Task<bool> WaitForParentActivationAsync()
    {
        var targetThread = NativeMethods.GetWindowThreadProcessId(this._parentHwnd, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;
        try
        {
            if (currentThread != targetThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }

            NativeMethods.SetForegroundWindow(this._parentHwnd);
            NativeMethods.BringWindowToTop(this._parentHwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
        }

        return await WaitForForegroundAsync(this._parentHwnd, 1000);
    }

    private void RestoreClipboardAndRefocus(string? previousClip, bool hadContent)
    {
        if (hadContent && previousClip is not null)
        {
            Win32Clipboard.SetText(previousClip);
        }
        else if (!hadContent)
        {
            Win32Clipboard.Clear();
        }

        if (NativeMethods.IsWindow(this._noteHwnd))
        {
            NativeMethods.SetForegroundWindow(this._noteHwnd);
        }
    }

    private static void SendKeyCombo(VirtualKey modifier, VirtualKey key)
    {
        var inputs = new[]
        {
            CreateKeyInput(modifier, false), CreateKeyInput(key, false), CreateKeyInput(key, true),
            CreateKeyInput(modifier, true)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendSingleKey(VirtualKey key)
    {
        var inputs = new[] { CreateKeyInput(key, false), CreateKeyInput(key, true) };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }


    private static string GetLastToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// Waits until Ctrl, Shift, and V are physically released to prevent
    /// the target window from receiving a spurious keystroke.
    /// </summary>
    private static async Task WaitForModifierReleaseAsync()
    {
        const int VK_CONTROL = 0x11;
        const int VK_SHIFT = 0x10;
        const int VK_V = 0x56;

        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 1500)
        {
            var ctrl = (NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            var shift = (NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            var v = (NativeMethods.GetAsyncKeyState(VK_V) & 0x8000) != 0;

            if (!ctrl && !shift && !v)
            {
                return;
            }

            await Task.Delay(30);
        }
    }

    private async Task CopyLastAssistantResultAsync()
    {
        var last = this._viewModel.Messages.LastOrDefault(m => !m.IsUser && !m.IsHero);
        if (last is null)
        {
            return;
        }

        var text = ExtractResultSegment(last.Text);
        await this.CopyTextToClipboardAsync(text);
    }

    private Task CopyTextToClipboardAsync(string text)
    {
        Win32Clipboard.SetText(text);
        return Task.CompletedTask;
    }

    private static string ExtractResultSegment(string text)
    {
        const string startTag = "<result>";
        const string endTag = "</result>";
        var startIndex = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return text;
        }

        // Use the last closing tag to handle cases where the LLM includes multiple blocks
        var endIndex = text.LastIndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (endIndex <= startIndex)
        {
            return text;
        }

        var innerStart = startIndex + startTag.Length;
        return text.Substring(innerStart, endIndex - innerStart).Trim();
    }

    private async void OnCopyMessageClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message)
        {
            return;
        }

        await this.CopyTextToClipboardAsync(ExtractResultSegment(message.Text));
    }

    private void OnRetryMessageClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message || PromptTextBox is null)
        {
            return;
        }

        PromptTextBox.Text = message.Text;
        this.FocusPrompt();
    }

    private async void OnPasteToParentClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message)
        {
            return;
        }

        var text = ExtractResultSegment(message.Text);
        await this.CopyTextToClipboardAsync(text).ConfigureAwait(true);
        await this.PasteToParentAsync().ConfigureAwait(true);
    }

    private async void OnTypeToParentClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessage message)
        {
            return;
        }

        var text = ExtractResultSegment(message.Text);
        await this.TypeIntoParentAsync(text);
    }

    private async Task PasteToParentAsync()
    {
        // Wait for physical modifier keys to be released to avoid the parent
        // receiving a spurious paste from the user's original Ctrl+Shift+V keystroke.
        await WaitForModifierReleaseAsync();

        if (!await this.ActivateParentWindowAsync(async () =>
            {
                await Task.Delay(200);
                var inputs = new[]
                {
                    CreateKeyInput(VirtualKey.Control, false), CreateKeyInput(VirtualKey.V, false),
                    CreateKeyInput(VirtualKey.V, true), CreateKeyInput(VirtualKey.Control, true)
                };
                var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
                this.ReportSendInputResult("paste", sent, inputs.Length);
            }))
        {
            return;
        }
    }

    private async Task TypeIntoParentAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Wait for physical modifier keys to be released
        await WaitForModifierReleaseAsync();

        if (!await this.ActivateParentWindowAsync(async () =>
            {
                await Task.Delay(400);

                var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
                foreach (var ch in text)
                {
                    inputs.Add(CreateUnicodeInput(ch, false));
                    inputs.Add(CreateUnicodeInput(ch, true));
                }

                if (inputs.Count > 0)
                {
                    var sent = NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(),
                        Marshal.SizeOf<NativeMethods.INPUT>());
                    this.ReportSendInputResult("type", sent, inputs.Count);
                }
            }))
        {
            return;
        }
    }

    private async Task<bool> ActivateParentWindowAsync(Func<Task> action)
    {
        this.EnsureHandle();
        if (!NativeMethods.IsWindow(this._parentHwnd))
        {
            return false;
        }

        this.ApplyTopMost(false, this._parentHwnd);
        var targetThread = NativeMethods.GetWindowThreadProcessId(this._parentHwnd, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;
        try
        {
            if (currentThread != targetThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }

            var success = NativeMethods.SetForegroundWindow(this._parentHwnd);
            NativeMethods.BringWindowToTop(this._parentHwnd);
            NativeMethods.SetFocus(this._parentHwnd);
            var effective = success || NativeMethods.GetForegroundWindow() == this._parentHwnd;

            if (!effective)
            {
                return false;
            }
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
        }

        if (!await WaitForForegroundAsync(this._parentHwnd, 1200))
        {
            return false;
        }

        await action();

        if (NativeMethods.GetForegroundWindow() == this._parentHwnd)
        {
            this.ApplyTopMost(true, IntPtr.Zero);
        }

        return true;
    }

    private static async Task<bool> WaitForForegroundAsync(IntPtr hwnd, int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    private void ReportSendInputResult(string action, uint sent, int expected)
    {
        if (sent == expected)
        {
            this._logger?.LogDebug("SendInput {Action}: {Sent}/{Expected} events sent.", action, sent, expected);
            return;
        }

        var error = Marshal.GetLastWin32Error();
        this._logger?.LogWarning("SendInput {Action} failed ({Sent}/{Expected}). Win32 error {Error}.", action, sent,
            expected, error);
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

    private static NativeMethods.INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private void HideForParentMinimize()
    {
        if (this._hiddenByParentMinimize)
        {
            return;
        }

        this._hiddenByParentMinimize = true;
        this._suppressMoveEvents = true;
        this.AppWindow.Hide();
        this._suppressMoveEvents = false;
    }

    private void ShowAfterParentRestore(NativeMethods.RECT parentRect)
    {
        if (!this._hiddenByParentMinimize)
        {
            return;
        }

        this._hiddenByParentMinimize = false;
        this._suppressMoveEvents = true;
        this.AppWindow.Show();
        this._suppressMoveEvents = false;

        if (this._followEnabled)
        {
            this.PositionRelativeTo(parentRect);
        }
        else
        {
            this.UpdateZOrderOnly();
        }
    }

    private async void OnPromptPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            this.HandleEscapeKey();
            e.Handled = true;
            return;
        }

        if (await this.TryHandlePasteShortcutAsync(e))
        {
            return;
        }

        if (this.TryHandleReservedShortcut(e))
        {
            return;
        }
    }
}

public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not int index)
        {
            return Visibility.Collapsed;
        }

        var targetIndex = 0;
        if (parameter is string paramText && int.TryParse(paramText, out var parsed))
        {
            targetIndex = parsed;
        }

        return index == targetIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DefaultTemplate { get; set; }

    public DataTemplate? HeroTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is ChatMessage { IsHero: true })
        {
            return this.HeroTemplate ?? this.DefaultTemplate ?? base.SelectTemplateCore(item, container);
        }

        return this.DefaultTemplate ?? base.SelectTemplateCore(item, container);
    }

}

public sealed class BooleanToHorizontalAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isUser = value is true;
        return isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class ChatBubbleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isUser = value is true;
        var resources = Application.Current.Resources;
        var otherBrush = resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var layer)
            ? layer as Brush
            : null;
        return isUser ? new SolidColorBrush(Colors.Black) : otherBrush ?? new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class ChatBubbleForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isUser = value is true;
        if (isUser)
        {
            return new SolidColorBrush(Colors.White);
        }

        var resources = Application.Current.Resources;
        var textBrush = resources.TryGetValue("TextFillColorPrimaryBrush", out var text) ? text as Brush : null;
        return textBrush ?? new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class FollowGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isFollowing = value is true;
        return isFollowing ? "\uE718" : "\uE77A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class SendGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isSending = value is true;
        return isSending ? "\uE71A" : "\uE122";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
