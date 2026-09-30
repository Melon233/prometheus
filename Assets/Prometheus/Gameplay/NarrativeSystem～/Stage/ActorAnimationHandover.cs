using System;
using Spine.Unity;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 演出期间对角色骨架驱动权的接管与归还。
    /// <para>
    /// 之所以必须接管：<see cref="SkeletonAnimation"/> 自身的 AnimationState 会在每帧写骨架姿态，
    /// 与剧情侧的按时刻采样争抢同一副骨架。接管做两件事——停掉组件自身的推进、清空已排入的动画轨道，
    /// 退出时再交还一副干净的 Setup Pose。
    /// </para>
    /// <para>
    /// 本类型只依赖 spine-unity 的公开 API，<b>不引用</b>项目战斗动画模块的任何类型
    /// （AnimationLine / AnimationLibrary / AnimationPlayback / SpineComponent）。
    /// 战斗侧那套是实体框架内的纯 C# 组件，本就无法也不应从 GameObject 上取到；
    /// 演出结束后由玩法侧自行重新驱动动画即可。
    /// </para>
    /// </summary>
    public sealed class ActorAnimationHandover : IDisposable
    {
        private readonly SkeletonAnimation skeleton;
        private readonly bool previousEnabled;
        private bool released;

        /// <summary>接管一个角色的骨架驱动权；角色没有 Spine 骨骼时该接管为空操作。</summary>
        /// <param name="handle">已解析的角色句柄。</param>
        public ActorAnimationHandover(ActorHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            skeleton = handle.Skeleton;
            if (skeleton == null) return;
            previousEnabled = skeleton.enabled;
            // 停掉组件自身的 AnimationState 推进，之后由剧情侧直接写 Skeleton 姿态。
            skeleton.enabled = false;
            if (skeleton.AnimationState != null) skeleton.AnimationState.ClearTracks();
        }

        /// <summary>把骨架恢复到 Setup Pose 并归还驱动权；重复释放保持幂等。</summary>
        public void Dispose()
        {
            if (released) return;
            released = true;
            if (skeleton == null) return;
            // 交还一副干净的初始姿态，避免残留剧情最后一帧的姿势污染后续动画的混合起点。
            if (skeleton.AnimationState != null) skeleton.AnimationState.ClearTracks();
            if (skeleton.Skeleton != null) skeleton.Skeleton.SetToSetupPose();
            skeleton.enabled = previousEnabled;
        }
    }
}
