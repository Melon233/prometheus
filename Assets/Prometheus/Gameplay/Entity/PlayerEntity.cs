using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    public class PlayerEntity : Entity
    {
        public PlayerEntity(GameObject bindGo)
        {
            this.bindGo = bindGo;
            AddComp<InputComponent>();
            AddComp<EventComponent>();
            AddComp<SpecialAttackComponent>();
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<MotionComponent>());
            AddComp(bindGo.GetComponent<AttackComponent>());
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddLogic<GroundMoveLogic>();
            AddLogic<MotionLogic>();
            AddLogic<TalentLogic>();
            AddLogic<AirMoveLogic>();
            AddLogic<JumpLogic>();
            AddLogic<RotateLogic>();
            AddLogic<LandLogic>();
            AddLogic<InputLogic>();
            AddLogic<DodgeLogic>();
            AddLogic<CooldownLogic>();
            AddLogic<EffectLogic>();
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
        }
    }
}