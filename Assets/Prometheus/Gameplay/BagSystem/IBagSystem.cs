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
    }
}
