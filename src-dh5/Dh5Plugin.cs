using System;
using System.IO;
using System.Threading;
using VoK.Sdk;
using VoK.Sdk.Ddo;

namespace VoK.HealersHelper;

[PluginId("54b48548-05ff-48cf-bce1-c6270e3d4a3f")]
[PluginName("Healers Helper")]
[PluginIcon("Icon.png")]
public sealed class Dh5Plugin : IPlugin
{
    private Dh5HealerEngine? _engine;
    private Dh5HealersHelperUi? _ui;
    private string _settingsFolder = DefaultSettingsFolder();

    // Icon comes from the [PluginIcon("Icon.png")] embedded resource so DH 5 displays it
    // without loading the plugin; ToolbarImage remains null unless overriding dynamically.
    public byte[]? ToolbarImage => null;

    // Must explicitly override IPlugin.WindowVisible (default interface impl ignores sets and returns true).
    public bool WindowVisible { get; set; } = false;

    internal Dh5HealerEngine? Engine => Volatile.Read(ref _engine);

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

    internal static string DefaultSettingsFolder()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        return Path.Combine(localAppData, "DungeonHelperData", "plugins", "Healers Helper");
    }

    public void Initialize(IDdoGameDataProvider gameDataProvider, IDdoEventProvider eventProvider, string pluginFolder)
    {
        ArgumentNullException.ThrowIfNull(gameDataProvider);
        ArgumentNullException.ThrowIfNull(eventProvider);

        Interlocked.Exchange(ref _ui, null)?.Dispose();
        Interlocked.Exchange(ref _engine, null)?.Dispose();

        string resolvedFolder = string.IsNullOrWhiteSpace(pluginFolder)
            ? SettingsFolder
            : pluginFolder;
        SettingsFolder = resolvedFolder;

        var engine = new Dh5HealerEngine(gameDataProvider, eventProvider, resolvedFolder);
        var ui = new Dh5HealersHelperUi(this, engine);
        Volatile.Write(ref _engine, engine);
        Volatile.Write(ref _ui, ui);
    }

    public void ClientFrameStep()
    {
        // Keep ClientFrameStep strictly non-blocking: world polling runs on Dh5HealerEngine's
        // async ThreadPool timer so frame-marshaled SDK calls can never deadlock DDO's client loop.
    }

    public void ImGui_Render()
    {
        var ui = Volatile.Read(ref _ui);
        if (ui is null)
            return;

        ui.RenderFrame();
    }

    public bool ResetUi()
    {
        var engine = Volatile.Read(ref _engine);
        if (engine is null)
            return false;

        var s = engine.GetSettings();
        s.HudX = 0.50;
        s.HudY = 0.42;
        s.StatusHudX = 0.50;
        s.StatusHudY = 0.76;
        s.RaidHudX = 0.02;
        s.RaidHudY = 0.25;
        engine.SaveSettings(s);
        return true;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _ui, null)?.Dispose();
        Interlocked.Exchange(ref _engine, null)?.Dispose();
    }
}
