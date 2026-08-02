using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>定义能够为一个或多个 Pawn 产生控制帧的运行时控制器。</summary>
    public interface IControllerRuntime : IDisposable
    {
        /// <summary>获取当前单局内唯一的控制器编号。</summary>
        int ControllerId { get; }

        /// <summary>采样一次控制输入；PossessionSystem 保证同一帧对同一控制器最多调用一次。</summary>
        /// <param name="context">本次采样使用的只读帧上下文。</param>
        /// <returns>控制器产生的完整控制帧。</returns>
        ControlFrame Sample(ControllerSampleContext context);
    }

    /// <summary>使用当前项目 Legacy UnityEngine.Input 映射产生控制帧，作为新控制架构接入旧输入配置的临时适配器。</summary>
    public sealed class LegacyPlayerControllerRuntime : IControllerRuntime
    {
        /// <summary>记录该控制器是否已经释放，防止释放后继续读取全局输入。</summary>
        private bool disposed;

        /// <summary>创建一个 Legacy 本地玩家控制器。</summary>
        /// <param name="controllerId">当前单局内唯一的正控制器编号。</param>
        public LegacyPlayerControllerRuntime(int controllerId)
        {
            if (controllerId <= 0) throw new ArgumentOutOfRangeException(nameof(controllerId), controllerId, "Controller ID must be positive.");
            ControllerId = controllerId;
        }

        /// <inheritdoc />
        public int ControllerId { get; }

        /// <inheritdoc />
        public ControlFrame Sample(ControllerSampleContext context)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LegacyPlayerControllerRuntime));
            Vector2 move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            ControlButton pressed = ControlButton.None;
            ControlButton held = ControlButton.None;
            if (Input.GetKeyDown(KeyCode.Space)) pressed |= ControlButton.Jump;
            if (Input.GetMouseButtonDown(0)) pressed |= ControlButton.Attack;
            if (Input.GetKeyDown(KeyCode.E)) pressed |= ControlButton.Skill;
            if (Input.GetKeyDown(KeyCode.R)) pressed |= ControlButton.Ultimate;
            if (Input.GetMouseButtonDown(1)) pressed |= ControlButton.Dodge;
            if (Input.GetKeyDown(KeyCode.LeftShift)) pressed |= ControlButton.SprintToggle;
            if (Input.GetKeyDown(KeyCode.LeftControl)) pressed |= ControlButton.WalkToggle;
            if (Input.GetMouseButton(0)) held |= ControlButton.Attack;
            return new ControlFrame(context.FrameId, 0, move, move, pressed, held);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            disposed = true;
        }
    }
}
