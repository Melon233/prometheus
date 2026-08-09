using System;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 定义面板在 UIKit 根节点中的显示顺序，枚举顺序与运行时层节点的兄弟顺序一致。
    /// </summary>
    public enum UIPanelLayer
    {
        Background = 0,
        Normal = 100,
        Popup = 200,
        Overlay = 300
    }

    /// <summary>
    /// 定义 ClosePanel 后对 Prefab 实例的处理策略。
    /// </summary>
    public enum UIPanelClosePolicy
    {
        Destroy = 0,
        Cache = 1
    }

    /// <summary>
    /// 表示 UIKit 内部记录使用的面板生命周期状态。
    /// </summary>
    internal enum UIPanelState
    {
        Opening = 0,
        Open = 1,
        Cached = 2,
        Disposed = 3
    }

    /// <summary>
    /// 将资源地址、显示层和关闭策略直接声明在具体 Panel 脚本上，UIKit 初始化时会自动扫描并缓存该配置。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class UIPanelConfigAttribute : Attribute
    {
        /// <summary>
        /// 创建一份面板类型配置。
        /// </summary>
        /// <param name="assetAddress">AssetKit 使用的 UI Prefab 资源地址。</param>
        /// <param name="layer">面板需要进入的显示层。</param>
        /// <param name="closePolicy">ClosePanel 后缓存还是销毁实例。</param>
        public UIPanelConfigAttribute(string assetAddress, UIPanelLayer layer = UIPanelLayer.Normal, UIPanelClosePolicy closePolicy = UIPanelClosePolicy.Destroy)
        {
            if (string.IsNullOrWhiteSpace(assetAddress))
                throw new ArgumentException("Panel asset address cannot be empty.", nameof(assetAddress));

            AssetAddress = assetAddress;
            Layer = layer;
            ClosePolicy = closePolicy;
        }

        /// <summary>
        /// 获取 AssetKit 使用的 UI Prefab 资源地址。
        /// </summary>
        public string AssetAddress { get; }

        /// <summary>
        /// 获取面板所属显示层。
        /// </summary>
        public UIPanelLayer Layer { get; }

        /// <summary>
        /// 获取 ClosePanel 后的实例处理策略。
        /// </summary>
        public UIPanelClosePolicy ClosePolicy { get; }
    }

    /// <summary>
    /// 保存 UIKit 打开一个面板所需的已验证配置和控制器工厂。
    /// </summary>
    internal sealed class UIPanelDescriptor
    {
        /// <summary>
        /// 根据具体面板类型及其特性配置创建描述记录。
        /// </summary>
        public UIPanelDescriptor(Type panelType, UIPanelConfigAttribute configuration)
        {
            PanelType = panelType ?? throw new ArgumentNullException(nameof(panelType));
            AssetAddress = configuration?.AssetAddress ?? throw new ArgumentNullException(nameof(configuration));
            Layer = configuration.Layer;
            ClosePolicy = configuration.ClosePolicy;
        }

        public Type PanelType { get; }
        public string AssetAddress { get; }
        public UIPanelLayer Layer { get; }
        public UIPanelClosePolicy ClosePolicy { get; }

        /// <summary>
        /// 使用面板公开无参构造函数创建纯 C# 控制器。
        /// </summary>
        public UIPanel CreateController()
        {
            return Activator.CreateInstance(PanelType) as UIPanel ?? throw new InvalidOperationException($"Unable to create panel controller '{PanelType.FullName}'. Ensure it has a public parameterless constructor.");
        }
    }
}
