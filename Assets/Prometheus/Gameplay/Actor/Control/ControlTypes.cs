using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>标识一个控制租约可以独占的输入领域，使移动、朝向、动作与镜头能够由不同控制器分别接管。</summary>
    [Flags]
    public enum ControlScope
    {
        /// <summary>不包含任何控制领域。</summary>
        None = 0,
        /// <summary>控制移动方向、跳跃、闪避和移动模式切换。</summary>
        Locomotion = 1 << 0,
        /// <summary>控制角色朝向；当前 Legacy 控制器默认使用移动方向作为朝向输入。</summary>
        Facing = 1 << 1,
        /// <summary>控制攻击、技能、终结技和特殊攻击。</summary>
        Action = 1 << 2,
        /// <summary>控制本地镜头请求；当前 ControlFrame 不携带镜头轴值，但租约仍可独立仲裁该领域。</summary>
        Camera = 1 << 3,
        /// <summary>包含第一阶段支持的全部控制领域。</summary>
        All = Locomotion | Facing | Action | Camera
    }

    /// <summary>标识 ControlFrame 中可按位组合的离散或保持型操作。</summary>
    [Flags]
    public enum ControlButton
    {
        /// <summary>没有按钮输入。</summary>
        None = 0,
        /// <summary>普通攻击。</summary>
        Attack = 1 << 0,
        /// <summary>主动技能。</summary>
        Skill = 1 << 1,
        /// <summary>终结技。</summary>
        Ultimate = 1 << 2,
        /// <summary>闪避。</summary>
        Dodge = 1 << 3,
        /// <summary>跳跃。</summary>
        Jump = 1 << 4,
        /// <summary>特殊攻击。</summary>
        SpecialAttack = 1 << 5,
        /// <summary>切换冲刺模式。</summary>
        SprintToggle = 1 << 6,
        /// <summary>切换行走模式。</summary>
        WalkToggle = 1 << 7
    }

    /// <summary>提供一次控制器采样所需的只读帧上下文。</summary>
    public readonly struct ControllerSampleContext
    {
        /// <summary>创建一次控制器采样上下文。</summary>
        /// <param name="frameId">由 PossessionSystem 分配的单调递增帧编号。</param>
        /// <param name="deltaTime">当前帧的非负增量时间。</param>
        public ControllerSampleContext(ulong frameId, float deltaTime)
        {
            FrameId = frameId;
            DeltaTime = Mathf.Max(0f, deltaTime);
        }

        /// <summary>获取当前控制采样帧编号。</summary>
        public ulong FrameId { get; }

        /// <summary>获取当前帧的非负增量时间。</summary>
        public float DeltaTime { get; }
    }

    /// <summary>保存一个控制器在单帧产生的连续输入、瞬时按钮和保持按钮；该值不持有任何可变引用。</summary>
    public readonly struct ControlFrame
    {
        /// <summary>创建一个完整的控制帧。</summary>
        /// <param name="frameId">采样帧编号。</param>
        /// <param name="possessionGeneration">Pawn 控制拓扑代数，用于拒绝旧租约遗留输入。</param>
        /// <param name="move">移动领域使用的二维输入。</param>
        /// <param name="facing">朝向领域使用的二维输入。</param>
        /// <param name="pressedButtons">仅在本帧按下的按钮集合。</param>
        /// <param name="heldButtons">本帧仍处于保持状态的按钮集合。</param>
        public ControlFrame(ulong frameId, uint possessionGeneration, Vector2 move, Vector2 facing, ControlButton pressedButtons, ControlButton heldButtons)
            : this(frameId, possessionGeneration, move, facing, pressedButtons, heldButtons, ControlScope.All)
        {
        }

        /// <summary>创建一个带有明确有效控制领域的最终 Pawn 控制帧；该重载由 PossessionSystem 在完成分领域仲裁后使用。</summary>
        /// <param name="frameId">采样帧编号。</param>
        /// <param name="possessionGeneration">Pawn 控制拓扑代数，用于拒绝旧租约遗留输入。</param>
        /// <param name="move">移动领域使用的二维输入。</param>
        /// <param name="facing">朝向领域使用的二维输入。</param>
        /// <param name="pressedButtons">仅在本帧按下的按钮集合。</param>
        /// <param name="heldButtons">本帧仍处于保持状态的按钮集合。</param>
        /// <param name="effectiveScopes">经过租约仲裁后实际存在获胜控制器的领域集合。</param>
        internal ControlFrame(ulong frameId, uint possessionGeneration, Vector2 move, Vector2 facing, ControlButton pressedButtons, ControlButton heldButtons, ControlScope effectiveScopes)
        {
            if ((effectiveScopes & ~ControlScope.All) != 0) throw new ArgumentOutOfRangeException(nameof(effectiveScopes), effectiveScopes, "Control frame contains an unsupported effective scope.");
            FrameId = frameId;
            PossessionGeneration = possessionGeneration;
            Move = move;
            Facing = facing;
            PressedButtons = pressedButtons;
            HeldButtons = heldButtons;
            EffectiveScopes = effectiveScopes;
        }

        /// <summary>获取控制器产生该数据时的帧编号。</summary>
        public ulong FrameId { get; }

        /// <summary>获取该数据对应的 Pawn 控制拓扑代数。</summary>
        public uint PossessionGeneration { get; }

        /// <summary>获取移动领域的二维输入。</summary>
        public Vector2 Move { get; }

        /// <summary>获取朝向领域的二维输入。</summary>
        public Vector2 Facing { get; }

        /// <summary>获取仅在当前帧按下的按钮集合。</summary>
        public ControlButton PressedButtons { get; }

        /// <summary>获取当前帧仍处于保持状态的按钮集合。</summary>
        public ControlButton HeldButtons { get; }

        /// <summary>获取该 Pawn 在当前帧经过租约仲裁后实际由控制器接管的领域；未包含的领域可以继续由后备 AI 或其他本地策略驱动。</summary>
        public ControlScope EffectiveScopes { get; }

        /// <summary>获取当前控制帧是否包含任意连续输入、瞬时按钮或保持按钮。</summary>
        public bool HasAnyInput => Move.sqrMagnitude > 0f || Facing.sqrMagnitude > 0f || PressedButtons != ControlButton.None || HeldButtons != ControlButton.None;

        /// <summary>创建一个不包含输入负载但保留帧编号和控制代数的控制帧。</summary>
        /// <param name="frameId">采样帧编号。</param>
        /// <param name="possessionGeneration">Pawn 控制拓扑代数。</param>
        /// <returns>空控制帧。</returns>
        public static ControlFrame Empty(ulong frameId, uint possessionGeneration)
        {
            return new ControlFrame(frameId, possessionGeneration, Vector2.zero, Vector2.zero, ControlButton.None, ControlButton.None, ControlScope.None);
        }

        /// <summary>只保留指定控制领域拥有的输入负载。</summary>
        /// <param name="scopes">需要保留的控制领域。</param>
        /// <returns>经过领域过滤的新控制帧。</returns>
        public ControlFrame Filter(ControlScope scopes)
        {
            Vector2 filteredMove = (scopes & ControlScope.Locomotion) != 0 ? Move : Vector2.zero;
            Vector2 filteredFacing = (scopes & ControlScope.Facing) != 0 ? Facing : Vector2.zero;
            ControlButton allowedButtons = ControlScopeRules.GetButtons(scopes);
            return new ControlFrame(FrameId, PossessionGeneration, filteredMove, filteredFacing, PressedButtons & allowedButtons, HeldButtons & allowedButtons, EffectiveScopes & scopes);
        }

        /// <summary>将来源控制帧中属于指定领域的负载合并到当前控制帧，并写入 Pawn 的最终控制代数。</summary>
        /// <param name="source">控制器原始采样帧。</param>
        /// <param name="scopes">当前租约赢得的控制领域。</param>
        /// <param name="possessionGeneration">Pawn 当前控制拓扑代数。</param>
        /// <returns>合并后的 Pawn 控制帧。</returns>
        internal ControlFrame Merge(ControlFrame source, ControlScope scopes, uint possessionGeneration)
        {
            ControlFrame filtered = source.Filter(scopes);
            Vector2 mergedMove = (scopes & ControlScope.Locomotion) != 0 ? filtered.Move : Move;
            Vector2 mergedFacing = (scopes & ControlScope.Facing) != 0 ? filtered.Facing : Facing;
            return new ControlFrame(FrameId, possessionGeneration, mergedMove, mergedFacing, PressedButtons | filtered.PressedButtons, HeldButtons | filtered.HeldButtons, EffectiveScopes | scopes);
        }
    }

    /// <summary>集中定义按钮与控制领域的稳定映射，避免各控制器和仲裁器分别维护不一致规则。</summary>
    internal static class ControlScopeRules
    {
        /// <summary>属于移动领域的全部按钮。</summary>
        private const ControlButton LocomotionButtons = ControlButton.Dodge | ControlButton.Jump | ControlButton.SprintToggle | ControlButton.WalkToggle;

        /// <summary>属于动作领域的全部按钮。</summary>
        private const ControlButton ActionButtons = ControlButton.Attack | ControlButton.Skill | ControlButton.Ultimate | ControlButton.SpecialAttack;

        /// <summary>返回指定控制领域允许携带的按钮集合。</summary>
        /// <param name="scopes">需要查询的控制领域。</param>
        /// <returns>允许通过仲裁的按钮集合。</returns>
        internal static ControlButton GetButtons(ControlScope scopes)
        {
            ControlButton buttons = ControlButton.None;
            if ((scopes & ControlScope.Locomotion) != 0) buttons |= LocomotionButtons;
            if ((scopes & ControlScope.Action) != 0) buttons |= ActionButtons;
            return buttons;
        }
    }
}
