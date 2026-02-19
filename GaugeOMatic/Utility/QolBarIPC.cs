using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace GaugeOMatic.Utility
{
    // Cammy-style QoLBar IPC wrapper using ICallGateSubscriber<T...>
    // Added: OnQoLBarEnabledChanged event so consumers can react when QoLBar enables/disables.
    public static class QoLBarIPC
    {
        public static bool QoLBarEnabled { get; private set; } = false;

        public static ICallGateSubscriber<object>? qolBarInitializedSubscriber;
        public static ICallGateSubscriber<object>? qolBarDisposedSubscriber;
        public static ICallGateSubscriber<string>? qolBarGetVersionSubscriber;
        public static ICallGateSubscriber<int>? qolBarGetIPCVersionSubscriber;
        public static ICallGateSubscriber<string[]>? qolBarGetConditionSetsProvider;
        public static ICallGateSubscriber<int, bool>? qolBarCheckConditionSetProvider;
        public static ICallGateSubscriber<int, int, object>? qolBarMovedConditionSetProvider;
        public static ICallGateSubscriber<int, object>? qolBarRemovedConditionSetProvider;

        // Event invoked when QoLBarEnabled changes (true = enabled, false = disabled)
        public static event Action<bool>? OnQoLBarEnabledChanged;

        public static int QoLBarIPCVersion
        {
            get
            {
                try { return qolBarGetIPCVersionSubscriber?.InvokeFunc() ?? 0; }
                catch { return 0; }
            }
        }

        public static string QoLBarVersion
        {
            get
            {
                try { return qolBarGetVersionSubscriber?.InvokeFunc() ?? "0.0.0.0"; }
                catch { return "0.0.0.0"; }
            }
        }

        public static string[] GetConditionSets()
        {
            try { return qolBarGetConditionSetsProvider?.InvokeFunc() ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        // Initialize exactly like Cammy: collect subscribers and subscribe.
        // Accept optional callbacks to forward moved/removed events into the plugin.
        public static void Initialize(IDalamudPluginInterface pluginInterface, Action<int, int>? onMoved = null, Action<int>? onRemoved = null)
        {
            qolBarInitializedSubscriber = pluginInterface.GetIpcSubscriber<object>("QoLBar.Initialized");
            qolBarDisposedSubscriber = pluginInterface.GetIpcSubscriber<object>("QoLBar.Disposed");
            qolBarGetIPCVersionSubscriber = pluginInterface.GetIpcSubscriber<int>("QoLBar.GetIPCVersion");
            qolBarGetVersionSubscriber = pluginInterface.GetIpcSubscriber<string>("QoLBar.GetVersion");
            qolBarGetConditionSetsProvider = pluginInterface.GetIpcSubscriber<string[]>("QoLBar.GetConditionSets");
            qolBarCheckConditionSetProvider = pluginInterface.GetIpcSubscriber<int, bool>("QoLBar.CheckConditionSet");
            qolBarMovedConditionSetProvider = pluginInterface.GetIpcSubscriber<int, int, object>("QoLBar.MovedConditionSet");
            qolBarRemovedConditionSetProvider = pluginInterface.GetIpcSubscriber<int, object>("QoLBar.RemovedConditionSet");

            // Subscriptions (use explicit delegate shapes to match ICallGateSubscriber API)
            try
            {
                // QoLBar.Initialized -> Enable
                qolBarInitializedSubscriber?.Subscribe(new Action(EnableQoLBarIPC));
            }
            catch { /* ignore subscribe failures */ }

            try
            {
                // QoLBar.Disposed -> Disable
                qolBarDisposedSubscriber?.Subscribe(new Action(DisableQoLBarIPC));
            }
            catch { /* ignore subscribe failures */ }

            try
            {
                if (qolBarMovedConditionSetProvider != null && onMoved != null)
                {
                    // Prefer Action<int,int> -- the provider may include a third param but this overload exists for ICallGateSubscriber
                    qolBarMovedConditionSetProvider.Subscribe(new Action<int, int>((from, to) =>
                    {
                        try { onMoved.Invoke(from, to); } catch { }
                    }));
                }
            }
            catch { /* ignore */ }

            try
            {
                if (qolBarRemovedConditionSetProvider != null && onRemoved != null)
                {
                    qolBarRemovedConditionSetProvider.Subscribe(new Action<int>(removed =>
                    {
                        try { onRemoved.Invoke(removed); } catch { }
                    }));
                }
            }
            catch { /* ignore */ }

            // Attempt immediate enable if QoLBar already present
            EnableQoLBarIPC();
        }

        // Public so callers can force a check (same as Cammy)
        public static void EnableQoLBarIPC()
        {
            try
            {
                if (QoLBarIPCVersion != 1) return;
                if (!QoLBarEnabled)
                {
                    QoLBarEnabled = true;
                    try { OnQoLBarEnabledChanged?.Invoke(true); } catch { }
                }
            }
            catch
            {
                QoLBarEnabled = false;
            }
        }

        public static void DisableQoLBarIPC()
        {
            try
            {
                if (QoLBarEnabled)
                {
                    QoLBarEnabled = false;
                    try { OnQoLBarEnabledChanged?.Invoke(false); } catch { }
                }
            }
            catch
            {
                QoLBarEnabled = false;
            }
        }

        public static bool CheckConditionSet(int i)
        {
            try { return qolBarCheckConditionSetProvider?.InvokeFunc(i) ?? false; }
            catch { return false; }
        }
    }
}