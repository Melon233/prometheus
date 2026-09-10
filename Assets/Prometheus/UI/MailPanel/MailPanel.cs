using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// MailPanel 的业务控制器；代码生成器只会首次创建本文件，不会覆盖后续业务修改。
    /// </summary>
    [UIPanelConfig("MailPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Destroy)]
    public sealed class MailPanel : MailPanelBase
    {
        /// <summary>
        /// 每次面板进入显示状态时调用，可在此刷新界面数据。
        /// </summary>
        protected override void OnOpen()
        {
            Debug.Log("[UIKit] MailPanel opened.", Root);
        }

        /// <summary>
        /// 响应 BackBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnBackBtnClick()
        {
            Close();
        }

        /// <summary>
        /// 响应 UnreadTabBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnUnreadTabBtnClick()
        {
        }

        /// <summary>
        /// 响应 ClaimedTabBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnClaimedTabBtnClick()
        {
        }

        /// <summary>
        /// 响应 CloseBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnCloseBtnClick()
        {
            Close();
        }

        /// <summary>
        /// 响应 SelectAllBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnSelectAllBtnClick()
        {
        }

        /// <summary>
        /// 响应 DeleteBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnDeleteBtnClick()
        {
        }

        /// <summary>
        /// 响应 ClaimBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnClaimBtnClick()
        {
        }

        /// <summary>
        /// 响应 ClaimAllBtn 点击事件；监听注册和移除由生成的 MailPanelBase 自动管理。
        /// </summary>
        protected override void OnClaimAllBtnClick()
        {
        }
    }
}
