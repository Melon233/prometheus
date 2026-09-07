namespace Xuan.Prometheus
{
    /// <summary>
    /// 由 UIKit 代码生成器根据 MapPanel Prefab 的 UIComponentBinder 自动生成。
    /// 地图纹理和关闭按钮由 MapPanel Prefab 固定绑定，POI 标记仍由 MapPanel 在初始化阶段按世界数据创建。
    /// </summary>
    public abstract class MapPanelBase : UIPanel
    {
        /// <summary>获取地图视口中由 MapPanel Prefab 绑定的地图纹理组件。</summary>
        protected global::UnityEngine.UI.RawImage MapImage { get; private set; }

        /// <summary>获取地图模板左上角由 Binder 绑定的关闭按钮。</summary>
        protected global::UnityEngine.UI.Button CloseButton { get; private set; }

        /// <summary>处理 CloseButton 点击，业务面板负责关闭当前地图面板。</summary>
        protected abstract void OnCloseButtonClick();

        /// <summary>按 Binder 稳定索引读取地图纹理和关闭按钮，并注册生成托管的点击监听。</summary>
        protected override void BindComponents(UIComponentBinder binder)
        {
            MapImage = binder.Get<global::UnityEngine.UI.RawImage>(0, "MapImage");
            CloseButton = binder.Get<global::UnityEngine.UI.Button>(1, "CloseButton");
            CloseButton.onClick.AddListener(OnCloseButtonClick);
        }

        /// <summary>移除关闭按钮监听并清空生成的组件引用。</summary>
        protected override void UnbindComponents()
        {
            CloseButton.onClick.RemoveListener(OnCloseButtonClick);
            MapImage = null;
            CloseButton = null;
        }
    }
}
