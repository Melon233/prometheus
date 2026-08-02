using System;
using UnityEngine;
using Xuan.Prometheus.Ai;
using Xuan.Prometheus.Actor;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class SlimeEntity : Entity
    {
        /// <summary>
        /// 使用 GameplayKit 已实例化的敌人对象构建实体，实体本身不关心资源地址或加载方式。
        /// </summary>
        /// <param name="bindGameObject">包含敌人表现组件和玩法组件的场景对象。</param>
        public SlimeEntity(GameObject bindGameObject)
        {
            bindGo = bindGameObject != null ? bindGameObject : throw new ArgumentNullException(nameof(bindGameObject));
            AddComp(bindGo.GetComponent<PropertyComponent>());
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<EnemyAiComponent>());
            AddComp<EventComponent>();
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddComp(bindGo.GetComponent<ActorAuthoringComponent>());
            AddComp<PawnComponent>();
            AddLogic<PawnRegistrationLogic>();
            AddLogic<EffectLogic>();
            AddLogic(new ActorRuntimeLogic(ActorControlRole.EnemyAi));
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
        }
    }
}
