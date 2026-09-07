using System;
using System.Collections.Generic;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Logic
{
    /// <summary>封装一个 Logic 独占的运行时永久 Effect，并保证替换与释放都先移除旧实例再销毁临时定义。</summary>
    internal sealed class RuntimePermanentEffectProjection : IDisposable
    {
        /// <summary>保存当前单局 EffectRuntime。</summary>
        private readonly EffectRuntime runtime;
        /// <summary>保存永久 Effect 的拥有者、施放者和实际来源。</summary>
        private readonly Entity owner;
        /// <summary>保存包含 EntityId 的稳定效果编号。</summary>
        private readonly string effectId;
        /// <summary>保存当前正在被 EffectInstance 引用的运行时定义。</summary>
        private EffectDefinition definition;

        /// <summary>创建一个绑定指定 Entity 与业务通道的永久 Effect 投影。</summary>
        public RuntimePermanentEffectProjection(EffectRuntime effectRuntime, Entity effectOwner, string channel)
        {
            runtime = effectRuntime ?? throw new ArgumentNullException(nameof(effectRuntime));
            owner = effectOwner ?? throw new ArgumentNullException(nameof(effectOwner));
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("Growth effect channel cannot be empty.", nameof(channel));
            effectId = $"Growth.{owner.EntityId}.{channel.Trim()}";
        }

        /// <summary>获取当前投影使用的稳定 EffectId，供测试、诊断和 UI 查询。</summary>
        public string EffectId => effectId;

        /// <summary>用最新操作集合原子替换旧永久 Effect；空操作集合仍保留一个可观测的永久 Effect 实例。</summary>
        public void Replace(IReadOnlyList<EffectOperation> operations)
        {
            RemoveCurrent();
            definition = EffectDefinition.CreateRuntime(effectId, EffectTag.Attribute | EffectTag.Growth, EffectDurationType.Permanent, operations);
            try
            {
                runtime.ApplyEffect(definition, owner, owner, owner);
                if (runtime.GetActiveEffect(owner, effectId) == null) throw new InvalidOperationException($"Growth effect '{effectId}' was not applied to entity {owner.EntityId}.");
            }
            catch
            {
                EffectDefinition.ReleaseRuntime(definition);
                definition = null;
                throw;
            }
        }

        /// <summary>移除当前永久 Effect 并释放运行时定义，重复调用保持幂等。</summary>
        public void Dispose()
        {
            RemoveCurrent();
        }

        /// <summary>精确移除当前通道的活动实例，使实例资源先回滚全部 Modifier，再释放定义。</summary>
        private void RemoveCurrent()
        {
            if (definition == null) return;
            EffectInstance instance = runtime.GetActiveEffect(owner, effectId);
            if (instance != null) runtime.RemoveEffect(instance, EffectRemovalReason.Replaced);
            EffectDefinition.ReleaseRuntime(definition);
            definition = null;
        }
    }
}
