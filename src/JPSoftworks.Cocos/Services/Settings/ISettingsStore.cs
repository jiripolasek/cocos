namespace JPSoftworks.Cocos.Services.Settings;

internal interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
