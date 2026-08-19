using System;
using System.Collections.Generic;
using SuperScrollView;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Hud 面板业务控制器，用于验证 UIKit 的类型扫描、Prefab 加载、组件绑定、打开生命周期和关闭缓存能力。
    /// </summary>
    [UIPanelConfig("HudPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Cache)]
    public sealed class HudPanel : HudPanelBase
    {
        /// <summary>定义小地图从中心向外保持完全不透明的归一化半径。</summary>
        private const float MinimapFadeStartDistance = 0.78f;

        /// <summary>定义小地图从中心向外变为完全透明的归一化半径。</summary>
        private const float MinimapFadeCompleteDistance = 1f;

        /// <summary>保存当前 HUD 实际订阅的事件总线实例，确保最终解绑时移除同一条监听。</summary>
        private IEventKit eventKit;

        /// <summary>保存当前上场成员的运行时 EntityId，切人时以该编号重新建立全部字段监听。</summary>
        private int observedEntityId;

        /// <summary>保存当前单局的实体与强类型字段监听系统。</summary>
        private EntitySystem entitySystem;

        /// <summary>保存生命、上限、核心能量、技能冷却、大招状态和 Buff 列表对应的可释放监听。</summary>
        private readonly ListenHandle[] listenHandles = new ListenHandle[9];

        /// <summary>复用当前上场成员的持续型 Buff 快照，避免列表逐帧刷新时产生临时集合。</summary>
        private readonly List<EffectInstance> observedBuffs = new List<EffectInstance>();

        /// <summary>标记当前缓存面板是否正在显示，关闭期间只记录目标编号而不持有字段监听。</summary>
        private bool isObserving;

        /// <summary>保存当前 HUD 绑定到的集中式输入系统，快捷键由 InputAction 仲裁，战斗按钮点击则提交定向实体命令。</summary>
        private InputSystem inputSystem;

        /// <summary>保存当前单局的小队系统，供三个头像按钮直接切换固定槽位。</summary>
        private TeamSystem teamSystem;

        /// <summary>保存独立 HUD 命令系统，普通点击只负责提交命令而不监听任何快捷键。</summary>
        private HudCommandSystem hudCommandSystem;

        /// <summary>保存当前单局的小地图系统，地图采样与玩家映射不由 UI 自行计算。</summary>
        private MinimapSystem minimapSystem;

        /// <summary>保存运行时创建在 MiniMapButton 内的地图 RawImage。</summary>
        private RawImage minimapImage;

        /// <summary>保存当前 HUD 独占的 MaskUIShader 材质，避免 uvRect 参数影响其他 UI。</summary>
        private Material minimapMaskMaterial;

        /// <summary>组件绑定完成后只订阅小队成员切换事实；具体数值统一通过 EntitySystem 观察。</summary>
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
            CreateMinimapView();
            Debug.Log($"[UIKit] {nameof(HudPanel)} initialized with {Binder.Count} generated component binding(s).", Root);
        }

        /// <summary>每次 HUD 进入显示状态时解析当前上场成员并建立立即回调的字段监听。</summary>
        protected override void OnOpen()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} opened.", Root);
            IGameplayKit gameplayKit = Core.Gameplay ?? throw new InvalidOperationException($"{nameof(HudPanel)} requires GameplayKit before opening.");
            if (!gameplayKit.TryGetSystem(out entitySystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(EntitySystem)}.");
            if (!gameplayKit.TryGetSystem(out teamSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(TeamSystem)}.");
            if (!gameplayKit.TryGetSystem(out inputSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(InputSystem)}.");
            if (!gameplayKit.TryGetSystem(out hudCommandSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(HudCommandSystem)}.");
            if (!gameplayKit.TryGetSystem(out minimapSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(MinimapSystem)}.");
            minimapSystem.BindView(minimapImage, minimapMaskMaterial);
            isObserving = true;
            BindObservedEntity(teamSystem.ActiveEntityId);
        }

        /// <summary>面板进入缓存关闭状态时释放字段监听，重新打开时会从当前值立即恢复。</summary>
        protected override void OnClose()
        {
            if (minimapSystem != null) minimapSystem.UnbindView(minimapImage);
            isObserving = false;
            ReleaseValueListeners();
        }

        /// <summary>在现有 MiniMapButton 内创建静态地图图层和由代码参数控制的径向虚化材质。</summary>
        private void CreateMinimapView()
        {
            Shader maskShader = Resources.Load<Shader>("MaskUIShader");
            if (maskShader == null) throw new InvalidOperationException($"{nameof(HudPanel)} requires Resources shader 'MaskUIShader'.");
            minimapMaskMaterial = new Material(maskShader) { name = "Hud Minimap Alpha Mask" };
            ConfigureMinimapFade(minimapMaskMaterial, MinimapFadeStartDistance, MinimapFadeCompleteDistance);
            GameObject mapObject = new GameObject("Map Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            mapObject.layer = MiniMapButton.gameObject.layer;
            RectTransform mapRect = mapObject.GetComponent<RectTransform>();
            mapRect.SetParent(MiniMapButton.transform, false);
            // 把运行时地图固定在容器最底层，使 Prefab 中手工配置的玩家标记和其他装饰稳定显示在地图之上。
            mapRect.SetAsFirstSibling();
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = new Vector2(14f, 14f);
            mapRect.offsetMax = new Vector2(-14f, -14f);
            minimapImage = mapObject.GetComponent<RawImage>();
            minimapImage.color = Color.white;
            minimapImage.material = minimapMaskMaterial;
            minimapImage.raycastTarget = false;
        }

        /// <summary>把径向虚化区间写入当前小地图独占材质，区间内由 Shader 使用五次 smootherstep 平滑过渡。</summary>
        /// <param name="material">使用 MaskUIShader 的小地图材质。</param>
        /// <param name="fadeStartDistance">开始降低 alpha 的归一化半径。</param>
        /// <param name="fadeCompleteDistance">alpha 降为零的归一化半径。</param>
        private static void ConfigureMinimapFade(Material material, float fadeStartDistance, float fadeCompleteDistance)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (fadeStartDistance < 0f) throw new ArgumentOutOfRangeException(nameof(fadeStartDistance), fadeStartDistance, "Minimap fade start distance cannot be negative.");
            if (fadeCompleteDistance <= fadeStartDistance) throw new ArgumentOutOfRangeException(nameof(fadeCompleteDistance), fadeCompleteDistance, "Minimap fade complete distance must be greater than fade start distance.");
            material.SetFloat("_FadeStartDistance", fadeStartDistance);
            material.SetFloat("_FadeCompleteDistance", fadeCompleteDistance);
        }

        /// <summary>释放旧成员监听后，为新成员的生命、核心能量、技能冷却、大招状态和 Buff 列表建立独立监听。</summary>
        private void BindObservedEntity(int entityId)
        {
            ReleaseValueListeners();
            observedEntityId = entityId;
            if (entitySystem == null || entityId <= 0) return;
            listenHandles[0] = entitySystem.Listen<PropertyComponent>(entityId, component => component.HpProperty, ApplyHealthState);
            listenHandles[1] = entitySystem.Listen<PropertyComponent>(entityId, component => component.MaxHpProperty, ApplyHealthState);
            listenHandles[2] = entitySystem.Listen<PropertyComponent>(entityId, component => component.CoreEnergyProperty, ApplyCoreEnergyState);
            listenHandles[3] = entitySystem.Listen<PropertyComponent>(entityId, component => component.CoreEnergyLimitProperty, ApplyCoreEnergyState);
            listenHandles[4] = entitySystem.Listen<PropertyComponent>(entityId, component => component.UltEnergyProperty, _ => ApplyUltimateState(entityId));
            listenHandles[5] = entitySystem.Listen<PropertyComponent>(entityId, component => component.UltEnergyLimitProperty, _ => ApplyUltimateState(entityId));
            listenHandles[6] = entitySystem.Listen<UltimateComponent>(entityId, component => component.CooldownRemainingProperty, _ => ApplyUltimateState(entityId));
            listenHandles[7] = entitySystem.Listen<SkillComponent>(entityId, component => component.CooldownRemainingProperty, ApplySkillState);
            listenHandles[8] = entitySystem.Listen<EffectComponent>(entityId, component => component.BuffRevisionProperty, ApplyBuffState);
        }

        /// <summary>读取同一 Entity 的大招能量和冷却组件，以一次 UI 写入保持两类进度显示一致。</summary>
        private void ApplyUltimateState(int entityId)
        {
            if (entityId != observedEntityId || entitySystem == null || !entitySystem.TryGetEntity(entityId, out Logic.Entity entity)) return;
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
            if (minimapSystem != null) minimapSystem.UnbindView(minimapImage);
            if (eventKit != null) eventKit.RemoveListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
            DestroyUiResource(minimapMaskMaterial);
            eventKit = null;
            inputSystem = null;
            teamSystem = null;
            hudCommandSystem = null;
            minimapSystem = null;
            entitySystem = null;
            minimapImage = null;
            minimapMaskMaterial = null;
            observedEntityId = 0;
            isObserving = false;
        }

        /// <summary>按照当前 Unity 运行环境释放 HUD 独占创建的材质或纹理资源。</summary>
        /// <param name="resource">需要释放的 Unity 对象。</param>
        private static void DestroyUiResource(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }

        /// <summary>把战斗按钮的一次点击定向提交给当前上场实体，并由输入阶段在实体更新前写入按钮命令。</summary>
        private void QueueCurrentEntityButtonAction(InputActionMask action)
        {
            inputSystem.QueueEntityButtonActions(observedEntityId, action);
        }

        /// <summary>点击抽奖按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnLotteryButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenLottery);
        }

        /// <summary>点击大招按钮时提交一次终结技玩法命令。</summary>
        protected override void OnUltButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Ultimate);
        }

        /// <summary>点击小地图按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnMiniMapButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenMiniMap);
        }

        /// <summary>点击任务按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnQuestButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenQuest);
        }

        /// <summary>点击菜单按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnMenuButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenMenu);
        }

        /// <summary>点击跳跃按钮时提交一次跳跃玩法命令。</summary>
        protected override void OnJumpButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Jump);
        }

        /// <summary>点击攻击按钮时提交一次普通攻击玩法命令。</summary>
        protected override void OnAtkButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Attack);
        }

        /// <summary>点击闪避按钮时提交一次闪避玩法命令。</summary>
        protected override void OnDodgeButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Dodge);
        }

        /// <summary>点击技能按钮时提交一次技能玩法命令。</summary>
        protected override void OnSkillButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Skill);
        }

        /// <summary>点击引导按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnGuideButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenGuide);
        }

        /// <summary>点击活动按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnEventButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenEvent);
        }

        /// <summary>点击角色按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnCharacterButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenCharacter);
        }

        /// <summary>点击背包按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnBagButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenBag);
        }

        /// <summary>点击第一个头像时直接切换到第一个固定小队槽位。</summary>
        protected override void OnAvatar1Click()
        {
            teamSystem.SwitchToSlot(0);
        }

        /// <summary>点击第二个头像时直接切换到第二个固定小队槽位。</summary>
        protected override void OnAvatar2Click()
        {
            teamSystem.SwitchToSlot(1);
        }

        /// <summary>点击第三个头像时直接切换到第三个固定小队槽位。</summary>
        protected override void OnAvatar3Click()
        {
            teamSystem.SwitchToSlot(2);
        }

    }
}
