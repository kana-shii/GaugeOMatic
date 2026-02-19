using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GaugeOMatic.Config;
using GaugeOMatic.Trackers;
using GaugeOMatic.Utility;
using GaugeOMatic.Widgets;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using Dalamud.Interface.Components;
using static Dalamud.Interface.FontAwesomeIcon;
using static Dalamud.Interface.UiBuilder;
using static Dalamud.Interface.Utility.ImGuiHelpers;
using static GaugeOMatic.GameData.JobData;
using static GaugeOMatic.Widgets.Common.WidgetUI;
using static GaugeOMatic.Widgets.Common.WidgetUI.UpdateFlags;
using static GaugeOMatic.Widgets.Common.WidgetUI.WidgetUiTab;
using static Dalamud.Bindings.ImGui.ImGuiCond;
using static Dalamud.Bindings.ImGui.ImGuiTableColumnFlags;
using static Dalamud.Bindings.ImGui.ImGuiTableFlags;

namespace GaugeOMatic.Windows;

public class TrackerWindow : Window, IDisposable
{
    public Configuration Configuration;
    public Tracker Tracker;

    // Window-local widget reference (may be a temporary widget instance)
    public Widget? Widget;
    private Widget? tempWidgetRef;
    private bool tempWidgetCreated = false;

    // Local tracker window update flag used for Save/Reset flow
    private UpdateFlags updateFlag = 0;

    public string Hash => Tracker.GetHashCode() + "-" + Widget?.GetHashCode();

    public TrackerWindow(Tracker tracker, Widget? widget, Configuration configuration, string name) : base(name)
    {
        Tracker = tracker;
        Widget = widget;
        Configuration = configuration;
        Collapsed = false;
        Flags = ImGuiWindowFlags.NoCollapse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(300f, 300f),
            MaximumSize = new(1100f)
        };
        SizeCondition = Appearing;
    }

    public override void Draw()
    {
        // Reset the window-local update flag each draw
        updateFlag = 0;

        // If we previously created a temporary widget and the tracker later produced a real widget,
        // prefer the real widget now and dispose the temporary one.
        try
        {
            if (tempWidgetCreated && Tracker.Widget != null && Tracker.Widget != tempWidgetRef)
            {
                try { tempWidgetRef?.Dispose(); } catch { }
                tempWidgetRef = null;
                tempWidgetCreated = false;
                Widget = Tracker.Widget;
            }
        }
        catch { /* swallow */ }

        // If there's no widget instance (tracker disposed due to gating), create a temporary widget
        // so the settings window remains fully usable (preview, controls, etc.).
        if (Widget == null)
        {
            try
            {
                // Use global qualifier to avoid class/namespace lookup collision
                tempWidgetRef = global::GaugeOMatic.Widgets.Widget.Create(Tracker);
                if (tempWidgetRef != null)
                {
                    Widget = tempWidgetRef;
                    tempWidgetCreated = true;
                }
            }
            catch { /* swallow */ }
        }

        // Important: DO NOT auto-close the window if the tracker is not available.
        // Users must be able to open/edit settings even when a tracker is hidden by rules.

        HeaderTable();

        WidgetOptionTable();

        if (updateFlag.HasFlag(UpdateFlags.Save)) Configuration.Save();
        if (updateFlag.HasFlag(Reset)) Tracker.JobModule.ResetWidgets();
    }

    private void HeaderTable()
    {
        using (var table = ImRaii.Table("TrackerHeaderTable" + Hash, 2, SizingFixedFit | PadOuterX))
        {
            if (table.Success) {
                ImGui.TableSetupColumn("Labels", WidthFixed, 60f * GlobalScale);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGuiHelpy.TextRightAligned("Widget");
                ImGui.TableNextColumn();
                Tracker.WidgetMenuWindow.Draw("[Select Widget]", 182f);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGuiHelpy.TextRightAligned("Pinned to:", true);
                ImGui.TableNextColumn();
                if (Tracker.AddonDropdown.Draw($"AddonSelect{GetHashCode()}", 182f))
                {
                    Tracker.AddonName = Tracker.AddonDropdown.CurrentSelection;
                    updateFlag |= Reset | UpdateFlags.Save;
                }

                PreviewControls();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
            }
        }

        ImGui.TextDisabled("Widget Settings");
        ImGui.SameLine();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() - (80 * GlobalScale));
        if (ImGuiComponents.IconButtonWithText(UndoAlt,"Default",null, null, null, new(80f,0)))
        {
            Widget?.ResetConfigs();
            Widget?.ApplyConfigs();
            Tracker.UpdateTracker();
            updateFlag |= UpdateFlags.Save;
        }

        if (ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();
            if (tt.Success)
            {
                ImGui.TextUnformatted("Reset this widget's settings to defaults.");
            }
        }

        // Apply configs from the underlying WidgetConfig into the active Widget instance so UI shows current values
        if (Widget != null)
            Widget.ApplyConfigs();
    }

    // Minimal widget options section (calls the widget's DrawUI if present).
    // This allows full editing of the widget even when it's a temporary instance.
    private void WidgetOptionTable()
    {
        using (var table = ImRaii.Table("TrackerOptionTable" + Hash, 1, SizingStretchSame))
        {
            if (!table.Success) return;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            if (Widget != null)
            {
                // Many widgets provide DrawUI for their configuration; call it when available.
                try
                {
                    Widget.DrawUI();
                }
                catch
                {
                    // If DrawUI isn't present / fails, fall back to showing nothing.
                }
            }
            else
            {
                ImGui.TextUnformatted("No widget available for preview.");
            }
        }
    }

    // Simple preview controls: toggle preview mode and adjust preview value.
    // This matches the original window's preview behavior but keeps it small and safe.
    private void PreviewControls()
    {
        var preview = Tracker.TrackerConfig.Preview;
        if (ImGui.Checkbox("Preview", ref preview))
        {
            Tracker.TrackerConfig.Preview = preview;
            updateFlag |= UpdateFlags.Save;
            Tracker.UpdateTracker();
        }

        ImGui.SameLine();

        float pv = Tracker.TrackerConfig.PreviewValue;
        if (ImGui.SliderFloat("Preview Value", ref pv, 0f, 1f))
        {
            Tracker.TrackerConfig.PreviewValue = pv;
            updateFlag |= UpdateFlags.Save;
            Tracker.UpdateTracker();
        }

        if (ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();
            if (tt.Success) ImGui.TextUnformatted("Toggle preview and adjust preview strength.");
        }
    }

    public void Dispose()
    {
        // Dispose only the temporary widget we created for this window.
        if (tempWidgetCreated && tempWidgetRef != null)
        {
            try { tempWidgetRef.Dispose(); } catch { }
            tempWidgetRef = null;
            tempWidgetCreated = false;
        }

        // Do NOT dispose the real widget: the Tracker owns the lifecycle of the actual widget instance.
    }
}