using UnityEngine;
using Xuan.Prometheus.Ai;

namespace Xuan.Prometheus
{
    /// <summary>集中保存史莱姆 Prefab 独有的 AI 根定义。</summary>
    public sealed class SlimeBinder : CharacterBinder
    {
        [SerializeField] private EnemyAiDefinition enemyAiDefinition;

        /// <summary>获取史莱姆 AI 的只读决策图定义。</summary>
        public EnemyAiDefinition EnemyAiDefinition => enemyAiDefinition;

        /// <summary>在共享角色引用基础上校验史莱姆 AI 配置。</summary>
        public override void Validate()
        {
            base.Validate();
            if (EnemyAiDefinition == null) throw new System.InvalidOperationException($"SlimeBinder '{name}' requires EnemyAiDefinition.");
        }
    }
}
