using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class PlayerEntity : Entity
    {
        public PlayerEntity(GameObject bindGo)
        {
            this.bindGo = bindGo;
            AddComp<InputComponent>();
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<MotionComponent>());
            AddComp(bindGo.GetComponent<AttackComponent>());

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
        }
    }
}