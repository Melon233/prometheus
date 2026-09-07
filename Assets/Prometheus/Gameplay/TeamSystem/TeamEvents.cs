using System;

namespace Xuan.Prometheus
{
    /// <summary>描述本地玩家当前上场成员从一个槽位切换到另一个槽位的完整事实。</summary>
    public sealed class ActiveTeamMemberChangedEvent : IEvent
    {
        /// <summary>创建一条不可变的小队上场成员变化事实，实体编号为零表示对应方向没有成员。</summary>
        /// <param name="previousEntityId">切换前的实体编号；没有旧成员时传零。</param>
        /// <param name="currentEntityId">切换后的实体编号；当前没有可用成员时传零。</param>
        /// <param name="previousSlotIndex">切换前的零基槽位；没有旧成员时传负一。</param>
        /// <param name="currentSlotIndex">切换后的零基槽位；当前没有可用成员时传负一。</param>
        public ActiveTeamMemberChangedEvent(int previousEntityId, int currentEntityId, int previousSlotIndex, int currentSlotIndex)
        {
            if (previousEntityId < 0) throw new ArgumentOutOfRangeException(nameof(previousEntityId), previousEntityId, "Previous entity ID cannot be negative.");
            if (currentEntityId < 0) throw new ArgumentOutOfRangeException(nameof(currentEntityId), currentEntityId, "Current entity ID cannot be negative.");
            PreviousEntityId = previousEntityId;
            CurrentEntityId = currentEntityId;
            PreviousSlotIndex = previousSlotIndex;
            CurrentSlotIndex = currentSlotIndex;
        }

        /// <summary>获取切换前的实体编号；没有旧成员时为零。</summary>
        public int PreviousEntityId { get; }

        /// <summary>获取切换后的实体编号；当前没有可用成员时为零。</summary>
        public int CurrentEntityId { get; }

        /// <summary>获取切换前的零基槽位；没有旧成员时为负一。</summary>
        public int PreviousSlotIndex { get; }

        /// <summary>获取切换后的零基槽位；当前没有可用成员时为负一。</summary>
        public int CurrentSlotIndex { get; }
    }
}
