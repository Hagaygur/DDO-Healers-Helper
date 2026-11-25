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

    internal IHealerEngineView? Engine => Volatile.Read(ref _engine);
    internal BannerHudController? Hud => Volatile.Read(ref _hud);

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

        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dungeon Helper",
                "HealersHelper");
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
