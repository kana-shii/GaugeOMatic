using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace GaugeOMatic.Utility
{
    // Cammy-style QoLBar IPC wrapper using ICallGateSubscriber<T...>
    // Cached condition-sets kept so UI code can read names even when QoLBar is absent.
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

        // Cached condition sets (last successfully read from QoLBar). Use this when provider is unavailable.
        private static string[] CachedConditionSets = Array.Empty<string>();

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

        // Return the last-known condition set names if the provider is missing/unavailable.
        public static string[] GetConditionSets()
        {
            try
            {
                var live = qolBarGetConditionSetsProvider?.InvokeFunc();
                if (live != null && live.Length > 0)
                {
                    CachedConditionSets = live;
                    return live;
                }
            }
            catch
            {
                // ignore and fall back to cached
            }

            return CachedConditionSets ?? Array.Empty<string>();
        }

        // Helper: safe name retrieval for a given index. Returns "[#] Unknown" when name not available.
        public static string GetConditionSetName(int index)
        {
            if (index < 0) return "None";
            var sets = GetConditionSets();
            if (index >= 0 && index < sets.Length)
            {
                return $"[{index + 1}] {sets[index]}";
            }

            // If index present but we don't have a name, show numeric fallback
            return $"[{index + 1}] (Unknown)";
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

            // Attempt immediate enable and refresh cached sets if possible
            EnableQoLBarIPC();
            TryRefreshCachedConditionSets();
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
                    try { TryRefreshCachedConditionSets(); } catch { }
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

        // Attempt to refresh cachedConditionSets from the live provider, if available.
        private static void TryRefreshCachedConditionSets()
        {
            try
            {
                var live = qolBarGetConditionSetsProvider?.InvokeFunc();
                if (live != null && live.Length > 0)
                {
                    CachedConditionSets = live;
                }
            }
            catch
            {
                // ignore failures, keep cached
            }
        }
    }
}