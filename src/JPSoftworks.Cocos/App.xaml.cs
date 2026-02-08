using System.Text;
using Windows.System;
using H.NotifyIcon;
using JPSoftworks.Cocos.Interop;
using JPSoftworks.Cocos.Services.Chat;
using JPSoftworks.Cocos.Services.Companion;
using JPSoftworks.Cocos.Services.Context;
using JPSoftworks.Cocos.Services.HotKeys;
using JPSoftworks.Cocos.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace JPSoftworks.Cocos;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private HotKeyManager? _hotKeyManager;
    private HotKeyManager? _notesHotKeyManager;
    private StickyNoteManager? _noteManager;
    private TaskbarIcon? _trayIcon;
    private bool _shuttingDown;
    private ServiceProvider? _serviceProvider;
    private OobeWindow? _oobeWindow;
    private int _unhandledReported;

    internal Window? MainWindow => this._mainWindow;
    internal StickyNoteManager? NoteManager => this._noteManager;
    internal IServiceProvider Services => this._serviceProvider ?? throw new InvalidOperationException("Services not initialized.");

    public App()
    {
        InitializeComponent();
        this._serviceProvider = ConfigureServices();
        this.RegisterGlobalExceptionHandlers();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        this._mainWindow = new MainWindow();
        if (this._mainWindow is MainWindow mainWindow)
        {
            mainWindow.Initialize(args.Arguments);
        }

        this._mainWindow.Activate();
        this._mainWindow.AppWindow.Hide();
        this._mainWindow.Closed += this.OnMainWindowClosed;

        this._noteManager = this.Services.GetRequiredService<StickyNoteManager>();
        var settingsService = this.Services.GetRequiredService<ISettingsService>();
        this._noteManager.SingleWindowMode = settingsService.Settings.SingleWindowMode;
        this._hotKeyManager = HotKeyManager.Register(this._mainWindow, NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)VirtualKey.K, this.OnHotKeyPressed);
        this._notesHotKeyManager = HotKeyManager.Register(this._mainWindow, NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, (uint)VirtualKey.J, this.OnNotesHotKeyPressed);

        this.InitializeTrayIcon();

        // TODO: remove later - always show OOBE on startup.
        this.ShowOobeWindow();
    }

    private void InitializeTrayIcon()
    {
        this._trayIcon = new TaskbarIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            ToolTipText = "Sticky Companion"
        };

        var flyout = new MenuFlyout();

        var showMainItem = new MenuFlyoutItem { Text = "Show main window" };
        showMainItem.Click += (_, _) => this.ShowMainWindow();
        flyout.Items.Add(showMainItem);

        var openItem = new MenuFlyoutItem { Text = "Open settings" };
        openItem.Click += (_, _) => this.ShowMainWindow();
        flyout.Items.Add(openItem);

        var introItem = new MenuFlyoutItem { Text = "Show intro" };
        introItem.Click += (_, _) => this.ShowOobeWindow();
        flyout.Items.Add(introItem);

        var newItem = new MenuFlyoutItem { Text = "New sticky (Win+Shift+K)" };
        newItem.Click += (_, _) => this._noteManager?.CreateStickyNoteForActiveWindow();
        flyout.Items.Add(newItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += this.OnExitClicked;
        flyout.Items.Add(exitItem);

        if (this._mainWindow?.Content is FrameworkElement root)
        {
            flyout.XamlRoot = root.XamlRoot;
        }

        this._trayIcon.ContextFlyout = flyout;
        this._trayIcon.ForceCreate();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        this.Shutdown();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        this.Shutdown();
    }

    private void OnHotKeyPressed()
    {
        this._mainWindow?.DispatcherQueue.TryEnqueue(() => this._noteManager?.CreateStickyNoteForActiveWindow());
    }

    private void OnNotesHotKeyPressed()
    {
        this._mainWindow?.DispatcherQueue.TryEnqueue(() => this._noteManager?.CreateStickyNoteForActiveWindow(showNotes: true));
    }

    internal void ShowMainWindow()
    {
        if (this._mainWindow is null)
        {
            return;
        }

        this._mainWindow.AppWindow.Show();
        this._mainWindow.Activate();
    }

    internal void ShowOobeWindow()
    {
        if (this._oobeWindow is null)
        {
            this._oobeWindow = new OobeWindow();
            this._oobeWindow.Closed += (_, _) => this._oobeWindow = null;
        }

        this._oobeWindow.Activate();
    }

    internal void Shutdown()
    {
        if (this._shuttingDown)
        {
            return;
        }

        this._shuttingDown = true;
        this._notesHotKeyManager?.Dispose();
        this._hotKeyManager?.Dispose();
        this._noteManager?.Dispose();
        this._trayIcon?.Dispose();
        this._serviceProvider?.Dispose();
        Log.CloseAndFlush();

        this._mainWindow?.Close();
        this.Exit();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        ConfigureLogging(services);
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICompanionDataStore, SqliteCompanionDataStore>();
        services.AddSingleton<ICompanionAppearanceProvider, CompanionAppearanceProvider>();
        services.AddSingleton<IParentWindowTextReader, ParentWindowTextReader>();
        services.AddSingleton<IClipboardTextCaptureService, ClipboardTextCaptureService>();
        services.AddSingleton<IParentContextProvider, ExplorerContextProvider>();
        services.AddSingleton<IParentContextProvider, DefaultContextProvider>();
        services.AddSingleton<IParentContextService, ParentContextService>();
        services.AddSingleton<IChatService, OllamaChatService>();
        services.AddSingleton<IChatService, OpenAiChatService>();
        services.AddSingleton<StickyNoteManager>();
        return services.BuildServiceProvider();
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        var logDirectory = GetAppDataDirectory();
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
        });
    }

    internal static string GetAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JPSoftworks",
            "CoCos");
    }

    internal static string GetLogDirectory()
    {
        return GetAppDataDirectory();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        this.UnhandledException += this.OnAppUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += this.OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += this.OnUnobservedTaskException;
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        this.ReportUnhandledException(e.Exception, "WinUI");
    }

    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new Exception($"Unhandled exception: {e.ExceptionObject}");
        this.ReportUnhandledException(exception, "AppDomain");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        this.ReportUnhandledException(e.Exception, "TaskScheduler");
        e.SetObserved();
    }

    private void ReportUnhandledException(Exception exception, string source)
    {
        if (Interlocked.Exchange(ref this._unhandledReported, 1) == 1)
        {
            return;
        }

        var reportPath = WriteCrashReport(exception, source);
        var message = $"An unexpected error occurred.\n\nDetails saved to:\n{reportPath}";
        NativeMethods.MessageBox(IntPtr.Zero, message, "Sticky Companion Error", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
    }

    private static string WriteCrashReport(Exception exception, string source)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var fileName = $"StickyCompanion_error_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.txt";
        var path = Path.Combine(desktop, fileName);

        var builder = new StringBuilder();
        builder.AppendLine("Sticky Companion crash report");
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"AppVersion: {typeof(App).Assembly.GetName().Version}");
        builder.AppendLine($"ProcessPath: {Environment.ProcessPath}");
        builder.AppendLine($"OSVersion: {Environment.OSVersion}");
        builder.AppendLine($".NET: {Environment.Version}");
        builder.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
        builder.AppendLine($"Machine: {Environment.MachineName}");
        builder.AppendLine($"User: {Environment.UserName}");
        builder.AppendLine($"CurrentDirectory: {Environment.CurrentDirectory}");
        builder.AppendLine($"CommandLine: {Environment.CommandLine}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());

        File.WriteAllText(path, builder.ToString());
        return path;
    }

}
