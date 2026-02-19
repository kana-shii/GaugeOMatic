using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using GaugeOMatic.CustomNodes.Animation;
using GaugeOMatic.GameData;
using GaugeOMatic.Utility;
using static GaugeOMatic.GameData.FrameworkData;
using static GaugeOMatic.GaugeOMatic.Service; // bring Log into scope

namespace GaugeOMatic.Widgets;

public abstract partial class Widget
{
    public virtual string? SharedEventGroup => null;

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public struct SharedEventArgs
    {
        public Color.ColorRGB? ColorRGB;
        public Color.AddRGB? AddRGB;
        public int? Intval;
        public float? Floatval;
    }

    public Dictionary<string, Action<SharedEventArgs?>> SharedEvents = new();

    public void InvokeSharedEvent(string group, string eventLabel, SharedEventArgs? args = null)
    {
        foreach (var widget in Tracker.JobModule.WidgetList.Where(widget => widget?.SharedEventGroup == group))
            if (widget!.SharedEvents.TryGetValue(eventLabel, out var action))
                action.Invoke(args);
    }

    public bool IsVisible { get; set; } = true;

    public void ApplyDisplayRules()
    {
        // DIAGNOSTIC: log condition-set / enabled state so we can debug why gating isn't working
        try
        {
            var condIndex = Tracker.TrackerConfig.ConditionSet;
            var qolEnabled = QoLBarIPC.QoLBarEnabled;
            var checkResult = condIndex >= 0 ? QoLBarIPC.CheckConditionSet(condIndex) : true;
            var prevStoredStr = Tracker.TrackerConfig.PrevEnabledBeforeConditionSet.HasValue
                ? Tracker.TrackerConfig.PrevEnabledBeforeConditionSet.Value.ToString()
                : "null";
            Log.Information(
                "TrackerDebug: TrackerType={0} ItemId={1} CondIndex={2} QoLBarEnabled={3} QoLCheck={4} Enabled={5} AutoDisabled={6} PrevStored={7}",
                Tracker.TrackerConfig.TrackerType,
                Tracker.TrackerConfig.ItemId,
                condIndex,
                qolEnabled,
                checkResult,
                Tracker.TrackerConfig.Enabled,
                Tracker.TrackerConfig.AutoDisabledByConditionSet,
                prevStoredStr);
        }
        catch (Exception e)
        {
            try { Log.Error("TrackerDebug log failed: {0}", e); } catch { }
        }

        // Evaluate QoLBar condition-set match (for runtime visibility only)
        var passesConditionSet = Tracker.TrackerConfig.ConditionSet < 0 || (QoLBarIPC.QoLBarEnabled && QoLBarIPC.CheckConditionSet(Tracker.TrackerConfig.ConditionSet));

        bool LevelOk()
        {
            if (!Tracker.TrackerConfig.LimitLevelRange) return true;
            if (Tracker.TrackerConfig is { LevelMin: 1, LevelMax: JobData.LevelCap }) return true;
            var level = LocalPlayer.Lvl;
            return level >= Tracker.TrackerConfig.LevelMin && level <= Tracker.TrackerConfig.LevelMax;
        }

        bool FlagsOk() => !Tracker.TrackerConfig.HideOutsideCombatDuty ||
                             Condition.Any(ConditionFlag.InCombat, ConditionFlag.BoundByDuty,
                                           ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95,
                                           ConditionFlag.InDeepDungeon);

        // Determine runtime availability based on Enabled and other rules
        var shouldBeAvailable = Tracker.TrackerConfig.Enabled && (Tracker.UsePreviewValue || (LevelOk() && FlagsOk())) && passesConditionSet;

        // Also keep UI visibility in sync
        Tracker.Available = shouldBeAvailable;

        if (shouldBeAvailable)
            Show();
        else
            Hide();
    }

    public void Hide()
    {
        if (!IsVisible) return;

        IsVisible = false;
        Animator += new Tween(WidgetRoot, WidgetRoot, new(100) { Alpha = 0 });
    }

    public void Show()
    {
        if (IsVisible) return;

        IsVisible = true;
        Animator += new Tween(WidgetRoot, WidgetRoot, new(100) { Alpha = 255 });
    }
}
