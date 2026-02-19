using System;
using Dalamud.Plugin;

namespace GaugeOMatic.Utility
{
    // QoLBar IPC wrapper (no instance Subscribe calls).
    // Initialize(...) grabs IPC providers; call Reevaluate() to re-check availability.
    public static class QoLBarIPC
    {
        private static dynamic? QolBarGetIPCVersionSubscriber;
        private static dynamic? QolBarGetVersionSubscriber;
        private static dynamic? QolBarGetConditionSetsProvider;
        private static dynamic? QolBarCheckConditionSetProvider;
        private static dynamic? QolBarMovedConditionSetProvider;
        private static dynamic? QolBarRemovedConditionSetProvider;

        public static bool QoLBarEnabled { get; private set; }

        // Initialize only collects the IPC subscribers/providers.
        // Do NOT assume Subscribe instance method is available in all Dalamud versions.
        public static void Initialize(IDalamudPluginInterface pluginInterface, Action<int, int>? onMoved = null, Action<int>? onRemoved = null, Action<string>? logger = null)
        {
            try
            {
                // Grab providers/subscribers (may be null)
                QolBarGetIPCVersionSubscriber = pluginInterface.GetIpcSubscriber<int>("QoLBar.GetIPCVersion");
                QolBarGetVersionSubscriber = pluginInterface.GetIpcSubscriber<string>("QoLBar.GetVersion");
                QolBarGetConditionSetsProvider = pluginInterface.GetIpcSubscriber<string[]>("QoLBar.GetConditionSets");
                QolBarCheckConditionSetProvider = pluginInterface.GetIpcSubscriber<int, bool>("QoLBar.CheckConditionSet");
                QolBarMovedConditionSetProvider = pluginInterface.GetIpcSubscriber<int, int, object>("QoLBar.MovedConditionSet");
                QolBarRemovedConditionSetProvider = pluginInterface.GetIpcSubscriber<int, object>("QoLBar.RemovedConditionSet");

                // We do not attempt dynamic .Subscribe() because some Dalamud versions implement Subscribe as an extension method,
                // which cannot be called via dynamic here and causes runtime binder exceptions. Instead we poll for IPC readiness.
                logger?.Invoke("QoLBarIPC: Initialized providers (subscribe skipped).");
            }
            catch (Exception e)
            {
                logger?.Invoke($"QoLBarIPC.Initialize exception: {e}");
            }
        }

        // Re-evaluate whether QoLBar IPC is available (calls GetIPCVersion).
        // Returns updated QoLBarEnabled state.
        public static bool Reevaluate(Action<string>? logger = null, Action<bool>? onEnabledChanged = null)
        {
            try
            {
                var sub = QolBarGetIPCVersionSubscriber;
                if (sub == null)
                {
                    logger?.Invoke("QoLBarIPC: GetIPCVersion provider is null.");
                    if (QoLBarEnabled)
                    {
                        QoLBarEnabled = false;
                        onEnabledChanged?.Invoke(false);
                    }
                    return QoLBarEnabled;
                }

                int ver = TryInvoke0Int((object?)sub);
                if (ver != 1)
                {
                    logger?.Invoke($"QoLBarIPC: GetIPCVersion returned {ver} (expect 1).");
                    if (QoLBarEnabled)
                    {
                        QoLBarEnabled = false;
                        onEnabledChanged?.Invoke(false);
                    }
                    return QoLBarEnabled;
                }

                if (!QoLBarEnabled)
                {
                    QoLBarEnabled = true;
                    logger?.Invoke($"QoLBarIPC: Enabled (IPCVer={ver}).");
                    onEnabledChanged?.Invoke(true);
                }
                return QoLBarEnabled;
            }
            catch (Exception e)
            {
                logger?.Invoke($"QoLBarIPC.Reevaluate exception: {e}");
                if (QoLBarEnabled)
                {
                    QoLBarEnabled = false;
                    onEnabledChanged?.Invoke(false);
                }
                return QoLBarEnabled;
            }
        }

        public static bool CheckConditionSet(int index)
        {
            if (!QoLBarEnabled) return false;
            var prov = QolBarCheckConditionSetProvider;
            if (prov == null) return false;
            try { return TryInvoke1Bool((object?)prov, index); }
            catch { return false; }
        }

        public static string[] GetConditionSets()
        {
            var prov = QolBarGetConditionSetsProvider;
            if (prov == null) return Array.Empty<string>();
            try
            {
                var obj = TryInvoke0((object?)prov);
                if (obj is string[] arr) return arr;
                return Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        public static int QoLBarIPCVersion
        {
            get
            {
                var sub = QolBarGetIPCVersionSubscriber;
                if (sub == null) return 0;
                try { return TryInvoke0Int((object?)sub); }
                catch { return 0; }
            }
        }

        public static string QoLBarVersion
        {
            get
            {
                var sub = QolBarGetVersionSubscriber;
                if (sub == null) return "0.0.0.0";
                try
                {
                    var obj = TryInvoke0((object?)sub);
                    return obj as string ?? "0.0.0.0";
                }
                catch { return "0.0.0.0"; }
            }
        }

        // ---- invocation helpers (accept object? to satisfy analyzer) ----

        private static object? TryInvoke0(object? subObj)
        {
            if (subObj == null) return null;
            dynamic sub = subObj;
            try { return sub.InvokeFunc(); } catch { }
            try { return sub.Invoke(); } catch { }
            try { return sub(); } catch { }
            return null;
        }

        private static int TryInvoke0Int(object? subObj)
        {
            if (subObj == null) return 0;
            dynamic sub = subObj;
            try { return (int)sub.InvokeFunc(); } catch { }
            try { return (int)sub.Invoke(); } catch { }
            try { return (int)sub(); } catch { }
            return 0;
        }

        private static bool TryInvoke1Bool(object? subObj, object a1)
        {
            if (subObj == null) return false;
            dynamic sub = subObj;
            try { return (bool)sub.InvokeFunc(a1); } catch { }
            try { return (bool)sub.Invoke(a1); } catch { }
            try { return (bool)sub(a1); } catch { }
            return false;
        }
    }
}