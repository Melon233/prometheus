using UnityEngine;
using Xuan.Prometheus.Ai;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class SlimeEntity : Entity
    {
        /// <summary>
        /// 使用资源地址和出生参数构造完整史莱姆 Entity；GameObject 在首个 GameObjectLogic.AfterNew 中由 Entity 自己创建。
        /// </summary>
        public SlimeEntity(string location, Vector3 position, Quaternion rotation, Transform parent) : this(GameObjectSpawnSpec.Spawned<SlimeBinder>(location, position, rotation, parent))
        {
        }

        /// <summary>接管一个已经存在且根节点挂有 SlimeBinder 的场景对象，主要用于场景绑定与编辑器验证。</summary>
        public SlimeEntity(GameObject instance) : this(GameObjectSpawnSpec.SceneBound<SlimeBinder>(instance))
        {
        }

        /// <summary>在 Created 阶段一次性注册史莱姆全部纯 C# Component 和普通 Logic。</summary>
        private SlimeEntity(GameObjectSpawnSpec spawnSpec)
        {
            AddComp<GameObjectComponent>(new GameObjectComponent(spawnSpec));
            AddComp<PropertyComponent>();
            AddComp<AttackComponent>();
            AddComp<SpineComponent>();
            AddComp<VfxComponent>();
            AddComp<MotionComponent>();
            AddComp<EnemyAiComponent>();
            AddComp<EventComponent>();
            AddComp<EffectComponent>();
            AddLogic<GameObjectLogic>();
            AddLogic<EnemyAiLogic>();
            AddLogic<GravityLogic>();
            AddLogic<MotionLogic>();
            AddLogic<EffectLogic>();
            AddLogic<AttackedLogic>();
            AddLogic<DieLogic>();
            AddLogic<WorldHpBarLogic>();
        }
    }
}
