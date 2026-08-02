using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Ai
{
    /// <summary>
    /// 将敌人 AI 根定义和场景移动适配器绑定到预制体；所有可变决策数据由 EnemyAiBrain 持有而不会写回本组件或资产。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class EnemyAiComponent : MonoComponent
    {
        [SerializeField] private EnemyAiDefinition definition;
        [SerializeField] private CharacterController characterController;

        /// <summary>获取预制体引用的只读 AI 根定义。</summary>
        public EnemyAiDefinition Definition => definition;

        /// <summary>获取用于实际位移的 CharacterController，并在旧预制体未显式赋值时自动从同一对象获取。</summary>
        public CharacterController CharacterController => characterController != null ? characterController : characterController = GetComponent<CharacterController>();

        /// <summary>
        /// 在选中预制体实例时显示资产配置的感知、攻击、追击和巡逻范围，不参与正式运行时决策。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (definition == null) return;
            Vector3 center = transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, definition.PerceptionRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(center, definition.AttackRadius);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(center, definition.ChaseRadius);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(center, definition.PatrolRadius);
        }

        /// <summary>
        /// 在编辑预制体时自动补齐同对象 CharacterController 引用，减少资产漏配。
        /// </summary>
        private void OnValidate()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
        }
    }
}
