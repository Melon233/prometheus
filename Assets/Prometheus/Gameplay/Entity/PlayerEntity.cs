using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    public class PlayerEntity : Entity
    {
        /// <summary>
        /// 使用 GameplayKit 已实例化的玩家对象构建实体，避免 Entity 反向依赖资源系统。
        /// </summary>
        /// <param name="bindGameObject">包含玩家表现组件和玩法组件的场景对象。</param>
        public PlayerEntity(GameObject bindGameObject)
        {
            bindGo = bindGameObject != null ? bindGameObject : throw new ArgumentNullException(nameof(bindGameObject));
            AddComp<InputComponent>();
            AddComp<TeamMemberComponent>();
            AddComp<EventComponent>();
            AddComp<DodgeComponent>();
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<VfxComponent>());
            AddComp(bindGo.GetComponent<MotionComponent>());
            AddComp(bindGo.GetComponent<AttackComponent>());
            AddComp(bindGo.GetComponent<PropertyComponent>());
            AddComp(bindGo.GetComponent<SkillComponent>());
            AddComp(bindGo.GetComponent<SpecialAttackComponent>());
            AddComp(bindGo.GetComponent<UltimateComponent>());
            AddComp<CoreTalentComponent>();
            AddLogic<GroundMoveLogic>();
            AddLogic<IdleLogic>();
            AddLogic<MotionLogic>();
            AddLogic<TalentLogic>();
            AddLogic<SkillCooldownLogic>();
            AddLogic<UltimateCooldownLogic>();
            // 同帧输入按大招、技能、特殊攻击、普通攻击的注册顺序尝试，低优先级请求会被统一动画仲裁拒绝。
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
            AddLogic<WorldHpBarLogic>();
        }
    }
}
