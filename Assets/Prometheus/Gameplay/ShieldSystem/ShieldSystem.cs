using System;
using System.Collections.Generic;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Shields
{
    /// <summary>
    /// 护盾系统的实现。
    ///
    /// 按 Docs/Design/Combat/05 第 6 节结算：
    /// 护盾对**对应元素**伤害有 2.5 倍吸收效率，对其它伤害 1.0 倍；多层护盾按剩余量从高到低消耗。
    ///
    /// 它不认识伤害怎么算出来的，也不认识护盾能存活多久——前者在 `DamageCalculator`，
    /// 后者在施加护盾的 `EffectDefinition`。本系统只管「有多少、挡多少、什么时候碎」。
    /// </summary>
    internal sealed class ShieldSystem : XSystem, IShieldSystem
    {
        /// <summary>
        /// 元素亲和护盾对同元素伤害的吸收效率。
        ///
        /// 一层名义量 1000 的火护盾能挡下 2500 点火伤，但只能挡下 1000 点其它伤害。
        /// 常量而非配表：它是护盾机制本身的规则，不随某一张表变化，见 05 第 6 节。
        /// </summary>
        public const float MatchingElementEfficiency = 2.5f;

        /// <summary>无元素亲和、或元素不匹配时的吸收效率。</summary>
        public const float DefaultEfficiency = 1f;

        /// <summary>按实体编号保存护盾层；列表始终按剩余量降序消耗，不维护排序。</summary>
        private readonly Dictionary<int, List<ShieldLayer>> layersByEntity = new Dictionary<int, List<ShieldLayer>>();

        /// <summary>空列表常量，避免查询无护盾目标时产生垃圾。</summary>
        private static readonly ShieldLayer[] EmptyLayers = Array.Empty<ShieldLayer>();

        /// <inheritdoc />
        public ShieldLayer AddLayer(int entityId, string groupKey, Cfg.ElementType element, float amount)
        {
            if (amount <= 0f) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Shield amount must be positive.");

            List<ShieldLayer> layers = GetOrCreate(entityId);
            // 同组互斥：结晶护盾「同时只能存在一个」正是靠这里表达，换元素也一样替换。
            if (!string.IsNullOrEmpty(groupKey))
                for (int index = layers.Count - 1; index >= 0; index--)
                    if (layers[index].GroupKey == groupKey) layers.RemoveAt(index);

            ShieldLayer layer = new ShieldLayer(groupKey, element, amount);
            layers.Add(layer);
            return layer;
        }

        /// <inheritdoc />
        public bool RemoveLayer(int entityId, ShieldLayer layer)
        {
            return layer != null && layersByEntity.TryGetValue(entityId, out List<ShieldLayer> layers) && layers.Remove(layer);
        }

        /// <inheritdoc />
        public float Absorb(int entityId, float damage, Cfg.ElementType element)
        {
            if (damage <= 0f || !layersByEntity.TryGetValue(entityId, out List<ShieldLayer> layers) || layers.Count == 0) return 0f;

            float remainingDamage = damage;
            // 按剩余量从高到低消耗（05 第 6 节）。厚的先挡，薄的留到最后，
            // 这样一次大伤害不会把多层薄盾一起打碎。
            layers.Sort(CompareByRemainingDescending);

            for (int index = 0; index < layers.Count && remainingDamage > 0f; index++)
            {
                ShieldLayer layer = layers[index];
                float efficiency = ResolveEfficiency(layer, element);
                float capacity = layer.Remaining * efficiency;
                float absorbed = remainingDamage < capacity ? remainingDamage : capacity;

                // 扣的是名义量而不是实际挡下的伤害：同元素时挡 2.5 点只消耗 1 点护盾。
                layer.Remaining -= absorbed / efficiency;
                remainingDamage -= absorbed;
            }

            for (int index = layers.Count - 1; index >= 0; index--)
                if (layers[index].Remaining <= 0f) layers.RemoveAt(index);

            return damage - remainingDamage;
        }

        /// <inheritdoc />
        public float GetTotalRemaining(int entityId)
        {
            if (!layersByEntity.TryGetValue(entityId, out List<ShieldLayer> layers)) return 0f;
            float total = 0f;
            for (int index = 0; index < layers.Count; index++) total += layers[index].Remaining;
            return total;
        }

        /// <inheritdoc />
        public IReadOnlyList<ShieldLayer> QueryLayers(int entityId)
        {
            return layersByEntity.TryGetValue(entityId, out List<ShieldLayer> layers) ? layers : EmptyLayers;
        }

        /// <inheritdoc />
        public void ClearShields(int entityId)
        {
            if (layersByEntity.TryGetValue(entityId, out List<ShieldLayer> layers)) layers.Clear();
        }

        /// <summary>释放全部护盾状态。</summary>
        public override void Dispose()
        {
            layersByEntity.Clear();
        }

        /// <summary>求一层护盾面对指定元素伤害时的吸收效率。</summary>
        private static float ResolveEfficiency(ShieldLayer layer, Cfg.ElementType element)
        {
            return layer.Element != Cfg.ElementType.None && layer.Element == element ? MatchingElementEfficiency : DefaultEfficiency;
        }

        /// <summary>按剩余名义量降序排序。</summary>
        private static int CompareByRemainingDescending(ShieldLayer left, ShieldLayer right)
        {
            return right.Remaining.CompareTo(left.Remaining);
        }

        /// <summary>取出或建立目标的护盾层列表。</summary>
        private List<ShieldLayer> GetOrCreate(int entityId)
        {
            if (layersByEntity.TryGetValue(entityId, out List<ShieldLayer> layers)) return layers;
            layers = new List<ShieldLayer>();
            layersByEntity.Add(entityId, layers);
            return layers;
        }
    }
}
