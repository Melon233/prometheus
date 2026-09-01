using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    public class PlayerEntity : Entity
    {
        /// <summary>
        /// 使用资源地址和出生参数构造完整玩家 Entity；GameObject 在首个 GameObjectLogic.AfterNew 中由 Entity 自己创建。
        /// </summary>
        public PlayerEntity(string location, Vector3 position, Quaternion rotation, Transform parent) : this(GameObjectSpawnSpec.Spawned<PlayerBinder>(location, position, rotation, parent))
        {
        }

        /// <summary>接管一个已经存在且根节点挂有 PlayerBinder 的场景对象，主要用于场景绑定与编辑器验证。</summary>
        public PlayerEntity(GameObject instance) : this(GameObjectSpawnSpec.SceneBound<PlayerBinder>(instance))
        {
        }

        /// <summary>在 Created 阶段一次性注册玩家全部纯 C# Component 和普通 Logic。</summary>
        private PlayerEntity(GameObjectSpawnSpec spawnSpec)
        {
            AddComp<GameObjectComponent>(new GameObjectComponent(spawnSpec));
            AddComp<InputComponent>();
            AddComp<TeamMemberComponent>();
            AddComp<EventComponent>();
            AddComp<DodgeComponent>();
            AddComp<EffectComponent>();
            AddComp<CharaLevelComponent>();
            AddComp<EquipmentComponent>();
            AddComp<WeaponComponent>();
            AddComp<SpineComponent>();
            AddComp<VfxComponent>();
            AddComp<MotionComponent>();
            AddComp<AttackComponent>();
            AddComp<PropertyComponent>();
            AddComp<SkillComponent>();
            AddComp<SpecialAttackComponent>();
            AddComp<UltimateComponent>();
            AddComp<CoreTalentComponent>();
            AddComp<InteractComponent>();
            AddLogic<GameObjectLogic>();
            AddLogic<GroundMoveLogic>();
            AddLogic<IdleLogic>();
            AddLogic<MotionLogic>();
            AddLogic<CharaLevelLogic>();
            AddLogic<EquipmentLogic>();
            AddLogic<WeaponLogic>();
            AddLogic<TalentLogic>();
            AddLogic<SkillCooldownLogic>();
            AddLogic<UltimateCooldownLogic>();
            AddLogic<UltimateLogic>();
            AddLogic<SkillLogic>();
            AddLogic<SpecialAttackLogic>();
            AddLogic<NormalAttackLogic>();
            AddLogic<GravityLogic>();
            AddLogic<AirMoveLogic>();
            AddLogic<JumpLogic>();
            AddLogic<RotateLogic>();
            AddLogic<LandLogic>();
            AddLogic<DodgeLogic>();
            AddLogic<EffectLogic>();
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
            AddLogic<InteractLogic>();
        }
    }
}
