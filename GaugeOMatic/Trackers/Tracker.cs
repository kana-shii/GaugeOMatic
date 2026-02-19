using GaugeOMatic.Config;
using GaugeOMatic.GameData;
using GaugeOMatic.JobModules;
using GaugeOMatic.Widgets;
using GaugeOMatic.Windows;
using GaugeOMatic.Windows.Dropdowns;
using System;
using System.Collections.Generic;
using System.Linq;
using static GaugeOMatic.GaugeOMatic;
using static System.Activator;

namespace GaugeOMatic.Trackers;

public abstract partial class Tracker : IDisposable
{
    public TrackerConfig TrackerConfig { get; set; } = null!;

    public Widget? Widget { get; set; }
    public bool Available;

    public bool UsePreviewValue => TrackerConfig.Preview && (GaugeOMatic.ConfigWindow.IsOpen || Window?.IsOpen == true);

    public JobModule JobModule = null!;
    public WidgetDropdown WidgetMenuTable = null!;
    public WidgetDropdown WidgetMenuWindow = null!;
    public AddonDropdown AddonDropdown = null!;
    public TrackerDropdown TrackerDropdown = null!;
    public ItemRef? ItemRef;

    public string AddonName
    {
        get => TrackerConfig.AddonName;
        set => TrackerConfig.AddonName = value;
    }

    public string? WidgetType
    {
        get => TrackerConfig.WidgetType;
        set => TrackerConfig.WidgetType = value;
    }

    public WidgetConfig WidgetConfig
    {
        get => TrackerConfig.WidgetConfig;
        set => TrackerConfig.WidgetConfig = value;
    }

    public TrackerWindow? Window { get; set; }

    public void Dispose()
    {
        if (WindowSystem.Windows.Contains(Window)) WindowSystem.RemoveWindow(Window!);
        Window?.Dispose();

        DisposeWidget();
    }

    public void BuildWidget(Configuration configuration, List<AddonOption> addonOptions)
    {
        if (!TrackerConfig.Enabled) return;

        UpdateValues();

        // Defensive: widget creation can touch native nodes; wrap in try so it doesn't crash whole process.
        try
        {
            Widget = null;
            Widget = Widget.Create(this);

            if (Widget != null)
            {
                if (Window == null) CreateWindow(Widget, configuration);
                else
                {
                    if (!WindowSystem.Windows.Contains(Window)) WindowSystem.AddWindow(Window);
                    Window.Widget = Widget;
                }

                Available = true;
            }
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"Widget creation failed for {TrackerConfig.TrackerType} item {TrackerConfig.ItemId}: {e}"); } catch { }
            // Ensure we leave a clean state: no widget, not available.
            Widget?.Dispose();
            Widget = null;
            Available = false;
        }

        AddonDropdown.Prepare(addonOptions);
    }

    // Accept nullable widget to avoid warnings when callers may pass null; do nothing in that case.
    public void CreateWindow(Widget? widget, Configuration configuration)
    {
        if (widget == null) return;
        Window = new(this, widget, configuration, $"{DisplayAttr.Name}##{GetHashCode()}");
        Window.Size = new(300, 600);
        WindowSystem.AddWindow(Window);
    }

    public void DisposeWidget()
    {
        Widget?.Dispose();
        Available = false;
        Widget = null;
    }

    public void UpdateValues()
    {
        Framework.RunOnFrameworkThread(() =>
        {
            PreviousData = CurrentData;
            CurrentData = GetCurrentData(UsePreviewValue ? TrackerConfig.PreviewValue : null);
        });
    }

    public void UpdateTracker()
    {
        UpdateValues();
        Widget?.Update();
        Widget?.UpdateIcon();
    }

    public static Tracker? Create(JobModule jobModule, TrackerConfig trackerConfig)
    {
        var qualifiedTypeStr = $"{typeof(Tracker).Namespace}.{trackerConfig.TrackerType}";
        var tracker = (Tracker?)CreateInstance(Type.GetType(qualifiedTypeStr) ?? typeof(EmptyTracker));

        if (tracker == null) return null;

        tracker.JobModule = jobModule;

        if (trackerConfig.ItemId != 0)
        {
            tracker.ItemRef = trackerConfig.TrackerType switch
            {
                nameof(ActionTracker) => (ActionRef)trackerConfig.ItemId,
                nameof(StatusTracker) => (StatusRef)trackerConfig.ItemId,
                nameof(ParameterTracker) => (ParamRef)trackerConfig.ItemId,
                _ => null
            };
        }

        tracker.TrackerConfig = trackerConfig;

        tracker.AddonDropdown = new(tracker);
        tracker.WidgetMenuTable = new(tracker);
        tracker.WidgetMenuWindow = new(tracker);
        tracker.TrackerDropdown = new(tracker);

        return tracker;
    }

    // Persist current widget config back into the tracker config.
    public void WriteWidgetConfig()
    {
        try
        {
            if (Widget == null) return;
            Widget.ApplyConfigs();

            // Find a property on the persisted WidgetConfig container that matches this widget's config type name,
            // and set it to the widget's live config instance.
            var liveTypeName = Widget.Config?.GetType().Name;
            if (liveTypeName == null) return;

            var prop = WidgetConfig.GetType()
                                   .GetProperties()
                                   .FirstOrDefault(p => p.PropertyType.Name == liveTypeName);

            if (prop != null)
            {
                prop.SetValue(WidgetConfig, Widget.Config);
            }
        }
        catch (Exception e)
        {
            try { Service.Log.Error($"WriteWidgetConfig failed for {TrackerConfig.TrackerType} item {TrackerConfig.ItemId}: {e}"); } catch { }
        }
    }
}