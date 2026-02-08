using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Settings;

internal sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly string _settingsPath;

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
    {
        this._logger = logger;
        var directory = App.GetAppDataDirectory();
        Directory.CreateDirectory(directory);
        this._settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(this._settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(this._settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            this._logger.LogError(ex, "Failed to parse settings file {SettingsPath}.", this._settingsPath);
            return new AppSettings();
        }
        catch (IOException ex)
        {
            this._logger.LogError(ex, "Failed to read settings file {SettingsPath}.", this._settingsPath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogError(ex, "Access denied reading settings file {SettingsPath}.", this._settingsPath);
            throw;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _options);
            File.WriteAllText(this._settingsPath, json);
        }
        catch (IOException ex)
        {
            this._logger.LogError(ex, "Failed to write settings file {SettingsPath}.", this._settingsPath);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogError(ex, "Access denied writing settings file {SettingsPath}.", this._settingsPath);
            throw;
        }
    }
}
