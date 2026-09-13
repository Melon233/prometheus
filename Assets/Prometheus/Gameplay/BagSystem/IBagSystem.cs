using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus
{
    /// <summary>定义背包缓存、修订通知和数据刷新入口。</summary>
    public interface IBagSystem : ISystemContract
    {
        /// <summary>获取背包缓存的变化版本属性。</summary>
        ModifiableProperty RevisionProperty { get; }

        /// <summary>获取当前缓存的物品列表。</summary>
        IReadOnlyList<Item> Items { get; }

        /// <summary>从权威数据源刷新背包缓存。</summary>
        UniTask RequestItemsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 按材料标识返回当前持有总量；同一材料的不同品质会被合并计数。
        /// 未持有返回 0——"没有这件材料"和"持有 0 个"在背包语义上是同一件事。
        /// </summary>
        /// <param name="materialId">材料标识，对应 `TbMaterial` 的主键。</param>
        int GetQuantity(string materialId);

        /// <summary>
        /// 校验一组材料消耗能否由当前缓存支付，**不扣除任何东西**。
        ///
        /// 背包是服务器权威的，扣减只能由服务器执行。本方法的作用是在发请求前挡掉
        /// 明显不成立的操作并给出缺口明细；它基于最近一次同步的快照，因此结果是建议而非承诺，
        /// 最终成败仍以服务器响应为准。
        /// </summary>
        /// <param name="costs">本次操作的全部消耗；为空表示无消耗。</param>
        MaterialCostCheck CheckCost(IReadOnlyList<MaterialCost> costs);
    }
}
