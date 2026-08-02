using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>描述一个可抢占基础镜头目标的有优先级请求。</summary>
    public readonly struct CameraRequest
    {
        /// <summary>创建一个镜头请求。</summary>
        /// <param name="ownerId">请求所有者的稳定运行时编号，用于批量清理行为或 Pawn 的全部请求。</param>
        /// <param name="subject">请求观察的镜头目标。</param>
        /// <param name="profile">请求使用的只读跟随配置。</param>
        /// <param name="priority">请求优先级；数值更大者优先，相同优先级由更早请求者获胜。</param>
        /// <param name="snapOnActivate">请求首次成为获胜者时是否立即应用目标镜头姿态。</param>
        public CameraRequest(long ownerId, CameraSubject subject, CameraFollowProfile profile, int priority, bool snapOnActivate = false)
        {
            OwnerId = ownerId;
            Subject = subject;
            Profile = profile;
            Priority = priority;
            SnapOnActivate = snapOnActivate;
        }

        /// <summary>获取请求所有者编号。</summary>
        public long OwnerId { get; }

        /// <summary>获取请求观察的镜头目标。</summary>
        public CameraSubject Subject { get; }

        /// <summary>获取请求使用的跟随配置。</summary>
        public CameraFollowProfile Profile { get; }

        /// <summary>获取请求仲裁优先级。</summary>
        public int Priority { get; }

        /// <summary>获取该请求首次激活时是否立即应用目标姿态。</summary>
        public bool SnapOnActivate { get; }
    }

    /// <summary>标识 CameraDirectorSystem 创建的一份镜头请求；句柄只对创建它的系统实例有效。</summary>
    public readonly struct CameraRequestHandle : IEquatable<CameraRequestHandle>
    {
        /// <summary>创建一个镜头请求句柄；仅允许 CameraDirectorSystem 调用。</summary>
        internal CameraRequestHandle(long systemToken, long requestId)
        {
            SystemToken = systemToken;
            RequestId = requestId;
        }

        /// <summary>获取句柄是否包含一个有效请求编号。</summary>
        public bool IsValid => SystemToken > 0 && RequestId > 0;

        /// <summary>获取创建请求的系统实例标识。</summary>
        internal long SystemToken { get; }

        /// <summary>获取系统实例内唯一请求编号。</summary>
        internal long RequestId { get; }

        /// <inheritdoc />
        public bool Equals(CameraRequestHandle other)
        {
            return SystemToken == other.SystemToken && RequestId == other.RequestId;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is CameraRequestHandle other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (SystemToken.GetHashCode() * 397) ^ RequestId.GetHashCode();
            }
        }

        /// <summary>比较两个句柄是否标识同一份镜头请求。</summary>
        /// <param name="left">左侧镜头请求句柄。</param>
        /// <param name="right">右侧镜头请求句柄。</param>
        /// <returns>两个句柄来自同一系统实例并指向同一请求时返回 true。</returns>
        public static bool operator ==(CameraRequestHandle left, CameraRequestHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>比较两个句柄是否标识不同镜头请求。</summary>
        /// <param name="left">左侧镜头请求句柄。</param>
        /// <param name="right">右侧镜头请求句柄。</param>
        /// <returns>两个句柄不指向同一份镜头请求时返回 true。</returns>
        public static bool operator !=(CameraRequestHandle left, CameraRequestHandle right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>管理单局主镜头、基础跟随目标和可抢占请求，并按照稳定优先级计算最终镜头姿态。</summary>
    public sealed class CameraDirectorSystem : XSystem
    {
        /// <summary>为每个 CameraDirectorSystem 实例分配跨实例唯一标识。</summary>
        private static long nextSystemToken;

        /// <summary>保存全部仍然有效的可抢占镜头请求。</summary>
        private readonly Dictionary<long, CameraRequestEntry> requests = new Dictionary<long, CameraRequestEntry>();

        /// <summary>为按所有者批量移除请求复用编号缓冲。</summary>
        private readonly List<long> requestRemovalBuffer = new List<long>();

        /// <summary>当前系统实例的跨实例唯一标识。</summary>
        private readonly long systemToken = Interlocked.Increment(ref nextSystemToken);

        /// <summary>下一个系统内镜头请求编号。</summary>
        private long nextRequestId = 1;

        /// <summary>下一个稳定申请序号；相同优先级时更小序号获胜。</summary>
        private long nextRequestSequence = 1;

        /// <summary>系统实际驱动的 Unity Camera。</summary>
        private Camera outputCamera;

        /// <summary>由当前系统拥有并负责销毁的独立客户端 CameraRig；直接 BindCamera 时为空。</summary>
        private CameraRigRuntime ownedCameraRig;

        /// <summary>没有高优先级请求时使用的基础镜头目标。</summary>
        private CameraSubject baseSubject;

        /// <summary>没有高优先级请求时使用的基础跟随配置。</summary>
        private CameraFollowProfile baseProfile;

        /// <summary>记录基础目标下次激活时是否需要立即应用姿态。</summary>
        private bool baseSnapPending;

        /// <summary>记录上一帧获胜请求编号；零表示基础目标。</summary>
        private long activeRequestId;

        /// <summary>记录系统是否已经释放。</summary>
        private bool disposed;

        /// <summary>创建一个尚未绑定 Unity Camera 的镜头系统。</summary>
        public CameraDirectorSystem()
        {
        }

        /// <summary>创建一个已经绑定 Unity Camera 的镜头系统。</summary>
        /// <param name="camera">需要由系统独占驱动的 Unity Camera。</param>
        public CameraDirectorSystem(Camera camera)
        {
            BindCamera(camera);
        }

        /// <summary>获取当前系统绑定的 Unity Camera；尚未绑定时为空。</summary>
        public Camera OutputCamera => outputCamera;

        /// <summary>获取当前系统拥有的独立客户端 CameraRig 标记；使用借用相机或尚未接管时为空。</summary>
        public CameraRigComponent CameraRig => ownedCameraRig == null ? null : ownedCameraRig.Component;

        /// <summary>获取当前实际生效的镜头目标；没有合法目标时为空。</summary>
        public CameraSubject ActiveSubject
        {
            get
            {
                CameraRequestEntry winner = FindWinningRequest();
                return winner == null ? IsUsable(baseSubject, baseProfile) ? baseSubject : null : winner.Request.Subject;
            }
        }

        /// <summary>绑定系统独占驱动的 Unity Camera；重复传入同一对象保持幂等。</summary>
        /// <param name="camera">需要驱动的有效 Unity Camera。</param>
        public void BindCamera(Camera camera)
        {
            ThrowIfDisposed();
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (outputCamera == camera) return;
            DisposeOwnedCameraRig();
            outputCamera = camera;
            baseSnapPending = true;
            activeRequestId = 0;
        }

        /// <summary>把 Prefab 中的来源相机提升为 Pawn 生命周期之外的独立 CameraRig，并由当前系统独占管理其销毁与音频监听器。</summary>
        /// <param name="sourceCamera">Prefab 提供的来源相机。</param>
        /// <param name="runtimeRoot">承载独立 CameraRig 的常驻运行时根节点。</param>
        /// <param name="sourceOwnerRoot">来源 Pawn 根节点；相机与 Pawn 共用对象时将改为复制相机而不是提升 Pawn。</param>
        /// <returns>独立 CameraRig 的标记组件。</returns>
        public CameraRigComponent AdoptCameraRig(Camera sourceCamera, Transform runtimeRoot, Transform sourceOwnerRoot = null)
        {
            ThrowIfDisposed();
            if (sourceCamera == null) throw new ArgumentNullException(nameof(sourceCamera));
            if (runtimeRoot == null) throw new ArgumentNullException(nameof(runtimeRoot));
            if (ownedCameraRig != null && ownedCameraRig.OutputCamera == sourceCamera)
            {
                ownedCameraRig.EnsureExclusiveAudioListener();
                outputCamera = sourceCamera;
                baseSnapPending = true;
                activeRequestId = 0;
                return ownedCameraRig.Component;
            }
            DisposeOwnedCameraRig();
            ownedCameraRig = CameraRigRuntime.Adopt(sourceCamera, runtimeRoot, sourceOwnerRoot);
            outputCamera = ownedCameraRig.OutputCamera;
            baseSnapPending = true;
            activeRequestId = 0;
            return ownedCameraRig.Component;
        }

        /// <summary>解除当前 Unity Camera 绑定，但保留基础目标和待处理请求。</summary>
        public void UnbindCamera()
        {
            ThrowIfDisposed();
            outputCamera = null;
            activeRequestId = 0;
        }

        /// <summary>设置没有可抢占请求时使用的基础镜头目标。</summary>
        /// <param name="subject">基础镜头观察目标。</param>
        /// <param name="profile">基础镜头跟随配置。</param>
        /// <param name="snapOnNextTick">下一次镜头推进时是否立即应用目标姿态。</param>
        public void SetBaseTarget(CameraSubject subject, CameraFollowProfile profile, bool snapOnNextTick = true)
        {
            ThrowIfDisposed();
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            baseSubject = subject;
            baseProfile = profile;
            baseSnapPending = snapOnNextTick;
        }

        /// <summary>清除基础镜头目标；可抢占请求仍会继续参与仲裁。</summary>
        public void ClearBaseTarget()
        {
            ThrowIfDisposed();
            baseSubject = null;
            baseProfile = null;
            baseSnapPending = false;
            if (activeRequestId == 0) activeRequestId = -1;
        }

        /// <summary>提交一个有优先级的可抢占镜头请求。</summary>
        /// <param name="request">请求所有者、目标、配置和优先级。</param>
        /// <returns>只对当前 CameraDirectorSystem 有效的请求句柄。</returns>
        public CameraRequestHandle PushRequest(CameraRequest request)
        {
            ThrowIfDisposed();
            ValidateRequest(request);
            long requestId = nextRequestId++;
            requests.Add(requestId, new CameraRequestEntry(requestId, nextRequestSequence++, request));
            return new CameraRequestHandle(systemToken, requestId);
        }

        /// <summary>释放一个镜头请求；空句柄、其他系统句柄和重复释放都安全返回 false。</summary>
        /// <param name="handle">PushRequest 返回的请求句柄。</param>
        /// <returns>实际释放请求时返回 true，否则返回 false。</returns>
        public bool ReleaseRequest(CameraRequestHandle handle)
        {
            ThrowIfDisposed();
            if (!handle.IsValid || handle.SystemToken != systemToken) return false;
            return requests.Remove(handle.RequestId);
        }

        /// <summary>释放指定所有者提交的全部镜头请求。</summary>
        /// <param name="ownerId">需要清理的请求所有者编号。</param>
        /// <returns>实际释放的请求数量。</returns>
        public int ReleaseOwnedBy(long ownerId)
        {
            ThrowIfDisposed();
            requestRemovalBuffer.Clear();
            foreach (KeyValuePair<long, CameraRequestEntry> pair in requests)
            {
                if (pair.Value.Request.OwnerId == ownerId) requestRemovalBuffer.Add(pair.Key);
            }
            for (int index = 0; index < requestRemovalBuffer.Count; index++) requests.Remove(requestRemovalBuffer[index]);
            int removedCount = requestRemovalBuffer.Count;
            requestRemovalBuffer.Clear();
            return removedCount;
        }

        /// <summary>在角色姿态更新完成后推进一次镜头；未来接入正式 LateUpdate 生命周期时应直接调用本方法。</summary>
        /// <param name="dt">当前帧的非负增量时间。</param>
        public void LateTick(float dt)
        {
            ThrowIfDisposed();
            if (outputCamera == null) return;
            CameraRequestEntry winner = FindWinningRequest();
            CameraSubject subject = winner == null ? baseSubject : winner.Request.Subject;
            CameraFollowProfile profile = winner == null ? baseProfile : winner.Request.Profile;
            if (!IsUsable(subject, profile)) return;
            long nextActiveRequestId = winner == null ? 0 : winner.RequestId;
            bool snap = winner == null ? baseSnapPending : winner.Request.SnapOnActivate && activeRequestId != nextActiveRequestId;
            ApplyCamera(subject, profile, Mathf.Max(0f, dt), snap);
            activeRequestId = nextActiveRequestId;
            if (winner == null) baseSnapPending = false;
        }

        /// <inheritdoc />
        public override void OnLateUpdate(float dt)
        {
            if (disposed) return;
            LateTick(dt);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (disposed) return;
            disposed = true;
            requests.Clear();
            requestRemovalBuffer.Clear();
            DisposeOwnedCameraRig();
            outputCamera = null;
            baseSubject = null;
            baseProfile = null;
            baseSnapPending = false;
            activeRequestId = 0;
        }

        /// <summary>释放当前系统拥有的 CameraRig；借用相机不会被销毁。</summary>
        private void DisposeOwnedCameraRig()
        {
            if (ownedCameraRig == null) return;
            CameraRigRuntime cameraRig = ownedCameraRig;
            ownedCameraRig = null;
            if (outputCamera == cameraRig.OutputCamera) outputCamera = null;
            cameraRig.Dispose();
        }

        /// <summary>选择优先级最高且目标仍有效的请求，相同优先级由更早申请者稳定获胜。</summary>
        private CameraRequestEntry FindWinningRequest()
        {
            CameraRequestEntry winner = null;
            foreach (CameraRequestEntry candidate in requests.Values)
            {
                if (!IsUsable(candidate.Request.Subject, candidate.Request.Profile)) continue;
                if (winner == null || candidate.Request.Priority > winner.Request.Priority || candidate.Request.Priority == winner.Request.Priority && candidate.Sequence < winner.Sequence) winner = candidate;
            }
            return winner;
        }

        /// <summary>按照配置计算目标位置、朝向和视场角，并使用指数阻尼或立即应用。</summary>
        private void ApplyCamera(CameraSubject subject, CameraFollowProfile profile, float dt, bool snap)
        {
            Transform followAnchor = subject.FollowAnchor;
            Transform lookAtAnchor = subject.LookAtAnchor;
            Vector3 targetPosition = followAnchor.TransformPoint(profile.LocalOffset);
            Vector3 targetLookAt = lookAtAnchor.TransformPoint(profile.LocalLookAtOffset);
            Vector3 lookDirection = targetLookAt - targetPosition;
            Quaternion targetRotation = lookDirection.sqrMagnitude <= 0.000001f ? outputCamera.transform.rotation : Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            float positionFactor = snap ? 1f : CalculateDampingFactor(profile.PositionDamping, dt);
            float rotationFactor = snap ? 1f : CalculateDampingFactor(profile.RotationDamping, dt);
            float fieldOfViewFactor = snap ? 1f : CalculateDampingFactor(profile.FieldOfViewDamping, dt);
            outputCamera.transform.position = Vector3.Lerp(outputCamera.transform.position, targetPosition, positionFactor);
            outputCamera.transform.rotation = Quaternion.Slerp(outputCamera.transform.rotation, targetRotation, rotationFactor);
            if (!outputCamera.orthographic) outputCamera.fieldOfView = Mathf.Lerp(outputCamera.fieldOfView, profile.FieldOfView, fieldOfViewFactor);
        }

        /// <summary>将非负阻尼和帧时间转换成与帧率无关的指数插值比例。</summary>
        private static float CalculateDampingFactor(float damping, float dt)
        {
            if (damping <= 0f) return 1f;
            if (dt <= 0f) return 0f;
            return 1f - Mathf.Exp(-damping * dt);
        }

        /// <summary>判断目标和配置是否都存在且目标当前允许被镜头使用。</summary>
        private static bool IsUsable(CameraSubject subject, CameraFollowProfile profile)
        {
            return subject != null && profile != null && subject.IsAvailable;
        }

        /// <summary>验证请求包含合法所有者、目标和配置。</summary>
        private static void ValidateRequest(CameraRequest request)
        {
            if (request.OwnerId <= 0) throw new ArgumentOutOfRangeException(nameof(request), request.OwnerId, "Camera request owner ID must be positive.");
            if (request.Subject == null) throw new ArgumentException("Camera request requires a subject.", nameof(request));
            if (request.Profile == null) throw new ArgumentException("Camera request requires a follow profile.", nameof(request));
        }

        /// <summary>阻止已经释放的系统继续接收镜头绑定、请求或推进调用。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CameraDirectorSystem));
        }

        /// <summary>保存一份镜头请求的不可变仲裁数据。</summary>
        private sealed class CameraRequestEntry
        {
            /// <summary>创建一份不可变镜头请求记录。</summary>
            internal CameraRequestEntry(long requestId, long sequence, CameraRequest request)
            {
                RequestId = requestId;
                Sequence = sequence;
                Request = request;
            }

            /// <summary>获取系统内唯一请求编号。</summary>
            internal long RequestId { get; }

            /// <summary>获取相同优先级稳定决胜使用的申请序号。</summary>
            internal long Sequence { get; }

            /// <summary>获取外部提交的只读镜头请求。</summary>
            internal CameraRequest Request { get; }
        }
    }
}
