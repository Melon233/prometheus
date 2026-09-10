using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Narrative;
using Xuan.Prometheus.Quest;
using Xuan.Prometheus.World;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Npc
{
    /// <summary>
    /// NPC 交互编排：查询对话绑定、下单演出、上报结果。
    ///
    /// 这是闭环里唯一同时认识任务系统与剧情系统的地方。它把三次跨系统调用收在一个方法里，
    /// 因此「必须成对调用开闭会话」「必须在演出后上报事件」这类义务不会散落到调用方身上。
    /// </summary>
    internal sealed class NpcSystem : XSystem, INpcSystem
    {
        /// <summary>
        /// 剧情用来表达「玩家在本次对话里接受了委托」的变量路径。
        ///
        /// 走变量而不是事件载荷，是因为**事件表达「发生了什么」，变量表达「现在是什么」**：
        /// 玩家选了哪个分支是一个状态，剧情图用 <c>SetVar</c> 写它，编排者演完读它。
        /// </summary>
        private const string AcceptedVariablePath = "var.accepted";

        /// <summary>对话界面宿主；由组合根注入，玩法层因此不必命名 UI 程序集里的面板类型。</summary>
        private readonly IDialogueHost dialogueHost;

        /// <summary>缓存每个 NPC 的头顶标记，避免每帧重复解析。</summary>
        private readonly Dictionary<string, QuestMarker> markerCache = new Dictionary<string, QuestMarker>(StringComparer.Ordinal);

        /// <summary>当前活动交互会话的取消源；为空表示没有活动会话。</summary>
        private CancellationTokenSource sessionCancellation;

        /// <summary>构造注入的任务系统；本系统的编排全部围绕它的查询结果展开。</summary>
        private readonly IQuestSystem questSystem;

        /// <summary>构造注入的剧情系统；本系统只把编排结果交给它演绎。</summary>
        private readonly INarrativeSystem narrativeSystem;

        /// <inheritdoc />
        public event Action<string> MarkerChanged;

        /// <summary>创建 NPC 系统。</summary>
        /// <param name="questSystem">提供任务状态与对话绑定解析的任务系统。</param>
        /// <param name="narrativeSystem">负责实际演出的剧情系统。</param>
        /// <param name="dialogueHost">对话界面宿主；由组合根提供 UI 侧实现。</param>
        public NpcSystem(IQuestSystem questSystem, INarrativeSystem narrativeSystem, IDialogueHost dialogueHost)
        {
            this.questSystem = questSystem ?? throw new ArgumentNullException(nameof(questSystem));
            this.narrativeSystem = narrativeSystem ?? throw new ArgumentNullException(nameof(narrativeSystem));
            this.dialogueHost = dialogueHost ?? throw new ArgumentNullException(nameof(dialogueHost));
        }

        /// <inheritdoc />
        public bool HasActiveInteraction => sessionCancellation != null;

        /// <summary>订阅任务系统的对话绑定失效通知，使标记缓存由通知驱动而不是轮询。</summary>
        public override void AfterNew()
        {
            questSystem.DialogueBindingsInvalidated += OnDialogueBindingsInvalidated;
        }

        /// <inheritdoc />
        public QuestMarker GetMarker(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return QuestMarker.None;
            if (markerCache.TryGetValue(npcId, out QuestMarker cached)) return cached;
            QuestMarker marker = questSystem.ResolveDialogue(npcId, out QuestDialogueResolution resolution) ? resolution.Marker : QuestMarker.None;
            markerCache[npcId] = marker;
            return marker;
        }

        /// <inheritdoc />
        public async UniTask<NpcInteractionResult> InteractAsync(PoiMono npc, CancellationToken cancellationToken = default)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            if (npc.Config == null || npc.Config.Npc == null) throw new InvalidOperationException($"POI '{npc.name}' is not an NPC: it has no NpcDefinition.");
            if (HasActiveInteraction) return NpcInteractionResult.Rejected;

            NpcDefinition definition = npc.Config.Npc;
            definition.Validate();

            // 任务绑定优先，没有任何绑定命中时才回落到 NPC 自己的兜底闲聊。
            bool hasBinding = questSystem.ResolveDialogue(definition.NpcId, out QuestDialogueResolution resolution);
            string storyLocation = hasBinding ? resolution.StoryLocation : definition.IdleStoryLocation;
            if (string.IsNullOrWhiteSpace(storyLocation)) return NpcInteractionResult.Rejected;

            sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = sessionCancellation.Token;
            string bindingId = hasBinding ? resolution.BindingId : null;
            try
            {
                questSystem.Emit(QuestEvent.Create(QuestEventNames.NpcTalkStarted, ("npcId", definition.NpcId), ("storyId", storyLocation)));
                StoryResult storyResult = await NarrativePlayback.PlayAsync(narrativeSystem, storyLocation, dialogueHost, token);
                NpcInteractionResult result = Translate(storyResult);
                Report(questSystem, definition.NpcId, storyLocation, bindingId, result);
                if (result != NpcInteractionResult.Aborted && hasBinding) TryAcceptOfferedQuest(resolution);
                return result;
            }
            catch (OperationCanceledException)
            {
                // 取消是正常的结束路径之一：舞台与界面已由 NarrativePlayback 的 finally 还原完毕。
                Report(questSystem, definition.NpcId, storyLocation, bindingId, NpcInteractionResult.Aborted);
                return NpcInteractionResult.Aborted;
            }
            finally
            {
                sessionCancellation?.Dispose();
                sessionCancellation = null;
            }
        }

        /// <inheritdoc />
        public void CancelInteraction()
        {
            // 只发出取消信号；舞台还原、剧情中止与会话关闭都挂在同一个令牌上，不存在第二条清理路径。
            sessionCancellation?.Cancel();
        }

        /// <summary>释放活动会话与全部外部订阅。</summary>
        public override void Dispose()
        {
            CancelInteraction();
            sessionCancellation?.Dispose();
            sessionCancellation = null;
            questSystem.DialogueBindingsInvalidated -= OnDialogueBindingsInvalidated;
            markerCache.Clear();
            MarkerChanged = null;
        }

        /// <summary>把演出结果上报成任务领域事件。</summary>
        private static void Report(IQuestSystem questSystem, string npcId, string storyLocation, string bindingId, NpcInteractionResult result)
        {
            if (result == NpcInteractionResult.Aborted)
            {
                questSystem.Emit(QuestEvent.Create(QuestEventNames.NpcTalkAborted, ("npcId", npcId), ("storyId", storyLocation)));
                return;
            }
            questSystem.Emit(QuestEvent.Create(QuestEventNames.NpcTalkFinished,
                ("npcId", npcId),
                ("storyId", storyLocation),
                ("bindingId", bindingId ?? string.Empty),
                ("result", result.ToString())));
        }

        /// <summary>
        /// 处理对话中的显式接取。
        /// 这是本系统唯一一处**写**任务状态的地方，其余全部是查询与事件上报；
        /// 之所以允许，是因为「玩家在这次对话里点了接受」这个事实只有编排者知道。
        /// </summary>
        private void TryAcceptOfferedQuest(QuestDialogueResolution resolution)
        {
            if (string.IsNullOrEmpty(resolution.OffersQuestId)) return;
            if (!narrativeSystem.Variables.TryResolve(AcceptedVariablePath, out StoryValue accepted) || !accepted.AsBool()) return;
            questSystem.Accept(resolution.OffersQuestId);
        }

        /// <summary>把剧情结束原因翻译成交互结果。</summary>
        private static NpcInteractionResult Translate(StoryResult result)
        {
            return result switch
            {
                StoryResult.Completed => NpcInteractionResult.Completed,
                StoryResult.Skipped => NpcInteractionResult.Skipped,
                _ => NpcInteractionResult.Aborted
            };
        }

        /// <summary>任务状态变化后让受影响 NPC 的标记缓存失效，并转发通知给表现层。</summary>
        private void OnDialogueBindingsInvalidated(IReadOnlyCollection<string> npcIds)
        {
            foreach (string npcId in npcIds)
            {
                markerCache.Remove(npcId);
                MarkerChanged?.Invoke(npcId);
            }
        }

    }
}
