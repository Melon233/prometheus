using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.LocalAuthority
{
    /// <summary>
    /// 单机版的物品定义与神像供奉数值，内容与 <c>Server/config/items.json</c> 保持一致。
    /// 客户端目前没有物品配表，因此先以代码常量承载；接入 Luban 物品表后应改为从 ConfigKit 读取。
    /// </summary>
    public static class LocalItemCatalog
    {
        /// <summary>长剑：神像每升一级发放一把。</summary>
        public const string Sword = "Sword";

        /// <summary>护甲：宝箱掉落。</summary>
        public const string Armor = "Armor";

        /// <summary>苹果：采集掉落。</summary>
        public const string Apple = "Apple";

        /// <summary>风神瞳：收集神瞳获得，用于神像供奉与抽卡。</summary>
        public const string Anemoculus = "Anemoculus";

        /// <summary>经验书：宝箱掉落。</summary>
        public const string ExpBook = "ExpBook";

        /// <summary>每个风神瞳提供的神像供奉进度。</summary>
        public const int StatueProgressPerOculus = 1;

        /// <summary>神像每级所需的供奉进度。</summary>
        public const int StatueLevelThreshold = 10;

        /// <summary>全部物品定义，顺序即抽卡候选的枚举顺序。</summary>
        public static readonly IReadOnlyList<LocalItemDefinition> Items = new[]
        {
            new LocalItemDefinition(Sword, "长剑", 1),
            new LocalItemDefinition(Armor, "护甲", 1),
            new LocalItemDefinition(Apple, "苹果", 1),
            new LocalItemDefinition(Anemoculus, "风神瞳", 1),
            new LocalItemDefinition(ExpBook, "经验书", 1)
        };

        /// <summary>按标识查找物品定义；未定义的标识属于代码错误，直接抛出。</summary>
        /// <param name="itemId">物品标识。</param>
        public static LocalItemDefinition Require(string itemId)
        {
            for (int index = 0; index < Items.Count; index++)
            {
                if (Items[index].ItemId == itemId) return Items[index];
            }
            throw new ArgumentException($"Unknown item '{itemId}'.", nameof(itemId));
        }
    }

    /// <summary>一条物品定义：标识、显示名与品质。</summary>
    public readonly struct LocalItemDefinition
    {
        /// <summary>创建物品定义。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="displayName">显示名。</param>
        /// <param name="quality">品质（1-5）。</param>
        public LocalItemDefinition(string itemId, string displayName, int quality)
        {
            ItemId = itemId;
            DisplayName = displayName;
            Quality = quality;
        }

        /// <summary>获取物品标识。</summary>
        public string ItemId { get; }

        /// <summary>获取显示名。</summary>
        public string DisplayName { get; }

        /// <summary>获取品质。</summary>
        public int Quality { get; }
    }
}
