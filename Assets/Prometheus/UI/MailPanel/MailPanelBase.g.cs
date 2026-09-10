namespace Xuan.Prometheus
{
    /// <summary>
    /// 由 UIKit 代码生成器根据 MailPanel Prefab 的 UIComponentBinder 自动生成。
    /// 本文件只保存强类型组件表，业务生命周期和配置应写在对应 Panel 脚本中。
    /// </summary>
    public abstract class MailPanelBase : UIPanel
    {
        /// <summary>
        /// 获取 Binder 中名为 BackBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button BackBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 UnreadTabBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button UnreadTabBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 UnreadTabBtnText 的强类型组件引用。
        /// </summary>
        protected global::TMPro.TextMeshProUGUI UnreadTabBtnText { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 ClaimedTabBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button ClaimedTabBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 CloseBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button CloseBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 MailList 的强类型组件引用。
        /// </summary>
        protected global::SuperScrollView.LoopListView2 MailList { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 SelectAllBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button SelectAllBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 DeleteBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button DeleteBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 DetailTitle 的强类型组件引用。
        /// </summary>
        protected global::TMPro.TextMeshProUGUI DetailTitle { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 DetailSender 的强类型组件引用。
        /// </summary>
        protected global::TMPro.TextMeshProUGUI DetailSender { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 DetailTime 的强类型组件引用。
        /// </summary>
        protected global::TMPro.TextMeshProUGUI DetailTime { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 DetailBody 的强类型组件引用。
        /// </summary>
        protected global::TMPro.TextMeshProUGUI DetailBody { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 AttachSlot1 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image AttachSlot1 { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 AttachSlot2 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image AttachSlot2 { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 AttachSlot3 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image AttachSlot3 { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 AttachSlot4 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image AttachSlot4 { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 ClaimBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button ClaimBtn { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 ClaimAllBtn 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button ClaimAllBtn { get; private set; }

        /// <summary>
        /// 处理 BackBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnBackBtnClick();

        /// <summary>
        /// 处理 UnreadTabBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnUnreadTabBtnClick();

        /// <summary>
        /// 处理 ClaimedTabBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnClaimedTabBtnClick();

        /// <summary>
        /// 处理 CloseBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnCloseBtnClick();

        /// <summary>
        /// 处理 SelectAllBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnSelectAllBtnClick();

        /// <summary>
        /// 处理 DeleteBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnDeleteBtnClick();

        /// <summary>
        /// 处理 ClaimBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnClaimBtnClick();

        /// <summary>
        /// 处理 ClaimAllBtn 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnClaimAllBtnClick();

        /// <summary>
        /// 按 Binder 表中的稳定索引和名称绑定全部强类型组件字段，并为普通 Button 注册点击监听；承担拖拽输入的 OnScreenStick 不注册点击回调。
        /// </summary>
        protected override void BindComponents(UIComponentBinder binder)
        {
            BackBtn = binder.Get<global::UnityEngine.UI.Button>(0, "BackBtn");
            UnreadTabBtn = binder.Get<global::UnityEngine.UI.Button>(1, "UnreadTabBtn");
            UnreadTabBtnText = binder.Get<global::TMPro.TextMeshProUGUI>(2, "UnreadTabBtnText");
            ClaimedTabBtn = binder.Get<global::UnityEngine.UI.Button>(3, "ClaimedTabBtn");
            CloseBtn = binder.Get<global::UnityEngine.UI.Button>(4, "CloseBtn");
            MailList = binder.Get<global::SuperScrollView.LoopListView2>(5, "MailList");
            SelectAllBtn = binder.Get<global::UnityEngine.UI.Button>(6, "SelectAllBtn");
            DeleteBtn = binder.Get<global::UnityEngine.UI.Button>(7, "DeleteBtn");
            DetailTitle = binder.Get<global::TMPro.TextMeshProUGUI>(8, "DetailTitle");
            DetailSender = binder.Get<global::TMPro.TextMeshProUGUI>(9, "DetailSender");
            DetailTime = binder.Get<global::TMPro.TextMeshProUGUI>(10, "DetailTime");
            DetailBody = binder.Get<global::TMPro.TextMeshProUGUI>(11, "DetailBody");
            AttachSlot1 = binder.Get<global::UnityEngine.UI.Image>(12, "AttachSlot1");
            AttachSlot2 = binder.Get<global::UnityEngine.UI.Image>(13, "AttachSlot2");
            AttachSlot3 = binder.Get<global::UnityEngine.UI.Image>(14, "AttachSlot3");
            AttachSlot4 = binder.Get<global::UnityEngine.UI.Image>(15, "AttachSlot4");
            ClaimBtn = binder.Get<global::UnityEngine.UI.Button>(16, "ClaimBtn");
            ClaimAllBtn = binder.Get<global::UnityEngine.UI.Button>(17, "ClaimAllBtn");

            BackBtn.onClick.AddListener(OnBackBtnClick);
            UnreadTabBtn.onClick.AddListener(OnUnreadTabBtnClick);
            ClaimedTabBtn.onClick.AddListener(OnClaimedTabBtnClick);
            CloseBtn.onClick.AddListener(OnCloseBtnClick);
            SelectAllBtn.onClick.AddListener(OnSelectAllBtnClick);
            DeleteBtn.onClick.AddListener(OnDeleteBtnClick);
            ClaimBtn.onClick.AddListener(OnClaimBtnClick);
            ClaimAllBtn.onClick.AddListener(OnClaimAllBtnClick);
        }

        /// <summary>
        /// 在面板最终释放时移除生成器托管的 Button 点击监听并清空组件引用，避免事件或控制器延长 Unity 对象生命周期。
        /// </summary>
        protected override void UnbindComponents()
        {
            BackBtn.onClick.RemoveListener(OnBackBtnClick);
            UnreadTabBtn.onClick.RemoveListener(OnUnreadTabBtnClick);
            ClaimedTabBtn.onClick.RemoveListener(OnClaimedTabBtnClick);
            CloseBtn.onClick.RemoveListener(OnCloseBtnClick);
            SelectAllBtn.onClick.RemoveListener(OnSelectAllBtnClick);
            DeleteBtn.onClick.RemoveListener(OnDeleteBtnClick);
            ClaimBtn.onClick.RemoveListener(OnClaimBtnClick);
            ClaimAllBtn.onClick.RemoveListener(OnClaimAllBtnClick);

            BackBtn = null;
            UnreadTabBtn = null;
            UnreadTabBtnText = null;
            ClaimedTabBtn = null;
            CloseBtn = null;
            MailList = null;
            SelectAllBtn = null;
            DeleteBtn = null;
            DetailTitle = null;
            DetailSender = null;
            DetailTime = null;
            DetailBody = null;
            AttachSlot1 = null;
            AttachSlot2 = null;
            AttachSlot3 = null;
            AttachSlot4 = null;
            ClaimBtn = null;
            ClaimAllBtn = null;
        }
    }
}
