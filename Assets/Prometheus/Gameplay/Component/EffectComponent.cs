using System;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 保存单个 Entity 接入 EffectSystem 时产生的运行时状态。
    /// EffectLogic 只保留该 Component 引用，EffectSystem、Entity 所有者和 IDisposable 注册句柄全部集中在此处管理。
    /// </summary>
    public sealed class EffectComponent : MonoComponent
    {
        private EffectSystem effectSystem;
        private Entity owner;
        private IDisposable attackTriggerRegistration;
        private IDisposable combatFlowTriggerRegistration;

        /// <summary>
        /// 获取当前 Entity 所属单局的 EffectRuntime；组件尚未初始化时抛出明确异常。
        /// </summary>
        public EffectRuntime Runtime
        {
            get
            {
                if (effectSystem == null)
                    throw new InvalidOperationException("EffectComponent has not been initialized by EffectLogic.");

                return effectSystem.Runtime;
            }
        }

        /// <summary>
        /// 将当前 Entity 接入指定单局 EffectSystem，并注册基础攻击触发规则。
        /// </summary>
        /// <param name="ownerSystem">从 Entity.GameplayKit 获取的单局 EffectSystem。</param>
        /// <param name="ownerEntity">当前组件所属的 Entity。</param>
        public void Initialize(EffectSystem ownerSystem, Entity ownerEntity)
        {
            if (ownerSystem == null)
                throw new ArgumentNullException(nameof(ownerSystem));

            if (ownerEntity == null)
                throw new ArgumentNullException(nameof(ownerEntity));

            if (effectSystem != null)
            {
                if (ReferenceEquals(effectSystem, ownerSystem) && ReferenceEquals(owner, ownerEntity))
                    return;

                throw new InvalidOperationException("EffectComponent is already bound to another EffectSystem or Entity.");
            }

            effectSystem = ownerSystem;
            owner = ownerEntity;
            try
            {
                attackTriggerRegistration = effectSystem.DefaultLibrary.RegisterAttackTriggers(effectSystem.Runtime, owner);
            }
            catch
            {
                effectSystem = null;
                owner = null;
                attackTriggerRegistration = null;
                throw;
            }
        }

        /// <summary>
        /// 为当前 Entity 注册由实际攻击伤害驱动的战意规则；重复调用不会重复安装规则。
        /// </summary>
        /// <param name="ownerEntity">请求注册战意规则的 Entity，必须与初始化所有者一致。</param>
        public void RegisterCombatFlowTriggers(Entity ownerEntity)
        {
            if (effectSystem == null || owner == null)
                throw new InvalidOperationException("EffectComponent must be initialized before registering combat-flow triggers.");

            if (!ReferenceEquals(owner, ownerEntity))
                throw new InvalidOperationException("EffectComponent cannot register triggers for another Entity.");

            if (combatFlowTriggerRegistration != null)
                return;

            combatFlowTriggerRegistration = effectSystem.DefaultLibrary.RegisterCombatFlowTriggers(effectSystem.Runtime, owner);
        }

        /// <summary>
        /// 注销当前 Entity 的全部触发规则，并移除其持有的持续效果和属性句柄。
        /// </summary>
        public void DisposeBindings()
        {
            EffectSystem activeSystem = effectSystem;
            Entity activeOwner = owner;
            combatFlowTriggerRegistration?.Dispose();
            combatFlowTriggerRegistration = null;
            attackTriggerRegistration?.Dispose();
            attackTriggerRegistration = null;
            effectSystem = null;
            owner = null;

            if (activeSystem != null && !activeSystem.IsDisposed && activeOwner != null)
                activeSystem.Runtime.RemoveAll(activeOwner, EffectRemovalReason.OwnerDisposed);
        }
    }
}
