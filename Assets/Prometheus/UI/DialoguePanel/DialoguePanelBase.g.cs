namespace Xuan.Prometheus
{
    /// <summary>
    /// 由 UIKit 代码生成器根据 DialoguePanel Prefab 的 UIComponentBinder 自动生成。
    /// 本文件只保存强类型组件表，业务生命周期和配置应写在对应 Panel 脚本中。
    /// </summary>
    public abstract class DialoguePanelBase : UIPanel
    {
        /// <summary>
        /// 获取 Binder 中名为 AdvanceBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button AdvanceBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 SpeakerText 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Text SpeakerText { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 LineText 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Text LineText { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 ChoiceRoot 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.RectTransform ChoiceRoot { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 ChoiceTemplate 的强类型组件引用。
        /// </summary>
        protected global::Xuan.Prometheus.ChoiceOptionMono ChoiceTemplate { get; private set; }

        /// <summary>
        /// 处理 AdvanceBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnAdvanceBtnClick();

        /// <summary>
        /// 按 Binder 表中的稳定索引和名称绑定全部强类型组件字段，并为普通 Button 注册点击监听；承担拖拽输入的 OnScreenStick 不注册点击回调。
        /// </summary>
        protected override void BindComponents(UIComponentBinder binder)
        {
            AdvanceBtn = binder.Get<global::UnityEngine.UI.Button>(0, "AdvanceBtn");
            SpeakerText = binder.Get<global::UnityEngine.UI.Text>(1, "SpeakerText");
            LineText = binder.Get<global::UnityEngine.UI.Text>(2, "LineText");
            ChoiceRoot = binder.Get<global::UnityEngine.RectTransform>(3, "ChoiceRoot");
            ChoiceTemplate = binder.Get<global::Xuan.Prometheus.ChoiceOptionMono>(4, "ChoiceTemplate");

            AdvanceBtn.onClick.AddListener(OnAdvanceBtnClick);
        }

        /// <summary>
        /// 在面板最终释放时移除生成器托管的 Button 点击监听并清空组件引用，避免事件或控制器延长 Unity 对象生命周期。
        /// </summary>
        protected override void UnbindComponents()
        {
            AdvanceBtn.onClick.RemoveListener(OnAdvanceBtnClick);

            AdvanceBtn = null;
            SpeakerText = null;
            LineText = null;
            ChoiceRoot = null;
            ChoiceTemplate = null;
        }
    }
}
