namespace Xuan.Prometheus
{
    /// <summary>
    /// 预留的登录面板控制器；补充 Prefab Binder、生成的 LoginPanelBase 和 UIPanelConfigAttribute 后即可由 UIKit 打开。
    /// </summary>
    public sealed class LoginPanel : UIPanel
    {
        /// <summary>
        /// 当前占位面板尚无组件表，因此绑定阶段不读取任何组件。
        /// </summary>
        protected override void BindComponents(UIComponentBinder binder)
        {
        }

        /// <summary>
        /// 当前占位面板尚无组件字段，因此释放阶段无需清空引用。
        /// </summary>
        protected override void UnbindComponents()
        {
        }
    }
}
