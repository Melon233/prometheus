using System;
using PromeArchTrial.Core.Networking;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.World;

namespace PromeArchTrial.Game.Networking
{
    /// <summary>
    /// 在纯 C# 角色领域对象与不依赖 GameNative 的协议 DTO 之间执行逐字段显式映射。
    /// </summary>
    public static class CharacterNetworkMapper
    {
        /// <summary>把一个完整角色状态转换为 Protobuf 边界使用的稳定网络状态。</summary>
        public static CharacterNetworkState ToNetworkState(CharacterState state)
        {
            return new CharacterNetworkState(state.Tick, state.Position.X, state.Position.Y, state.Position.Z, state.FacingXRaw, state.FacingZRaw, (int)state.LocomotionState, (int)state.ActionKind, state.ActionElapsedTicks, state.ActionDirectionXRaw, state.ActionDirectionZRaw, state.HorizontalRemainderX, state.HorizontalRemainderZ, state.VerticalVelocityRaw, state.VerticalAccelerationRemainder, state.VerticalPositionRemainder, state.IsGrounded, state.IsInvincible, state.Hp, state.CoreEnergy, state.UltimateEnergy, state.AttackChargeTicks, state.AttackHoldConsumed, state.LightAttackBufferRemainingTicks, state.UsesMovingAttackVariant, state.NextAttackComboIndex, state.ComboTimeoutRemainingTicks, state.DodgeCooldownRemainingTicks, state.AttackCooldownRemainingTicks, state.HeavyAttackCooldownRemainingTicks, state.SkillCooldownRemainingTicks, state.UltimateCooldownRemainingTicks);
        }

        /// <summary>校验网络枚举并从完整网络状态恢复可直接参与预测重放的角色状态。</summary>
        public static CharacterState ToCharacterState(CharacterNetworkState state)
        {
            if (!Enum.IsDefined(typeof(CharacterLocomotionState), state.LocomotionState)) throw new ArgumentOutOfRangeException(nameof(state), $"Unknown network locomotion state {state.LocomotionState}.");
            if (!Enum.IsDefined(typeof(CharacterActionKind), state.ActionKind)) throw new ArgumentOutOfRangeException(nameof(state), $"Unknown network action kind {state.ActionKind}.");
            FixedVector3 position = new FixedVector3(state.PositionX, state.PositionY, state.PositionZ);
            return new CharacterState(state.Tick, position, state.FacingXRaw, state.FacingZRaw, (CharacterLocomotionState)state.LocomotionState, (CharacterActionKind)state.ActionKind, state.ActionElapsedTicks, state.ActionDirectionXRaw, state.ActionDirectionZRaw, state.HorizontalRemainderX, state.HorizontalRemainderZ, state.VerticalVelocityRaw, state.VerticalAccelerationRemainder, state.VerticalPositionRemainder, state.IsGrounded, state.IsInvincible, state.Hp, state.CoreEnergy, state.UltimateEnergy, state.AttackChargeTicks, state.AttackHoldConsumed, state.LightAttackBufferRemainingTicks, state.UsesMovingAttackVariant, state.NextAttackComboIndex, state.ComboTimeoutRemainingTicks, state.DodgeCooldownRemainingTicks, state.AttackCooldownRemainingTicks, state.HeavyAttackCooldownRemainingTicks, state.SkillCooldownRemainingTicks, state.UltimateCooldownRemainingTicks);
        }

        /// <summary>把一个角色命令及其 Tick 后预测状态编码为客户端输入协议消息。</summary>
        public static ClientInputMessage ToClientInputMessage(CharacterCommand command, CharacterState predictedState)
        {
            if (command.Tick != predictedState.Tick) throw new ArgumentException("Predicted state tick must equal its command tick.", nameof(predictedState));
            CharacterInputButtons buttons = CharacterInputButtons.None;
            if (command.JumpPressed) buttons |= CharacterInputButtons.JumpPressed;
            if (command.DodgePressed) buttons |= CharacterInputButtons.DodgePressed;
            if (command.DodgeBackward) buttons |= CharacterInputButtons.DodgeBackward;
            if (command.AttackPressed) buttons |= CharacterInputButtons.AttackPressed;
            if (command.AttackHeld) buttons |= CharacterInputButtons.AttackHeld;
            if (command.AttackReleased) buttons |= CharacterInputButtons.AttackReleased;
            if (command.SkillPressed) buttons |= CharacterInputButtons.SkillPressed;
            if (command.UltimatePressed) buttons |= CharacterInputButtons.UltimatePressed;
            return new ClientInputMessage(command.Tick, command.MoveX, command.MoveZ, (int)command.RequestedMoveMode, buttons, ToNetworkState(predictedState));
        }

        /// <summary>把经过协议边界校验的客户端输入消息恢复为共享角色命令。</summary>
        public static CharacterCommand ToCharacterCommand(ClientInputMessage message)
        {
            if (!Enum.IsDefined(typeof(CharacterMoveMode), message.RequestedMoveMode)) throw new ArgumentOutOfRangeException(nameof(message), $"Unknown network move mode {message.RequestedMoveMode}.");
            CharacterInputButtons buttons = message.InputButtons;
            return new CharacterCommand(message.ClientTick, message.MoveX, message.MoveZ, (CharacterMoveMode)message.RequestedMoveMode, Has(buttons, CharacterInputButtons.JumpPressed), Has(buttons, CharacterInputButtons.DodgePressed), Has(buttons, CharacterInputButtons.DodgeBackward), Has(buttons, CharacterInputButtons.AttackPressed), Has(buttons, CharacterInputButtons.AttackHeld), Has(buttons, CharacterInputButtons.AttackReleased), Has(buttons, CharacterInputButtons.SkillPressed), Has(buttons, CharacterInputButtons.UltimatePressed));
        }

        /// <summary>创建包含完整角色状态的服务器权威快照协议消息。</summary>
        public static ServerSnapshotMessage ToServerSnapshotMessage(int serverTick, int acknowledgedClientTick, CharacterState state)
        {
            return new ServerSnapshotMessage(serverTick, acknowledgedClientTick, ToNetworkState(state));
        }

        /// <summary>把权威世界事件转换为不依赖 GameNative 的 Protobuf 边界领域值。</summary>
        public static BattleEventMessage ToBattleEventMessage(WorldEvent worldEvent)
        {
            int characterEventType = worldEvent.Kind == WorldEventKind.Character ? (int)worldEvent.CharacterEvent.Type : 0;
            return new BattleEventMessage((BattleEventKind)worldEvent.Kind, worldEvent.SourceEntityId, worldEvent.TargetEntityId, worldEvent.WorldTick, worldEvent.Ordinal, characterEventType, (int)worldEvent.ActionKind, worldEvent.ActionId, worldEvent.Value, worldEvent.IsCritical);
        }

        /// <summary>判断指定稳定输入比特位是否存在。</summary>
        private static bool Has(CharacterInputButtons buttons, CharacterInputButtons expected)
        {
            return (buttons & expected) != CharacterInputButtons.None;
        }
    }
}
