using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务系统的公共契约。
    ///
    /// 任务系统是剧情闭环的脊柱，但它**谁也不认识**：只做被查询、被投喂、被订阅三件事，
    /// 不通过 <c>Core.Gameplay</c> 解析任何其他 System（铁律 Q6）。
    /// 与剧情系统的数据面是一份共享的变量存储——两边各拥有自己的根命名空间（<c>quest</c> 与 <c>flag</c>、<c>var</c>），
    /// 因此谁都读得到谁，却没有人需要认识谁；与 NPC 系统的接缝是 <see cref="ResolveDialogue"/> 与 <see cref="Emit"/>。
    /// </summary>
    public interface IQuestSystem : ISystemContract
    {
        /// <summary>任务状态或当前步骤发生变化时触发；状态、步骤、进度合并成一条通知。</summary>
        event Action<QuestChanged> QuestChanged;

        /// <summary>任务产生奖励声明时触发；实际写入由外部适配器完成。</summary>
        event Action<QuestRewardGranted> RewardGranted;

        /// <summary>对话绑定可能变化时触发，参数为本次受影响的 NPC 标识集合。</summary>
        event Action<IReadOnlyCollection<string>> DialogueBindingsInvalidated;

        /// <summary>获取任务变量命名空间；实际的值存在它背后由组合根创建的共享存储里。</summary>
        QuestVariables Variables { get; }

        /// <summary>获取或设置当前追踪的任务；同时只追一个，与原神一致。</summary>
        string TrackedQuestId { get; set; }

        /// <summary>批量注册任务目录。</summary>
        /// <param name="catalog">任务配置目录。</param>
        void RegisterCatalog(QuestCatalog catalog);

        /// <summary>注册一个任务配置；配置错误会在此处抛出，而不是留到运行期。</summary>
        /// <param name="definition">任务配置。</param>
        void RegisterDefinition(QuestDefinition definition);

        /// <summary>读取一个任务的当前状态；未注册的任务抛出。</summary>
        /// <param name="questId">任务稳定标识。</param>
        QuestStatus GetStatus(string questId);

        /// <summary>读取一个任务的当前步骤；非进行中时为空。</summary>
        /// <param name="questId">任务稳定标识。</param>
        string GetCurrentStepId(string questId);

        /// <summary>获取全部处于进行中的任务标识。</summary>
        IReadOnlyList<string> GetActiveQuestIds();

        /// <summary>读取一个进行中任务当前步骤的全部目标行进度，供任务追踪与界面展示。</summary>
        /// <param name="questId">任务稳定标识。</param>
        IReadOnlyList<QuestObjectiveProgress> GetObjectiveProgress(string questId);

        /// <summary>
        /// 读取当前追踪任务的展示摘要；没有追踪任务或它不在进行中时返回 false。
        /// 标题、步骤与导航一次取齐，避免 UI 读到彼此不一致的中间状态。
        /// </summary>
        /// <param name="summary">命中时返回追踪摘要。</param>
        bool TryGetTrackedSummary(out QuestTrackSummary summary);

        /// <summary>
        /// 显式接取一个任务。
        /// 仅当任务处于可接取状态且解锁条件成立时生效；自动接取的任务由帧末推进自行进入。
        /// </summary>
        /// <param name="questId">任务稳定标识。</param>
        /// <returns>成功接取时返回 true。</returns>
        bool Accept(string questId);

        /// <summary>放弃一个进行中的任务；按配置决定是否清空该任务的进度与变量。</summary>
        /// <param name="questId">任务稳定标识。</param>
        /// <returns>成功放弃时返回 true。</returns>
        bool Abandon(string questId);

        /// <summary>
        /// 向任务系统投喂一条领域事件。
        /// 事件只写变量、弄脏相关任务，**不立即求值**——推进统一发生在帧末（铁律 Q4）。
        /// </summary>
        /// <param name="questEvent">领域事件。</param>
        void Emit(QuestEvent questEvent);

        /// <summary>
        /// 通知一个外部路径已经变化，使引用该路径的任务在下次推进时被重算。
        /// 共享存储里的变化会自动经由本入口进来，因此不再需要组合根做任何转发接线；
        /// 该入口仍然公开，供尚未进入变量存储的外部数据源（例如将来的服务器推送）投喂变化。
        /// </summary>
        /// <param name="path">发生变化的完整点分路径。</param>
        void Invalidate(string path);

        /// <summary>立即执行一次推进，供需要即时看到结果的调用点使用；常规推进在帧末自动发生。</summary>
        void FlushNow();

        /// <summary>
        /// 解析一个 NPC 当前应当呈现的对话与标记。纯查询，无副作用。
        /// 无任何绑定命中时返回 false，由调用方回落到该 NPC 的兜底闲聊。
        /// </summary>
        /// <param name="npcId">NPC 稳定标识。</param>
        /// <param name="resolution">命中时返回解析结果。</param>
        bool ResolveDialogue(string npcId, out QuestDialogueResolution resolution);

        /// <summary>捕获任务系统快照；只包含偏离默认的记录与已写入的变量。</summary>
        string CaptureSnapshot();

        /// <summary>从快照恢复任务状态；未知任务的记录记警告并丢弃，不抛异常。</summary>
        /// <param name="json">快照 JSON。</param>
        void RestoreSnapshot(string json);
    }

    /// <summary>一个目标行的求值结果；纯展示数据，不持有状态。</summary>
    public readonly struct QuestObjectiveProgress
    {
        /// <summary>创建一条目标行求值结果。</summary>
        public QuestObjectiveProgress(string objectiveId, string descTextKey, double current, double target, bool isDone, bool hidden)
        {
            ObjectiveId = objectiveId;
            DescTextKey = descTextKey;
            Current = current;
            Target = target;
            IsDone = isDone;
            Hidden = hidden;
        }

        /// <summary>获取目标稳定标识。</summary>
        public string ObjectiveId { get; }

        /// <summary>获取目标描述的文案键。</summary>
        public string DescTextKey { get; }

        /// <summary>获取当前进度；未配置进度表达式时为零。</summary>
        public double Current { get; }

        /// <summary>获取目标数量；未配置数量表达式时为零。</summary>
        public double Target { get; }

        /// <summary>获取该目标是否已完成。</summary>
        public bool IsDone { get; }

        /// <summary>获取该目标是否对玩家隐藏。</summary>
        public bool Hidden { get; }

        /// <summary>获取该目标是否应当显示进度数字。</summary>
        public bool HasProgress => Target > 0d;
    }
}
