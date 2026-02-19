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

    public GaugeOMatic(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        PluginInterface.Create<Service>();
        PluginDirPath = PluginInterface.AssemblyLocation.DirectoryName!;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // Initialize QoLBar IPC and pass handlers to update saved condition-set indices
        QoLBarIPC.Initialize(pluginInterface, OnQoLBarMovedConditionSet, OnQoLBarRemovedConditionSet);

        PluginInterface.UiBuilder.Draw += DrawWindows;
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
