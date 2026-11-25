using System;
using System.IO;
using System.Threading;
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Plugins;

namespace VoK.HealersHelper;

public sealed class HealersHelperPlugin : IDdoPlugin
{
    public Guid PluginId => new("9b6c8e42-3d19-4f7a-9c54-7e21d804a6b3");
    public GameId Game => GameId.DDO;
    public string PluginKey => "ddo-healers-helper";
    public string Name => "DDO Healer's Helper";
    public string Description => "Large flashing center low-HP alert banner and quick-target plugin in Dungeon Helper";
    public string Author => "Community";
    public Version Version => typeof(HealersHelperPlugin).Assembly.GetName().Version!;

    private HealerEngine? _engine;
    private HealersHelperUi? _ui;
    private BannerHudController? _hud;
    private string _settingsFolder = DefaultSettingsFolder();

    internal IHealerEngineView? Engine => Volatile.Read(ref _engine);
    internal BannerHudController? Hud => Volatile.Read(ref _hud);

    internal string SettingsFolder
    {
        get
        {
            string current = Volatile.Read(ref _settingsFolder);
            if (!string.IsNullOrWhiteSpace(current))
                return current;

            string fallback = DefaultSettingsFolder();
            Volatile.Write(ref _settingsFolder, fallback);
            return fallback;
        }
        set => Volatile.Write(ref _settingsFolder, string.IsNullOrWhiteSpace(value) ? DefaultSettingsFolder() : value);
    }

    private static string DefaultSettingsFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dungeon Helper",
        "plugins",
        "HealersHelper");

    public IPluginUI GetPluginUI()
    {
        var existing = Volatile.Read(ref _ui);
        if (existing is not null) return existing;
        var created = new HealersHelperUi(this);
        return Interlocked.CompareExchange(ref _ui, created, null) ?? created;
    }

    public void Initialize(IDdoGameDataProvider gameDataProvider, string folder)
    {
        Interlocked.Exchange(ref _hud, null)?.Dispose();
        Interlocked.Exchange(ref _engine, null)?.Dispose();

        if (!string.IsNullOrWhiteSpace(folder))
        {
            SettingsFolder = folder;
        }
        else
        {
            folder = SettingsFolder;
        }

        var engine = new HealerEngine(gameDataProvider, folder);
        var hud = new BannerHudController(engine);
        Volatile.Write(ref _engine, engine);
        Volatile.Write(ref _hud, hud);
    }

    public void Terminate()
    {
        Interlocked.Exchange(ref _hud, null)?.Dispose();
        Interlocked.Exchange(ref _engine, null)?.Dispose();
        Interlocked.Exchange(ref _ui, null)?.Dispose();
    }
}
