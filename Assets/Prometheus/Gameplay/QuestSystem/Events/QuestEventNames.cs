using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 已知领域事件名的唯一真相源。
    ///
    /// 任务事件用「名字 + 载荷」而不是封闭枚举，好处是新增一种触发方式只需加一个常量与一个发布方，
    /// 任务内核零改动；代价是名字拼错不会编译报错。对策就是这张表——
    /// 编辑期校验器（<see cref="QuestValidator"/>）据此检查每条触发器引用的事件名是否已登记。
    ///
    /// 注释里的键名是该事件的载荷契约：触发器的过滤表达式用 <c>e.&lt;键名&gt;</c> 引用它们，
    /// 因此**改键名等于改配置**，发布方不得随意重命名。
    /// </summary>
    public static class QuestEventNames
    {
        /// <summary>NPC 交互开始；载荷 npcId、storyId。发布方：NpcSystem。</summary>
        public const string NpcTalkStarted = "npc.talk_started";

        /// <summary>NPC 交互正常结束；载荷 npcId、storyId、bindingId、result。发布方：NpcSystem。</summary>
        public const string NpcTalkFinished = "npc.talk_finished";

        /// <summary>NPC 交互被中止；载荷 npcId、storyId。发布方：NpcSystem。</summary>
        public const string NpcTalkAborted = "npc.talk_aborted";

        /// <summary>POI 被交互；载荷 poiId、poiType。发布方：PoiSystem。</summary>
        public const string PoiInteracted = "world.poi_interacted";

        /// <summary>POI 被解锁；载荷 poiId。发布方：PoiSystem。</summary>
        public const string PoiUnlocked = "world.poi_unlocked";

        /// <summary>玩家进入区域；载荷 regionId。发布方：PoiSystem。</summary>
        public const string EnteredRegion = "world.entered_region";

        /// <summary>敌人被击败；载荷 id、count。发布方：EntitySystem。</summary>
        public const string EnemyDefeated = "combat.enemy_defeated";

        /// <summary>物品进入背包；载荷 id、count。发布方：BagSystem。</summary>
        public const string ItemAdded = "bag.item_added";

        /// <summary>载荷在触发器过滤表达式中可见的命名空间前缀。</summary>
        public const string PayloadRoot = "e";

        /// <summary>保存全部已登记的事件名，供编辑期校验。</summary>
        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal)
        {
            NpcTalkStarted,
            NpcTalkFinished,
            NpcTalkAborted,
            PoiInteracted,
            PoiUnlocked,
            EnteredRegion,
            EnemyDefeated,
            ItemAdded
        };

        /// <summary>判断一个事件名是否已经登记。</summary>
        /// <param name="eventName">待检查的事件名。</param>
        public static bool IsKnown(string eventName)
        {
            return !string.IsNullOrEmpty(eventName) && Known.Contains(eventName);
        }

        /// <summary>获取全部已登记的事件名。</summary>
        public static IReadOnlyCollection<string> All => Known;
    }
}
