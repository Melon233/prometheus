using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务目录：列出会话建立时自动接取的任务。QuestSystem 在自己的 AfterNewAsync 中按地址加载它。
    /// 本类型独占一个文件：ScriptableObject 子类与文件名不一致时，资产会在域重载后反序列化成 null。
    /// </summary>
    [CreateAssetMenu(fileName = "QuestCatalog", menuName = "Prometheus/Narrative/Quest Catalog")]
    public sealed class QuestCatalog : ScriptableObject
    {
        [SerializeField] [Tooltip("会话建立时自动接取的任务（主线）。单机不存档，每次进入游戏都从第一个节点开始。")]
        private List<QuestDefinition> autoAcceptQuests = new List<QuestDefinition>();

        /// <summary>会话建立时自动接取的任务。</summary>
        public IReadOnlyList<QuestDefinition> AutoAcceptQuests => autoAcceptQuests;
    }
}
