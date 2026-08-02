using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>声明一个可被 CameraDirectorSystem 跟随和观察的 Pawn 表现目标，并提供稳定的跟随与注视锚点。</summary>
    [DisallowMultipleComponent]
    public sealed class CameraSubject : MonoBehaviour
    {
        /// <summary>镜头位置跟随使用的锚点；为空时回退到当前 Transform。</summary>
        [SerializeField] private Transform followAnchor;

        /// <summary>镜头朝向计算使用的锚点；为空时回退到跟随锚点。</summary>
        [SerializeField] private Transform lookAtAnchor;

        /// <summary>获取镜头位置跟随锚点。</summary>
        public Transform FollowAnchor => followAnchor != null ? followAnchor : transform;

        /// <summary>获取镜头朝向观察锚点。</summary>
        public Transform LookAtAnchor => lookAtAnchor != null ? lookAtAnchor : FollowAnchor;

        /// <summary>获取当前 Subject 是否处于可被镜头使用的激活状态。</summary>
        public bool IsAvailable => isActiveAndEnabled && gameObject.activeInHierarchy;
    }
}
