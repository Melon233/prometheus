using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 场景中的命名锚点：给 NPC、地标、广场中心这类「世界内容」一个稳定的 id，供演出与任务按 id 引用。
    ///
    /// 锚点属于关卡而不属于任何一个任务：同一个 NPC 只挂一个锚点，任意多个任务与演出都可以引用它；
    /// 任务专属的触发区域与交互点写在任务资产里、运行时生成，场景中不再摆放任务专属组件。
    /// id 必须在场景内唯一，由 <see cref="IWorldAnchorSystem"/> 在会话建立时建立索引。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldAnchor : MonoBehaviour
    {
        [SerializeField] [Tooltip("场景内唯一的锚点 id，例如 npc_barbarossa、square_center。")]
        private string anchorId;

        /// <summary>场景内唯一的锚点 id。</summary>
        public string AnchorId => anchorId;

        /// <summary>在场景视图中画出锚点位置、朝向与 id，便于摆放与对照任务配置。</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.3f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, anchorId);
#endif
        }
    }
}
