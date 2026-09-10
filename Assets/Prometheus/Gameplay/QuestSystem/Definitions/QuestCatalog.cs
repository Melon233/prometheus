using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>任务配置目录；由组合根批量注册到任务系统。</summary>
    [CreateAssetMenu(fileName = "QuestCatalog", menuName = "Prometheus/Quest/Quest Catalog")]
    public sealed class QuestCatalog : ScriptableObject
    {
        [SerializeField] [Tooltip("本目录包含的全部任务配置。")]
        private List<QuestDefinition> definitions = new List<QuestDefinition>();

        /// <summary>获取目录中的任务定义。</summary>
        public IReadOnlyList<QuestDefinition> Definitions => definitions;

        /// <summary>追加一个任务定义；供编辑器工具与测试使用。</summary>
        public void Add(QuestDefinition definition)
        {
            definitions.Add(definition ?? throw new ArgumentNullException(nameof(definition)));
        }
    }
}
