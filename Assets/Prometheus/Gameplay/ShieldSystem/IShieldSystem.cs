using System.Collections.Generic;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Shields
{
    /// <summary>
    /// 一层护盾。
    ///
    /// 按对象身份持有：施加它的 Effect 拿着这个引用，实例结束时精确撤下自己那一层，
    /// 不会误伤其他来源提供的同元素护盾。
    /// </summary>
    public sealed class ShieldLayer
    {
        /// <summary>
        /// 获取互斥组标识；同组的护盾同时只能存在一层，新的替换旧的。
        /// 结晶护盾用它表达「同时只能存在一个」，空串表示本层不与任何层互斥。
        /// </summary>
        public string GroupKey { get; }

        /// <summary>
        /// 获取护盾的元素亲和；`None` 表示无亲和，对任何伤害都按一倍效率吸收。
        /// </summary>
        public Cfg.ElementType Element { get; }

        /// <summary>获取剩余的**名义**吸收量。面对同元素伤害时它能挡下的实际伤害是这个值的 2.5 倍。</summary>
        public float Remaining { get; internal set; }

        /// <summary>创建一层只能由护盾系统登记的护盾。</summary>
        internal ShieldLayer(string groupKey, Cfg.ElementType element, float amount)
        {
            GroupKey = groupKey ?? string.Empty;
            Element = element;
            Remaining = amount;
        }
    }

    /// <summary>
    /// 护盾的唯一权威：登记护盾层、按规则吸收伤害、在护盾耗尽时移除它。
    ///
    /// 护盾按实体编号存放而不是做成 Entity 组件，与 `IElementSystem` 的附着一致：
    /// 这样新增一种会吃护盾的实体不需要改动它的组装流程。
    ///
    /// 本系统**不持有时长**：护盾的存活时间由施加它的 `EffectDefinition` 决定，
    /// Effect 实例结束时通过资源句柄撤下自己那一层。
    /// </summary>
    public interface IShieldSystem : ISystemContract
    {
        /// <summary>
        /// 为目标添加一层护盾并返回只能按对象身份移除的句柄。
        /// </summary>
        /// <param name="entityId">受护盾保护的实体编号。</param>
        /// <param name="groupKey">互斥组标识；同组已有护盾时替换它。传空串表示不互斥。</param>
        /// <param name="element">护盾的元素亲和。</param>
        /// <param name="amount">名义吸收量；必须为正。</param>
        ShieldLayer AddLayer(int entityId, string groupKey, Cfg.ElementType element, float amount);

        /// <summary>按对象身份移除一层护盾；该层已经耗尽或被替换时返回 false。</summary>
        bool RemoveLayer(int entityId, ShieldLayer layer);

        /// <summary>
        /// 用目标身上的护盾吸收一笔伤害，并返回**被吸收掉的伤害量**。
        ///
        /// 调用方应当只把剩余部分交给生命值结算。吸收会改变护盾状态，因此不能「先查询后吸收」。
        /// </summary>
        /// <param name="entityId">承伤实体编号。</param>
        /// <param name="damage">已完成全部乘区计算的伤害。</param>
        /// <param name="element">本笔伤害的元素，决定各层的吸收效率。</param>
        float Absorb(int entityId, float damage, Cfg.ElementType element);

        /// <summary>获取目标当前全部护盾层的剩余名义量之和，供 HUD 与测试读取。</summary>
        float GetTotalRemaining(int entityId);

        /// <summary>只读查询目标当前的护盾层；没有护盾时返回空。</summary>
        IReadOnlyList<ShieldLayer> QueryLayers(int entityId);

        /// <summary>清除目标全部护盾，供实体死亡或回收时调用。</summary>
        void ClearShields(int entityId);
    }
}
