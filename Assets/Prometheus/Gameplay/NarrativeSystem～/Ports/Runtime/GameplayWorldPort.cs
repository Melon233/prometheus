using System;
using UnityEngine;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 走 InputSystem 的运行时世界接管端口。
    ///
    /// 输入锁的做法是以 Cutscene 上下文申请一份排他控制权，接收端把输入直接丢弃：
    /// 玩法动作因此在演出期间无人响应，而释放租约后原有的低优先级绑定会自动恢复，
    /// 不需要端口记录任何「接管前的状态」。
    ///
    /// 只锁 <see cref="InputActionMask.Gameplay"/>，不锁 <see cref="InputActionMask.Navigation"/>——
    /// 对话界面的确认与选择走的正是导航动作，一并锁掉会让演出无法推进。
    /// </summary>
    internal sealed class GameplayWorldPort : INarrativeWorldPort
    {
        /// <summary>演出输入锁在同一上下文内的绑定优先级，高于同上下文的其他申请。</summary>
        private const int CutsceneBindingPriority = 100;

        /// <summary>复用同一个丢弃式接收端，避免每次接管都产生一个新对象。</summary>
        private readonly InputSink sink = new InputSink();

        /// <inheritdoc />
        public IDisposable LockGameplayInput()
        {
            if (!Core.Gameplay.TryGetSystem(out IInputSystem inputSystem)) throw new InvalidOperationException($"{nameof(GameplayWorldPort)} requires {nameof(IInputSystem)}.");
            return inputSystem.AcquireControl(inputSystem.DefaultSourceId, sink, InputActionMask.Gameplay, InputContexts.Cutscene, CutsceneBindingPriority);
        }

        /// <summary>
        /// 冻结指定范围内的 AI 与怪物。
        ///
        /// 当前没有任何系统提供「按范围枚举活动实体」的能力：<c>IEntitySystem</c> 只能按编号查询，
        /// 补一个全场景查找会绕过实体托管边界。因此本能力显式未实现，剧情在接入范围查询之前
        /// 必须把 <c>StageSpec.FreezeAiRadius</c> 保持为零；<c>Stage.EnterAiFreeze</c> 只在半径大于零时
        /// 才会走到这里，所以这条路径不会被无意触发。
        /// </summary>
        public IDisposable FreezeAi(Vector3 center, float radius)
        {
            throw new NotSupportedException($"{nameof(GameplayWorldPort)} cannot freeze AI yet: {nameof(IEntitySystem)} provides no ranged entity query. Keep StageSpec.FreezeAiRadius at zero until it does.");
        }

        /// <summary>把授权动作全部丢弃的输入接收端，用于在演出期间占住玩法输入。</summary>
        private sealed class InputSink : IInputReceiver
        {
            /// <summary>接收端与端口同生命周期，租约释放前始终有效。</summary>
            public bool IsAlive => true;

            /// <summary>没有需要跨帧保留的状态，无需清理。</summary>
            public void ResetInput()
            {
            }

            /// <summary>丢弃全部授权动作，这正是「锁住玩法输入」的语义。</summary>
            public void ReceiveInput(in InputFrame frame, InputActionMask actions)
            {
            }
        }
    }
}
