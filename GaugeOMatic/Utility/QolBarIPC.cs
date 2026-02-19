using System;
using Dalamud.Plugin;

namespace GaugeOMatic.Utility
{
    // QoLBar IPC wrapper adapted from Cammy.
    // Initialize with QoLBarIPC.Initialize(pluginInterface, onMoved, onRemoved) during plugin init.
    public static class QoLBarIPC
    {
        // Stored as object to avoid compile-time dependency on Dalamud.Game.Ipc types.
        private static object? qolBarInitializedSubscriber;
        private static object? qolBarDisposedSubscriber;
        private static object? qolBarGetIPCVersionSubscriber;
        private static object? qolBarGetVersionSubscriber;
        private static object? qolBarGetConditionSetsProvider;
        private static object? qolBarCheckConditionSetProvider;
        private static object? qolBarMovedConditionSetProvider;
        private static object? qolBarRemovedConditionSetProvider;

        public static bool QoLBarEnabled { get; private set; }

        // Initialize and subscribe to QoLBar IPC events.
        // onMoved(from, to) will be called when QoLBar reports a moved condition set (supply indices).
        // onRemoved(removedIndex) will be called when QoLBar reports a removed condition set.
        public static void Initialize(IDalamudPluginInterface pluginInterface, Action<int, int>? onMoved = null, Action<int>? onRemoved = null)
        {
            try
            {
                qolBarInitializedSubscriber = pluginInterface.GetIpcSubscriber<object>("QoLBar.Initialized");
                qolBarDisposedSubscriber = pluginInterface.GetIpcSubscriber<object>("QoLBar.Disposed");
                qolBarGetIPCVersionSubscriber = pluginInterface.GetIpcSubscriber<int>("QoLBar.GetIPCVersion");
                qolBarGetVersionSubscriber = pluginInterface.GetIpcSubscriber<string>("QoLBar.GetVersion");
                qolBarGetConditionSetsProvider = pluginInterface.GetIpcSubscriber<string[]>("QoLBar.GetConditionSets");
                qolBarCheckConditionSetProvider = pluginInterface.GetIpcSubscriber<int, bool>("QoLBar.CheckConditionSet");
                qolBarMovedConditionSetProvider = pluginInterface.GetIpcSubscriber<int, int, object>("QoLBar.MovedConditionSet");
                qolBarRemovedConditionSetProvider = pluginInterface.GetIpcSubscriber<int, object>("QoLBar.RemovedConditionSet");

                // Subscribe to init/dispose so we can enable/disable behavior when QoLBar starts/stops.
                try
                {
                    if (qolBarInitializedSubscriber != null)
                        ((dynamic)qolBarInitializedSubscriber).Subscribe((Action<object>)(_ => EnableQoLBarIPC()));
                }
                catch { /* ignore subscribe failures */ }

                try
                {
                    if (qolBarDisposedSubscriber != null)
                        ((dynamic)qolBarDisposedSubscriber).Subscribe((Action<object>)(_ => DisableQoLBarIPC()));
                }
                catch { /* ignore subscribe failures */ }

                // Subscribe to moved/removed events and forward indices to caller callbacks.
                try
                {
                    if (qolBarMovedConditionSetProvider != null)
                        ((dynamic)qolBarMovedConditionSetProvider).Subscribe((Action<int, int, object>)((from, to, _) =>
                        {
                            try { onMoved?.Invoke(from, to); } catch { }
                        }));
                }
                catch { /* ignore subscribe failures */ }

                try
                {
                    if (qolBarRemovedConditionSetProvider != null)
                        ((dynamic)qolBarRemovedConditionSetProvider).Subscribe((Action<int, object>)((removed, _) =>
                        {
                            try { onRemoved?.Invoke(removed); } catch { }
                        }));
                }
                catch { /* ignore subscribe failures */ }

                // Attempt to enable IPC immediately if QoLBar is already present.
                EnableQoLBarIPC();
            }
            catch
            {
                QoLBarEnabled = false;
            }
        }

        private static void EnableQoLBarIPC()
        {
            try
            {
                if (qolBarGetIPCVersionSubscriber == null) return;
                var ver = ((dynamic)qolBarGetIPCVersionSubscriber).InvokeFunc();
                if (ver != 1) return;
                QoLBarEnabled = true;
            }
            catch
            {
                QoLBarEnabled = false;
            }
        }

        private static void DisableQoLBarIPC() => QoLBarEnabled = false;

        public static bool CheckConditionSet(int index)
        {
            if (!QoLBarEnabled || qolBarCheckConditionSetProvider == null) return false;
            try { return ((dynamic)qolBarCheckConditionSetProvider).InvokeFunc(index); }
            catch { return false; }
        }

        public static string[] GetConditionSets()
        {
            try { return ((dynamic)qolBarGetConditionSetsProvider)?.InvokeFunc() ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        public static int QoLBarIPCVersion
        {
            get
            {
                try { return ((dynamic)qolBarGetIPCVersionSubscriber)?.InvokeFunc() ?? 0; }
                catch { return 0; }
            }
        }

        public static string QoLBarVersion
        {
            get
            {
                try { return ((dynamic)qolBarGetVersionSubscriber)?.InvokeFunc() ?? "0.0.0.0"; }
                catch { return "0.0.0.0"; }
            }
        }
    }
}
