using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// CharacterPanel 的业务控制器；代码生成器只会首次创建本文件，不会覆盖后续业务修改。
    /// </summary>
    [UIPanelConfig("CharacterPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Destroy)]
    public sealed class CharacterPanel : CharacterPanelBase
    {
        /// <summary>
        /// 每次面板进入显示状态时调用，可在此刷新界面数据。
        /// </summary>
        protected override void OnOpen()
        {
            Debug.Log("[UIKit] CharacterPanel opened.", Root);
        }

        /// <summary>
        /// 响应 PrevCharacterBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnPrevCharacterBtnClick()
        {
        }

        /// <summary>
        /// 响应 NextCharacterBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnNextCharacterBtnClick()
        {
        }

        /// <summary>
        /// 响应 CloseBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnCloseBtnClick()
        {
            Close();
        }

        /// <summary>
        /// 响应 TabAttribute 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnTabAttributeClick()
        {
        }

        /// <summary>
        /// 响应 TabWeapon 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnTabWeaponClick()
        {
        }

        /// <summary>
        /// 响应 TabArtifact 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnTabArtifactClick()
        {
        }

        /// <summary>
        /// 响应 TabConstellation 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnTabConstellationClick()
        {
        }

        /// <summary>
        /// 响应 TabTalent 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnTabTalentClick()
        {
        }

        /// <summary>
        /// 响应 TabProfile 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnTabProfileClick()
        {
        }

        /// <summary>
        /// 响应 DetailBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnDetailBtnClick()
        {
        }

        /// <summary>
        /// 响应 LayoutToggleBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnLayoutToggleBtnClick()
        {
        }

        /// <summary>
        /// 响应 GuideBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnGuideBtnClick()
        {
        }

        /// <summary>
        /// 响应 AscendHintBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnAscendHintBtnClick()
        {
        }

        /// <summary>
        /// 响应 AscendBtn 点击事件；监听注册和移除由生成的 CharacterPanelBase 自动管理。
        /// </summary>
        protected override void OnAscendBtnClick()
        {
        }
    }
}
