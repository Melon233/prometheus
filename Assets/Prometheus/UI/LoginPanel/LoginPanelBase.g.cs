namespace Xuan.Prometheus
{
    /// <summary>
    /// 由 UIKit 代码生成器根据 LoginPanel Prefab 的 UIComponentBinder 自动生成。
    /// 本文件只保存强类型组件表，业务生命周期和配置应写在对应 Panel 脚本中。
    /// </summary>
    public abstract class LoginPanelBase : UIPanel
    {
        /// <summary>
        /// 获取 Binder 中名为 EnterBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button EnterBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 TitleText 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Text TitleText { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 HintText 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Text HintText { get; private set; }

        /// <summary>
        /// 处理 EnterBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnEnterBtnClick();

        /// <summary>
        /// 按 Binder 表中的稳定索引和名称绑定全部强类型组件字段，并为普通 Button 注册点击监听；承担拖拽输入的 OnScreenStick 不注册点击回调。
        /// </summary>
        protected override void BindComponents(UIComponentBinder binder)
        {
            EnterBtn = binder.Get<global::UnityEngine.UI.Button>(0, "EnterBtn");
            TitleText = binder.Get<global::UnityEngine.UI.Text>(1, "TitleText");
            HintText = binder.Get<global::UnityEngine.UI.Text>(2, "HintText");

            EnterBtn.onClick.AddListener(OnEnterBtnClick);
        }

        /// <summary>
        /// 在面板最终释放时移除生成器托管的 Button 点击监听并清空组件引用，避免事件或控制器延长 Unity 对象生命周期。
        /// </summary>
        protected override void UnbindComponents()
        {
            EnterBtn.onClick.RemoveListener(OnEnterBtnClick);

            EnterBtn = null;
            TitleText = null;
            HintText = null;
        }
    }
}
