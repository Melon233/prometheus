using System;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Logic
{
    /// <summary>
    /// EffectLogic 负责把 Entity 接入其所属 GameplayKit 的单局 EffectSystem。
    /// 本 Logic 只保存继承 IComponent 的 EffectComponent；其他运行时状态全部由 EffectComponent 持有。
    /// </summary>
    public sealed class EffectLogic : Logic
    {
        private EffectComponent effectComponent;

        /// <summary>
        /// 将 EffectLogic 固定在 Buff 阶段，使其他 Gameplay Logic 初始化前已经可以使用 EffectComponent.Runtime。
        /// </summary>
        public EffectLogic()
        {
            OrderTag = OrderTag.Buff;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <summary>
        /// 通过 Entity 持有的 IGameplayKit 获取本局 EffectSystem，并把非 Component 状态交给 EffectComponent。
        /// </summary>
        public override void AfterNew()
        {
            if (!Entity.TryGetComp(out effectComponent))
                throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' requires an EffectComponent before EffectLogic initialization.");

            EffectSystem effectSystem = Entity.GameplayKit.GetSystem<EffectSystem>();
            effectComponent.Initialize(effectSystem, Entity);
        }

        /// <summary>
        /// Effect 接入逻辑在 Entity 存活期间始终允许启用。
        /// </summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>
        /// Effect 接入逻辑只在 Entity 销毁时释放，不参与普通玩法状态切换。
        /// </summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>
        /// 触发规则已经在 AfterNew 中安装，启用时不需要重复注册。
        /// </summary>
        public override void OnEnable()
        {
        }

        /// <summary>
        /// 普通禁用阶段不释放规则，避免临时逻辑阻塞导致持续效果意外丢失。
        /// </summary>
        public override void OnDisable()
        {
        }

        /// <summary>
        /// EffectRuntime 由单局 EffectSystem 统一推进，Entity Logic 不再执行私有 Tick。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
        }

        /// <summary>
        /// Entity 销毁时通过 EffectComponent 完成触发注销、持续效果移除和属性句柄回滚。
        /// </summary>
        public override void OnDispose()
        {
            effectComponent?.DisposeBindings();
            effectComponent = null;
        }
    }
}
