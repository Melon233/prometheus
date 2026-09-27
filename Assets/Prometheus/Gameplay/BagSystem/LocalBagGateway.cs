using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.LocalAuthority;
using Xuan.Prometheus.Protocol;
using Random = UnityEngine.Random;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 背包领域网关的单机实现：直接读写内存中的背包，抽卡规则移植自服务器 <c>Service.Gacha</c>。
    /// 响应仍使用协议消息，BagSystem 与联网版共用同一套处理逻辑。单机版不做存档，每次进入游戏背包都从空开始。
    /// </summary>
    internal sealed class LocalBagGateway : XSystem, IBagGateway
    {
        /// <summary>与 POI 网关共享的背包。</summary>
        private readonly LocalInventory inventory;

        /// <summary>创建单机背包网关。</summary>
        /// <param name="inventory">与 POI 网关共享的背包。</param>
        public LocalBagGateway(LocalInventory inventory)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        /// <inheritdoc />
        public UniTask<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default)
        {
            GetItemsResponse response = new GetItemsResponse();
            AppendItems(response.Items);
            return UniTask.FromResult(response);
        }

        /// <summary>
        /// 消耗一个风神瞳，从风神瞳以外的全部物品中等概率抽取一个发放。
        /// 风神瞳不足时不修改背包，返回失败与当前背包快照。
        /// </summary>
        public UniTask<GachaResponse> DrawGachaAsync(CancellationToken cancellationToken = default)
        {
            GachaResponse response = new GachaResponse();
            if (!inventory.Consume(LocalItemCatalog.Anemoculus, 1))
            {
                response.Success = false;
                response.Error = "insufficient Anemoculus";
                AppendItems(response.Items);
                return UniTask.FromResult(response);
            }

            LocalItemDefinition reward = PickReward();
            inventory.Grant(reward.ItemId, 1);
            response.Success = true;
            response.Reward = new Item { ItemId = reward.ItemId, Quality = reward.Quality, Quantity = 1 };
            AppendItems(response.Items);
            return UniTask.FromResult(response);
        }

        /// <summary>从风神瞳以外的物品定义中等概率抽取一个。</summary>
        private static LocalItemDefinition PickReward()
        {
            List<LocalItemDefinition> candidates = new List<LocalItemDefinition>();
            foreach (LocalItemDefinition definition in LocalItemCatalog.Items)
            {
                if (definition.ItemId != LocalItemCatalog.Anemoculus) candidates.Add(definition);
            }
            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>把当前背包转换为协议物品并追加到目标集合。</summary>
        /// <param name="target">协议响应中的物品列表。</param>
        private void AppendItems(ICollection<Item> target)
        {
            foreach (LocalItemStack stack in inventory.Stacks) target.Add(new Item { ItemId = stack.ItemId, Quality = stack.Quality, Quantity = stack.Quantity });
        }
    }
}
