using System;

namespace Xuan.Prometheus
{
    /// <summary>表示 FilmSystem 对一台 Cinemachine 演出镜头优先级的独占租约，释放后 CameraSystem 会恢复镜头原优先级。</summary>
    public sealed class FilmCameraLease : IDisposable
    {
        private CameraSystem owner;

        /// <summary>由 CameraSystem 创建与管理一份演出镜头租约。</summary>
        internal FilmCameraLease(CameraSystem owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>获取当前租约是否已经释放或随 CameraSystem 一起失效。</summary>
        public bool IsReleased => owner == null;

        /// <summary>归还演出镜头控制权并恢复申请前的镜头优先级；重复释放保持幂等。</summary>
        public void Dispose()
        {
            CameraSystem currentOwner = owner;
            if (currentOwner == null) return;
            owner = null;
            currentOwner.ReleaseFilmCamera(this);
        }

        /// <summary>由 CameraSystem 在自身销毁时断开租约，避免外部句柄继续回调已释放系统。</summary>
        internal void Invalidate()
        {
            owner = null;
        }
    }
}
