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
            AddComp<EventComponent>();
            AddComp<DodgeComponent>();
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddComp(bindGo.GetComponent<SpineComponent>());
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
            AddLogic<AirMoveLogic>();
            AddLogic<JumpLogic>();
            AddLogic<RotateLogic>();
            AddLogic<LandLogic>();
            AddLogic<InputLogic>();
            AddLogic<DodgeLogic>();
            AddLogic<EffectLogic>();
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
        }
    }
}
