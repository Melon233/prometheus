using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一个任务的配置：若干线性节点，当前节点的全部目标完成后进入下一个节点，最后一个节点完成即任务完成。
    /// 名称与描述是 TbText 的 id。
    /// 本类型独占一个文件：ScriptableObject 子类与文件名不一致时，资产会在域重载后反序列化成 null。
    /// </summary>
    [CreateAssetMenu(fileName = "Quest", menuName = "Prometheus/Narrative/Quest")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [SerializeField] [Tooltip("任务类型。")]
        private QuestType questType = QuestType.Main;

        [SerializeField] [Tooltip("任务名文本 id。")]
        private int nameTextId;

        [SerializeField] [Tooltip("任务描述文本 id。")]
        private int descTextId;

        [SerializeField] [Tooltip("线性节点。")]
        private List<QuestNodeDef> nodes = new List<QuestNodeDef>();

        /// <summary>任务类型。</summary>
        public QuestType QuestType => questType;

        /// <summary>任务名文本 id。</summary>
        public int NameTextId => nameTextId;

        /// <summary>任务描述文本 id。</summary>
        public int DescTextId => descTextId;

        /// <summary>线性节点。</summary>
        public IReadOnlyList<QuestNodeDef> Nodes => nodes;

        /// <summary>收集本任务引用的全部世界锚点 id（区域锚点与交互挂载锚点），供编辑期与场景核对。</summary>
        public List<string> CollectAnchorIds()
        {
            List<string> ids = new List<string>();
            foreach (QuestNodeDef node in nodes)
            {
                foreach (QuestCutsceneTrigger trigger in node.Triggers)
                {
                    if (trigger is AreaTrigger area && area.Area.HasAnchor) ids.Add(area.Area.AnchorId);
                    else if (trigger is InteractTrigger interact && !string.IsNullOrEmpty(interact.AnchorId)) ids.Add(interact.AnchorId);
                }
                foreach (QuestObjective objective in node.Objectives)
                {
                    if (objective is ReachObjective reach && reach.Area.HasAnchor) ids.Add(reach.Area.AnchorId);
                }
            }
            return ids;
        }

        /// <summary>
        /// 校验资产结构：至少一个节点、每个节点至少一个目标、触发器与目标参数齐全。
        /// 引用的锚点是否存在于场景，由 QuestSystem 在激活节点时检查，编辑期可用菜单「校验场景锚点」核对。
        /// </summary>
        /// <param name="errors">收集到的错误描述；为空表示通过。</param>
        public void Validate(List<string> errors)
        {
            if (nameTextId == NarrativeText.None) errors.Add("未配置任务名文本 id。");
            if (nodes.Count == 0) errors.Add("至少需要一个节点。");
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                QuestNodeDef node = nodes[nodeIndex];
                if (node.Objectives.Count == 0) errors.Add($"节点 {nodeIndex} 没有目标，永远无法完成。");
                for (int index = 0; index < node.Triggers.Count; index++)
                {
                    if (node.Triggers[index] == null) errors.Add($"节点 {nodeIndex} 的触发器 {index} 为空。");
                    else node.Triggers[index].Validate($"节点 {nodeIndex} / 触发器 {index}", errors);
                }
                for (int index = 0; index < node.Objectives.Count; index++)
                {
                    if (node.Objectives[index] == null) errors.Add($"节点 {nodeIndex} 的目标 {index} 为空。");
                    else node.Objectives[index].Validate($"节点 {nodeIndex} / 目标 {index}", errors);
                }
            }
        }
    }
}
