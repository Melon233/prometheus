using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class SlimeEntity : Entity
    {
        public SlimeEntity(GameObject bindGo)
        {
            this.bindGo = bindGo;
            AddComp(bindGo.GetComponent<PropertyComponent>());
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddComp(bindGo.GetComponent<AttackComponent>());
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<PatrolComponent>());
            AddComp(bindGo.GetComponent<EnmityComponent>());
            AddComp(bindGo.GetComponent<EAttackComponent>());
            AddComp(bindGo.GetComponent<EIdleComponent>());
            AddComp<EventComponent>();
            AddLogic<PatrolLogic>();
            AddLogic<EnmityLogic>();
            AddLogic<EIdleLogic>();
            AddLogic<EAttackLogic>();
            AddLogic<EffectLogic>();
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
        }
    }
}