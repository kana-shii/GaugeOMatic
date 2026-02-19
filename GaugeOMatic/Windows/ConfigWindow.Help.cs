using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Components;
using Dalamud.Bindings.ImGui;
using static GaugeOMatic.Utility.ImGuiHelpy;
using System;
using System.Linq;
using System.Numerics;
using GaugeOMatic.Utility;
using GaugeOMatic.Trackers;
using static GaugeOMatic.GameData.JobData;
using static Dalamud.Interface.Utility.ImGuiHelpers;

namespace GaugeOMatic.Windows;

public partial class ConfigWindow
{
    private static void DrawHelpTab()
    {
        ImGui.TextDisabled("GAUGE-O-MATIC HELP");

        using var tb = ImRaii.TabBar("HelpTabs");
        if (tb.Success)
        {
            AboutTab();
            HowToTab();
            ConditionSetsTab(); // minimal addition: global QoLBar condition set editor + diagnostics
        }
    }

    public const string AboutText =
        "Gauge-O-Matic allows you to customize your job gauges by adding extra " +
        "counters, bars, and indicators in various styles. The plugin is a work in progress, " +
        "so please be aware that you may run into bugs or performance issues, and future plugin " +
        "updates might affect your saved settings.\n\n" +
        "The plugin currently has built-in default presets set up for a few jobs, but most still " +
        "need work. The goal for these defaults is to pick out useful datapoints that the " +
        "current gauges don't show, and integrate them into the existing gauges in a way that suits " +
        "each job's aesthetic.\n\n" +
        "Feedback is very helpful! If you have a request for an element that " +
        "you'd like to see on your job's gauge, an idea for a widget design, or if you've come " +
        "up with a preset that you want to share, be sure to send feedback at the plugin's Github repo.";

    private static void AboutTab()
    {
        using var ti = ImRaii.TabItem("About the Plugin");
        if (ti) ImGui.TextWrapped(AboutText);
    }

    private static void HowToTab()
    {
        using var ti = ImRaii.TabItem("How-to");
        if (ti)
        {
            ImGui.TextWrapped("There are two ways to add elements to your job gauge: setting them up manually, or loading a preset.");

            ImGui.Spacing();
            ImGui.TextDisabled("ADDING ELEMENTS MANUALLY");

            ImGui.Text("1) Click ");
            ImGui.SameLine();
            ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus,"Add##dummyAdd");

            ImGui.Spacing();
            ImGui.Text("2) Select a tracker to use. There are three categories:");

            ImGui.Indent(30f);
            WriteIcon(FontAwesomeIcon.Tags, null, 0x1c6e68ff);
            ImGui.Text("Status Effects - Retrieves the time remaining or number of stacks.");

            WriteIcon(FontAwesomeIcon.FistRaised, null, 0xaa372dff);
            ImGui.Text("Actions - Retrieves the recast time or the number of charges available.");

            WriteIcon(FontAwesomeIcon.Gauge, null, 0x2b455cff);
            ImGui.Text("Job Gauge - Retrieves unique data options per job gauge.");

            ImGui.Indent(-30f);

            ImGui.Spacing();
            ImGui.TextWrapped("3) Select a Widget. Widgets fall into these categories:");

            ImGui.Indent(30f);
            ImGui.TextWrapped(
                              "Counters - Shows a count of stacks or charges\n" +
                              "Bars & Timers - Shows a timer or resource value\n" +
                              "State Indicators - Toggles between different visual states (usually on/off)\n" +
                              "Multi-Component - These are groups of widgets designed to layer on top of each other, making one combined design.");

            ImGui.Indent(-30f);

            ImGui.Spacing();
            ImGui.TextWrapped("4) Customize!");
            ImGui.Indent(30f);

            ImGui.TextWrapped("Each widget design has its own set of options to customize the way it looks and behaves. " +
                              "You can also choose which HUD element to pin each widget to, " +
                              "and control the order that widgets layer on top of each other.");

            ImGui.Indent(-30f);

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.TextDisabled("USING PRESETS");
            ImGui.TextWrapped(
                "Upon opening the preset window, you'll be presented with a list of installed presets. Selecting any one of these will show you its contents.\n\n" +
                "You can:");

            ImGui.Indent(30f);

            ImGui.Text("-Add elements from the preset individually");
            ImGui.SameLine();
            IconButton("", FontAwesomeIcon.Plus, 10f);

            ImGui.Text("-Add all elements at once");
            ImGui.SameLine();
            ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus,"Add all to...##dummyAddAll");

            ImGui.Text("-Copy a design onto one of your existing trackers");
            ImGui.SameLine();
            IconButton("", FontAwesomeIcon.Copy, 10f);

            ImGui.Text("-Replace your current settings with the contents of the preset.");
            ImGui.SameLine();
            ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PaintRoller,"Overwrite Current##dummyOverwrite");


            ImGui.Indent(-30f);
            ImGui.TextWrapped("If the preset contains trackers that are not applicable to the selected job, they'll be greyed out, but you can still use the widget designs.");


            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.TextDisabled("ADDING PRESETS");
            ImGui.TextWrapped("To save your current options as a preset, simply type in a name and click");
            ImGui.SameLine();
            ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save,"Save##dummySave");

            ImGui.TextWrapped("If you've copied a preset from an external source to your clipboard, you can import it by clicking ");
        }
    }

    // minimal additions start here
    // Keeps currently selected index in the Condition Sets tab (0 = None, >0 = QoLBar set index+1)
    private static int SelectedConditionSetIndex = 0;

    private static void ConditionSetsTab()
    {
        using var ti = ImRaii.TabItem("Condition Sets");
        if (!ti) return;

        ImGui.Spacing();
        ImGui.TextWrapped("If you use QoLBar's condition set feature, you can assign a condition set to trackers " +
                          "so they only show when the chosen QoLBar set is active. Use the controls below to apply a selected " +
                          "condition set globally (to all trackers) or to the currently selected job tab.");

        ImGui.Spacing();

        // DIAGNOSTIC STATUS (minimal, non-invasive)
        try
        {
            var enabled = QoLBarIPC.QoLBarEnabled;
            var ipcVer = QoLBarIPC.QoLBarIPCVersion;
            var ver = QoLBarIPC.QoLBarVersion;
            var setsDiag = QoLBarIPC.GetConditionSets() ?? Array.Empty<string>();
            ImGui.TextDisabled($"QoLBarEnabled: {enabled}  IPCVer: {ipcVer}  QoLBarVer: {ver}  ConditionSetsCount: {setsDiag.Length}");
        }
        catch
        {
            ImGui.TextDisabled("QoLBar IPC diagnostic: error reading IPC state.");
        }

        ImGui.Spacing();

        // Get condition set names from QoLBarIPC
        var sets = QoLBarIPC.GetConditionSets() ?? Array.Empty<string>();

        if (sets.Length == 0)
        {
            ImGui.TextDisabled("QoLBar not installed or no condition sets available.");
            ImGui.Spacing();
            ImGui.TextWrapped("Install QoLBar and create condition sets to use this feature. Trackers will remain unchanged until you assign a condition set.");
            return;
        }

        // Build choices array: index 0 = "None", then QoLBar sets
        var choices = new string[sets.Length + 1];
        choices[0] = "None";
        for (int i = 0; i < sets.Length; i++) choices[i + 1] = string.IsNullOrEmpty(sets[i]) ? $"Set {i}" : sets[i];

        // Clamp selection
        if (SelectedConditionSetIndex < 0) SelectedConditionSetIndex = 0;
        if (SelectedConditionSetIndex > sets.Length) SelectedConditionSetIndex = sets.Length;

        // Show combo
        ImGui.SetNextItemWidth(400f * GlobalScale);
        if (ImGui.Combo("Selected Condition Set", ref SelectedConditionSetIndex, choices, choices.Length))
        {
            // selection changed; wait for user to press Apply
        }

        ImGui.Spacing();

        // Buttons row: Apply to All | Apply to Current Job | Clear All
        if (ImGui.Button("Apply to All", new Vector2(160f * GlobalScale, 0)))
        {
            ApplyConditionSetToAll(SelectedConditionSetIndex - 1); // maps 0 -> -1 (None)
        }
        ImGui.SameLine();
        if (ImGui.Button("Apply to Current Job", new Vector2(200f * GlobalScale, 0)))
        {
            ApplyConditionSetToCurrentJob(SelectedConditionSetIndex - 1);
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear All", new Vector2(120f * GlobalScale, 0)))
        {
            ApplyConditionSetToAll(-1);
            SelectedConditionSetIndex = 0;
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Tip: You can also set condition sets per-tracker from the tracker's settings window. This global editor is for quick bulk changes.");
    }

    private static void ApplyConditionSetToAll(int conditionSetIndex)
    {
        try
        {
            var cfg = GaugeOMatic.ConfigWindow.Configuration;
            if (cfg == null) return;

            var tc = cfg.TrackerConfigs;
            if (tc == null) return;

            var props = tc.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.PropertyType != typeof(TrackerConfig[])) continue;
                var arr = (TrackerConfig[]?)prop.GetValue(tc);
                if (arr == null) continue;
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    t.ConditionSet = conditionSetIndex;
                }
            }

            cfg.Save();

            // Rebuild trackers so changes take effect immediately
            var tm = GaugeOMatic.ConfigWindow.TrackerManager;
            if (tm != null)
            {
                foreach (var jm in tm.JobModules) jm.RebuildTrackerList();
            }
        }
        catch
        {
            // swallow errors to avoid breaking UI
        }
    }

    private static void ApplyConditionSetToCurrentJob(int conditionSetIndex)
    {
        try
        {
            var cfgWindow = GaugeOMatic.ConfigWindow;
            var cfg = cfgWindow.Configuration;
            if (cfg == null) return;

            var jobTab = cfg.JobTab;
            if (jobTab == Job.None) return;

            // Find the job module matching the selected job tab
            var jm = cfgWindow.JobModules.FirstOrDefault(m => m.Job == jobTab);
            if (jm == null) return;

            // Apply to that job's tracker configs
            foreach (var tracker in jm.TrackerList)
            {
                tracker.TrackerConfig.ConditionSet = conditionSetIndex;
            }

            cfg.Save();
            jm.RebuildTrackerList();
        }
        catch
        {
            // swallow
        }
    }
    // minimal additions end here
}