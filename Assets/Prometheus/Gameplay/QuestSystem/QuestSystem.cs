using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Cutscene;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务系统：推进线性任务节点，并按节点上的触发器播放演出。
    ///
    /// 依赖只有一个方向——任务认识演出，演出不认识任务：
    /// <list type="bullet">
    /// <item>开始演出：节点的触发器调用 <see cref="ICutsceneSystem.Play"/>（演出播放中时 OnEnter 触发排队，区域与交互触发等待演出结束后再判定）。</item>
    /// <item>演出完成：监听 <see cref="CutsceneFinishedEvent"/>，命中当前节点的 <see cref="CutsceneObjective"/>。</item>
    /// </list>
    /// 任务专属的区域与交互点写在任务资产里：区域每帧按「锚点位置 + 偏移」做纯几何判定，
    /// 交互点在节点激活时生成到锚点下、节点结束时销毁。场景里只需要被引用的 <see cref="WorldAnchor"/>。
    /// 状态只在内存中，单机不存档（ARCH-NET-013）。
    /// </summary>
    internal sealed class QuestSystem : IQuestSystem
    {
        /// <summary>任务目录的 YooAsset 地址。</summary>
        private const string CatalogAddress = "QuestCatalog";

        private readonly ICutsceneSystem cutsceneSystem;
        private readonly ITeamSystem teamSystem;
        private readonly IWorldAnchorSystem anchorSystem;

        /// <summary>进行中的任务。</summary>
        private readonly List<QuestProgress> activeQuests = new List<QuestProgress>();

        /// <summary>每个进行中任务当前节点生成的交互点，节点结束时销毁。</summary>
        private readonly Dictionary<QuestProgress, List<QuestInteractable>> interactables = new Dictionary<QuestProgress, List<QuestInteractable>>();

        /// <summary>因演出正在播放而排队等待的 OnEnter 演出，按入队顺序播放。</summary>
        private readonly Queue<CutsceneDefinition> pendingCutscenes = new Queue<CutsceneDefinition>();

        private QuestCatalog catalog;

        /// <summary>创建任务系统。</summary>
        /// <param name="cutsceneSystem">播放演出。</param>
        /// <param name="teamSystem">提供玩家位置与交互组件。</param>
        /// <param name="anchorSystem">按 id 定位区域与交互点挂载的场景锚点。</param>
        public QuestSystem(ICutsceneSystem cutsceneSystem, ITeamSystem teamSystem, IWorldAnchorSystem anchorSystem)
        {
            this.cutsceneSystem = cutsceneSystem ?? throw new ArgumentNullException(nameof(cutsceneSystem));
            this.teamSystem = teamSystem ?? throw new ArgumentNullException(nameof(teamSystem));
            this.anchorSystem = anchorSystem ?? throw new ArgumentNullException(nameof(anchorSystem));
        }

        /// <inheritdoc />
        public IReadOnlyList<QuestProgress> ActiveQuests => activeQuests;

        /// <inheritdoc />
        public QuestProgress TrackedQuest => activeQuests.Count > 0 ? activeQuests[0] : null;

        /// <summary>加载任务目录（ARCH-COMP-006：资源由使用它的系统自己加载）。</summary>
        public async UniTask AfterNewAsync()
        {
            await Core.Asset.LoadAssetAsync<QuestCatalog>(CatalogAddress, asset => catalog = asset, error => throw new InvalidOperationException(error)).ToUniTask();
        }

        /// <summary>订阅演出结束，接取目录中的自动任务。锚点索引已由先注册的锚点系统建立。</summary>
        public void AfterNew()
        {
            Core.Event.AddListener<CutsceneFinishedEvent>(OnCutsceneFinished);
            foreach (QuestDefinition quest in catalog.AutoAcceptQuests) Accept(quest);
        }

        /// <summary>播放排队的演出，并在没有演出播放时判定区域触发器与到达目标。</summary>
        /// <param name="dt">帧时间（秒）。</param>
        public void OnUpdate(float dt)
        {
            if (cutsceneSystem.IsPlaying) return;
            if (pendingCutscenes.Count > 0)
            {
                cutsceneSystem.Play(pendingCutscenes.Dequeue());
                return;
            }
            // 没有上场角色（例如全队倒下）是合法状态，此时不做区域判定。
            Entity player = teamSystem.ActiveMember;
            if (player == null) return;
            Vector3 position = player.bindGo.transform.position;
            for (int questIndex = 0; questIndex < activeQuests.Count; questIndex++)
            {
                if (EvaluateAreas(activeQuests[questIndex], position)) return;
            }
            AdvanceCompletedNodes();
        }

        /// <summary>
        /// 退订事件并销毁仍存在的交互点。进程退出时锚点所在的场景对象可能已被 Unity 销毁，
        /// 交互点随之一起消失，因此销毁前先判断对象是否仍然存在（ARCH-LIFE-004）。
        /// </summary>
        public void Dispose()
        {
            Core.Event.RemoveListener<CutsceneFinishedEvent>(OnCutsceneFinished);
            foreach (List<QuestInteractable> list in interactables.Values)
            {
                foreach (QuestInteractable interactable in list)
                {
                    if (interactable != null) UnityEngine.Object.Destroy(interactable.gameObject);
                }
            }
            interactables.Clear();
            activeQuests.Clear();
            pendingCutscenes.Clear();
            catalog = null;
        }

        /// <summary>接取任务并进入第一个节点。</summary>
        /// <param name="quest">任务配置。</param>
        private void Accept(QuestDefinition quest)
        {
            List<string> errors = new List<string>();
            quest.Validate(errors);
            if (errors.Count > 0) throw new InvalidOperationException($"任务 '{quest.name}' 配置错误：\n{string.Join("\n", errors)}");
            QuestProgress progress = new QuestProgress(quest);
            activeQuests.Add(progress);
            interactables.Add(progress, new List<QuestInteractable>());
            EnterNode(progress, 0);
        }

        /// <summary>
        /// 进入节点：重置节点状态，启动 OnEnter 演出，生成交互点，校验区域引用的锚点存在，并广播节点变化。
        /// 区域触发器与到达目标不需要生成任何对象，下一帧起由 <see cref="OnUpdate"/> 判定。
        /// </summary>
        private void EnterNode(QuestProgress progress, int nodeIndex)
        {
            progress.EnterNode(nodeIndex);
            QuestNodeDef node = progress.CurrentNode;
            string owner = $"任务 '{progress.Definition.name}' 节点 {nodeIndex}";
            List<QuestInteractable> spawned = interactables[progress];
            foreach (QuestCutsceneTrigger trigger in node.Triggers)
            {
                switch (trigger)
                {
                    case OnEnterTrigger:
                        pendingCutscenes.Enqueue(trigger.Cutscene);
                        break;
                    case AreaTrigger areaTrigger:
                        AnchorPosition(areaTrigger.Area, owner);
                        break;
                    case InteractTrigger interactTrigger:
                        Transform anchor = anchorSystem.Require(interactTrigger.AnchorId, owner);
                        string name = $"QuestInteract_{progress.Definition.name}_{nodeIndex}";
                        spawned.Add(QuestInteractable.Spawn(anchor, interactTrigger.Offset, interactTrigger.Radius, NarrativeText.Get(interactTrigger.PromptTextId), () => OnInteract(interactTrigger), name));
                        break;
                    default:
                        throw new InvalidOperationException($"未知的任务触发器类型 {trigger.GetType().Name}。");
                }
            }
            foreach (QuestObjective objective in node.Objectives)
            {
                if (objective is ReachObjective reach) AnchorPosition(reach.Area, owner);
            }
            Debug.Log($"[任务] {progress.Definition.name} 进入节点 {nodeIndex}");
            Core.Event.Invoke(new QuestNodeChangedEvent(progress.Definition, nodeIndex));
        }

        /// <summary>
        /// 离开节点：销毁该节点生成的交互点，并把它们从玩家交互栏移除——对象销毁时 Unity 不会发出 OnTriggerExit。
        /// </summary>
        private void ExitNode(QuestProgress progress)
        {
            List<QuestInteractable> spawned = interactables[progress];
            InteractComponent component = null;
            teamSystem.ActiveMember?.TryGetComp(out component);
            foreach (QuestInteractable interactable in spawned)
            {
                component?.RemoveNearby(interactable);
                UnityEngine.Object.Destroy(interactable.gameObject);
            }
            spawned.Clear();
        }

        /// <summary>区域锚点的当前世界坐标；区域未挂锚点时为世界原点。锚点缺失直接抛出。</summary>
        private Vector3 AnchorPosition(QuestArea area, string owner)
        {
            return area.HasAnchor ? anchorSystem.Require(area.AnchorId, owner).position : Vector3.zero;
        }

        /// <summary>
        /// 判定一个任务当前节点的区域触发器与到达目标。
        /// </summary>
        /// <returns>本帧开始播放了演出时返回真，调用方应停止本帧的后续判定。</returns>
        private bool EvaluateAreas(QuestProgress progress, Vector3 position)
        {
            QuestNodeDef node = progress.CurrentNode;
            string owner = $"任务 '{progress.Definition.name}' 节点 {progress.NodeIndex}";
            for (int index = 0; index < node.Objectives.Count; index++)
            {
                if (!(node.Objectives[index] is ReachObjective reach) || progress.IsObjectiveDone(index)) continue;
                if (reach.Area.Contains(AnchorPosition(reach.Area, owner), position)) progress.CompleteObjective(index);
            }
            for (int index = 0; index < node.Triggers.Count; index++)
            {
                if (!(node.Triggers[index] is AreaTrigger areaTrigger) || progress.FiredAreaTriggers.Contains(index)) continue;
                if (!areaTrigger.Area.Contains(AnchorPosition(areaTrigger.Area, owner), position)) continue;
                progress.FiredAreaTriggers.Add(index);
                cutsceneSystem.Play(areaTrigger.Cutscene);
                return true;
            }
            return false;
        }

        /// <summary>演出结束：命中所有进行中任务当前节点里等待这段演出的目标，然后推进完成的节点。</summary>
        private void OnCutsceneFinished(CutsceneFinishedEvent eventData)
        {
            foreach (QuestProgress progress in activeQuests)
            {
                QuestNodeDef node = progress.CurrentNode;
                for (int index = 0; index < node.Objectives.Count; index++)
                {
                    if (node.Objectives[index] is CutsceneObjective objective && objective.Cutscene == eventData.Definition) progress.CompleteObjective(index);
                }
            }
            AdvanceCompletedNodes();
        }

        /// <summary>交互点被点击：演出播放中忽略（此时 HUD 已隐藏，只可能是同帧竞争），否则播放该触发器配置的演出。</summary>
        private void OnInteract(InteractTrigger trigger)
        {
            if (cutsceneSystem.IsPlaying) return;
            cutsceneSystem.Play(trigger.Cutscene);
        }

        /// <summary>把全部目标已完成的节点推进到下一个节点；最后一个节点完成时任务完成并移出进行中列表。</summary>
        private void AdvanceCompletedNodes()
        {
            for (int index = activeQuests.Count - 1; index >= 0; index--)
            {
                QuestProgress progress = activeQuests[index];
                if (!progress.IsNodeComplete) continue;
                ExitNode(progress);
                int next = progress.NodeIndex + 1;
                if (next < progress.Definition.Nodes.Count)
                {
                    EnterNode(progress, next);
                    continue;
                }
                activeQuests.RemoveAt(index);
                interactables.Remove(progress);
                Debug.Log($"[任务] {progress.Definition.name} 完成");
                Core.Event.Invoke(new QuestCompletedEvent(progress.Definition));
            }
        }
    }
}
