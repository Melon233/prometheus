using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus
{
    /// <summary>封装背包数据请求与缓存，提供可监听的修订号供 UI 响应式更新。</summary>
    internal sealed class BagSystem : IBagSystem
    {
        /// <summary>记录背包缓存版本，供界面订阅后按需重绘。</summary>
        private readonly ModifiableProperty revision = new ModifiableProperty();
        /// <summary>保存网关最近一次返回的当前玩家物品快照。</summary>
        private readonly List<Item> items = new List<Item>();
        /// <summary>按物品标识聚合的持有量索引；同一标识的不同品质合并计数，每次刷新快照后重建。</summary>
        private readonly Dictionary<string, int> quantityByItemId = new Dictionary<string, int>();

        /// <summary>在系统释放时取消尚未完成的背包请求，阻止响应继续修改已清空的缓存。</summary>
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        /// <summary>本领域的权威数据适配器，构造注入。</summary>
        private readonly IBagGateway Gateway;

        /// <summary>创建背包系统。</summary>
        /// <param name="bagGateway">负责本领域组包解包的网关。</param>
        public BagSystem(IBagGateway bagGateway)
        {
            Gateway = bagGateway ?? throw new System.ArgumentNullException(nameof(bagGateway));
        }

        /// <summary>背包物品列表的变化版本；UI 通过 Listen 监听它并在变化时重新读取列表。</summary>
        public ModifiableProperty RevisionProperty => revision;

        /// <summary>当前缓存的全部物品（按网关返回顺序）。</summary>
        public IReadOnlyList<Item> Items => items;

        /// <summary>向网关请求全部物品并刷新缓存与修订号。</summary>
        public async UniTask RequestItemsAsync(CancellationToken cancellationToken = default)
        {
            using (CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token))
            {
                try
                {
                    GetItemsResponse response = await Gateway.GetItemsAsync(operationCancellation.Token);
                    operationCancellation.Token.ThrowIfCancellationRequested();
                    if (response == null) return;
                    items.Clear();
                    items.AddRange(response.Items);
                    RebuildQuantityIndex();
                    revision.SetBaseValue(revision.Value + 1f);
                }
                catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested) { }
            }
        }

        /// <summary>按材料标识返回当前持有总量；未持有返回 0。</summary>
        public int GetQuantity(string materialId)
        {
            return string.IsNullOrEmpty(materialId) ? 0 : quantityByItemId.TryGetValue(materialId, out int quantity) ? quantity : 0;
        }

        /// <summary>基于最近一次同步的快照校验一组消耗；只读，不扣除。</summary>
        public MaterialCostCheck CheckCost(IReadOnlyList<MaterialCost> costs)
        {
            return MaterialCostEvaluator.Evaluate(costs, GetQuantity);
        }

        /// <summary>本系统没有异步初始化工作，直接返回已完成任务。</summary>
        public UniTask AfterNewAsync()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>本系统没有同步初始化工作。</summary>
        public void AfterNew()
        {
        }

        /// <summary>本系统不需要逐帧驱动。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public void OnUpdate(float dt)
        {
        }

        /// <summary>在每次刷新快照后重建持有量索引，使查询不必每次遍历整个背包。</summary>
        private void RebuildQuantityIndex()
        {
            quantityByItemId.Clear();
            for (int index = 0; index < items.Count; index++)
            {
                Item item = items[index];
                quantityByItemId.TryGetValue(item.ItemId, out int accumulated);
                quantityByItemId[item.ItemId] = accumulated + item.Quantity;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lifetimeCancellation.Cancel();
            items.Clear();
            quantityByItemId.Clear();
        }
    }
}
