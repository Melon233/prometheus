using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 表示客户端或机器人在单个固定 Tick 提交的完整角色操作命令，边沿输入必须由采集层在进入模拟前确定。
    /// </summary>
    public readonly struct CharacterCommand : IEquatable<CharacterCommand>
    {
        /// <summary>创建一个经过八方向范围校验的角色操作命令。</summary>
        public CharacterCommand(int tick, sbyte moveX, sbyte moveZ, CharacterMoveMode requestedMoveMode, bool jumpPressed, bool dodgePressed, bool dodgeBackward, bool attackPressed, bool attackHeld, bool attackReleased, bool skillPressed, bool ultimatePressed)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick), "Character command tick cannot be negative.");
            if (moveX < -1 || moveX > 1) throw new ArgumentOutOfRangeException(nameof(moveX), "Character movement X input must be -1, 0, or 1.");
            if (moveZ < -1 || moveZ > 1) throw new ArgumentOutOfRangeException(nameof(moveZ), "Character movement Z input must be -1, 0, or 1.");
            if (requestedMoveMode < CharacterMoveMode.Walk || requestedMoveMode > CharacterMoveMode.Sprint) throw new ArgumentOutOfRangeException(nameof(requestedMoveMode), "Unknown character movement mode.");
            Tick = tick;
            MoveX = moveX;
            MoveZ = moveZ;
            RequestedMoveMode = requestedMoveMode;
            JumpPressed = jumpPressed;
            DodgePressed = dodgePressed;
            DodgeBackward = dodgeBackward;
            AttackPressed = attackPressed;
            AttackHeld = attackHeld;
            AttackReleased = attackReleased;
            SkillPressed = skillPressed;
            UltimatePressed = ultimatePressed;
        }

        /// <summary>获取该命令对应的客户端和模拟 Tick。</summary>
        public int Tick { get; }

        /// <summary>获取 X 轴八方向离散输入。</summary>
        public sbyte MoveX { get; }

        /// <summary>获取 Z 轴八方向离散输入。</summary>
        public sbyte MoveZ { get; }

        /// <summary>获取有方向输入时请求的走、跑或冲刺档位。</summary>
        public CharacterMoveMode RequestedMoveMode { get; }

        /// <summary>获取该 Tick 是否出现跳跃按下边沿。</summary>
        public bool JumpPressed { get; }

        /// <summary>获取该 Tick 是否出现闪避按下边沿。</summary>
        public bool DodgePressed { get; }

        /// <summary>获取闪避命令是否请求沿当前朝向反方向移动。</summary>
        public bool DodgeBackward { get; }

        /// <summary>获取该 Tick 是否出现攻击按下边沿。</summary>
        public bool AttackPressed { get; }

        /// <summary>获取该 Tick 结束采样时攻击键是否仍被按住。</summary>
        public bool AttackHeld { get; }

        /// <summary>获取该 Tick 是否出现攻击释放边沿，未达到蓄力阈值时将尝试触发普攻。</summary>
        public bool AttackReleased { get; }

        /// <summary>获取该 Tick 是否出现普通技能按下边沿。</summary>
        public bool SkillPressed { get; }

        /// <summary>获取该 Tick 是否出现终结技能按下边沿。</summary>
        public bool UltimatePressed { get; }

        /// <summary>获取该命令是否包含有效移动方向。</summary>
        public bool HasMovement => MoveX != 0 || MoveZ != 0;

        /// <summary>创建指定 Tick 且不包含任何操作的空命令。</summary>
        public static CharacterCommand Empty(int tick)
        {
            return new CharacterCommand(tick, 0, 0, CharacterMoveMode.Run, false, false, false, false, false, false, false, false);
        }

        /// <summary>判断两个角色命令的全部输入字段是否完全一致。</summary>
        public bool Equals(CharacterCommand other)
        {
            return Tick == other.Tick && MoveX == other.MoveX && MoveZ == other.MoveZ && RequestedMoveMode == other.RequestedMoveMode && JumpPressed == other.JumpPressed && DodgePressed == other.DodgePressed && DodgeBackward == other.DodgeBackward && AttackPressed == other.AttackPressed && AttackHeld == other.AttackHeld && AttackReleased == other.AttackReleased && SkillPressed == other.SkillPressed && UltimatePressed == other.UltimatePressed;
        }

        /// <summary>判断指定对象是否为字段完全一致的角色命令。</summary>
        public override bool Equals(object obj)
        {
            return obj is CharacterCommand other && Equals(other);
        }

        /// <summary>获取角色命令字段组成的哈希码。</summary>
        public override int GetHashCode()
        {
            CharacterStableHashBuilder builder = CharacterStableHashBuilder.Create();
            builder.Add(Tick);
            builder.Add(MoveX);
            builder.Add(MoveZ);
            builder.Add((int)RequestedMoveMode);
            builder.Add(JumpPressed);
            builder.Add(DodgePressed);
            builder.Add(DodgeBackward);
            builder.Add(AttackPressed);
            builder.Add(AttackHeld);
            builder.Add(AttackReleased);
            builder.Add(SkillPressed);
            builder.Add(UltimatePressed);
            return unchecked((int)builder.ToHash());
        }
    }

    /// <summary>
    /// 表示权威世界在角色 Resolve 阶段提供的外部结算输入，客户端预测通常使用空上下文并由权威快照纠正差异。
    /// </summary>
    public readonly struct CharacterTickContext
    {
        /// <summary>创建当前 Tick 的外部伤害与命中确认输入。</summary>
        public CharacterTickContext(int incomingDamage, int confirmedHitCount)
        {
            if (incomingDamage < 0) throw new ArgumentOutOfRangeException(nameof(incomingDamage), "Incoming damage cannot be negative.");
            if (confirmedHitCount < 0) throw new ArgumentOutOfRangeException(nameof(confirmedHitCount), "Confirmed hit count cannot be negative.");
            IncomingDamage = incomingDamage;
            ConfirmedHitCount = confirmedHitCount;
        }

        /// <summary>获取本 Tick 在无敌帧判断后尝试结算的总伤害。</summary>
        public int IncomingDamage { get; }

        /// <summary>获取权威命中查询对当前动作给出的确认命中数量。</summary>
        public int ConfirmedHitCount { get; }

        /// <summary>获取不包含外部结算输入的空上下文。</summary>
        public static CharacterTickContext Empty => new CharacterTickContext(0, 0);
    }

    /// <summary>
    /// 表示固定 Tick 模拟按稳定顺序输出的单个领域事件，实体编号由更外层 BattleWorld 包装。
    /// </summary>
    public readonly struct CharacterEvent
    {
        /// <summary>创建一个角色模拟领域事件。</summary>
        public CharacterEvent(int tick, CharacterEventType type, CharacterActionKind actionKind, int actionId, int value)
        {
            Tick = tick;
            Type = type;
            ActionKind = actionKind;
            ActionId = actionId;
            Value = value;
        }

        /// <summary>获取事件发生的固定模拟 Tick。</summary>
        public int Tick { get; }

        /// <summary>获取事件种类。</summary>
        public CharacterEventType Type { get; }

        /// <summary>获取事件关联的动作种类，无关联动作时为 None。</summary>
        public CharacterActionKind ActionKind { get; }

        /// <summary>获取事件关联的 Luban 动作表行编号，无关联动作时为零。</summary>
        public int ActionId { get; }

        /// <summary>获取事件种类定义的附加整数值。</summary>
        public int Value { get; }
    }

    /// <summary>
    /// 保存单个固定 Tick 提交后的完整不可变状态和按发生顺序冻结的领域事件。
    /// </summary>
    public sealed class CharacterTickResult
    {
        private readonly ReadOnlyCollection<CharacterEvent> events;

        /// <summary>创建一个已完成的角色固定 Tick 结果并复制事件集合。</summary>
        public CharacterTickResult(CharacterState state, IList<CharacterEvent> events)
        {
            State = state;
            if (events == null) throw new ArgumentNullException(nameof(events));
            CharacterEvent[] eventCopy = new CharacterEvent[events.Count];
            events.CopyTo(eventCopy, 0);
            this.events = Array.AsReadOnly(eventCopy);
        }

        /// <summary>获取该 Tick 提交后的完整角色状态。</summary>
        public CharacterState State { get; }

        /// <summary>获取该 Tick 按稳定顺序生成的只读领域事件。</summary>
        public IReadOnlyList<CharacterEvent> Events => events;
    }
}
