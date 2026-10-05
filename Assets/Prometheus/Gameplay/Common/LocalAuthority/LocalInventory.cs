using System.Collections.Generic;

namespace Xuan.Prometheus.LocalAuthority
{
    /// <summary>
    /// 单机版背包的权威规则与运行时数据，规则移植自服务器 <c>internal/service/inventory.go</c>。
    ///
    /// 它不是 System 而是会话级共享状态：由组合根创建一次，
    /// 构造注入给两个本地网关，因此 POI 交互发放的物品与背包读到的物品天然是同一份数据。
    /// 数据只存在于内存中：单机版不做存档，每次进入游戏背包都从空开始。
    /// </summary>
    public sealed class LocalInventory
    {
        /// <summary>当前全部物品叠，按「物品标识 + 品质」唯一；数量归零的叠会被移除，因此列表中数量恒为正。</summary>
        private readonly List<LocalItemStack> stacks = new List<LocalItemStack>();

        /// <summary>获取当前全部物品叠（数量恒为正）。</summary>
        public IReadOnlyList<LocalItemStack> Stacks => stacks;

        /// <summary>发放指定物品；品质取自物品定义，同标识同品质的叠合并计数。</summary>
        /// <param name="itemId">物品标识；必须已在 <see cref="LocalItemCatalog"/> 中定义。</param>
        /// <param name="quantity">发放数量，必须为正。</param>
        public void Grant(string itemId, int quantity)
        {
            LocalItemDefinition definition = LocalItemCatalog.Require(itemId);
            LocalItemStack stack = Find(itemId, definition.Quality);
            if (stack == null) stacks.Add(new LocalItemStack(itemId, definition.Quality, quantity));
            else stack.Quantity += quantity;
        }

        /// <summary>消耗指定数量的物品；持有量不足时不做任何修改。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="quantity">消耗数量，必须为正。</param>
        /// <returns>持有量足够并完成扣除时返回 true。</returns>
        public bool Consume(string itemId, int quantity)
        {
            LocalItemStack stack = Find(itemId, LocalItemCatalog.Require(itemId).Quality);
            if (stack == null || stack.Quantity < quantity) return false;
            stack.Quantity -= quantity;
            if (stack.Quantity == 0) stacks.Remove(stack);
            return true;
        }

        /// <summary>消耗某物品的全部数量（不区分品质）。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>实际消耗的总数量；未持有时为 0。</returns>
        public int ConsumeAll(string itemId)
        {
            int total = 0;
            for (int index = stacks.Count - 1; index >= 0; index--)
            {
                if (stacks[index].ItemId != itemId) continue;
                total += stacks[index].Quantity;
                stacks.RemoveAt(index);
            }
            return total;
        }

        /// <summary>按「物品标识 + 品质」查找物品叠；未持有时返回空。</summary>
        private LocalItemStack Find(string itemId, int quality)
        {
            for (int index = 0; index < stacks.Count; index++)
            {
                if (stacks[index].ItemId == itemId && stacks[index].Quality == quality) return stacks[index];
            }
            return null;
        }
    }

    /// <summary>背包中的一叠物品，与协议 Item 一一对应。</summary>
    public sealed class LocalItemStack
    {
        /// <summary>创建一叠物品。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="quality">物品品质（1-5）。</param>
        /// <param name="quantity">初始数量，必须为正。</param>
        public LocalItemStack(string itemId, int quality, int quantity)
        {
            ItemId = itemId;
            Quality = quality;
            Quantity = quantity;
        }

        /// <summary>获取物品标识，对应 <see cref="LocalItemCatalog"/> 中的定义。</summary>
        public string ItemId { get; }

        /// <summary>获取物品品质，取自物品定义。</summary>
        public int Quality { get; }

        /// <summary>获取或设置持有数量；只由 <see cref="LocalInventory"/> 修改。</summary>
        public int Quantity { get; internal set; }
    }
}
