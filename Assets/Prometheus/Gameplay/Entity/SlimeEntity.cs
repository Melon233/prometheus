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
            AddLogic<EnmityLogic>();
        }
    }
}