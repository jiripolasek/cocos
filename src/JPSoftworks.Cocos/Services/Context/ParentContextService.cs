using System.Reflection;
using System.Runtime.InteropServices;
using JPSoftworks.Cocos.Services.Chat;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Context;

internal sealed record ParentWindowInfo(IntPtr Handle, uint ProcessId, string ProcessName, string WindowTitle);

internal interface IParentContextService
{
    Task<IReadOnlyList<ChatContextItem>> GetContextAsync(ParentWindowInfo info, CancellationToken cancellationToken);
}

internal interface IParentContextProvider
{
    int Order { get; }

    bool CanHandle(ParentWindowInfo info);

    Task<IReadOnlyList<ChatContextItem>> GetContextAsync(ParentWindowInfo info, CancellationToken cancellationToken);
}

internal sealed class ParentContextService : IParentContextService
{
    private readonly IReadOnlyList<IParentContextProvider> _providers;
    private readonly ILogger<ParentContextService> _logger;

    public ParentContextService(IEnumerable<IParentContextProvider> providers, ILogger<ParentContextService> logger)
    {
        this._logger = logger;
        this._providers = providers.OrderBy(p => p.Order).ToList();
    }

    public async Task<IReadOnlyList<ChatContextItem>> GetContextAsync(ParentWindowInfo info, CancellationToken cancellationToken)
    {
        var items = new List<ChatContextItem>();
        foreach (var provider in this._providers)
        {
            if (!provider.CanHandle(info))
            {
                continue;
            }

            var providerItems = await provider.GetContextAsync(info, cancellationToken).ConfigureAwait(false);
            if (providerItems.Count > 0)
            {
                items.AddRange(providerItems);
            }
        }

        this._logger.LogDebug("Context providers returned {Count} items for {Process}.", items.Count, info.ProcessName);
        return items;
    }
}

internal sealed class DefaultContextProvider : IParentContextProvider
{
    private readonly IParentWindowTextReader _textReader;

    public DefaultContextProvider(IParentWindowTextReader textReader)
    {
        this._textReader = textReader;
    }

    public int Order => 100;

    public bool CanHandle(ParentWindowInfo info) => true;

    public Task<IReadOnlyList<ChatContextItem>> GetContextAsync(ParentWindowInfo info, CancellationToken cancellationToken)
    {
        var items = new List<ChatContextItem>();
        if (!string.IsNullOrWhiteSpace(info.WindowTitle))
        {
            items.Add(new ChatContextItem("Window", info.WindowTitle));
        }

        if (!string.IsNullOrWhiteSpace(info.ProcessName))
        {
            items.Add(new ChatContextItem("App", info.ProcessName));
        }

        var selection = this._textReader.GetFocusedSelectionText(info.Handle);
        if (!string.IsNullOrWhiteSpace(selection))
        {
            items.Add(new ChatContextItem("Selection", selection));
        }

        var input = this._textReader.GetFocusedInputText(info.Handle);
        if (!string.IsNullOrWhiteSpace(input))
        {
            items.Add(new ChatContextItem("Input", input));
        }

        return Task.FromResult<IReadOnlyList<ChatContextItem>>(items);
    }
}

internal sealed class ExplorerContextProvider : IParentContextProvider
{
    private readonly ILogger<ExplorerContextProvider> _logger;

    public ExplorerContextProvider(ILogger<ExplorerContextProvider> logger)
    {
        this._logger = logger;
    }

    public int Order => 0;

    public bool CanHandle(ParentWindowInfo info)
    {
        return string.Equals(info.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.ProcessName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<ChatContextItem>> GetContextAsync(ParentWindowInfo info, CancellationToken cancellationToken)
    {
        var items = new List<ChatContextItem>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return Task.FromResult<IReadOnlyList<ChatContextItem>>(items);
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            var count = (int)windows.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic window = windows.Item(i);
                var hwnd = (int)window.HWND;
                if (hwnd != info.Handle.ToInt32())
                {
                    continue;
                }

                dynamic? document = window.Document;
                dynamic? folder = document?.Folder;
                var folderPath = folder?.Self?.Path as string;
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    items.Add(new ChatContextItem("Explorer folder", folderPath));
                }

                dynamic? selected = document?.SelectedItems();
                if (selected is not null)
                {
                    var selectedCount = (int)selected.Count;
                    if (selectedCount > 0)
                    {
                        var paths = new List<string>();
                        for (var j = 0; j < selectedCount; j++)
                        {
                            dynamic item = selected.Item(j);
                            var path = item?.Path as string;
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                paths.Add(path);
                            }
                        }

                        if (paths.Count > 0)
                        {
                            items.Add(new ChatContextItem("Selected files", string.Join(Environment.NewLine, paths)));
                        }
                    }
                }

                break;
            }
        }
        catch (COMException ex)
        {
            this._logger.LogDebug(ex, "Failed to query Explorer context.");
        }
        catch (InvalidCastException ex)
        {
            this._logger.LogDebug(ex, "Failed to query Explorer context.");
        }
        catch (TargetInvocationException ex)
        {
            this._logger.LogDebug(ex, "Failed to query Explorer context.");
        }
        catch (RuntimeBinderException ex)
        {
            this._logger.LogDebug(ex, "Failed to query Explorer context.");
        }

        return Task.FromResult<IReadOnlyList<ChatContextItem>>(items);
    }
}
