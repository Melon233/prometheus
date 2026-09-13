using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Shields;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Effects
{
    /// <summary>指定护盾的元素亲和从哪里读取。</summary>
    public enum ShieldElementSource
    {
        /// <summary>使用本操作配置的固定元素。</summary>
        Fixed = 0,
        /// <summary>
        /// 继承驱动信号已经解析的元素。
        /// 结晶护盾用它：四种结晶共用一份资产，元素由产生它的那次反应决定。
        /// </summary>
        InheritSignal = 1
    }

    /// <summary>
    /// ShieldOperation 为持续 Effect 登记一层护盾，并由实例资源生命周期自动撤下。
    ///
    /// 护盾的**存活时间**由 Effect 定义的 duration 决定，**吸收量**由数值公式决定，
    /// **吸收规则**由护盾系统决定。本操作只负责把三者接起来，自己不算任何数值。
    /// </summary>
    [Serializable]
    public sealed class ShieldOperation : EffectOperation
    {
        /// <summary>配置护盾的名义吸收量；结晶护盾用 `SignalValue` 读取反应算好的值。</summary>
        [SerializeField] private EffectValueFormula amount = new EffectValueFormula();
        /// <summary>配置护盾强效的读取方式：为空表示不额外乘算，由数值公式自行包含。</summary>
        [SerializeField] private bool applyShieldStrength = true;
        /// <summary>配置元素亲和的来源。</summary>
        [SerializeField] private ShieldElementSource elementSource = ShieldElementSource.Fixed;
        /// <summary>仅在 Fixed 来源下使用的固定元素。</summary>
        [SerializeField] private Cfg.ElementType fixedElement = Cfg.ElementType.None;
        /// <summary>
        /// 配置互斥组标识；同组护盾同时只能存在一层。
        /// 结晶护盾填 `Crystallize`，从而表达 04 第 5.6 节的「同时只能存在一个」。
        /// </summary>
        [SerializeField] private string groupKey = string.Empty;

        /// <summary>创建默认护盾操作，供 Unity 序列化器使用。</summary>
        public ShieldOperation()
        {
        }

        /// <summary>创建一个按指定公式与元素来源施加护盾的操作。</summary>
        public ShieldOperation(EffectValueFormula shieldAmount, ShieldElementSource source, Cfg.ElementType element, string group, bool useShieldStrength = true)
        {
            amount = shieldAmount ?? EffectValueFormula.Constant(0f);
            elementSource = source;
            fixedElement = element;
            groupKey = group ?? string.Empty;
            applyShieldStrength = useShieldStrength;
        }

        /// <summary>
        /// 为持续效果目标登记一层护盾；即时效果不施加护盾，因为没有实例来承载它的撤下。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Instance == null || context.Target == null) return;

            float shieldAmount = Mathf.Max(0f, amount.Evaluate(context));
            if (applyShieldStrength)
            {
                // 护盾强效读**施加者**的面板：护盾量在释放瞬间定死，之后不随被护对象的属性变化。
                float shieldStrength = context.Caster != null && context.Caster.TryGetComp(out PropertyComponent casterProperty) ? casterProperty.GetValue(PropertyType.ShieldStrength) : 0f;
                shieldAmount = Xuan.Prometheus.Combat.DamageCalculator.EvaluateShield(new Xuan.Prometheus.Combat.ShieldContext
                {
                    Multiplier = 1f,
                    ScalingValue = shieldAmount,
                    ShieldStrength = shieldStrength
                });
            }
            if (shieldAmount <= 0f) return;

            Cfg.ElementType element = elementSource == ShieldElementSource.InheritSignal ? context.Signal.DamageElement : fixedElement;
            context.Instance.SetResource(BuildResourceKey(groupKey), new EffectShieldHandle(context.Runtime.ShieldSystem, context.Target.EntityId, groupKey, element, shieldAmount));
        }

        /// <summary>按互斥组生成同一 EffectInstance 内稳定且可读的资源键。</summary>
        public static string BuildResourceKey(string group)
        {
            return $"Shield:{group}";
        }
    }

    /// <summary>
    /// EffectShieldHandle 精确拥有一层护盾，并保证 Effect 结束时只撤销自身贡献。
    /// </summary>
    internal sealed class EffectShieldHandle : IDisposable
    {
        private readonly IShieldSystem shieldSystem;
        private readonly int entityId;
        private readonly ShieldLayer layer;

        /// <summary>登记一层护盾并保存撤销所需的身份信息。</summary>
        public EffectShieldHandle(IShieldSystem shields, int targetEntityId, string groupKey, Cfg.ElementType element, float amount)
        {
            shieldSystem = shields;
            entityId = targetEntityId;
            layer = shields?.AddLayer(targetEntityId, groupKey, element, amount);
        }

        /// <summary>撤下本句柄登记的那一层；护盾早已被打碎或被同组替换时静默返回。</summary>
        public void Dispose()
        {
            if (layer == null) return;
            shieldSystem.RemoveLayer(entityId, layer);
        }
    }
}
