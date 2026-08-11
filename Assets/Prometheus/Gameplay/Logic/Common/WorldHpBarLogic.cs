using System;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>
    /// 为实体生成独立世界空间血条、建立属性事件绑定，并在实体释放时归还 UIKit 对象池。
    /// </summary>
    public sealed class WorldHpBarLogic : Logic
    {
        /// <summary>共享世界血条 Prefab 在 AssetKit 中的资源地址。</summary>
        private const string WorldHpBarAssetAddress = "Prefabs_WorldHpBar";

        /// <summary>当前实体持有的世界血条租约。</summary>
        private WorldUIHandle worldHpBarHandle;

        /// <summary>缓存当前血条表现组件，使最终释放能够显式解绑而临时隐藏仍保留目标属性。</summary>
        private HpBar hpBar;

        /// <summary>保存可选的小队成员状态，用于让后备成员的独立世界血条随角色一同隐藏。</summary>
        private TeamMemberComponent teamMemberComponent;

        /// <summary>
        /// 血条属于不受受击、眩晕或技能封锁影响的表现基础设施，并在其他 Gameplay Logic 之后完成绑定。
        /// </summary>
        public WorldHpBarLogic()
        {
            OrderTag = OrderTag.AfterGameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>
        /// 读取实体属性与 Prefab 锚点配置，从 UIKit 世界空间 Canvas 获取血条实例并显式绑定目标。
        /// </summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out PropertyComponent propertyComponent)) throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' requires PropertyComponent before world HP bar initialization.");
            if (Entity.bindGo == null) throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' requires a bound GameObject before world HP bar initialization.");
            WorldHpBarAnchor anchor = Entity.bindGo.GetComponent<WorldHpBarAnchor>();
            if (anchor == null) throw new InvalidOperationException($"Entity prefab '{Entity.bindGo.name}' requires WorldHpBarAnchor after its embedded HP bar is removed.");
            if (Core.UI == null) throw new InvalidOperationException("UIKit must be initialized before world HP bars are spawned.");
            worldHpBarHandle = Core.UI.SpawnWorldUI(WorldHpBarAssetAddress, anchor.FollowTarget, anchor.WorldOffset);
            hpBar = worldHpBarHandle.GetComponent<HpBar>();
            if (hpBar != null)
            {
                hpBar.Initialize(propertyComponent, anchor.HpColor, anchor.ChaserColor);
                if (Entity.TryGetComp(out teamMemberComponent))
                {
                    teamMemberComponent.OnFieldStateChanged += OnFieldStateChanged;
                    OnFieldStateChanged(teamMemberComponent.IsOnField);
                }
                return;
            }
            worldHpBarHandle.Release();
            worldHpBarHandle = null;
            throw new InvalidOperationException($"World HP bar asset '{WorldHpBarAssetAddress}' requires HpBar on its root object.");
        }

        /// <summary>世界血条在实体存活期间始终保持启用。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>世界血条只随实体最终释放，不参与普通玩法状态切换。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>启用阶段不重复生成或绑定血条。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>普通禁用阶段不提前回收血条。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>跟随、朝向和位置更新由 UIKit 统一处理，当前 Logic 不执行逐帧逻辑。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
        }

        /// <summary>实体最终释放时主动归还血条实例，并让旧租约立即失效。</summary>
        public override void OnDispose()
        {
            if (teamMemberComponent != null) teamMemberComponent.OnFieldStateChanged -= OnFieldStateChanged;
            teamMemberComponent = null;
            hpBar?.Uninitialize();
            hpBar = null;
            worldHpBarHandle?.Release();
            worldHpBarHandle = null;
        }

        /// <summary>同步切换当前世界血条根对象显隐，租约与数值监听在离场期间保持有效。</summary>
        private void OnFieldStateChanged(bool isOnField)
        {
            if (worldHpBarHandle == null || worldHpBarHandle.Root == null) return;
            if (isOnField) worldHpBarHandle.RefreshTransform();
            worldHpBarHandle.Root.SetActive(isOnField);
        }
    }
}
