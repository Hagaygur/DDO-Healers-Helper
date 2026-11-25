using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: WindowsSmoke <plugin-zip-or-dll> <dungeon-helper-dir>");

        string pluginPath = Path.GetFullPath(args[0]);
        string sdkPath = Path.Combine(Path.GetFullPath(args[1]), "VoK.Sdk.dll");
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "VoK.Sdk"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(sdkPath)
            : null;

        Assembly pluginAssembly = LoadPlugin(pluginPath);
        if (pluginAssembly.GetCustomAttribute<SupportedOSPlatformAttribute>()?.PlatformName != "windows7.0")
            throw new Exception("Plugin must declare [SupportedOSPlatform(\"windows7.0\")]");

        Assembly sdk = Assembly.LoadFrom(sdkPath);
        Type ddoPluginInterface = sdk.GetType("VoK.Sdk.Ddo.IDdoPlugin")!;
        Type pluginType = pluginAssembly.GetTypes().Single(t => t.IsPublic && !t.IsAbstract && ddoPluginInterface.IsAssignableFrom(t));
        object plugin = Activator.CreateInstance(pluginType)!;

        Guid pluginId = (Guid)pluginType.GetProperty("PluginId")!.GetValue(plugin)!;
        string pluginKey = (string)pluginType.GetProperty("PluginKey")!.GetValue(plugin)!;
        Version runtimeVersion = (Version)pluginType.GetProperty("Version")!.GetValue(plugin)!;
        if (pluginId != new Guid("9b6c8e42-3d19-4f7a-9c54-7e21d804a6b3"))
            throw new Exception($"Unexpected PluginId: {pluginId}");
        if (pluginKey != "ddo-healers-helper")
            throw new Exception($"Unexpected PluginKey: {pluginKey}");
        if (runtimeVersion != pluginAssembly.GetName().Version)
            throw new Exception("Runtime Version disagrees with assembly version");

        object ui = pluginType.GetMethod("GetPluginUI")!.Invoke(plugin, null)!;
        Type uiContract = sdk.GetType("VoK.Sdk.Plugins.IPluginUI")!;
        using var host = new Form { Width = 400, Height = 400 };
        var strip = new Panel { Width = 720, Height = 45 };
        strip.Controls.Add(new PictureBox { Name = (string)pluginType.GetProperty("Name")!.GetValue(plugin)!, Left = 680, Width = 36 });
        host.Controls.Add(strip);
        host.Show();

        using var form = (Form)uiContract.GetProperty("UserInterfaceForm")!.GetValue(ui)!;
        // First verify at minimum width (380px) before 720px toolbar expansion.
        form.Size = form.MinimumSize;
        form.PerformLayout();
        CheckLayoutControls(form, form);

        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        if (form.Width < strip.Width)
            throw new Exception($"Plugin form width {form.Width} < host toolbar strip width {strip.Width}");
        CheckLayoutControls(form, form);

        // Verify expected panel controls & labels exist and are visible.
        var badge = (Label)form.Controls.Find("PluginStateLabel", true).Single();
        var savedLabel = (Label)form.Controls.Find("PeopleSavedLabel", true).Single();
        if (!badge.Visible || !savedLabel.Visible || form.Controls.Find("MoveStatusButton", true).Length != 1 || form.Controls.Find("ResetSavedButton", true).Length != 1)
            throw new Exception("Expected PluginStateLabel, PeopleSavedLabel, MoveStatusButton, and ResetSavedButton on panel");

        if (ui is IDisposable disposableUi)
            disposableUi.Dispose();
        Console.WriteLine("PASS Windows plugin ZIP load, IDdoPlugin metadata, and 380px/720px toolbar UI layout");

        CheckSettingsDialogAndHuds(pluginAssembly);
    }

    private static void CheckSettingsDialogAndHuds(Assembly assembly)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Type engineType = assembly.GetType("VoK.HealersHelper.HealerEngine")!;
        object engine = RuntimeHelpers.GetUninitializedObject(engineType);
        void Set(string name, object value) => engineType.GetField(name, fields)!.SetValue(engine, value);

        string folder = Path.Combine(Path.GetTempPath(), "healers-helper-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            Type settingsType = assembly.GetType("VoK.HealersHelper.Settings")!;
            object settings = Activator.CreateInstance(settingsType)!;
            settingsType.GetProperty("PeopleSaved")!.SetValue(settings, 5);
            Type storeType = assembly.GetType("VoK.HealersHelper.SettingsStore")!;
            object store = Activator.CreateInstance(storeType, folder)!;

            Set("_gate", new object());
            Set("_settings", settings);
            Set("_store", store);
            Set("_state", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.HealerState")!)!);
            Set("_savedTracker", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.PeopleSavedTracker")!)!);
            Set("_hotkeys", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.HotkeyMatcher")!, 0.25d)!);
            Set("_selector", Activator.CreateInstance(assembly.GetType("VoK.HealersHelper.TargetSelector")!, new object?[] { null })!);
            Set("_members", Array.CreateInstance(assembly.GetType("VoK.HealersHelper.WatchedMemberSnapshot")!, 0));

            Type viewType = assembly.GetType("VoK.HealersHelper.AlertBannerView")!;
            Type severityEnum = assembly.GetType("VoK.HealersHelper.BannerSeverity")!;
            Type modeEnum = assembly.GetType("VoK.HealersHelper.TrackingMode")!;
            object idleView = Activator.CreateInstance(
                viewType,
                Enum.Parse(severityEnum, "None"),
                Enum.Parse(modeEnum, "Party"),
                "",
                "",
                0,
                null,
                "",
                null,
                0L,
                "Smoke status",
                "Targeting ready",
                true,
                5)!;
            Set("_view", idleView);

            // 1. SettingsDialog at minimum size + round-trip save (including PeopleSaved stale-dialog guard)
            Type dialogType = assembly.GetType("VoK.HealersHelper.SettingsDialog")!;
            using (var dialog = (Form)Activator.CreateInstance(dialogType, engine)!)
            {
                dialog.Show();
                dialog.Size = dialog.MinimumSize;
                Application.DoEvents();
                dialog.PerformLayout();
                CheckLayoutControls(dialog, dialog);

                var pluginEnabledBox = (CheckBox)dialog.Controls.Find("PluginEnabledCheckBox", true).Single();
                var statusHudEnabledBox = (CheckBox)dialog.Controls.Find("StatusHudEnabledCheckBox", true).Single();
                var statusFontBox = (TextBox)dialog.Controls.Find("StatusFontSizeTextBox", true).Single();
                var toggleHotkeyBox = (TextBox)dialog.Controls.Find("TogglePluginHotkeyBox", true).Single();
                var fontBox = (TextBox)dialog.Controls.Find("FontSizeTextBox", true).Single();
                var deadNumeric = (NumericUpDown)dialog.Controls.Find("DeadAlertSecondsNumeric", true).Single();
                var excludeBox = (CheckBox)dialog.Controls.Find("ExcludeFromCaptureCheckBox", true).Single();
                var cooldownNumeric = (NumericUpDown)dialog.Controls.Find("AutoTargetCooldownNumeric", true).Single();

                if (toggleHotkeyBox.Text != "Ctrl+Shift+K")
                    throw new Exception($"Unexpected default TogglePluginHotkey text: {toggleHotkeyBox.Text}");

                pluginEnabledBox.Checked = true;
                statusHudEnabledBox.Checked = true;
                statusFontBox.Text = "16";
                fontBox.Text = "68";
                deadNumeric.Value = 11;
                excludeBox.Checked = false;
                cooldownNumeric.Value = 4.5m;

                // Simulate a live PeopleSaved increment while the dialog is open.
                settingsType.GetProperty("PeopleSaved")!.SetValue(settings, 8);

                var saveBtn = (Button)dialog.Controls.Find("SaveButton", true).Single();
                saveBtn.PerformClick();
            }

            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "healers-helper.json"))))
            {
                var root = saved.RootElement;
                if (root.GetProperty("FontSizePixels").GetInt32() != 68 ||
                    root.GetProperty("StatusFontSizePixels").GetInt32() != 16 ||
                    root.GetProperty("DeadAlertSeconds").GetInt32() != 11 ||
                    root.GetProperty("ExcludeFromCapture").GetBoolean() != false ||
                    root.GetProperty("PeopleSaved").GetInt32() != 8 ||
                    Math.Abs(root.GetProperty("AutoTargetCooldownSeconds").GetDouble() - 4.5) > 0.001)
                {
                    throw new Exception("SettingsDialog save did not round-trip exact values and preserve live PeopleSaved");
                }
            }

            // Verify TogglePluginEnabled() and ResetPeopleSaved() on engine
            engineType.GetMethod("TogglePluginEnabled")!.Invoke(engine, null);
            var toggledView = engineType.GetMethod("View")!.Invoke(engine, null)!;
            if ((bool)viewType.GetProperty("PluginEnabled")!.GetValue(toggledView)!)
                throw new Exception("TogglePluginEnabled did not flip PluginEnabled to false");

            engineType.GetMethod("TogglePluginEnabled")!.Invoke(engine, null);
            engineType.GetMethod("ResetPeopleSaved")!.Invoke(engine, null);
            var resetView = engineType.GetMethod("View")!.Invoke(engine, null)!;
            if ((int)viewType.GetProperty("PeopleSaved")!.GetValue(resetView)! != 0)
                throw new Exception("ResetPeopleSaved did not reset PeopleSaved to 0");

            Console.WriteLine("PASS SettingsDialog minimum-size layout, JSON round-trip, TogglePluginEnabled, and ResetPeopleSaved");

            // 2. BannerHudWindow layered native surface, styles, flash phases, and disposal
            Type hudType = assembly.GetType("VoK.HealersHelper.BannerHudWindow")!;
            Type cmdEnum = assembly.GetType("VoK.HealersHelper.HudCommand")!;
            object queue = Activator.CreateInstance(typeof(ConcurrentQueue<>).MakeGenericType(cmdEnum))!;
            var hud = (Form)Activator.CreateInstance(hudType, engine, queue, new Action<string>(_ => { }))!;
            Size bannerSize;
            try
            {
                IntPtr handle = hud.Handle;
                long exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
                const long requiredEx = WsExTransparent | WsExLayered | WsExNoActivate;
                if ((exStyle & requiredEx) != requiredEx)
                    throw new Exception($"Missing required extended styles on BannerHudWindow: 0x{exStyle:X}");

                Type frameType = assembly.GetType("VoK.HealersHelper.BannerFrame")!;
                MethodInfo renderMethod = hudType.GetMethod("RenderForSmoke", fields)!;
                PropertyInfo surfaceProp = hudType.GetProperty("Surface", fields)!;

                Color SampleCorner()
                {
                    object surface = surfaceProp.GetValue(hud)!;
                    var img = (Bitmap)surface.GetType().GetProperty("Image", fields)!.GetValue(surface)!;
                    return img.GetPixel(1, 1);
                }

                object frameOn = Activator.CreateInstance(
                    frameType,
                    true,
                    "DOWN: TANKY",
                    "-12/450 HP — CRITICAL",
                    72,
                    true,
                    unchecked((int)0xFFFFFFFF),
                    unchecked((int)0xF0960A14),
                    unchecked((int)0xFFFFD644))!;
                renderMethod.Invoke(hud, new[] { frameOn, true });
                bannerSize = hud.ClientSize;
                Color onColor = SampleCorner();
                if (bannerSize.Width < 420 || bannerSize.Height < 100)
                    throw new Exception($"Expected huge banner dimensions, got {bannerSize}");
                if (onColor.A == 0 || onColor.R < 100)
                    throw new Exception($"Expected crimson/gold flash-on pixel, got {onColor}");

                object frameOff = Activator.CreateInstance(
                    frameType,
                    true,
                    "DOWN: TANKY",
                    "-12/450 HP — CRITICAL",
                    72,
                    false,
                    unchecked((int)0xFFFF4D5A),
                    unchecked((int)0xEC0A0A0E),
                    unchecked((int)0xFFFF3848))!;
                renderMethod.Invoke(hud, new[] { frameOff, false });
                Color offColor = SampleCorner();
                if (hud.ClientSize != bannerSize || offColor.ToArgb() == onColor.ToArgb())
                    throw new Exception($"Flash off must keep dimensions and transition color ({onColor} -> {offColor})");

                object frameHidden = Activator.CreateInstance(frameType, false, "", "", 72, false, 0, 0, 0)!;
                renderMethod.Invoke(hud, new[] { frameHidden, false });
                Color clearedColor = SampleCorner();
                if (clearedColor.A != 0)
                    throw new Exception($"Hidden frame must clear surface to alpha 0, got {clearedColor}");
            }
            finally
            {
                hud.Dispose();
            }

            if (!hud.IsDisposed)
                throw new Exception("BannerHudWindow did not dispose cleanly");

            Console.WriteLine("PASS BannerHudWindow WS_EX styles, huge banner DIB render, flash transitions, and disposal");

            // 3. StatusHudWindow compact DIB surface, WS_EX styles, OFF vs PARTY rendering, and disposal
            Type statusHudType = assembly.GetType("VoK.HealersHelper.StatusHudWindow")!;
            var statusHud = (Form)Activator.CreateInstance(statusHudType, engine)!;
            try
            {
                IntPtr statusHandle = statusHud.Handle;
                long statusExStyle = GetWindowLongPtr(statusHandle, GwlExStyle).ToInt64();
                const long requiredEx = WsExTransparent | WsExLayered | WsExNoActivate;
                if ((statusExStyle & requiredEx) != requiredEx)
                    throw new Exception($"Missing required extended styles on StatusHudWindow: 0x{statusExStyle:X}");

                Type statusFrameType = assembly.GetType("VoK.HealersHelper.StatusHudFrame")!;
                MethodInfo statusRender = statusHudType.GetMethod("RenderForSmoke", fields)!;
                PropertyInfo statusSurfaceProp = statusHudType.GetProperty("Surface", fields)!;

                object partyStatusFrame = Activator.CreateInstance(
                    statusFrameType,
                    true,
                    "HEALER  PARTY <=50%  •  SAVED 3",
                    14,
                    unchecked((int)0xFF7AE582),
                    false,
                    "")!;
                statusRender.Invoke(statusHud, new[] { partyStatusFrame, true });
                Size partySize = statusHud.ClientSize;
                if (partySize.Width >= bannerSize.Width || partySize.Height >= bannerSize.Height / 2)
                    throw new Exception($"StatusHudWindow must be compact compared to BannerHudWindow ({partySize} vs {bannerSize})");

                object statusSurface = statusSurfaceProp.GetValue(statusHud)!;
                var statusBitmap = (Bitmap)statusSurface.GetType().GetProperty("Image", fields)!.GetValue(statusSurface)!;
                if (statusBitmap.GetPixel(0, 0).A != 0)
                    throw new Exception("StatusHudWindow normal mode must keep transparent background");

                object offStatusFrame = Activator.CreateInstance(
                    statusFrameType,
                    true,
                    "HEALER  OFF",
                    14,
                    unchecked((int)0xFF9AA4B2),
                    false,
                    "")!;
                statusRender.Invoke(statusHud, new[] { offStatusFrame, false });
                if (statusHud.ClientSize.Width >= partySize.Width)
                    throw new Exception($"HEALER  OFF should be narrower than PARTY status ({statusHud.ClientSize} vs {partySize})");
            }
            finally
            {
                statusHud.Dispose();
            }

            if (!statusHud.IsDisposed)
                throw new Exception("StatusHudWindow did not dispose cleanly");

            Console.WriteLine("PASS StatusHudWindow WS_EX styles, compact DIB size, transparent rendering, and disposal");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    private static Assembly LoadPlugin(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Assembly.LoadFrom(path);

        using var archive = ZipFile.OpenRead(path);
        var expected = new[] { "dll", "deps.json", "metadata" }
            .Select(ext => "plugins/HealersHelper/VoK.HealersHelper." + ext);
        if (!archive.Entries.Select(e => e.FullName).SequenceEqual(expected))
            throw new Exception("Installer ZIP must contain exactly three runtime entries, DLL first");

        using var source = archive.Entries[0].Open();
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        ms.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(ms);
    }

    private static void CheckLayoutControls(Control root, Control topForm)
    {
        foreach (Control control in root.Controls)
        {
            if (control is Button)
            {
                Size preferred = control.GetPreferredSize(Size.Empty);
                if (!root.ClientRectangle.Contains(control.Bounds) ||
                    control.Height < preferred.Height ||
                    control.Width < preferred.Width)
                {
                    throw new Exception($"Clipped button '{control.Text}' bounds={control.Bounds} pref={preferred} parentClient={root.ClientRectangle}");
                }
            }

            if (control.Visible && (control is Button or ComboBox or TextBox or NumericUpDown or CheckBox))
            {
                Rectangle inForm = topForm.RectangleToClient(control.Parent!.RectangleToScreen(control.Bounds));
                if (inForm.Left < 0 || inForm.Right > topForm.ClientSize.Width)
                {
                    throw new Exception($"Control '{control.Name}:{control.Text}' clips horizontally ({inForm}) in form width {topForm.ClientSize.Width}");
                }
            }

            CheckLayoutControls(control, topForm);
        }
    }
}
