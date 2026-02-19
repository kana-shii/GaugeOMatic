global using static GaugeOMatic.GaugeOMatic.Service;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using GaugeOMatic.Config;
using GaugeOMatic.Trackers;
using GaugeOMatic.Windows;
using GaugeOMatic.Utility;
using System;
using System.Collections.Generic;
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
    private bool qolBarInitDone = false;

    // Polling state for QoLBar condition-set changes
    private readonly Dictionary<int, bool> _qolConditionSetStates = new();
    private DateTime _lastConditionPoll = DateTime.MinValue;
    private static readonly TimeSpan ConditionPollInterval = TimeSpan.FromSeconds(1);

    public GaugeOMatic(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        PluginInterface.Create<Service>();
        PluginDirPath = PluginInterface.AssemblyLocation.DirectoryName!;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // Defer QoLBar IPC initialization to first UI draw (match Cammy timing)
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

    private void OnFirstDraw()
    {
        if (qolBarInitDone) return;

        try
        {
            // Initialize QoLBar IPC using Cammy-style Initialize signature and forward events
            QoLBarIPC.Initialize(PluginInterface, OnQoLBarMovedConditionSet, OnQoLBarRemovedConditionSet);

            // Subscribe to QoLBar enabled/disabled changes so we can rebuild trackers and apply rules
            QoLBarIPC.OnQoLBarEnabledChanged += enabled =>
            {
                try
                {
                    Service.Log.Information($"QoLBar enabled changed: {enabled}");
                }
                catch { }

                try
                {
                    // Rebuild trackers so ApplyDisplayRules runs and auto-disable logic runs immediately
                    foreach (var jm in TrackerManager.JobModules) jm.RebuildTrackerList();
                }
                catch { }
            };
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"QoLBarIPC.Initialize threw: {e}"); } catch { }
        }

        // Log immediate state so you can see whether it's detected
        try
        {
            Service.Log.Information("QoLBarIPC initial: Enabled={Enabled} IPCVer={IPCVer} QoLBarVer={QoLBarVer} Sets={Sets}",
                QoLBarIPC.QoLBarEnabled,
                QoLBarIPC.QoLBarIPCVersion,
                QoLBarIPC.QoLBarVersion,
                QoLBarIPC.GetConditionSets()?.Length ?? 0);
        }
        catch { }

        // Start polling for condition-set changes by hooking a draw callback
        PluginInterface.UiBuilder.Draw += PollQoLConditionSets;

        qolBarInitDone = true;
        PluginInterface.UiBuilder.Draw -= OnFirstDraw;
    }

    // Poll every ~1s (draw callback) to detect QoLBar condition set activations.
    private void PollQoLConditionSets()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastConditionPoll) < ConditionPollInterval) return;
        _lastConditionPoll = now;

        try
        {
            // Collect all configured indices across tracker configs
            var indices = new HashSet<int>();
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
                    if (t.ConditionSet >= 0) indices.Add(t.ConditionSet);
                }
            }

            if (indices.Count == 0) return;

            var changedIndices = new HashSet<int>();

            // Check each index with QoLBarIPC and compare to cached state
            foreach (var idx in indices)
            {
                var current = QoLBarIPC.QoLBarEnabled && QoLBarIPC.CheckConditionSet(idx);
                if (!_qolConditionSetStates.TryGetValue(idx, out var prev) || prev != current)
                {
                    _qolConditionSetStates[idx] = current;
                    changedIndices.Add(idx);
                    Service.Log.Debug("QoLBar condition set {0} changed to {1}", idx, current);
                }
            }

            if (changedIndices.Count > 0)
            {
                // Apply persistent enable/disable changes based on which indices changed,
                // then rebuild trackers once if anything changed.
                var anyConfigChanged = ApplyConditionSetStateChanges(changedIndices);

                if (anyConfigChanged)
                {
                    foreach (var jm in TrackerManager.JobModules) jm.RebuildTrackerList();
                    Service.Log.Information("QoLBar condition-set change triggered tracker rebuild");
                }
            }
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"QoLBar condition poll error: {e}"); } catch { }
        }
    }

    // Update TrackerConfig.Enabled / PrevEnabledBeforeConditionSet / AutoDisabledByConditionSet for trackers whose ConditionSet is in changedIndices.
    // Returns true if any config was modified (so caller can save + rebuild).
    private bool ApplyConditionSetStateChanges(HashSet<int> changedIndices)
    {
        var anyChange = false;
        var tc = Configuration.TrackerConfigs;
        if (tc == null) return false;

        var props = tc.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (prop.PropertyType != typeof(TrackerConfig[])) continue;
            var arr = (TrackerConfig[]?)prop.GetValue(tc);
            if (arr == null) continue;
            foreach (var t in arr)
            {
                if (t == null) continue;
                var idx = t.ConditionSet;
                if (idx < 0 || !changedIndices.Contains(idx)) continue;

                var active = QoLBarIPC.QoLBarEnabled && QoLBarIPC.CheckConditionSet(idx);

                if (!active)
                {
                    // auto-disable if currently enabled and not already auto-disabled
                    if (!t.AutoDisabledByConditionSet && t.Enabled)
                    {
                        t.AutoDisabledByConditionSet = true;
                        if (t.PrevEnabledBeforeConditionSet == null)
                            t.PrevEnabledBeforeConditionSet = t.Enabled;
                        t.Enabled = false;
                        anyChange = true;
                        Service.Log.Debug("Auto-disabled tracker {0} (cond {1})", t.TrackerType, idx);
                    }
                }
                else
                {
                    // condition set active: restore if we previously auto-disabled or we have a stored previous value
                    if (t.AutoDisabledByConditionSet || t.PrevEnabledBeforeConditionSet.HasValue)
                    {
                        t.AutoDisabledByConditionSet = false;
                        if (t.PrevEnabledBeforeConditionSet.HasValue)
                        {
                            t.Enabled = t.PrevEnabledBeforeConditionSet.Value;
                            t.PrevEnabledBeforeConditionSet = null;
                        }
                        else
                        {
                            t.Enabled = true;
                        }
                        anyChange = true;
                        Service.Log.Debug("Auto-reenabled tracker {0} (cond {1})", t.TrackerType, idx);
                    }
                }
            }
        }

        if (anyChange)
        {
            try { Configuration.Save(); } catch { }
        }

        return anyChange;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawWindows;
        PluginInterface.UiBuilder.Draw -= PollQoLConditionSets;
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

            // Rebuild trackers so changes take effect immediately
            foreach (var jm in TrackerManager.JobModules) jm.RebuildTrackerList();
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

            // Rebuild trackers so changes take effect immediately
            foreach (var jm in TrackerManager.JobModules) jm.RebuildTrackerList();
        }
        catch { /* swallow errors to avoid breaking IPC callbacks */ }
    }
}