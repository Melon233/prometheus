using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    public class PlayerEntity : Entity
    {
        public PlayerEntity(GameObject bindGo)
        {
            if (bindGo == null) throw new ArgumentNullException(nameof(bindGo));
            this.bindGo = bindGo;
            bindGo.SetActive(true);
            AddComp<InputComponent>();
            AddComp<EventComponent>();
            AddComp<DodgeComponent>();
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<MotionComponent>());
            AddComp(bindGo.GetComponent<AttackComponent>());
            AddComp(bindGo.GetComponent<EffectComponent>());
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