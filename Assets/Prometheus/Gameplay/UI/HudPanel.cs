using System;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Hud 面板业务控制器，用于验证 UIKit 的类型扫描、Prefab 加载、组件绑定、打开生命周期和关闭缓存能力。
    /// </summary>
    [UIPanelConfig("Prefabs_HudPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Cache)]
    public sealed class HudPanel : HudPanelBase
    {
        /// <summary>保存当前 HUD 实际订阅的事件总线实例，确保最终解绑时移除同一条监听。</summary>
        private IEventKit eventKit;

        /// <summary>组件绑定完成后订阅当前玩家生命值变化事件，使缓存中的 HUD 也能持续同步最新数值。</summary>
        protected override void OnBind()
        {
            eventKit = Core.Event ?? throw new System.InvalidOperationException($"{nameof(HudPanel)} requires EventKit before binding.");
            eventKit.AddListener<SelfHpChangedEvent>(Event.SelfHpChanged, OnSelfHpChanged);
            eventKit.AddListener<SelfCoreEnergyChangedEvent>(Event.SelfCoreEnergyChanged, OnSelfCoreEnergyChanged);
        }

        private void OnSelfCoreEnergyChanged(SelfCoreEnergyChangedEvent eventData)
        {
            EnergyImg.fillAmount = eventData.Max > 0f ? Mathf.Clamp01(eventData.Current / eventData.Max) : 0f;
        }


        /// <summary>
        /// 首次创建控制器时验证生成字段已经成功绑定。
        /// </summary>
        protected override void OnInitialize()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} initialized with {Binder.Count} generated component binding(s).", Root);
        }

        /// <summary>
        /// 每次 Hud 进入显示状态时输出测试日志，便于从 Console 验证启动链路。
        /// </summary>
        protected override void OnOpen()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} opened.", Root);
        }

        /// <summary>收到当前玩家受伤事件后同步血条填充长度与生命值文本。</summary>
        private void OnSelfHpChanged(SelfHpChangedEvent eventData)
        {
            HpBar.fillAmount = eventData.MaxHp > 0f ? Mathf.Clamp01(eventData.CurrentHp / eventData.MaxHp) : 0f;
            Hp.text = $"{eventData.CurrentHp:0.##} / {eventData.MaxHp:0.##}";
        }

        /// <summary>HUD 最终释放前移除全局生命值监听，避免事件总线继续持有失效控制器。</summary>
        protected override void OnUnbind()
        {
            if (eventKit == null) return;
            eventKit.RemoveListener<SelfHpChangedEvent>(Event.SelfHpChanged, OnSelfHpChanged);
            eventKit.RemoveListener<SelfCoreEnergyChangedEvent>(Event.SelfCoreEnergyChanged, OnSelfCoreEnergyChanged);

            eventKit = null;
        }

        /// <summary>
        /// 响应摇杆按钮点击；监听的注册和移除由 HudPanelBase 自动管理。
        /// </summary>
        protected override void OnStickButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} StickButton clicked.", Root);
        }

        /// <summary>
        /// 响应攻击按钮点击；监听的注册和移除由 HudPanelBase 自动管理。
        /// </summary>
        protected override void OnAtkButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} AtkButton clicked.", Root);
        }

        /// <summary>
        /// 响应闪避按钮点击；监听的注册和移除由 HudPanelBase 自动管理。
        /// </summary>
        protected override void OnDodgeButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} DodgeButton clicked.", Root);
        }

        protected override void OnMiniMapButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} MiniMapButton clicked.", Root);
        }

        protected override void OnQuestButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} QuestButton clicked.", Root);
        }

        protected override void OnMenuButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} MenuButton clicked.", Root);
        }

        protected override void OnSkillButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} SkillButton clicked.", Root);
        }

        protected override void OnUltButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} UltButton clicked.", Root);
        }

        protected override void OnWalkButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} WalkButton clicked.", Root);
        }

        protected override void OnRunButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} RunButton clicked.", Root);
        }

        protected override void OnJumpButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} JumpButton clicked.", Root);
        }

        protected override void OnEventButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} EventButton clicked.", Root);
        }

        protected override void OnLotteryButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} LotteryButton clicked.", Root);
        }

        protected override void OnGuideButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} GuideButton clicked.", Root);
        }

        protected override void OnCharacterButtonClick()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} CharacterButton clicked.", Root);
        }

        protected override void OnBagButtonClick()
        {
            throw new System.NotImplementedException();
        }
    }
}
