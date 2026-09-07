using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 承载跨场景运行时对象的唯一常驻根节点。
    /// 需要场景锚点的系统按需自取，因此"运行时根节点"不再作为启动参数逐层传递；
    /// 单点持有 DontDestroyOnLoad 也使层级结构保持整洁，便于运行期排查。
    /// </summary>
    public static class PersistentRoot
    {
        /// <summary>常驻根节点在层级中的显示名称。</summary>
        private const string RootName = "[Prometheus]";

        /// <summary>当前常驻根节点；首次访问时创建，Core 释放时销毁。</summary>
        private static Transform shared;

        /// <summary>获取常驻根节点；不存在时立即创建并标记为跨场景保留。</summary>
        public static Transform Shared
        {
            get
            {
                if (shared == null)
                {
                    GameObject rootObject = new GameObject(RootName);
                    // DontDestroyOnLoad 是运行时专有 API：EditMode 下调用会抛异常，
                    // 而编辑期本来就不发生场景切换，因此该标记只在播放模式下需要。
                    if (Application.isPlaying) Object.DontDestroyOnLoad(rootObject);
                    shared = rootObject.transform;
                }

                return shared;
            }
        }

        /// <summary>
        /// 销毁常驻根节点及其全部子对象。
        /// 由 Core 在释放阶段调用，使一局结束后不残留跨场景对象；Editor 下的重复进入播放也据此复位。
        /// </summary>
        internal static void Reset()
        {
            if (shared == null)
            {
                shared = null;
                return;
            }

            GameObject rootObject = shared.gameObject;
            shared = null;
            if (Application.isPlaying) Object.Destroy(rootObject);
            else Object.DestroyImmediate(rootObject);
        }
    }
}
