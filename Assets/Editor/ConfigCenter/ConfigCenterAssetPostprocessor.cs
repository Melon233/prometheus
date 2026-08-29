using UnityEditor;

namespace Xuan.Prometheus.ConfigKit.Editor
{
    /// <summary>在配置资产导入、删除或移动后延迟刷新配置中心窗口，避免导入批次内重复扫描。</summary>
    internal sealed class ConfigCenterAssetPostprocessor : AssetPostprocessor
    {
        private static bool pendingRefresh;

        /// <summary>接收 Unity 资产变更通知；只有配置中心允许的路径才触发增量刷新请求。</summary>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets) if (ConfigCenterIndexer.IsIncluded(path)) { RequestRefresh(); return; }
            foreach (string path in deletedAssets) if (ConfigCenterIndexer.IsIncluded(path)) { RequestRefresh(); return; }
            foreach (string path in movedAssets) if (ConfigCenterIndexer.IsIncluded(path)) { RequestRefresh(); return; }
            foreach (string path in movedFromAssetPaths) if (ConfigCenterIndexer.IsIncluded(path)) { RequestRefresh(); return; }
        }

        /// <summary>合并刷新请求并在当前导入批次结束后通知窗口。</summary>
        private static void RequestRefresh()
        {
            if (pendingRefresh) return;
            pendingRefresh = true;
            EditorApplication.delayCall += FlushRefresh;
        }

        /// <summary>执行一次合并后的完整索引重建，并让打开的配置中心重新读取结果。</summary>
        private static void FlushRefresh()
        {
            pendingRefresh = false;
            ConfigCenterWindow.NotifyIndexChanged(ConfigCenterIndexer.Rebuild());
        }
    }
}
