using System;
using UnityEngine;

namespace Xuan.Prometheus.Input
{
    /// <summary>定义可以独立分配控制权的最小输入动作集合，组合值只用于批量声明绑定范围。</summary>
    [Flags]
    public enum InputActionMask
    {
        None = 0,
        Move = 1 << 0,
        Attack = 1 << 1,
        Skill = 1 << 2,
        Ultimate = 1 << 3,
        Dodge = 1 << 4,
        Jump = 1 << 5,
        SpecialAttack = 1 << 6,
        ToggleSprint = 1 << 7,
        ToggleWalk = 1 << 8,
        Navigate = 1 << 9,
        Submit = 1 << 10,
        Cancel = 1 << 11,
        Gameplay = Move | Attack | Skill | Ultimate | Dodge | Jump | SpecialAttack | ToggleSprint | ToggleWalk,
        Navigation = Navigate | Submit | Cancel,
        All = Gameplay | Navigation
    }

    /// <summary>定义同一输入动作在相同仲裁层级中的分发方式。</summary>
    public enum InputDeliveryMode
    {
        /// <summary>当前动作只允许一个接收者获得控制权。</summary>
        Exclusive,
        /// <summary>当前动作允许同层级的多个接收者同时获得输入。</summary>
        Shared,
        /// <summary>当前接收者只观察输入，不参与控制权竞争。</summary>
        Observe
    }

    /// <summary>描述一组输入绑定所属的上下文及其相对优先级。</summary>
    public readonly struct InputContext : IEquatable<InputContext>
    {
        /// <summary>创建一个具有稳定名称和仲裁优先级的输入上下文。</summary>
        /// <param name="name">用于诊断和表达用途的上下文名称。</param>
        /// <param name="priority">上下文优先级，数值越大越先获得动作控制权。</param>
        public InputContext(string name, int priority)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Input context name cannot be empty.", nameof(name));
            Name = name;
            Priority = priority;
        }

        /// <summary>获取上下文名称。</summary>
        public string Name { get; }

        /// <summary>获取上下文仲裁优先级。</summary>
        public int Priority { get; }

        /// <inheritdoc />
        public bool Equals(InputContext other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal) && Priority == other.Priority;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is InputContext other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? StringComparer.Ordinal.GetHashCode(Name) : 0) * 397) ^ Priority;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Name}({Priority})";
        }
    }

    /// <summary>提供项目内约定的常用输入上下文，业务仍可创建自定义上下文。</summary>
    public static class InputContexts
    {
        /// <summary>普通玩法控制上下文。</summary>
        public static readonly InputContext Gameplay = new InputContext("Gameplay", 0);

        /// <summary>调试工具输入上下文。</summary>
        public static readonly InputContext Debug = new InputContext("Debug", 50);

        /// <summary>模态界面输入上下文。</summary>
        public static readonly InputContext ModalUI = new InputContext("ModalUI", 100);

        /// <summary>演出或过场接管输入的上下文。</summary>
        public static readonly InputContext Cutscene = new InputContext("Cutscene", 200);
    }

    /// <summary>保存一个按钮在当前采样帧中的按下、持续和释放状态。</summary>
    public readonly struct InputButtonState
    {
        /// <summary>创建按钮的完整帧状态。</summary>
        public InputButtonState(bool pressedThisFrame, bool held, bool releasedThisFrame)
        {
            PressedThisFrame = pressedThisFrame;
            Held = held;
            ReleasedThisFrame = releasedThisFrame;
        }

        /// <summary>获取按钮是否在当前帧刚刚按下。</summary>
        public bool PressedThisFrame { get; }

        /// <summary>获取按钮当前是否处于按住状态。</summary>
        public bool Held { get; }

        /// <summary>获取按钮是否在当前帧刚刚释放。</summary>
        public bool ReleasedThisFrame { get; }

        /// <summary>获取当前帧是否包含任何可观察的按钮状态。</summary>
        public bool IsActive => PressedThisFrame || Held || ReleasedThisFrame;
    }

    /// <summary>保存输入源一次采样产生的不可变输入快照。</summary>
    public readonly struct InputFrame
    {
        /// <summary>创建包含玩法与界面动作的完整输入快照。</summary>
        public InputFrame(long frameId, Vector2 move, Vector2 navigate, InputButtonState attack, InputButtonState skill, InputButtonState ultimate, InputButtonState dodge, InputButtonState jump, InputButtonState specialAttack, InputButtonState toggleSprint, InputButtonState toggleWalk, InputButtonState submit, InputButtonState cancel)
        {
            FrameId = frameId;
            Move = move;
            Navigate = navigate;
            Attack = attack;
            Skill = skill;
            Ultimate = ultimate;
            Dodge = dodge;
            Jump = jump;
            SpecialAttack = specialAttack;
            ToggleSprint = toggleSprint;
            ToggleWalk = toggleWalk;
            Submit = submit;
            Cancel = cancel;
        }

        /// <summary>获取由 InputSystem 分配的单调递增帧编号。</summary>
        public long FrameId { get; }

        /// <summary>获取世界移动输入。</summary>
        public Vector2 Move { get; }

        /// <summary>获取界面导航输入。</summary>
        public Vector2 Navigate { get; }

        /// <summary>获取普通攻击按钮状态。</summary>
        public InputButtonState Attack { get; }

        /// <summary>获取技能按钮状态。</summary>
        public InputButtonState Skill { get; }

        /// <summary>获取终结技按钮状态。</summary>
        public InputButtonState Ultimate { get; }

        /// <summary>获取闪避按钮状态。</summary>
        public InputButtonState Dodge { get; }

        /// <summary>获取跳跃按钮状态。</summary>
        public InputButtonState Jump { get; }

        /// <summary>获取特殊攻击按钮状态。</summary>
        public InputButtonState SpecialAttack { get; }

        /// <summary>获取冲刺模式切换按钮状态。</summary>
        public InputButtonState ToggleSprint { get; }

        /// <summary>获取行走模式切换按钮状态。</summary>
        public InputButtonState ToggleWalk { get; }

        /// <summary>获取界面确认按钮状态。</summary>
        public InputButtonState Submit { get; }

        /// <summary>获取界面取消按钮状态。</summary>
        public InputButtonState Cancel { get; }

        /// <summary>判断指定动作集合在当前快照中是否包含有效输入。</summary>
        public bool HasAny(InputActionMask actions)
        {
            if ((actions & InputActionMask.Move) != 0 && Move.sqrMagnitude > 0f) return true;
            if ((actions & InputActionMask.Navigate) != 0 && Navigate.sqrMagnitude > 0f) return true;
            if ((actions & InputActionMask.Attack) != 0 && Attack.IsActive) return true;
            if ((actions & InputActionMask.Skill) != 0 && Skill.IsActive) return true;
            if ((actions & InputActionMask.Ultimate) != 0 && Ultimate.IsActive) return true;
            if ((actions & InputActionMask.Dodge) != 0 && Dodge.IsActive) return true;
            if ((actions & InputActionMask.Jump) != 0 && Jump.IsActive) return true;
            if ((actions & InputActionMask.SpecialAttack) != 0 && SpecialAttack.IsActive) return true;
            if ((actions & InputActionMask.ToggleSprint) != 0 && ToggleSprint.IsActive) return true;
            if ((actions & InputActionMask.ToggleWalk) != 0 && ToggleWalk.IsActive) return true;
            if ((actions & InputActionMask.Submit) != 0 && Submit.IsActive) return true;
            return (actions & InputActionMask.Cancel) != 0 && Cancel.IsActive;
        }
    }

    /// <summary>抽象一个可由 InputSystem 每帧采样一次的输入设备或输入数据流。</summary>
    public interface IInputSource : IDisposable
    {
        /// <summary>获取输入源在当前 InputSystem 中的唯一标识。</summary>
        string SourceId { get; }

        /// <summary>采样指定系统帧并返回不可变输入快照。</summary>
        InputFrame Sample(long frameId);
    }

    /// <summary>抽象可以接收部分输入动作的目标，不要求目标属于 Entity。</summary>
    public interface IInputReceiver
    {
        /// <summary>获取接收者是否仍允许继续持有输入绑定。</summary>
        bool IsAlive { get; }

        /// <summary>在新输入帧分发前清除上一帧残留状态。</summary>
        void ResetInput();

        /// <summary>接收当前输入源被授权的动作片段；同一帧存在多个输入源时可能调用多次，接收者必须合并结果。</summary>
        void ReceiveInput(in InputFrame frame, InputActionMask actions);
    }

    /// <summary>表示一份可释放的动作控制权，释放后原有低优先级绑定会在下一输入帧自动恢复。</summary>
    public sealed class ControlLease : IDisposable
    {
        private InputSystem owner;
        private readonly int bindingId;

        /// <summary>由 InputSystem 创建一份与内部绑定对应的控制权租约。</summary>
        internal ControlLease(InputSystem owner, int bindingId)
        {
            this.owner = owner;
            this.bindingId = bindingId;
        }

        /// <summary>获取当前租约是否已经释放或随 InputSystem 一同失效。</summary>
        public bool IsReleased => owner == null;

        /// <summary>释放当前动作控制权；重复调用保持幂等。</summary>
        public void Dispose()
        {
            InputSystem currentOwner = owner;
            if (currentOwner == null) return;
            owner = null;
            currentOwner.ReleaseControl(bindingId);
        }

        /// <summary>由 InputSystem 在整体销毁或目标失效时断开租约。</summary>
        internal void Invalidate()
        {
            owner = null;
        }
    }
}
