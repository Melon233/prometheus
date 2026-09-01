using System;
using System.Collections.Generic;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 保存单个 Entity 接入 EffectSystem 时产生的运行时状态。
    /// EffectLogic 只保留该 Component 引用，EffectSystem、Entity 所有者和 IDisposable 注册句柄全部集中在此处管理。
    /// </summary>
    public sealed class EffectComponent : Component
    {
        private EffectSystem effectSystem;
        private Entity owner;
        private IDisposable attackTriggerRegistration;
        private IDisposable combatFlowTriggerRegistration;

        /// <summary>保存活动 Buff 列表的变化版本，使 EntitySystem 能通过统一字段接口监听集合变化和持续时间推进。</summary>
        private readonly ModifiableProperty buffRevision = new ModifiableProperty();

        /// <summary>复用 EffectRuntime 的活动效果复制缓冲区，避免持续时间逐帧变化时产生临时数组。</summary>
        private readonly List<EffectInstance> activeEffectBuffer = new List<EffectInstance>();

        /// <summary>获取活动 Buff 列表的可监听版本字段；监听方收到脏回调后应重新读取当前列表快照。</summary>
        public ModifiableProperty BuffRevisionProperty => buffRevision;

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
        /// <param name="ownerSystem">从 Core.Gameplay 获取的单局 EffectSystem。</param>
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
            effectSystem.Runtime.ActiveEffectsChanged += OnActiveEffectsChanged;
            try
            {
                attackTriggerRegistration = effectSystem.DefaultLibrary.RegisterAttackTriggers(effectSystem.Runtime, owner);
            }
            catch
            {
                effectSystem.Runtime.ActiveEffectsChanged -= OnActiveEffectsChanged;
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
            if (activeSystem != null && !activeSystem.IsDisposed) activeSystem.Runtime.ActiveEffectsChanged -= OnActiveEffectsChanged;
            combatFlowTriggerRegistration?.Dispose();
            combatFlowTriggerRegistration = null;
            attackTriggerRegistration?.Dispose();
            attackTriggerRegistration = null;
            effectSystem = null;
            owner = null;

            if (activeSystem != null && !activeSystem.IsDisposed && activeOwner != null)
                activeSystem.Runtime.RemoveAll(activeOwner, EffectRemovalReason.OwnerDisposed);
        }

        /// <summary>把当前实体持有的活动持续型 Buff 复制到调用方缓冲区；即时 Effect、Debuff 和 Control Effect 不进入 HUD Buff 列表。</summary>
        public void CopyActiveBuffs(List<EffectInstance> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            activeEffectBuffer.Clear();
            if (effectSystem == null || owner == null) return;
            effectSystem.Runtime.CopyActiveEffects(owner, activeEffectBuffer);
            foreach (EffectInstance instance in activeEffectBuffer)
            {
                if (instance == null || !instance.IsActive || instance.Definition.DurationType == EffectDurationType.Instant || !instance.Definition.ShowInBuffList) continue;
                if ((instance.Definition.Tags & EffectTag.Buff) == 0) continue;
                buffer.Add(instance);
            }
        }

        /// <summary>把所属实体的 EffectRuntime 集合变化转换为 ModifiableProperty 脏通知，其他实体变化不会污染当前组件。</summary>
        private void OnActiveEffectsChanged(Entity changedOwner, EffectInstance changedInstance)
        {
            if (!ReferenceEquals(owner, changedOwner)) return;
            if (changedInstance == null || changedInstance.Definition.DurationType == EffectDurationType.Instant) return;
            if ((changedInstance.Definition.Tags & EffectTag.Buff) == 0) return;
            float nextRevision = buffRevision.Value >= 1000000f ? 0f : buffRevision.Value + 1f;
            buffRevision.SetValue(nextRevision);
        }
    }
}
