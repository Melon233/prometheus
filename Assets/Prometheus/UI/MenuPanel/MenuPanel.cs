using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// MenuPanel 的业务控制器；代码生成器只会首次创建本文件，不会覆盖后续业务修改。
    /// </summary>
    [UIPanelConfig("MenuPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Destroy)]
    public sealed class MenuPanel : MenuPanelBase
    {
        /// <summary>
        /// 每次面板进入显示状态时调用，可在此刷新界面数据。
        /// </summary>
        protected override void OnOpen()
        {
            Debug.Log("[UIKit] MenuPanel opened.", Root);
        }

        /// <summary>
        /// 响应 BackBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnBackBtnClick()
        {
            Close();
        }

        /// <summary>
        /// 响应 CameraBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnCameraBtnClick()
        {
        }

        /// <summary>
        /// 响应 GiftBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnGiftBtnClick()
        {
        }

        /// <summary>
        /// 响应 MailBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnMailBtnClick()
        {
            Core.UI.OpenPanel<MailPanel>();
        }

        /// <summary>
        /// 响应 CompassBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnCompassBtnClick()
        {
        }

        /// <summary>
        /// 响应 SettingsBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnSettingsBtnClick()
        {
        }

        /// <summary>
        /// 响应 EditProfileBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnEditProfileBtnClick()
        {
        }

        /// <summary>
        /// 响应 CopyUidBtn 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnCopyUidBtnClick()
        {
        }

        /// <summary>
        /// 响应 TileShop 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileShopClick()
        {
        }

        /// <summary>
        /// 响应 TileParty 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTilePartyClick()
        {
        }

        /// <summary>
        /// 响应 TileFriends 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileFriendsClick()
        {
        }

        /// <summary>
        /// 响应 TileAchievement 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileAchievementClick()
        {
        }

        /// <summary>
        /// 响应 TileArchive 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileArchiveClick()
        {
        }

        /// <summary>
        /// 响应 TileCharacterArchive 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileCharacterArchiveClick()
        {
        }

        /// <summary>
        /// 响应 TileCharacter 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileCharacterClick()
        {
            Core.UI.OpenPanel<CharacterPanel>();
        }

        /// <summary>
        /// 响应 TileGuide 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileGuideClick()
        {
        }

        /// <summary>
        /// 响应 TileBag 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileBagClick()
        {
            Core.UI.OpenPanel<BagPanel>();
        }

        /// <summary>
        /// 响应 TileQuest 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileQuestClick()
        {
        }

        /// <summary>
        /// 响应 TileMap 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileMapClick()
        {
        }

        /// <summary>
        /// 响应 TileEvent 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileEventClick()
        {
        }

        /// <summary>
        /// 响应 TileHandbook 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileHandbookClick()
        {
        }

        /// <summary>
        /// 响应 TileWish 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileWishClick()
        {
        }

        /// <summary>
        /// 响应 TileBattlePass 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileBattlePassClick()
        {
        }

        /// <summary>
        /// 响应 TileCoop 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileCoopClick()
        {
        }

        /// <summary>
        /// 响应 TileSpecialEvent 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileSpecialEventClick()
        {
        }

        /// <summary>
        /// 响应 TileCommunity 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileCommunityClick()
        {
        }

        /// <summary>
        /// 响应 TileHighlights 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileHighlightsClick()
        {
        }

        /// <summary>
        /// 响应 TileFeedback 点击事件；监听注册和移除由生成的 MenuPanelBase 自动管理。
        /// </summary>
        protected override void OnTileFeedbackClick()
        {
        }
    }
}
