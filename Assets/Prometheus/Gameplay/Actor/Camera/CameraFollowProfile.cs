using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>保存基础跟随镜头的局部偏移、注视偏移、阻尼和视场角；资产在运行时只读并可被多个请求共享。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Actor/Camera Follow Profile", fileName = "CameraFollowProfile")]
    public sealed class CameraFollowProfile : ScriptableObject
    {
        /// <summary>相对 FollowAnchor 的镜头局部位置。</summary>
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 3.488f, -4.074f);

        /// <summary>相对 LookAtAnchor 的局部注视偏移。</summary>
        [SerializeField] private Vector3 localLookAtOffset = new Vector3(0f, 1f, 0f);

        /// <summary>位置指数阻尼；零表示立即到达目标位置。</summary>
        [SerializeField, Min(0f)] private float positionDamping = 10f;

        /// <summary>旋转指数阻尼；零表示立即朝向目标。</summary>
        [SerializeField, Min(0f)] private float rotationDamping = 12f;

        /// <summary>视场角指数阻尼；零表示立即应用目标视场角。</summary>
        [SerializeField, Min(0f)] private float fieldOfViewDamping = 10f;

        /// <summary>透视相机使用的目标垂直视场角。</summary>
        [SerializeField, Range(1f, 179f)] private float fieldOfView = 60f;

        /// <summary>获取相对 FollowAnchor 的镜头局部位置。</summary>
        public Vector3 LocalOffset => localOffset;

        /// <summary>获取相对 LookAtAnchor 的局部注视偏移。</summary>
        public Vector3 LocalLookAtOffset => localLookAtOffset;

        /// <summary>获取位置指数阻尼。</summary>
        public float PositionDamping => positionDamping;

        /// <summary>获取旋转指数阻尼。</summary>
        public float RotationDamping => rotationDamping;

        /// <summary>获取视场角指数阻尼。</summary>
        public float FieldOfViewDamping => fieldOfViewDamping;

        /// <summary>获取透视相机目标垂直视场角。</summary>
        public float FieldOfView => fieldOfView;

        /// <summary>在 Inspector 修改资产时约束全部阻尼和镜头参数到合法范围。</summary>
        private void OnValidate()
        {
            positionDamping = Mathf.Max(0f, positionDamping);
            rotationDamping = Mathf.Max(0f, rotationDamping);
            fieldOfViewDamping = Mathf.Max(0f, fieldOfViewDamping);
            fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
        }
    }
}
