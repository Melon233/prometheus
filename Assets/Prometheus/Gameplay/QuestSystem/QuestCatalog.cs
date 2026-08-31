using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>任务配置目录；由启动流程或测试工具批量注册到 QuestSystem。</summary>
    [CreateAssetMenu(fileName = "QuestCatalog", menuName = "Prometheus/Quest/Quest Catalog")]
    public sealed class QuestCatalog : ScriptableObject
    {
        [SerializeField] private List<QuestDefinition> definitions = new List<QuestDefinition>();

        /// <summary>获取目录中的任务定义。</summary>
        public IReadOnlyList<QuestDefinition> Definitions => definitions;

        /// <summary>校验目录中的空引用和重复任务 ID。</summary>
        public void Validate()
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (QuestDefinition definition in definitions)
            {
                if (definition == null) throw new InvalidOperationException("QuestCatalog contains a null definition.");
                definition.Validate();
                if (!ids.Add(definition.QuestId)) throw new InvalidOperationException($"QuestCatalog contains duplicate quest '{definition.QuestId}'.");
            }
        }
    }
}
