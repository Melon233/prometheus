using System.Collections.Generic;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务系统契约。任务状态只存在于内存中（单机不存档），每次进入游戏从第一个节点重新开始。
    /// UI 只读取进度，不提交任何命令：任务推进完全由玩法事实（演出结束、玩家到达区域）驱动。
    /// </summary>
    public interface IQuestSystem : ISystem
    {
        /// <summary>全部进行中的任务。</summary>
        IReadOnlyList<QuestProgress> ActiveQuests { get; }

        /// <summary>HUD 追踪的任务（当前取第一个进行中的任务）；没有时为空。</summary>
        QuestProgress TrackedQuest { get; }
    }
}
