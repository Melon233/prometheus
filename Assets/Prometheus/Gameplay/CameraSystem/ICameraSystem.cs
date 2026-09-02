using Unity.Cinemachine;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>定义玩法输出镜头、跟随镜头和演出镜头租约的公共入口。</summary>
    public interface ICameraSystem : ISystemContract
    {
        /// <summary>获取当前玩法输出相机。</summary>
        Camera OutputCamera { get; }

        /// <summary>获取当前玩法跟随相机。</summary>
        CinemachineCamera FollowCamera { get; }

        /// <summary>申请一份演出镜头优先级租约。</summary>
        FilmCameraLease AcquireFilmCamera(CinemachineCamera filmCamera, int priority);
    }
}
