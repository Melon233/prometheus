using System;

namespace Xuan.Prometheus.Editor.Prototype
{
    /// <summary>
    /// 标记一个原型定义类，并声明它生成的 Prefab 路径。
    /// Prefab 属于生成产物，每次重建都会被整体覆盖，因此不应在 Prefab 上手工修改任何内容。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIPrototypeAttribute : Attribute
    {
        /// <summary>声明该原型定义生成到的 Prefab 资产路径。</summary>
        /// <param name="prefabPath">以 Assets 开头的 Prefab 资产路径。</param>
        public UIPrototypeAttribute(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath)) throw new ArgumentException("UIPrototype requires a prefab path.", nameof(prefabPath));
            PrefabPath = prefabPath.Replace('\\', '/');
        }

        /// <summary>获取生成目标 Prefab 路径。</summary>
        public string PrefabPath { get; }

        /// <summary>获取或设置重建顺序；被别的原型引用的列表项等 Prefab 应使用更小的值。</summary>
        public int Order { get; set; }

        /// <summary>获取或设置是否在生成 Prefab 后调用 UIPanelCodeGenerator 产出强类型 PanelBase。</summary>
        public bool GeneratePanelCode { get; set; }

        /// <summary>获取或设置设计分辨率宽度，用于构建期计算布局尺寸。</summary>
        public float DesignWidth { get; set; } = 1920f;

        /// <summary>获取或设置设计分辨率高度，用于构建期计算布局尺寸。</summary>
        public float DesignHeight { get; set; } = 1080f;
    }
}
