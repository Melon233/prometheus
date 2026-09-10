using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Quest;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc
{
    /// <summary>
    /// NPC 系统的公共契约。
    ///
    /// NpcSystem 是剧情闭环里**唯一的编排者**：向任务系统「查」这个 NPC 现在该说什么，
    /// 向剧情系统「下单」把这段演出来，再把演出结果「报」回任务系统。
    /// 它自己不保存任务进度，也不实现任何演出。
    ///
    /// 依赖是单向的：NpcSystem 依赖 Quest 与 Narrative，后两者既不依赖它，也不互相依赖（铁律 N1）。
    /// </summary>
    public interface INpcSystem : ISystemContract
    {
        /// <summary>NPC 头顶标记可能变化时触发，参数为受影响的 NPC 标识。</summary>
        event Action<string> MarkerChanged;

        /// <summary>获取当前是否有活动交互会话。</summary>
        bool HasActiveInteraction { get; }

        /// <summary>
        /// 读取一个 NPC 当前的头顶标记。
        /// 标记不由本系统计算——它与「该说哪段话」来自任务系统的同一次解析，因此不可能不一致。
        /// </summary>
        /// <param name="npcId">NPC 稳定标识。</param>
        QuestMarker GetMarker(string npcId);

        /// <summary>
        /// 执行一次完整交互：查询对话绑定、加载剧情、进入舞台、演出、上报结果、逆序还原。
        /// 同一时刻只允许一个活动会话，重入时立即返回 <see cref="NpcInteractionResult.Rejected"/>。
        /// </summary>
        /// <param name="npc">承载该 NPC 的场景 POI 组件。</param>
        /// <param name="cancellationToken">外部取消令牌。</param>
        UniTask<NpcInteractionResult> InteractAsync(PoiMono npc, CancellationToken cancellationToken = default);

        /// <summary>中止当前交互；chunk 卸载与外部打断使用，无活动会话时为空操作。</summary>
        void CancelInteraction();
    }
}
