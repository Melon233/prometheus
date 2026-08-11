using System;
using System.Collections.Generic;
using SuperScrollView;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

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

        /// <summary>保存当前上场成员的运行时 EntityId，切人时以该编号重新建立全部字段监听。</summary>
        private int observedEntityId;

        /// <summary>保存当前单局的强类型字段监听系统。</summary>
        private ListenSystem listenSystem;

        /// <summary>保存生命、上限、核心能量、技能冷却、大招状态和 Buff 列表对应的可释放监听。</summary>
        private readonly ListenHandle[] listenHandles = new ListenHandle[9];

        /// <summary>复用当前上场成员的持续型 Buff 快照，避免列表逐帧刷新时产生临时集合。</summary>
        private readonly List<EffectInstance> observedBuffs = new List<EffectInstance>();

        /// <summary>标记当前缓存面板是否正在显示，关闭期间只记录目标编号而不持有字段监听。</summary>
        private bool isObserving;

        /// <summary>组件绑定完成后只订阅小队成员切换事实；具体数值统一通过 ListenSystem 观察。</summary>
        protected override void OnBind()
        {
            eventKit = Core.Event ?? throw new System.InvalidOperationException($"{nameof(HudPanel)} requires EventKit before binding.");
            eventKit.AddListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
        }

        /// <summary>收到上场成员变化后保存新编号，并在面板显示期间原子替换全部字段监听。</summary>
        private void OnActiveTeamMemberChanged(ActiveTeamMemberChangedEvent eventData)
        {
            int entityId = eventData == null ? 0 : eventData.CurrentEntityId;
            observedEntityId = entityId;
            if (isObserving) BindObservedEntity(entityId);
        }

        /// <summary>从属性组件当前值刷新生命条和生命文本；监听注册时会立即执行一次。</summary>
        private void ApplyHealthState(PropertyComponent propertyComponent)
        {
            HpBar.fillAmount = propertyComponent.MaxHp > 0f ? Mathf.Clamp01(propertyComponent.Hp / propertyComponent.MaxHp) : 0f;
            Hp.text = $"{propertyComponent.Hp:0.##} / {propertyComponent.MaxHp:0.##}";
        }

        /// <summary>从属性组件当前值刷新核心能量条；当前值或上限变化都会走同一入口。</summary>
        private void ApplyCoreEnergyState(PropertyComponent propertyComponent)
        {
            EnergyImg.fillAmount = propertyComponent.CoreEnergyLimit > 0f ? Mathf.Clamp01(propertyComponent.CoreEnergy / propertyComponent.CoreEnergyLimit) : 0f;
        }

        /// <summary>
        /// 首次创建控制器时验证生成字段已经成功绑定。
        /// </summary>
        protected override void OnInitialize()
        {
            BuffList.InitListView(0, OnGetBuffItemByIndex);
            Debug.Log($"[UIKit] {nameof(HudPanel)} initialized with {Binder.Count} generated component binding(s).", Root);
        }

        /// <summary>每次 HUD 进入显示状态时解析当前上场成员并建立立即回调的字段监听。</summary>
        protected override void OnOpen()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} opened.", Root);
            IGameplayKit gameplayKit = Core.Gameplay ?? throw new InvalidOperationException($"{nameof(HudPanel)} requires GameplayKit before opening.");
            if (!gameplayKit.TryGetSystem(out listenSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(ListenSystem)}.");
            if (!gameplayKit.TryGetSystem(out TeamSystem teamSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(TeamSystem)}.");
            isObserving = true;
            BindObservedEntity(teamSystem.ActiveEntityId);
        }

        /// <summary>面板进入缓存关闭状态时释放字段监听，重新打开时会从当前值立即恢复。</summary>
        protected override void OnClose()
        {
            isObserving = false;
            ReleaseValueListeners();
        }

        /// <summary>释放旧成员监听后，为新成员的生命、核心能量、技能冷却、大招状态和 Buff 列表建立独立监听。</summary>
        private void BindObservedEntity(int entityId)
        {
            ReleaseValueListeners();
            observedEntityId = entityId;
            if (listenSystem == null || entityId <= 0) return;
            listenHandles[0] = listenSystem.Listen<PropertyComponent>(entityId, component => component.HpProperty, ApplyHealthState);
            listenHandles[1] = listenSystem.Listen<PropertyComponent>(entityId, component => component.MaxHpProperty, ApplyHealthState);
            listenHandles[2] = listenSystem.Listen<PropertyComponent>(entityId, component => component.CoreEnergyProperty, ApplyCoreEnergyState);
            listenHandles[3] = listenSystem.Listen<PropertyComponent>(entityId, component => component.CoreEnergyLimitProperty, ApplyCoreEnergyState);
            listenHandles[4] = listenSystem.Listen<PropertyComponent>(entityId, component => component.UltEnergyProperty, _ => ApplyUltimateState(entityId));
            listenHandles[5] = listenSystem.Listen<PropertyComponent>(entityId, component => component.UltEnergyLimitProperty, _ => ApplyUltimateState(entityId));
            listenHandles[6] = listenSystem.Listen<UltimateComponent>(entityId, component => component.CooldownRemainingProperty, _ => ApplyUltimateState(entityId));
            listenHandles[7] = listenSystem.Listen<SkillComponent>(entityId, component => component.CooldownRemainingProperty, ApplySkillState);
            listenHandles[8] = listenSystem.Listen<EffectComponent>(entityId, component => component.BuffRevisionProperty, ApplyBuffState);
        }

        /// <summary>读取同一 Entity 的大招能量和冷却组件，以一次 UI 写入保持两类进度显示一致。</summary>
        private void ApplyUltimateState(int entityId)
        {
            if (entityId != observedEntityId || Core.Gameplay == null || !Core.Gameplay.TryGetEntity(entityId, out Logic.Entity entity)) return;
            if (!entity.TryGetComp(out PropertyComponent propertyComponent) || !entity.TryGetComp(out UltimateComponent ultimateComponent)) return;
            Ult.ApplyState(propertyComponent.UltEnergy, propertyComponent.UltEnergyLimit, ultimateComponent.CooldownRemaining, ultimateComponent.CooldownDuration);
        }

        /// <summary>从当前上场成员的技能组件读取完整冷却与剩余冷却，并同步技能区遮罩和一位小数文本。</summary>
        private void ApplySkillState(SkillComponent skillComponent)
        {
            Skill.ApplyState(skillComponent.CooldownRemaining, skillComponent.CooldownDuration);
        }

        /// <summary>从当前上场成员的 EffectComponent 复制持续型 Buff 快照，并刷新可见的循环列表项。</summary>
        private void ApplyBuffState(EffectComponent effectComponent)
        {
            effectComponent.CopyActiveBuffs(observedBuffs);
            BuffList.SetListItemCount(observedBuffs.Count, false);
            BuffList.RefreshAllShownItem();
        }

        /// <summary>按索引从复用快照创建或复用 Buff 列表项，并把 EffectInstance 的只读状态写入 BuffMono。</summary>
        private LoopListViewItem2 OnGetBuffItemByIndex(LoopListView2 listView, int index)
        {
            if (index < 0 || index >= observedBuffs.Count) return null;
            LoopListViewItem2 item = listView.NewListViewItem("Buff");
            if (item == null) throw new InvalidOperationException("HudPanel BuffList requires an item prefab named 'Buff'.");
            BuffMono buffMono = item.GetComponent<BuffMono>();
            if (buffMono == null) throw new InvalidOperationException("HudPanel BuffList item prefab requires BuffMono.");
            buffMono.Apply(observedBuffs[index]);
            return item;
        }

        /// <summary>幂等释放当前成员的全部字段监听，防止缓存面板和旧角色继续互相持有。</summary>
        private void ReleaseValueListeners()
        {
            for (int index = 0; index < listenHandles.Length; index++)
            {
                listenHandles[index]?.Dispose();
                listenHandles[index] = null;
            }
            observedBuffs.Clear();
            if (BuffList != null) BuffList.SetListItemCount(0, false);
        }

        /// <summary>HUD 最终释放前移除小队监听和全部字段监听，避免事件总线或属性继续持有失效控制器。</summary>
        protected override void OnUnbind()
        {
            ReleaseValueListeners();
            if (eventKit != null) eventKit.RemoveListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
            eventKit = null;
            listenSystem = null;
            observedEntityId = 0;
            isObserving = false;
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
