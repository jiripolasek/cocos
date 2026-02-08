using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using JPSoftworks.Cocos.Interop;
using JPSoftworks.Cocos.Services.Chat;
using JPSoftworks.Cocos.Services.Context;
using JPSoftworks.Cocos.Services.Settings;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Companion;

internal sealed class StickyNoteManager : IDisposable
{
    private readonly List<StickyNoteWindow> _notes = [];
    private readonly DispatcherTimer _topMostTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StickyNoteManager> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IReadOnlyList<IChatService> _chatServices;
    private readonly ICompanionDataStore _dataStore;
    private readonly IParentContextService _contextService;
    private readonly IParentWindowTextReader _textReader;
    private readonly IClipboardTextCaptureService _clipboardCapture;
    private readonly ICompanionAppearanceProvider _appearanceProvider;
    private bool _disposed;
    private bool _singleWindowMode;
    private CompanionCornerPreference _cornerPreference;

    public event EventHandler<int>? ActiveNotesChanged;

    public void CreateStickyNoteForActiveWindow(bool showNotes = false)
    {
        var parent = NativeMethods.GetForegroundWindow();
        if (parent == IntPtr.Zero || !NativeMethods.IsWindow(parent))
        {
            return;
        }

        var activeNote = this._notes.FirstOrDefault(n => n.OwnsWindow(parent));
        if (activeNote is not null)
        {
            if (showNotes)
            {
                activeNote.ShowNotesTab();
            }
            return;
        }

        if (!NativeMethods.GetWindowRect(parent, out var rect))
        {
            return;
        }

        //if (_singleWindowMode)
        //{
        //    foreach (var note2 in _notes.ToList())
        //    {
        //        note2.Close();
        //    }
        //}

        var existing = this._notes.FirstOrDefault(n => n.MatchesParent(parent));
        if (existing is not null)
        {
            existing.Summon(rect);
            if (showNotes)
            {
                existing.ShowNotesTab();
            }
            return;
        }

        var parentInfo = this.BuildParentInfo(parent);
        var scope = this.BuildScope(parentInfo);
        var session = this.EnsureSessionAppearance(this._dataStore.GetOrCreateSession(scope));
        var notesSession = this._dataStore.GetOrCreateSession(this.BuildAppScope(parentInfo));
        var noteLogger = this._loggerFactory.CreateLogger<StickyNoteWindow>();
        var note = new StickyNoteWindow(
            parentInfo,
            rect,
            noteLogger,
            this._settingsService,
            this._chatServices,
            this._dataStore,
            this._contextService,
            this._textReader,
            this._clipboardCapture,
            this._appearanceProvider,
            session,
            notesSession,
            this._cornerPreference);
        note.Closed += this.OnNoteClosed;
        this._notes.Add(note);
        note.Activate();
        if (showNotes)
        {
            note.ShowNotesTab();
        }
        note.StartTracking();
        this.EnsureOnlyActiveTopMost();
        this.ActiveNotesChanged?.Invoke(this, this._notes.Count);
    }

    public bool SingleWindowMode
    {
        get => this._singleWindowMode;
        set => this._singleWindowMode = value;
    }

    private void OnNoteClosed(object sender, WindowEventArgs e)
    {
        if (sender is StickyNoteWindow note)
        {
            this._notes.Remove(note);
            this.ActiveNotesChanged?.Invoke(this, this._notes.Count);
        }
    }

    public int ActiveCount => this._notes.Count;

    public StickyNoteManager(
        ILoggerFactory loggerFactory,
        ISettingsService settingsService,
        IEnumerable<IChatService> chatServices,
        ICompanionDataStore dataStore,
        IParentContextService contextService,
        IParentWindowTextReader textReader,
        IClipboardTextCaptureService clipboardCapture,
        ICompanionAppearanceProvider appearanceProvider)
    {
        this._loggerFactory = loggerFactory;
        this._logger = loggerFactory.CreateLogger<StickyNoteManager>();
        this._settingsService = settingsService;
        this._chatServices = chatServices.ToList();
        this._dataStore = dataStore;
        this._contextService = contextService;
        this._textReader = textReader;
        this._clipboardCapture = clipboardCapture;
        this._appearanceProvider = appearanceProvider;
        this.ApplySettings(settingsService.Settings);
        this._settingsService.SettingsChanged += this.OnSettingsChanged;
        this._topMostTimer.Tick += this.OnTopMostTick;
        this._topMostTimer.Start();
    }

    private void OnTopMostTick(object? sender, object e) => this.EnsureOnlyActiveTopMost();

    private void EnsureOnlyActiveTopMost()
    {
        if (this._notes.Count == 0)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var active = this._notes.FirstOrDefault(n => n.MatchesParent(foreground) || n.OwnsWindow(foreground));
        var anchor = active?.ParentHandle ?? IntPtr.Zero;

        foreach (var note in this._notes.ToList())
        {
            var isActive = note == active;
            note.ApplyTopMost(isActive, isActive ? IntPtr.Zero : anchor);
        }
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._settingsService.SettingsChanged -= this.OnSettingsChanged;
        this._topMostTimer.Stop();
        this._topMostTimer.Tick -= this.OnTopMostTick;
        foreach (var note in this._notes.ToList())
        {
            note.Closed -= this.OnNoteClosed;
            note.Close();
        }

        this._notes.Clear();
        this._disposed = true;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => this.ApplySettings(settings);

    private void ApplySettings(AppSettings settings)
    {
        this._singleWindowMode = settings.SingleWindowMode;
        this._cornerPreference = settings.CornerPreference;
        foreach (var note in this._notes.ToList())
        {
            note.ApplyCornerPreference(this._cornerPreference);
        }
    }

    private ParentWindowInfo BuildParentInfo(IntPtr parent)
    {
        NativeMethods.GetWindowThreadProcessId(parent, out var processId);
        var appName = this.ResolveProcessName(processId);
        var windowTitle = GetWindowTitle(parent);
        return new ParentWindowInfo(parent, processId, appName, windowTitle);
    }

    private CompanionScope BuildScope(ParentWindowInfo info)
    {
        var scopeKey = $"{info.ProcessId}:{info.Handle}";
        return new CompanionScope(CompanionScopeType.Window, scopeKey, info.ProcessName, info.WindowTitle);
    }

    private CompanionScope BuildAppScope(ParentWindowInfo info)
    {
        var key = string.IsNullOrWhiteSpace(info.ProcessName)
            ? info.ProcessId.ToString(CultureInfo.InvariantCulture)
            : info.ProcessName.ToLowerInvariant();
        var title = string.IsNullOrWhiteSpace(info.ProcessName) ? "App" : info.ProcessName;
        return new CompanionScope(CompanionScopeType.App, key, info.ProcessName, title);
    }

    private CompanionSession EnsureSessionAppearance(CompanionSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.Emoji) && !string.IsNullOrWhiteSpace(session.AccentColor))
        {
            return session;
        }

        var option = this._appearanceProvider.FindByEmoji(session.Emoji) ?? this._appearanceProvider.GetRandom();
        var accentHex = CompanionAppearanceSerializer.ToHex(option.AccentColor);
        return this._dataStore.UpdateSessionAppearance(session.Id, option.Emoji, accentHex);
    }

    private string ResolveProcessName(uint processId)
    {
        if (processId == 0)
        {
            return "unknown";
        }

        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            this._logger.LogDebug("Process {ProcessId} no longer exists.", processId);
        }
        catch (InvalidOperationException)
        {
            this._logger.LogDebug("Process {ProcessId} is not available.", processId);
        }
        catch (Win32Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to resolve process name for {ProcessId}.", processId);
        }

        return $"pid:{processId}";
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var length = NativeMethods.GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }
}
