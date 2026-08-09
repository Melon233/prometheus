using System;
using System.Collections.Generic;

namespace PromeArchTrial.Game.Character
{
    /// <summary>
    /// 按 Prepare、Action Arbitration、Motion、Resolve、Commit、Events 顺序执行确定性三十赫兹角色模拟，Spine 或其他表现事件不得反向驱动此逻辑。
    /// </summary>
    public static class CharacterSimulation
    {
        /// <summary>使用空外部结算上下文执行一个固定 Tick，适用于客户端本地预测。</summary>
        public static CharacterTickResult Step(CharacterState previousState, CharacterCommand command, CharacterRuntimeConfig config)
        {
            return Step(previousState, command, config, CharacterTickContext.Empty);
        }

        /// <summary>使用权威世界提供的伤害和命中确认执行一个完整固定 Tick。</summary>
        public static CharacterTickResult Step(CharacterState previousState, CharacterCommand command, CharacterRuntimeConfig config, CharacterTickContext context)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            ValidateState(previousState, config);
            if (command.Tick != previousState.Tick + 1) throw new InvalidOperationException($"Character command tick {command.Tick} must immediately follow state tick {previousState.Tick}.");
            CharacterMutableState state = new CharacterMutableState(previousState) { Tick = command.Tick };
            List<CharacterEvent> events = new List<CharacterEvent>(8);
            CharacterLocomotionState previousLocomotion = previousState.LocomotionState;
            Prepare(state, command, config);
            ArbitrateAction(state, command, config, events);
            Motion(state, command, config);
            Resolve(state, command, config, context, events);
            Commit(state, command, config, events);
            AgeLightAttackBuffer(state);
            EmitLocomotionEvent(state, previousLocomotion, events);
            return new CharacterTickResult(state.Freeze(), events);
        }

        /// <summary>根据角色基础攻击力和动作千分比倍率计算不含防御与暴击的确定性基础伤害。</summary>
        public static int CalculateBaseDamage(CharacterRuntimeConfig config, CharacterActionKind actionKind)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            CharacterActionRuntimeConfig action = config.GetAction(actionKind);
            return checked((int)((long)config.Stats.Attack * action.DamagePermille / 1000L));
        }

        /// <summary>校验网络恢复状态是否符合当前配置上限和动作区间。</summary>
        private static void ValidateState(CharacterState state, CharacterRuntimeConfig config)
        {
            if (state.Hp > config.Stats.MaxHp) throw new InvalidOperationException("Character state HP exceeds the configured maximum.");
            if (state.CoreEnergy > config.Stats.MaxCoreEnergy) throw new InvalidOperationException("Character state core energy exceeds the configured maximum.");
            if (state.UltimateEnergy > config.Stats.MaxUltimateEnergy) throw new InvalidOperationException("Character state ultimate energy exceeds the configured maximum.");
            if (state.IsGrounded && state.Position.Y != 0L) throw new InvalidOperationException("A grounded character must be on the simulation ground plane.");
            if (state.ActionKind == CharacterActionKind.None && state.ActionElapsedTicks != 0) throw new InvalidOperationException("A character without an action cannot have elapsed action ticks.");
            if (state.ActionKind != CharacterActionKind.None && state.ActionElapsedTicks >= config.GetAction(state.ActionKind).TotalTicks) throw new InvalidOperationException("Character action elapsed ticks exceed the configured action duration.");
            if (state.LightAttackBufferRemainingTicks > config.Combat.AttackBufferTicks) throw new InvalidOperationException("Character light attack buffer exceeds the configured duration.");
            if (state.AttackHoldConsumed && state.AttackChargeTicks != 0) throw new InvalidOperationException("A consumed attack hold cannot retain charge ticks.");
            if (state.UsesMovingAttackVariant && !IsNormalAttack(state.ActionKind)) throw new InvalidOperationException("Only a normal attack may retain the moving attack animation variant.");
        }

        /// <summary>准备阶段递减冷却、维护连击超时并采集攻击蓄力时长。</summary>
        private static void Prepare(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config)
        {
            state.DodgeCooldownRemainingTicks = Decrement(state.DodgeCooldownRemainingTicks);
            state.AttackCooldownRemainingTicks = Decrement(state.AttackCooldownRemainingTicks);
            state.HeavyAttackCooldownRemainingTicks = Decrement(state.HeavyAttackCooldownRemainingTicks);
            state.SkillCooldownRemainingTicks = Decrement(state.SkillCooldownRemainingTicks);
            state.UltimateCooldownRemainingTicks = Decrement(state.UltimateCooldownRemainingTicks);
            if (state.Hp == 0)
            {
                state.AttackChargeTicks = 0;
                state.AttackHoldConsumed = false;
                state.LightAttackBufferRemainingTicks = 0;
                state.ComboTimeoutRemainingTicks = 0;
                state.NextAttackComboIndex = 0;
                state.LocomotionState = CharacterLocomotionState.Dead;
                return;
            }
            if (!state.IsGrounded)
            {
                state.AttackChargeTicks = 0;
                state.LightAttackBufferRemainingTicks = 0;
                if (command.AttackReleased) state.AttackHoldConsumed = false;
                return;
            }
            CaptureAttackInput(state, command, config);
        }

        /// <summary>动作仲裁阶段按终结技、技能、闪避、跳跃、蓄力重击和普攻的稳定优先级选择至多一个行为。</summary>
        private static void ArbitrateAction(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config, List<CharacterEvent> events)
        {
            if (state.Hp == 0 || state.ActionKind != CharacterActionKind.None || !state.IsGrounded) return;
            if (command.UltimatePressed && state.UltimateCooldownRemainingTicks == 0 && CanPay(state, config.GetAction(CharacterActionKind.Ultimate)))
            {
                StartAction(state, command, config, CharacterActionKind.Ultimate, events);
                return;
            }
            if (command.SkillPressed && state.SkillCooldownRemainingTicks == 0 && CanPay(state, config.GetAction(CharacterActionKind.Skill)))
            {
                StartAction(state, command, config, CharacterActionKind.Skill, events);
                return;
            }
            if (command.DodgePressed && state.DodgeCooldownRemainingTicks == 0)
            {
                StartAction(state, command, config, command.DodgeBackward ? CharacterActionKind.DodgeBackward : CharacterActionKind.DodgeForward, events);
                return;
            }
            if (command.JumpPressed)
            {
                state.IsGrounded = false;
                state.VerticalVelocityRaw = config.Locomotion.JumpSpeedRaw;
                state.VerticalAccelerationRemainder = 0L;
                state.VerticalPositionRemainder = 0L;
                state.LocomotionState = CharacterLocomotionState.Jump;
                state.JumpStartedThisTick = true;
                state.AttackChargeTicks = 0;
                events.Add(new CharacterEvent(state.Tick, CharacterEventType.Jumped, CharacterActionKind.None, 0, 0));
                return;
            }
            if (command.AttackHeld && !command.AttackReleased && !state.AttackHoldConsumed && state.AttackChargeTicks >= config.Combat.HeavyAttackChargeTicks && state.HeavyAttackCooldownRemainingTicks == 0 && CanPay(state, config.GetAction(CharacterActionKind.HeavyAttack)))
            {
                StartAction(state, command, config, CharacterActionKind.HeavyAttack, events);
                return;
            }
            if (state.LightAttackBufferRemainingTicks > 0 && state.AttackCooldownRemainingTicks == 0)
            {
                StartAction(state, command, config, GetComboAction(state.NextAttackComboIndex), events);
            }
        }

        /// <summary>按最终按键状态解释同 Tick 的按下与释放边沿，累计未消费蓄力，并把有效轻击释放写入可回滚单槽缓冲。</summary>
        private static void CaptureAttackInput(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config)
        {
            bool releasePrecedesPress = command.AttackReleased && command.AttackPressed && command.AttackHeld;
            if (releasePrecedesPress) ReleaseAttackHold(state, config);
            if (command.AttackPressed && !state.AttackHoldConsumed) state.AttackChargeTicks = 1;
            else if (command.AttackHeld && !state.AttackHoldConsumed && state.ActionKind == CharacterActionKind.None) state.AttackChargeTicks = checked(state.AttackChargeTicks + 1);
            bool implicitRelease = state.AttackChargeTicks > 0 && !command.AttackHeld && !command.AttackPressed;
            if (!releasePrecedesPress && (command.AttackReleased || implicitRelease)) ReleaseAttackHold(state, config);
        }

        /// <summary>结束当前物理按住；只有尚未消费为重击且确实累计过蓄力的释放才刷新轻击缓冲。</summary>
        private static void ReleaseAttackHold(CharacterMutableState state, CharacterRuntimeConfig config)
        {
            if (!state.AttackHoldConsumed && state.AttackChargeTicks > 0)
            {
                state.LightAttackBufferRemainingTicks = config.Combat.AttackBufferTicks;
                state.LightAttackBufferRefreshedThisTick = true;
            }
            state.AttackChargeTicks = 0;
            state.AttackHoldConsumed = false;
        }

        /// <summary>在本 Tick 所有动作仲裁结束后老化未消费缓冲；新写入的缓冲从下一个 Tick 才开始扣减。</summary>
        private static void AgeLightAttackBuffer(CharacterMutableState state)
        {
            if (state.LightAttackBufferRemainingTicks > 0 && !state.LightAttackBufferRefreshedThisTick) state.LightAttackBufferRemainingTicks--;
        }

        /// <summary>运动阶段积分普通移动、动作配置位移、跳跃速度和重力，并在固定地面平面执行落地检测。</summary>
        private static void Motion(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config)
        {
            if (state.Hp == 0) return;
            if (state.ActionKind != CharacterActionKind.None) ApplyActionMotion(state, config.GetAction(state.ActionKind));
            else ApplyFreeHorizontalMotion(state, command, config);
            if (!state.IsGrounded) ApplyVerticalMotion(state, config);
            else
            {
                state.VerticalVelocityRaw = 0L;
                state.VerticalAccelerationRemainder = 0L;
                state.VerticalPositionRemainder = 0L;
            }
        }

        /// <summary>结算阶段生成命中窗口事件、消费权威命中确认、处理无敌伤害并在落地后启动配置硬直。</summary>
        private static void Resolve(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config, CharacterTickContext context, List<CharacterEvent> events)
        {
            CharacterActionRuntimeConfig action = state.ActionKind == CharacterActionKind.None ? null : config.GetAction(state.ActionKind);
            state.IsInvincible = action != null && action.IsInvincibleAt(state.ActionElapsedTicks);
            if (action != null && action.HasHitWindow && state.ActionElapsedTicks == action.WindupTicks) events.Add(new CharacterEvent(state.Tick, CharacterEventType.HitWindowOpened, action.Kind, action.Id, action.DamagePermille));
            if (action != null && action.HasHitWindow && state.ActionElapsedTicks == action.WindupTicks + action.ActiveTicks) events.Add(new CharacterEvent(state.Tick, CharacterEventType.HitWindowClosed, action.Kind, action.Id, 0));
            if (action != null && action.HasHitWindow && action.RecoveryTicks == 0 && state.ActionElapsedTicks == action.TotalTicks - 1) events.Add(new CharacterEvent(state.Tick, CharacterEventType.HitWindowClosed, action.Kind, action.Id, 0));
            if (action != null && action.GetPhaseAt(state.ActionElapsedTicks) == CharacterActionPhase.Active && context.ConfirmedHitCount > 0) ApplyConfirmedHits(state, action, config, context.ConfirmedHitCount, events);
            if (context.IncomingDamage > 0) ApplyIncomingDamage(state, context.IncomingDamage, config, events);
            if (state.LandedThisTick && state.Hp > 0)
            {
                events.Add(new CharacterEvent(state.Tick, CharacterEventType.Landed, CharacterActionKind.None, 0, 0));
                StartAction(state, command, config, CharacterActionKind.Land, events);
                state.LocomotionState = CharacterLocomotionState.Land;
            }
        }

        /// <summary>提交阶段推进或结束当前动作，并将连击接续窗口设置在普攻动作真正结束之后。</summary>
        private static void Commit(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config, List<CharacterEvent> events)
        {
            if (state.Hp == 0)
            {
                state.LocomotionState = CharacterLocomotionState.Dead;
                state.IsInvincible = false;
                return;
            }
            if (state.ActionKind == CharacterActionKind.None)
            {
                if (state.ComboTimeoutRemainingTicks > 0)
                {
                    state.ComboTimeoutRemainingTicks--;
                    if (state.ComboTimeoutRemainingTicks == 0) state.NextAttackComboIndex = 0;
                }
                state.IsInvincible = false;
                return;
            }
            CharacterActionRuntimeConfig action = config.GetAction(state.ActionKind);
            if (state.ActionElapsedTicks + 1 >= action.TotalTicks)
            {
                CharacterActionKind endedKind = state.ActionKind;
                events.Add(new CharacterEvent(state.Tick, CharacterEventType.ActionEnded, endedKind, action.Id, 0));
                state.ActionKind = CharacterActionKind.None;
                state.ActionElapsedTicks = 0;
                state.ActionDirectionXRaw = state.FacingXRaw;
                state.ActionDirectionZRaw = state.FacingZRaw;
                state.UsesMovingAttackVariant = false;
                state.IsInvincible = false;
                state.LocomotionState = CharacterLocomotionState.Idle;
                if (IsNormalAttack(endedKind)) state.ComboTimeoutRemainingTicks = config.Combat.ComboTimeoutTicks;
                return;
            }
            state.ActionElapsedTicks++;
            state.IsInvincible = action.IsInvincibleAt(state.ActionElapsedTicks);
        }

        /// <summary>开始一个动作并冻结方向、消费资源、设置动作组冷却及输出开始事件。</summary>
        private static void StartAction(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config, CharacterActionKind kind, List<CharacterEvent> events)
        {
            CharacterActionRuntimeConfig action = config.GetAction(kind);
            if (!CanPay(state, action)) throw new InvalidOperationException($"Character cannot pay the configured energy cost for action {kind}.");
            GetActionDirection(state, command, kind, out long directionXRaw, out long directionZRaw);
            state.ActionKind = kind;
            state.ActionElapsedTicks = 0;
            state.ActionDirectionXRaw = directionXRaw;
            state.ActionDirectionZRaw = directionZRaw;
            state.UsesMovingAttackVariant = IsNormalAttack(kind) && command.HasMovement;
            state.IsInvincible = action.IsInvincibleAt(0);
            state.CoreEnergy -= action.CoreEnergyCost;
            state.UltimateEnergy -= action.UltimateEnergyCost;
            if (IsNormalAttack(kind) || kind == CharacterActionKind.HeavyAttack)
            {
                state.AttackChargeTicks = 0;
                state.LightAttackBufferRemainingTicks = 0;
            }
            if (kind == CharacterActionKind.HeavyAttack) state.AttackHoldConsumed = true;
            state.ComboTimeoutRemainingTicks = 0;
            SetCooldown(state, kind, action.CooldownTicks);
            if (IsNormalAttack(kind)) state.NextAttackComboIndex = (state.NextAttackComboIndex + 1) % 4;
            state.LocomotionState = kind == CharacterActionKind.Land ? CharacterLocomotionState.Land : CharacterLocomotionState.Idle;
            events.Add(new CharacterEvent(state.Tick, CharacterEventType.ActionStarted, kind, action.Id, 0));
        }

        /// <summary>根据输入或既有朝向确定动作期间不可改变的八方向向量。</summary>
        private static void GetActionDirection(CharacterMutableState state, CharacterCommand command, CharacterActionKind kind, out long directionXRaw, out long directionZRaw)
        {
            if (kind == CharacterActionKind.DodgeBackward)
            {
                directionXRaw = -state.FacingXRaw;
                directionZRaw = -state.FacingZRaw;
                return;
            }
            if (command.HasMovement)
            {
                CharacterFixedPoint.GetNormalizedDirection(command.MoveX, command.MoveZ, out directionXRaw, out directionZRaw);
                state.FacingXRaw = directionXRaw;
                state.FacingZRaw = directionZRaw;
                return;
            }
            directionXRaw = state.FacingXRaw;
            directionZRaw = state.FacingZRaw;
        }

        /// <summary>按照累计位移差值分摊动作位移，确保最后一个动作移动 Tick 后总和严格等于配置位移。</summary>
        private static void ApplyActionMotion(CharacterMutableState state, CharacterActionRuntimeConfig action)
        {
            int elapsed = state.ActionElapsedTicks;
            if (elapsed < action.MotionStartTick || elapsed >= action.MotionEndTick || action.ForwardDisplacementRaw == 0L) return;
            int motionTickCount = action.MotionEndTick - action.MotionStartTick;
            int progress = elapsed - action.MotionStartTick;
            long totalX = checked(action.ForwardDisplacementRaw * state.ActionDirectionXRaw / CharacterFixedPoint.DirectionScale);
            long totalZ = checked(action.ForwardDisplacementRaw * state.ActionDirectionZRaw / CharacterFixedPoint.DirectionScale);
            long previousX = checked(totalX * progress / motionTickCount);
            long previousZ = checked(totalZ * progress / motionTickCount);
            long currentX = checked(totalX * (progress + 1) / motionTickCount);
            long currentZ = checked(totalZ * (progress + 1) / motionTickCount);
            state.PositionX = checked(state.PositionX + currentX - previousX);
            state.PositionZ = checked(state.PositionZ + currentZ - previousZ);
        }

        /// <summary>积分地面或空中的自由八方向移动，并更新朝向和移动表现状态。</summary>
        private static void ApplyFreeHorizontalMotion(CharacterMutableState state, CharacterCommand command, CharacterRuntimeConfig config)
        {
            if (!command.HasMovement)
            {
                state.HorizontalRemainderX = 0L;
                state.HorizontalRemainderZ = 0L;
                if (state.IsGrounded) state.LocomotionState = CharacterLocomotionState.Idle;
                return;
            }
            CharacterFixedPoint.GetNormalizedDirection(command.MoveX, command.MoveZ, out long directionXRaw, out long directionZRaw);
            state.FacingXRaw = directionXRaw;
            state.FacingZRaw = directionZRaw;
            long speedRaw = state.IsGrounded ? GetGroundSpeed(config.Locomotion, command.RequestedMoveMode) : config.Locomotion.AirMoveSpeedRaw;
            long denominator = checked(CharacterFixedPoint.DirectionScale * config.TickRate);
            long numeratorX = checked(speedRaw * directionXRaw + state.HorizontalRemainderX);
            long numeratorZ = checked(speedRaw * directionZRaw + state.HorizontalRemainderZ);
            long deltaX = Math.DivRem(numeratorX, denominator, out long remainderX);
            long deltaZ = Math.DivRem(numeratorZ, denominator, out long remainderZ);
            state.PositionX = checked(state.PositionX + deltaX);
            state.PositionZ = checked(state.PositionZ + deltaZ);
            state.HorizontalRemainderX = remainderX;
            state.HorizontalRemainderZ = remainderZ;
            if (state.IsGrounded) state.LocomotionState = GetGroundLocomotion(command.RequestedMoveMode);
        }

        /// <summary>使用半隐式固定 Tick 规则积分跳跃位置和重力，并检测 Y 等于零的模拟平面。</summary>
        private static void ApplyVerticalMotion(CharacterMutableState state, CharacterRuntimeConfig config)
        {
            long positionNumerator = checked(state.VerticalVelocityRaw + state.VerticalPositionRemainder);
            long deltaY = Math.DivRem(positionNumerator, config.TickRate, out long positionRemainder);
            state.PositionY = checked(state.PositionY + deltaY);
            state.VerticalPositionRemainder = positionRemainder;
            long accelerationNumerator = checked(config.Locomotion.GravityRaw + state.VerticalAccelerationRemainder);
            long velocityDelta = Math.DivRem(accelerationNumerator, config.TickRate, out long accelerationRemainder);
            state.VerticalVelocityRaw = checked(state.VerticalVelocityRaw - velocityDelta);
            state.VerticalAccelerationRemainder = accelerationRemainder;
            if (state.PositionY <= 0L && state.VerticalVelocityRaw <= 0L)
            {
                state.PositionY = 0L;
                state.VerticalVelocityRaw = 0L;
                state.VerticalAccelerationRemainder = 0L;
                state.VerticalPositionRemainder = 0L;
                state.IsGrounded = true;
                state.LandedThisTick = true;
                state.LocomotionState = CharacterLocomotionState.Land;
                return;
            }
            if (!state.JumpStartedThisTick) state.LocomotionState = state.VerticalVelocityRaw > 0L ? CharacterLocomotionState.Rise : CharacterLocomotionState.Fall;
        }

        /// <summary>把当前动作的命中确认转化为受上限约束的双能量增长。</summary>
        private static void ApplyConfirmedHits(CharacterMutableState state, CharacterActionRuntimeConfig action, CharacterRuntimeConfig config, int confirmedHitCount, List<CharacterEvent> events)
        {
            int oldCoreEnergy = state.CoreEnergy;
            int oldUltimateEnergy = state.UltimateEnergy;
            long coreGain = checked((long)action.CoreEnergyGainOnConfirmedHit * confirmedHitCount);
            long ultimateGain = checked((long)action.UltimateEnergyGainOnConfirmedHit * confirmedHitCount);
            state.CoreEnergy = (int)Math.Min(config.Stats.MaxCoreEnergy, oldCoreEnergy + coreGain);
            state.UltimateEnergy = (int)Math.Min(config.Stats.MaxUltimateEnergy, oldUltimateEnergy + ultimateGain);
            events.Add(new CharacterEvent(state.Tick, CharacterEventType.HitConfirmed, action.Kind, action.Id, confirmedHitCount));
            if (state.CoreEnergy != oldCoreEnergy || state.UltimateEnergy != oldUltimateEnergy) events.Add(new CharacterEvent(state.Tick, CharacterEventType.EnergyChanged, action.Kind, action.Id, state.CoreEnergy - oldCoreEnergy));
        }

        /// <summary>在当前 Tick 的动作无敌判定之后应用权威世界已经计算完成的非负伤害。</summary>
        private static void ApplyIncomingDamage(CharacterMutableState state, int incomingDamage, CharacterRuntimeConfig config, List<CharacterEvent> events)
        {
            if (state.Hp == 0) return;
            if (state.IsInvincible)
            {
                events.Add(new CharacterEvent(state.Tick, CharacterEventType.DamageIgnored, state.ActionKind, GetActionId(config, state.ActionKind), incomingDamage));
                return;
            }
            int actualDamage = Math.Min(state.Hp, incomingDamage);
            state.Hp -= actualDamage;
            events.Add(new CharacterEvent(state.Tick, CharacterEventType.DamageTaken, state.ActionKind, GetActionId(config, state.ActionKind), actualDamage));
            if (state.Hp > 0) return;
            if (state.ActionKind != CharacterActionKind.None)
            {
                CharacterActionRuntimeConfig interruptedAction = config.GetAction(state.ActionKind);
                events.Add(new CharacterEvent(state.Tick, CharacterEventType.ActionEnded, interruptedAction.Kind, interruptedAction.Id, -1));
            }
            state.ActionKind = CharacterActionKind.None;
            state.ActionElapsedTicks = 0;
            state.AttackChargeTicks = 0;
            state.AttackHoldConsumed = false;
            state.LightAttackBufferRemainingTicks = 0;
            state.UsesMovingAttackVariant = false;
            state.ComboTimeoutRemainingTicks = 0;
            state.NextAttackComboIndex = 0;
            state.IsInvincible = false;
            state.LocomotionState = CharacterLocomotionState.Dead;
            events.Add(new CharacterEvent(state.Tick, CharacterEventType.Died, CharacterActionKind.None, 0, 0));
        }

        /// <summary>根据动作种类设置对应共享冷却组。</summary>
        private static void SetCooldown(CharacterMutableState state, CharacterActionKind kind, int cooldownTicks)
        {
            if (kind == CharacterActionKind.DodgeForward || kind == CharacterActionKind.DodgeBackward) state.DodgeCooldownRemainingTicks = Math.Max(state.DodgeCooldownRemainingTicks, cooldownTicks);
            else if (IsNormalAttack(kind)) state.AttackCooldownRemainingTicks = Math.Max(state.AttackCooldownRemainingTicks, cooldownTicks);
            else if (kind == CharacterActionKind.HeavyAttack) state.HeavyAttackCooldownRemainingTicks = Math.Max(state.HeavyAttackCooldownRemainingTicks, cooldownTicks);
            else if (kind == CharacterActionKind.Skill) state.SkillCooldownRemainingTicks = Math.Max(state.SkillCooldownRemainingTicks, cooldownTicks);
            else if (kind == CharacterActionKind.Ultimate) state.UltimateCooldownRemainingTicks = Math.Max(state.UltimateCooldownRemainingTicks, cooldownTicks);
        }

        /// <summary>判断角色当前双能量是否足以支付指定动作。</summary>
        private static bool CanPay(CharacterMutableState state, CharacterActionRuntimeConfig action)
        {
            return state.CoreEnergy >= action.CoreEnergyCost && state.UltimateEnergy >= action.UltimateEnergyCost;
        }

        /// <summary>把零起始连段索引映射到四段普攻动作。</summary>
        private static CharacterActionKind GetComboAction(int comboIndex)
        {
            if (comboIndex == 0) return CharacterActionKind.Attack1;
            if (comboIndex == 1) return CharacterActionKind.Attack2;
            if (comboIndex == 2) return CharacterActionKind.Attack3;
            if (comboIndex == 3) return CharacterActionKind.Attack4;
            throw new ArgumentOutOfRangeException(nameof(comboIndex), "Attack combo index must be between 0 and 3.");
        }

        /// <summary>判断指定动作是否属于共享四段普攻冷却和连击组。</summary>
        private static bool IsNormalAttack(CharacterActionKind kind)
        {
            return kind >= CharacterActionKind.Attack1 && kind <= CharacterActionKind.Attack4;
        }

        /// <summary>获取地面移动档位对应的每秒定点速度。</summary>
        private static long GetGroundSpeed(CharacterLocomotionRuntimeConfig locomotion, CharacterMoveMode mode)
        {
            if (mode == CharacterMoveMode.Walk) return locomotion.WalkSpeedRaw;
            if (mode == CharacterMoveMode.Run) return locomotion.RunSpeedRaw;
            if (mode == CharacterMoveMode.Sprint) return locomotion.SprintSpeedRaw;
            throw new ArgumentOutOfRangeException(nameof(mode), "Unknown character movement mode.");
        }

        /// <summary>获取地面移动档位对应的表现移动状态。</summary>
        private static CharacterLocomotionState GetGroundLocomotion(CharacterMoveMode mode)
        {
            if (mode == CharacterMoveMode.Walk) return CharacterLocomotionState.Walk;
            if (mode == CharacterMoveMode.Run) return CharacterLocomotionState.Run;
            if (mode == CharacterMoveMode.Sprint) return CharacterLocomotionState.Sprint;
            throw new ArgumentOutOfRangeException(nameof(mode), "Unknown character movement mode.");
        }

        /// <summary>获取动作关联的表行编号。</summary>
        private static int GetActionId(CharacterRuntimeConfig config, CharacterActionKind kind)
        {
            return kind == CharacterActionKind.None ? 0 : config.GetAction(kind).Id;
        }

        /// <summary>把大于零的 Tick 计数递减一。</summary>
        private static int Decrement(int value)
        {
            return value > 0 ? value - 1 : 0;
        }

        /// <summary>在全部提交完成后输出一次最终移动状态变化事件。</summary>
        private static void EmitLocomotionEvent(CharacterMutableState state, CharacterLocomotionState previousLocomotion, List<CharacterEvent> events)
        {
            if (state.LocomotionState != previousLocomotion) events.Add(new CharacterEvent(state.Tick, CharacterEventType.LocomotionChanged, state.ActionKind, 0, (int)state.LocomotionState));
        }

        /// <summary>提供单个模拟 Tick 内部使用的可变工作副本，提交后必须冻结为 CharacterState。</summary>
        private sealed class CharacterMutableState
        {
            /// <summary>从不可变状态复制全部回滚字段。</summary>
            public CharacterMutableState(CharacterState state)
            {
                Tick = state.Tick;
                PositionX = state.Position.X;
                PositionY = state.Position.Y;
                PositionZ = state.Position.Z;
                FacingXRaw = state.FacingXRaw;
                FacingZRaw = state.FacingZRaw;
                LocomotionState = state.LocomotionState;
                ActionKind = state.ActionKind;
                ActionElapsedTicks = state.ActionElapsedTicks;
                ActionDirectionXRaw = state.ActionDirectionXRaw;
                ActionDirectionZRaw = state.ActionDirectionZRaw;
                HorizontalRemainderX = state.HorizontalRemainderX;
                HorizontalRemainderZ = state.HorizontalRemainderZ;
                VerticalVelocityRaw = state.VerticalVelocityRaw;
                VerticalAccelerationRemainder = state.VerticalAccelerationRemainder;
                VerticalPositionRemainder = state.VerticalPositionRemainder;
                IsGrounded = state.IsGrounded;
                IsInvincible = state.IsInvincible;
                Hp = state.Hp;
                CoreEnergy = state.CoreEnergy;
                UltimateEnergy = state.UltimateEnergy;
                AttackChargeTicks = state.AttackChargeTicks;
                AttackHoldConsumed = state.AttackHoldConsumed;
                LightAttackBufferRemainingTicks = state.LightAttackBufferRemainingTicks;
                UsesMovingAttackVariant = state.UsesMovingAttackVariant;
                NextAttackComboIndex = state.NextAttackComboIndex;
                ComboTimeoutRemainingTicks = state.ComboTimeoutRemainingTicks;
                DodgeCooldownRemainingTicks = state.DodgeCooldownRemainingTicks;
                AttackCooldownRemainingTicks = state.AttackCooldownRemainingTicks;
                HeavyAttackCooldownRemainingTicks = state.HeavyAttackCooldownRemainingTicks;
                SkillCooldownRemainingTicks = state.SkillCooldownRemainingTicks;
                UltimateCooldownRemainingTicks = state.UltimateCooldownRemainingTicks;
            }

            /// <summary>当前工作 Tick。</summary>
            public int Tick;
            /// <summary>当前 X 轴定点位置。</summary>
            public long PositionX;
            /// <summary>当前 Y 轴定点位置。</summary>
            public long PositionY;
            /// <summary>当前 Z 轴定点位置。</summary>
            public long PositionZ;
            /// <summary>当前朝向 X 轴定点分量。</summary>
            public long FacingXRaw;
            /// <summary>当前朝向 Z 轴定点分量。</summary>
            public long FacingZRaw;
            /// <summary>当前移动状态。</summary>
            public CharacterLocomotionState LocomotionState;
            /// <summary>当前动作种类。</summary>
            public CharacterActionKind ActionKind;
            /// <summary>当前动作内 Tick 索引。</summary>
            public int ActionElapsedTicks;
            /// <summary>动作锁定方向 X 轴定点分量。</summary>
            public long ActionDirectionXRaw;
            /// <summary>动作锁定方向 Z 轴定点分量。</summary>
            public long ActionDirectionZRaw;
            /// <summary>自由移动 X 轴积分余数。</summary>
            public long HorizontalRemainderX;
            /// <summary>自由移动 Z 轴积分余数。</summary>
            public long HorizontalRemainderZ;
            /// <summary>当前竖直速度定点值。</summary>
            public long VerticalVelocityRaw;
            /// <summary>重力速度积分余数。</summary>
            public long VerticalAccelerationRemainder;
            /// <summary>竖直位置积分余数。</summary>
            public long VerticalPositionRemainder;
            /// <summary>当前是否位于模拟地面。</summary>
            public bool IsGrounded;
            /// <summary>当前动作 Tick 是否无敌。</summary>
            public bool IsInvincible;
            /// <summary>当前生命值。</summary>
            public int Hp;
            /// <summary>当前核心能量。</summary>
            public int CoreEnergy;
            /// <summary>当前终结能量。</summary>
            public int UltimateEnergy;
            /// <summary>当前攻击蓄力 Tick 数。</summary>
            public int AttackChargeTicks;
            /// <summary>当前物理按住是否已经消费为一次重击并等待释放。</summary>
            public bool AttackHoldConsumed;
            /// <summary>尚未消费的单槽轻击缓冲剩余 Tick 数。</summary>
            public int LightAttackBufferRemainingTicks;
            /// <summary>当前普攻是否冻结为移动起手动画变体。</summary>
            public bool UsesMovingAttackVariant;
            /// <summary>下一次普攻的连段索引。</summary>
            public int NextAttackComboIndex;
            /// <summary>剩余连击接续 Tick 数。</summary>
            public int ComboTimeoutRemainingTicks;
            /// <summary>剩余闪避冷却 Tick 数。</summary>
            public int DodgeCooldownRemainingTicks;
            /// <summary>剩余普攻冷却 Tick 数。</summary>
            public int AttackCooldownRemainingTicks;
            /// <summary>剩余蓄力重击冷却 Tick 数。</summary>
            public int HeavyAttackCooldownRemainingTicks;
            /// <summary>剩余普通技能冷却 Tick 数。</summary>
            public int SkillCooldownRemainingTicks;
            /// <summary>剩余终结技能冷却 Tick 数。</summary>
            public int UltimateCooldownRemainingTicks;
            /// <summary>当前 Tick 是否刚刚执行起跳仲裁。</summary>
            public bool JumpStartedThisTick;
            /// <summary>当前 Tick 是否首次接触模拟地面。</summary>
            public bool LandedThisTick;
            /// <summary>当前 Tick 是否刚写入轻击缓冲，用于避免在同一 Tick 立即扣减新缓冲。</summary>
            public bool LightAttackBufferRefreshedThisTick;

            /// <summary>把当前工作副本冻结为可跨网络完整序列化的不可变状态。</summary>
            public CharacterState Freeze()
            {
                return new CharacterState(Tick, new FixedVector3(PositionX, PositionY, PositionZ), FacingXRaw, FacingZRaw, LocomotionState, ActionKind, ActionElapsedTicks, ActionDirectionXRaw, ActionDirectionZRaw, HorizontalRemainderX, HorizontalRemainderZ, VerticalVelocityRaw, VerticalAccelerationRemainder, VerticalPositionRemainder, IsGrounded, IsInvincible, Hp, CoreEnergy, UltimateEnergy, AttackChargeTicks, AttackHoldConsumed, LightAttackBufferRemainingTicks, UsesMovingAttackVariant, NextAttackComboIndex, ComboTimeoutRemainingTicks, DodgeCooldownRemainingTicks, AttackCooldownRemainingTicks, HeavyAttackCooldownRemainingTicks, SkillCooldownRemainingTicks, UltimateCooldownRemainingTicks);
            }
        }
    }
}
