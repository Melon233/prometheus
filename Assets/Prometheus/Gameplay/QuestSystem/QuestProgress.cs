using System.Collections.Generic;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一个进行中任务的进度：当前节点与该节点各目标的完成情况。
    /// 对外只读；只有 QuestSystem 能推进它。
    /// </summary>
    public sealed class QuestProgress
    {
        /// <summary>当前节点各目标是否完成，下标与 <see cref="QuestNodeDef.Objectives"/> 对应。</summary>
        private bool[] objectiveDone;

        /// <summary>当前节点中已经触发过的区域触发器下标；区域触发器每次节点激活只触发一次。</summary>
        internal readonly HashSet<int> FiredAreaTriggers = new HashSet<int>();

        /// <summary>创建任务进度，停在第一个节点之前；由 QuestSystem 随后进入第一个节点。</summary>
        /// <param name="definition">任务配置。</param>
        internal QuestProgress(QuestDefinition definition)
        {
            Definition = definition;
            NodeIndex = -1;
        }

        /// <summary>任务配置。</summary>
        public QuestDefinition Definition { get; }

        /// <summary>当前节点下标。</summary>
        public int NodeIndex { get; private set; }

        /// <summary>当前节点配置。</summary>
        public QuestNodeDef CurrentNode => Definition.Nodes[NodeIndex];

        /// <summary>当前节点的指定目标是否已完成。</summary>
        /// <param name="objectiveIndex">目标下标。</param>
        public bool IsObjectiveDone(int objectiveIndex) => objectiveDone[objectiveIndex];

        /// <summary>当前节点的全部目标是否都已完成。</summary>
        public bool IsNodeComplete
        {
            get
            {
                for (int index = 0; index < objectiveDone.Length; index++)
                {
                    if (!objectiveDone[index]) return false;
                }
                return true;
            }
        }

        /// <summary>进入指定节点并清空节点级状态。</summary>
        /// <param name="nodeIndex">节点下标。</param>
        internal void EnterNode(int nodeIndex)
        {
            NodeIndex = nodeIndex;
            objectiveDone = new bool[Definition.Nodes[nodeIndex].Objectives.Count];
            FiredAreaTriggers.Clear();
        }

        /// <summary>标记当前节点的一个目标完成。</summary>
        /// <param name="objectiveIndex">目标下标。</param>
        internal void CompleteObjective(int objectiveIndex) => objectiveDone[objectiveIndex] = true;
    }
}
