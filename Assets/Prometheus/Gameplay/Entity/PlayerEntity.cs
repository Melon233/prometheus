using System;
using UnityEngine;
using Xuan.Prometheus.Actor;
using Xuan.Prometheus.Component;

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
            AddComp<EventComponent>();
            AddComp(bindGo.GetComponent<EffectComponent>());
            AddComp(bindGo.GetComponent<SpineComponent>());
            AddComp(bindGo.GetComponent<PropertyComponent>());
            AddComp(bindGo.GetComponent<ActorAuthoringComponent>());
            AddComp<PawnComponent>();
            AddLogic<PawnRegistrationLogic>();
            AddLogic<EffectLogic>();
            AddLogic(new ActorRuntimeLogic(ActorControlRole.Player));
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
        }
    }
}
