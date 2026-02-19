global using static GaugeOMatic.GaugeOMatic.Service;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using GaugeOMatic.Config;
using GaugeOMatic.Trackers;
using GaugeOMatic.Windows;
using GaugeOMatic.Utility;
using System;
using System.Reflection;
using static GaugeOMatic.GameData.ActionRef;
using static GaugeOMatic.GameData.FrameworkData;
using static GaugeOMatic.Widgets.Common.CommonParts;
using static GaugeOMatic.Widgets.WidgetAttribute;

namespace GaugeOMatic;

public sealed partial class GaugeOMatic : IDalamudPlugin
{
    // ReSharper disable once UnusedMember.Global
    public static string Name => "Gauge-O-Matic";
    private const string CommandName = "/gomatic";
    internal static string PluginDirPath = null!;

    internal static WindowSystem WindowSystem = new("Gauge-O-Matic");
    internal static ConfigWindow ConfigWindow { get; set; } = null!;
    internal static PresetWindow PresetWindow { get; set; } = null!;
    internal Configuration Configuration { get; set; }
    private IDalamudPluginInterface PluginInterface { get; init; }
    private TrackerManager TrackerManager { get; init; }

    // One-time flag so we initialize QoLBarIPC only once on first Draw
    private bool _qolBarInitDone = false;

    // Polling state for re-evaluating QoLBar presence
    private bool _qolBarPolling = false;
    private DateTime _qolBarPollStart;
    private DateTime _qolBarLastCheck;

    public GaugeOMatic(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        PluginInterface.Create<Service>();
        PluginDirPath = PluginInterface.AssemblyLocation.DirectoryName!;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // Defer QoLBar IPC initialization to the first UI draw.
        PluginInterface.UiBuilder.Draw += DrawWindows;
        PluginInterface.UiBuilder.Draw += OnFirstDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigWindow;

        BuildWidgetList();
        PopulateActions();

        TrackerManager = new(Configuration);

        ConfigWindow = new(TrackerManager);
        PresetWindow = new(TrackerManager);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(PresetWindow);

        Framework.Update += UpdatePlayerData;

        CommandManager.AddHandler(CommandName, new(OnCommand) { HelpMessage = "Open Gauge-O-Matic Settings" });
    }

    // Called on every UI draw; use it once to initialize QoLBar IPC and then set up short polling if needed.
    private void OnFirstDraw()
    {
        if (_qolBarInitDone) return;

        try
        {
            QoLBarIPC.Initialize(PluginInterface,
                onMoved: OnQoLBarMovedConditionSet,
                onRemoved: OnQoLBarRemovedConditionSet,
                logger: msg => { try { Service.Log.Debug($"QoLBarIPC: {msg}"); } catch { } });
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"QoLBarIPC initialization failed: {e}"); } catch { }
        }

        // Immediately re-evaluate and log
        try
        {
            var enabled = QoLBarIPC.Reevaluate(
                logger: msg => { try { Service.Log.Debug($"QoLBarIPC: {msg}"); } catch { } },
                onEnabledChanged: en => { try { Service.Log.Information($"QoLBar enabled changed: {en}"); } catch { } });

            Service.Log.Information("QoLBarIPC initial: Enabled={Enabled} IPCVer={IPCVer} QoLBarVer={QoLBarVer} Sets={Sets}",
                QoLBarIPC.QoLBarEnabled,
                QoLBarIPC.QoLBarIPCVersion,
                QoLBarIPC.QoLBarVersion,
                QoLBarIPC.GetConditionSets()?.Length ?? 0);

            // If not enabled, start polling (short-lived) so toggles are detected even without Subscribe.
            if (!enabled)
            {
                _qolBarPolling = true;
                _qolBarPollStart = DateTime.UtcNow;
                _qolBarLastCheck = DateTime.UtcNow.AddSeconds(-2); // allow immediate check
                PluginInterface.UiBuilder.Draw += QolBarPollDraw;
            }
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"QoLBarIPC reeval failed: {e}"); } catch { }
        }

        _qolBarInitDone = true;
        PluginInterface.UiBuilder.Draw -= OnFirstDraw;
    }

    // Poll every ~1s for up to 15s to catch QoLBar toggles when Subscribe isn't available.
    private void QolBarPollDraw()
    {
        if (!_qolBarPolling) return;

        var now = DateTime.UtcNow;
        if ((now - _qolBarLastCheck).TotalSeconds < 1.0) return;

        _qolBarLastCheck = now;

        try
        {
            var prev = QoLBarIPC.QoLBarEnabled;
            var curr = QoLBarIPC.Reevaluate(
                logger: msg => { try { Service.Log.Debug($"QoLBarIPC: {msg}"); } catch { } },
                onEnabledChanged: en => { try { Service.Log.Information($"QoLBar enabled changed: {en}"); } catch { } });

            if (curr != prev)
            {
                // log already done in callback; also log summary
                Service.Log.Information("QoLBarIPC poll detected change: Enabled={0}, IPCVer={1}, Sets={2}", curr, QoLBarIPC.QoLBarIPCVersion, QoLBarIPC.GetConditionSets()?.Length ?? 0);
            }

            // stop polling if enabled or timeout
            if (curr || (now - _qolBarPollStart).TotalSeconds > 15)
            {
                _qolBarPolling = false;
                PluginInterface.UiBuilder.Draw -= QolBarPollDraw;
            }
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"QoLBar poll error: {e}"); } catch { }
            _qolBarPolling = false;
            PluginInterface.UiBuilder.Draw -= QolBarPollDraw;
        }
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawWindows;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigWindow;
        Framework.Update -= UpdatePlayerData;

        TrackerManager.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        DisposeSharedParts();
    }

    public static void OnCommand(string command, string args) => ConfigWindow.IsOpen = !ConfigWindow.IsOpen;

    public static void DrawWindows() => WindowSystem.Draw();
    public static void OpenConfigWindow() => ConfigWindow.IsOpen = true;

    // Called when QoLBar notifies that a condition set was moved: update saved indices across all tracker config arrays.
    private void OnQoLBarMovedConditionSet(int from, int to)
    {
        try
        {
            var tc = Configuration.TrackerConfigs;
            if (tc == null) return;

            var props = tc.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.PropertyType != typeof(TrackerConfig[])) continue;
                var arr = (TrackerConfig[]?)prop.GetValue(tc);
                if (arr == null) continue;
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    if (t.ConditionSet == from) t.ConditionSet = to;
                    else if (t.ConditionSet == to) t.ConditionSet = from;
                }
            }

            Configuration.Save();
        }
        catch { /* swallow errors to avoid breaking IPC callbacks */ }
    }

    // Called when QoLBar notifies that a condition set was removed: shift or clear saved indices.
    private void OnQoLBarRemovedConditionSet(int removed)
    {
        try
        {
            var tc = Configuration.TrackerConfigs;
            if (tc == null) return;

            var props = tc.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.PropertyType != typeof(TrackerConfig[])) continue;
                var arr = (TrackerConfig[]?)prop.GetValue(tc);
                if (arr == null) continue;
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    if (t.ConditionSet > removed) t.ConditionSet -= 1;
                    else if (t.ConditionSet == removed) t.ConditionSet = -1;
                }
            }

            Configuration.Save();
        }
        catch { /* swallow errors to avoid breaking IPC callbacks */ }
    }
}