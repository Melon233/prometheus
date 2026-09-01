using System;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Effects.Tests
{
    /// <summary>
    /// EffectRuntimeTestExtensions 保存 EditMode 测试专用的组件生命周期模拟和示例战斗辅助逻辑，正式运行时程序集不包含这些入口。
    /// </summary>
    internal static class EffectRuntimeTestExtensions
    {
        /// <summary>
        /// 在 EditMode 中显式初始化纯 C# PropertyComponent，模拟 GameObjectLogic 从 Binder 绑定配置的真实顺序。
        /// </summary>
        public static void InitializeForTests(this PropertyComponent property, PropertyConfig config)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (config == null) throw new ArgumentNullException(nameof(config));
            property.Initialize(config);
        }

        /// <summary>
        /// 为测试攻击者同时注册基础攻击与战意规则，并返回能够按相反顺序释放两份注册的测试句柄。
        /// </summary>
        public static IDisposable RegisterAllForTests(this EffectLibrary library, EffectRuntime runtime, Entity attacker)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            IDisposable attackRegistration = library.RegisterAttackTriggers(runtime, attacker);
            IDisposable combatFlowRegistration = library.RegisterCombatFlowTriggers(runtime, attacker);
            return new CompositeTestRegistration(attackRegistration, combatFlowRegistration);
        }

        /// <summary>
        /// 发布测试示例使用的火属性普通攻击信号；正式玩法必须由攻击结算逻辑根据真实命中结果发布信号。
        /// </summary>
        public static void PublishFireAttackForTests(this EffectLibrary library, EffectRuntime runtime, Entity attacker, Entity target, string abilityId = "Example.FireAttack")
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            float requestedDamage = 0f;
            if (attacker != null && attacker.TryGetComp(out PropertyComponent property)) requestedDamage = property.Atk;
            EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, attacker, target, attacker, requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack, abilityId, damageAttribute: DamageAttribute.Fire, damageActionType: DamageActionType.NormalAttack);
            runtime.Publish(signal);
        }

        /// <summary>
        /// CompositeTestRegistration 将测试安装的两组触发规则组合为一个可释放资源，避免 TearDown 遗漏任一注册。
        /// </summary>
        private sealed class CompositeTestRegistration : IDisposable
        {
            private IDisposable attackRegistration;
            private IDisposable combatFlowRegistration;

            /// <summary>
            /// 保存两份由 EffectRuntime 返回的独立注册句柄。
            /// </summary>
            public CompositeTestRegistration(IDisposable attack, IDisposable combatFlow)
            {
                attackRegistration = attack;
                combatFlowRegistration = combatFlow;
            }

            /// <summary>
            /// 按注册的相反顺序释放规则，并允许测试清理流程安全重复调用。
            /// </summary>
            public void Dispose()
            {
                combatFlowRegistration?.Dispose();
                combatFlowRegistration = null;
                attackRegistration?.Dispose();
                attackRegistration = null;
            }
        }
    }
}
