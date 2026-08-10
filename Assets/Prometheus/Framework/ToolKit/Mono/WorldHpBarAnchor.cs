using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 保存实体血条的运行时跟随配置，使角色 Prefab 只保留锚点数据而不再内嵌 Canvas 和血条节点。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldHpBarAnchor : MonoBehaviour
    {
        /// <summary>可选的实际跟随节点；未指定时跟随当前实体根节点。</summary>
        [SerializeField] private Transform followTarget;

        /// <summary>叠加到跟随节点世界坐标上的血条偏移。</summary>
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2f, 0f);

        /// <summary>当前实体类型使用的主血条颜色。</summary>
        [SerializeField] private Color hpColor = new Color(0.6981132f, 0.2540304f, 0.2540304f, 1f);

        /// <summary>当前实体类型使用的受伤缓冲血条颜色。</summary>
        [SerializeField] private Color chaserColor = new Color(0.735849f, 0.4343691f, 0.4343691f, 1f);

        /// <summary>获取有效的运行时跟随节点。</summary>
        public Transform FollowTarget => followTarget != null ? followTarget : transform;

        /// <summary>获取血条相对跟随节点的世界坐标偏移。</summary>
        public Vector3 WorldOffset => worldOffset;

        /// <summary>获取主血条颜色。</summary>
        public Color HpColor => hpColor;

        /// <summary>获取受伤缓冲血条颜色。</summary>
        public Color ChaserColor => chaserColor;
    }
}
