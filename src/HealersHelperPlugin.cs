using System;
using System.IO;
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

    internal IHealerEngineView? Engine => _engine;
    internal BannerHudController? Hud => _hud;

    public IPluginUI GetPluginUI() => _ui ??= new HealersHelperUi(this);

    public void Initialize(IDdoGameDataProvider gameDataProvider, string folder)
    {
        _hud?.Dispose();
        _engine?.Dispose();

        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dungeon Helper",
                "HealersHelper");
        }

        _engine = new HealerEngine(gameDataProvider, folder);
        _hud = new BannerHudController(_engine);
    }

    public void Terminate()
    {
        _hud?.Dispose();
        _hud = null;
        _engine?.Dispose();
        _engine = null;
        _ui?.Dispose();
        _ui = null;
    }
}
