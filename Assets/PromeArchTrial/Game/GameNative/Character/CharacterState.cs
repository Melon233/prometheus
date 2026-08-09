using System;

namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 保存可跨网络完整序列化的不可变角色模拟状态，包含确保回滚重放一致所需的全部积分余数、动作、资源和冷却字段。
    /// </summary>
    public readonly struct CharacterState : IEquatable<CharacterState>
    {
        /// <summary>从网络快照或持久化数据恢复一个完整角色状态。</summary>
        /// <param name="tick">最近完成的固定模拟 Tick，初始状态使用负一。</param>
        /// <param name="position">当前三维定点位置。</param>
        /// <param name="facingXRaw">当前朝向的 X 轴归一化定点分量。</param>
        /// <param name="facingZRaw">当前朝向的 Z 轴归一化定点分量。</param>
        /// <param name="locomotionState">当前移动状态。</param>
        /// <param name="actionKind">当前排他动作种类。</param>
        /// <param name="actionElapsedTicks">当前动作已经进入的 Tick 索引。</param>
        /// <param name="actionDirectionXRaw">动作开始时锁定方向的 X 轴定点分量。</param>
        /// <param name="actionDirectionZRaw">动作开始时锁定方向的 Z 轴定点分量。</param>
        /// <param name="horizontalRemainderX">横向 X 轴速度除以 TickRate 后保留的积分余数。</param>
        /// <param name="horizontalRemainderZ">横向 Z 轴速度除以 TickRate 后保留的积分余数。</param>
        /// <param name="verticalVelocityRaw">当前每秒竖直速度定点值。</param>
        /// <param name="verticalAccelerationRemainder">重力除以 TickRate 后保留的速度积分余数。</param>
        /// <param name="verticalPositionRemainder">竖直速度除以 TickRate 后保留的位置积分余数。</param>
        /// <param name="isGrounded">角色是否位于模拟地面。</param>
        /// <param name="isInvincible">当前动作 Tick 是否处于无敌区间。</param>
        /// <param name="hp">当前生命值。</param>
        /// <param name="coreEnergy">当前核心能量。</param>
        /// <param name="ultimateEnergy">当前终结能量。</param>
        /// <param name="attackChargeTicks">攻击键已经连续蓄力的 Tick 数。</param>
        /// <param name="attackHoldConsumed">当前物理按住是否已经触发过一次重击并等待释放重新武装。</param>
        /// <param name="lightAttackBufferRemainingTicks">尚未消费的单槽轻击输入剩余保留 Tick 数，零表示没有缓冲输入。</param>
        /// <param name="usesMovingAttackVariant">当前普攻是否冻结为移动起手动画变体。</param>
        /// <param name="nextAttackComboIndex">下一次普攻将使用的零起始连段索引。</param>
        /// <param name="comboTimeoutRemainingTicks">当前普攻连段剩余接续 Tick 数。</param>
        /// <param name="dodgeCooldownRemainingTicks">闪避组剩余冷却 Tick 数。</param>
        /// <param name="attackCooldownRemainingTicks">普攻组剩余冷却 Tick 数。</param>
        /// <param name="heavyAttackCooldownRemainingTicks">蓄力重击剩余冷却 Tick 数。</param>
        /// <param name="skillCooldownRemainingTicks">普通技能剩余冷却 Tick 数。</param>
        /// <param name="ultimateCooldownRemainingTicks">终结技能剩余冷却 Tick 数。</param>
        public CharacterState(int tick, FixedVector3 position, long facingXRaw, long facingZRaw, CharacterLocomotionState locomotionState, CharacterActionKind actionKind, int actionElapsedTicks, long actionDirectionXRaw, long actionDirectionZRaw, long horizontalRemainderX, long horizontalRemainderZ, long verticalVelocityRaw, long verticalAccelerationRemainder, long verticalPositionRemainder, bool isGrounded, bool isInvincible, int hp, int coreEnergy, int ultimateEnergy, int attackChargeTicks, bool attackHoldConsumed, int lightAttackBufferRemainingTicks, bool usesMovingAttackVariant, int nextAttackComboIndex, int comboTimeoutRemainingTicks, int dodgeCooldownRemainingTicks, int attackCooldownRemainingTicks, int heavyAttackCooldownRemainingTicks, int skillCooldownRemainingTicks, int ultimateCooldownRemainingTicks)
        {
            if (tick < -1) throw new ArgumentOutOfRangeException(nameof(tick), "Character state tick cannot be lower than -1.");
            if (actionElapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(actionElapsedTicks), "Action elapsed ticks cannot be negative.");
            if (hp < 0) throw new ArgumentOutOfRangeException(nameof(hp), "HP cannot be negative.");
            if (coreEnergy < 0) throw new ArgumentOutOfRangeException(nameof(coreEnergy), "Core energy cannot be negative.");
            if (ultimateEnergy < 0) throw new ArgumentOutOfRangeException(nameof(ultimateEnergy), "Ultimate energy cannot be negative.");
            if (attackChargeTicks < 0) throw new ArgumentOutOfRangeException(nameof(attackChargeTicks), "Attack charge ticks cannot be negative.");
            if (lightAttackBufferRemainingTicks < 0) throw new ArgumentOutOfRangeException(nameof(lightAttackBufferRemainingTicks), "Light attack buffer remaining ticks cannot be negative.");
            if (nextAttackComboIndex < 0 || nextAttackComboIndex > 3) throw new ArgumentOutOfRangeException(nameof(nextAttackComboIndex), "Attack combo index must be between 0 and 3.");
            if (comboTimeoutRemainingTicks < 0 || dodgeCooldownRemainingTicks < 0 || attackCooldownRemainingTicks < 0 || heavyAttackCooldownRemainingTicks < 0 || skillCooldownRemainingTicks < 0 || ultimateCooldownRemainingTicks < 0) throw new ArgumentOutOfRangeException(nameof(comboTimeoutRemainingTicks), "Cooldown and timeout fields cannot be negative.");
            Tick = tick;
            Position = position;
            FacingXRaw = facingXRaw;
            FacingZRaw = facingZRaw;
            LocomotionState = locomotionState;
            ActionKind = actionKind;
            ActionElapsedTicks = actionElapsedTicks;
            ActionDirectionXRaw = actionDirectionXRaw;
            ActionDirectionZRaw = actionDirectionZRaw;
            HorizontalRemainderX = horizontalRemainderX;
            HorizontalRemainderZ = horizontalRemainderZ;
            VerticalVelocityRaw = verticalVelocityRaw;
            VerticalAccelerationRemainder = verticalAccelerationRemainder;
            VerticalPositionRemainder = verticalPositionRemainder;
            IsGrounded = isGrounded;
            IsInvincible = isInvincible;
            Hp = hp;
            CoreEnergy = coreEnergy;
            UltimateEnergy = ultimateEnergy;
            AttackChargeTicks = attackChargeTicks;
            AttackHoldConsumed = attackHoldConsumed;
            LightAttackBufferRemainingTicks = lightAttackBufferRemainingTicks;
            UsesMovingAttackVariant = usesMovingAttackVariant;
            NextAttackComboIndex = nextAttackComboIndex;
            ComboTimeoutRemainingTicks = comboTimeoutRemainingTicks;
            DodgeCooldownRemainingTicks = dodgeCooldownRemainingTicks;
            AttackCooldownRemainingTicks = attackCooldownRemainingTicks;
            HeavyAttackCooldownRemainingTicks = heavyAttackCooldownRemainingTicks;
            SkillCooldownRemainingTicks = skillCooldownRemainingTicks;
            UltimateCooldownRemainingTicks = ultimateCooldownRemainingTicks;
        }

        /// <summary>获取最近完成的固定模拟 Tick。</summary>
        public int Tick { get; }

        /// <summary>获取当前三维定点位置。</summary>
        public FixedVector3 Position { get; }

        /// <summary>获取当前朝向的 X 轴归一化定点分量。</summary>
        public long FacingXRaw { get; }

        /// <summary>获取当前朝向的 Z 轴归一化定点分量。</summary>
        public long FacingZRaw { get; }

        /// <summary>获取当前用于表现同步的移动状态。</summary>
        public CharacterLocomotionState LocomotionState { get; }

        /// <summary>获取当前排他动作种类。</summary>
        public CharacterActionKind ActionKind { get; }

        /// <summary>获取当前动作已经进入的 Tick 索引。</summary>
        public int ActionElapsedTicks { get; }

        /// <summary>获取动作开始时锁定方向的 X 轴定点分量。</summary>
        public long ActionDirectionXRaw { get; }

        /// <summary>获取动作开始时锁定方向的 Z 轴定点分量。</summary>
        public long ActionDirectionZRaw { get; }

        /// <summary>获取横向 X 轴积分余数。</summary>
        public long HorizontalRemainderX { get; }

        /// <summary>获取横向 Z 轴积分余数。</summary>
        public long HorizontalRemainderZ { get; }

        /// <summary>获取当前每秒竖直速度定点值。</summary>
        public long VerticalVelocityRaw { get; }

        /// <summary>获取重力对竖直速度的积分余数。</summary>
        public long VerticalAccelerationRemainder { get; }

        /// <summary>获取竖直速度对位置的积分余数。</summary>
        public long VerticalPositionRemainder { get; }

        /// <summary>获取角色是否位于模拟地面。</summary>
        public bool IsGrounded { get; }

        /// <summary>获取当前动作 Tick 是否处于配置无敌区间。</summary>
        public bool IsInvincible { get; }

        /// <summary>获取当前生命值。</summary>
        public int Hp { get; }

        /// <summary>获取当前核心能量。</summary>
        public int CoreEnergy { get; }

        /// <summary>获取当前终结能量。</summary>
        public int UltimateEnergy { get; }

        /// <summary>获取攻击键已经连续蓄力的 Tick 数。</summary>
        public int AttackChargeTicks { get; }

        /// <summary>获取当前物理按住是否已经消费为重击；该状态只会在释放或新的合法按下边沿后重新武装。</summary>
        public bool AttackHoldConsumed { get; }

        /// <summary>获取尚未消费的单槽轻击输入剩余保留 Tick 数，零表示没有缓冲输入。</summary>
        public int LightAttackBufferRemainingTicks { get; }

        /// <summary>获取当前普攻是否在起手 Tick 冻结为移动动画变体。</summary>
        public bool UsesMovingAttackVariant { get; }

        /// <summary>获取下一次普攻将使用的零起始连段索引。</summary>
        public int NextAttackComboIndex { get; }

        /// <summary>获取当前普攻连段剩余接续 Tick 数。</summary>
        public int ComboTimeoutRemainingTicks { get; }

        /// <summary>获取闪避组剩余冷却 Tick 数。</summary>
        public int DodgeCooldownRemainingTicks { get; }

        /// <summary>获取普攻组剩余冷却 Tick 数。</summary>
        public int AttackCooldownRemainingTicks { get; }

        /// <summary>获取蓄力重击剩余冷却 Tick 数。</summary>
        public int HeavyAttackCooldownRemainingTicks { get; }

        /// <summary>获取普通技能剩余冷却 Tick 数。</summary>
        public int SkillCooldownRemainingTicks { get; }

        /// <summary>获取终结技能剩余冷却 Tick 数。</summary>
        public int UltimateCooldownRemainingTicks { get; }

        /// <summary>获取生命值是否已经归零。</summary>
        public bool IsDead => Hp == 0;

        /// <summary>获取覆盖完整网络状态字段的稳定六十四位哈希。</summary>
        public ulong StableHash
        {
            get
            {
                CharacterStableHashBuilder builder = CharacterStableHashBuilder.Create();
                builder.Add(Tick);
                builder.Add(Position.X);
                builder.Add(Position.Y);
                builder.Add(Position.Z);
                builder.Add(FacingXRaw);
                builder.Add(FacingZRaw);
                builder.Add((int)LocomotionState);
                builder.Add((int)ActionKind);
                builder.Add(ActionElapsedTicks);
                builder.Add(ActionDirectionXRaw);
                builder.Add(ActionDirectionZRaw);
                builder.Add(HorizontalRemainderX);
                builder.Add(HorizontalRemainderZ);
                builder.Add(VerticalVelocityRaw);
                builder.Add(VerticalAccelerationRemainder);
                builder.Add(VerticalPositionRemainder);
                builder.Add(IsGrounded);
                builder.Add(IsInvincible);
                builder.Add(Hp);
                builder.Add(CoreEnergy);
                builder.Add(UltimateEnergy);
                builder.Add(AttackChargeTicks);
                builder.Add(AttackHoldConsumed);
                builder.Add(LightAttackBufferRemainingTicks);
                builder.Add(UsesMovingAttackVariant);
                builder.Add(NextAttackComboIndex);
                builder.Add(ComboTimeoutRemainingTicks);
                builder.Add(DodgeCooldownRemainingTicks);
                builder.Add(AttackCooldownRemainingTicks);
                builder.Add(HeavyAttackCooldownRemainingTicks);
                builder.Add(SkillCooldownRemainingTicks);
                builder.Add(UltimateCooldownRemainingTicks);
                return builder.ToHash();
            }
        }

        /// <summary>使用配置上限创建位于指定位置的初始角色状态。</summary>
        public static CharacterState CreateInitial(CharacterRuntimeConfig config, FixedVector3 position)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (position.Y < 0L) throw new ArgumentOutOfRangeException(nameof(position), "Initial character position cannot be below the simulation ground.");
            bool grounded = position.Y == 0L;
            CharacterLocomotionState locomotion = grounded ? CharacterLocomotionState.Idle : CharacterLocomotionState.Fall;
            return new CharacterState(-1, position, 0L, CharacterFixedPoint.DirectionScale, locomotion, CharacterActionKind.None, 0, 0L, CharacterFixedPoint.DirectionScale, 0L, 0L, 0L, 0L, 0L, grounded, false, config.Stats.MaxHp, 0, 0, 0, false, 0, false, 0, 0, 0, 0, 0, 0, 0);
        }

        /// <summary>根据运行时动作配置获取当前动作阶段。</summary>
        public CharacterActionPhase GetActionPhase(CharacterRuntimeConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return ActionKind == CharacterActionKind.None ? CharacterActionPhase.None : config.GetAction(ActionKind).GetPhaseAt(ActionElapsedTicks);
        }

        /// <summary>判断两个角色状态的全部可序列化字段是否完全一致。</summary>
        public bool Equals(CharacterState other)
        {
            return Tick == other.Tick && Position == other.Position && FacingXRaw == other.FacingXRaw && FacingZRaw == other.FacingZRaw && LocomotionState == other.LocomotionState && ActionKind == other.ActionKind && ActionElapsedTicks == other.ActionElapsedTicks && ActionDirectionXRaw == other.ActionDirectionXRaw && ActionDirectionZRaw == other.ActionDirectionZRaw && HorizontalRemainderX == other.HorizontalRemainderX && HorizontalRemainderZ == other.HorizontalRemainderZ && VerticalVelocityRaw == other.VerticalVelocityRaw && VerticalAccelerationRemainder == other.VerticalAccelerationRemainder && VerticalPositionRemainder == other.VerticalPositionRemainder && IsGrounded == other.IsGrounded && IsInvincible == other.IsInvincible && Hp == other.Hp && CoreEnergy == other.CoreEnergy && UltimateEnergy == other.UltimateEnergy && AttackChargeTicks == other.AttackChargeTicks && AttackHoldConsumed == other.AttackHoldConsumed && LightAttackBufferRemainingTicks == other.LightAttackBufferRemainingTicks && UsesMovingAttackVariant == other.UsesMovingAttackVariant && NextAttackComboIndex == other.NextAttackComboIndex && ComboTimeoutRemainingTicks == other.ComboTimeoutRemainingTicks && DodgeCooldownRemainingTicks == other.DodgeCooldownRemainingTicks && AttackCooldownRemainingTicks == other.AttackCooldownRemainingTicks && HeavyAttackCooldownRemainingTicks == other.HeavyAttackCooldownRemainingTicks && SkillCooldownRemainingTicks == other.SkillCooldownRemainingTicks && UltimateCooldownRemainingTicks == other.UltimateCooldownRemainingTicks;
        }

        /// <summary>判断指定对象是否为字段完全一致的角色状态。</summary>
        public override bool Equals(object obj)
        {
            return obj is CharacterState other && Equals(other);
        }

        /// <summary>获取由稳定状态哈希折叠得到的三十二位哈希码。</summary>
        public override int GetHashCode()
        {
            ulong hash = StableHash;
            return unchecked((int)(hash ^ hash >> 32));
        }

        /// <summary>判断两个角色状态是否逐字段完全一致。</summary>
        public static bool operator ==(CharacterState left, CharacterState right)
        {
            return left.Equals(right);
        }

        /// <summary>判断两个角色状态是否存在任一不同字段。</summary>
        public static bool operator !=(CharacterState left, CharacterState right)
        {
            return !left.Equals(right);
        }
    }
}
