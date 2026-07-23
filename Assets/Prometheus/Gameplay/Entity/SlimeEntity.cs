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
            AddComp(bindGo.GetComponent<SlimeComponent>());
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddComp(bindGo.GetComponent<AttackComponent>());
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp<EventComponent>();
            AddLogic<EnmityLogic>();
            AddLogic<EffectLogic>();
            AddLogic<AttackedLogic>();
            AddLogic<PatrolLogic>();
            AddLogic<DieLogic>();
        }
    }
}