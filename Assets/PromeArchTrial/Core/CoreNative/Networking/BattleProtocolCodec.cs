using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using PromeArchTrial.Core.Networking.Protobuf;

namespace PromeArchTrial.Core.Networking
{
    /// <summary>
    /// 使用共享 Protobuf Envelope 编解码全部业务消息，并使用固定四字节长度前缀处理 TCP 半包与粘包。
    /// </summary>
    public static class BattleProtocolCodec
    {
        private const int FrameHeaderLength = 4;

        /// <summary>把客户端握手领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ClientHelloMessage message)
        {
            ClientHelloPayload payload = new ClientHelloPayload { ProtocolVersion = message.ProtocolVersion, ConfigHash = message.ConfigHash };
            return SerializeEnvelope(new BattleEnvelope { ClientHello = payload });
        }

        /// <summary>把服务器握手确认领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ServerWelcomeMessage message)
        {
            ServerWelcomePayload payload = new ServerWelcomePayload { PlayerId = message.PlayerId, EntityId = message.EntityId, CharacterId = message.CharacterId, ServerTick = message.ServerTick, TickRate = message.TickRate, ConfigHash = message.ConfigHash, InitialState = EncodeCharacterState(message.InitialState) };
            return SerializeEnvelope(new BattleEnvelope { ServerWelcome = payload });
        }

        /// <summary>把客户端固定 Tick 输入领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ClientInputMessage message)
        {
            ClientInputPayload payload = new ClientInputPayload { ClientTick = message.ClientTick, MoveX = message.MoveX, MoveZ = message.MoveZ, RequestedMoveMode = message.RequestedMoveMode, InputButtons = (uint)message.InputButtons, PredictedState = EncodeCharacterState(message.PredictedState) };
            return SerializeEnvelope(new BattleEnvelope { ClientInput = payload });
        }

        /// <summary>把服务器权威状态快照领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ServerSnapshotMessage message)
        {
            ServerSnapshotPayload payload = new ServerSnapshotPayload { ServerTick = message.ServerTick, AcknowledgedClientTick = message.AcknowledgedClientTick, State = EncodeCharacterState(message.State) };
            for (int index = 0; index < message.Events.Count; index++) payload.Events.Add(EncodeBattleEvent(message.Events[index]));
            return SerializeEnvelope(new BattleEnvelope { ServerSnapshot = payload });
        }

        /// <summary>把服务器拒绝领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ServerRejectMessage message)
        {
            ServerRejectPayload payload = new ServerRejectPayload { Reason = (ServerRejectReasonCode)(int)message.Reason };
            return SerializeEnvelope(new BattleEnvelope { ServerReject = payload });
        }

        /// <summary>把独立于模拟 Tick 的客户端 Ping 领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ClientPingMessage message)
        {
            ClientPingPayload payload = new ClientPingPayload { Sequence = message.Sequence };
            return SerializeEnvelope(new BattleEnvelope { ClientPing = payload });
        }

        /// <summary>把服务器立即返回的 Pong 领域消息映射为 Protobuf Envelope。</summary>
        public static byte[] Encode(ServerPongMessage message)
        {
            ServerPongPayload payload = new ServerPongPayload { Sequence = message.Sequence };
            return SerializeEnvelope(new BattleEnvelope { ServerPong = payload });
        }

        /// <summary>解析 Protobuf Envelope 的 oneof 分支并返回稳定业务消息类型。</summary>
        public static BattleMessageType ReadMessageType(byte[] payload)
        {
            return GetMessageType(ParseEnvelope(payload));
        }

        /// <summary>只解析一次 Protobuf Envelope，并立即转换为不依赖 Protobuf 运行时的只读领域消息。</summary>
        public static DecodedBattleMessage DecodeFrame(byte[] payload)
        {
            BattleEnvelope envelope = ParseEnvelope(payload);
            switch (envelope.PayloadCase)
            {
                case BattleEnvelope.PayloadOneofCase.ClientHello:
                    ClientHelloPayload clientHello = envelope.ClientHello;
                    return new DecodedBattleMessage(BattleMessageType.ClientHello, new ClientHelloMessage(clientHello.ProtocolVersion, clientHello.ConfigHash));
                case BattleEnvelope.PayloadOneofCase.ServerWelcome:
                    ServerWelcomePayload serverWelcome = envelope.ServerWelcome;
                    return new DecodedBattleMessage(BattleMessageType.ServerWelcome, new ServerWelcomeMessage(serverWelcome.PlayerId, serverWelcome.EntityId, serverWelcome.CharacterId, serverWelcome.ServerTick, serverWelcome.TickRate, serverWelcome.ConfigHash, DecodeCharacterState(serverWelcome.InitialState)));
                case BattleEnvelope.PayloadOneofCase.ClientInput:
                    ClientInputPayload clientInput = envelope.ClientInput;
                    return new DecodedBattleMessage(BattleMessageType.ClientInput, new ClientInputMessage(clientInput.ClientTick, DecodeMovementAxis(clientInput.MoveX, nameof(clientInput.MoveX)), DecodeMovementAxis(clientInput.MoveZ, nameof(clientInput.MoveZ)), DecodeRequestedMoveMode(clientInput.RequestedMoveMode), DecodeInputButtons(clientInput.InputButtons), DecodeCharacterState(clientInput.PredictedState)));
                case BattleEnvelope.PayloadOneofCase.ServerSnapshot:
                    ServerSnapshotPayload serverSnapshot = envelope.ServerSnapshot;
                    return new DecodedBattleMessage(BattleMessageType.ServerSnapshot, new ServerSnapshotMessage(serverSnapshot.ServerTick, serverSnapshot.AcknowledgedClientTick, DecodeCharacterState(serverSnapshot.State), DecodeBattleEvents(serverSnapshot.Events)));
                case BattleEnvelope.PayloadOneofCase.ServerReject:
                    return new DecodedBattleMessage(BattleMessageType.ServerReject, new ServerRejectMessage((ServerRejectReason)(int)envelope.ServerReject.Reason));
                case BattleEnvelope.PayloadOneofCase.ClientPing:
                    return new DecodedBattleMessage(BattleMessageType.ClientPing, new ClientPingMessage(envelope.ClientPing.Sequence));
                case BattleEnvelope.PayloadOneofCase.ServerPong:
                    return new DecodedBattleMessage(BattleMessageType.ServerPong, new ServerPongMessage(envelope.ServerPong.Sequence));
                default:
                    throw new InvalidDataException($"Unsupported Protobuf battle payload case {envelope.PayloadCase}.");
            }
        }

        /// <summary>解码客户端握手 Protobuf Payload，并转换为不依赖序列化库的只读领域消息。</summary>
        public static ClientHelloMessage DecodeClientHello(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ClientHelloMessage>();
        }

        /// <summary>解码服务器握手确认 Protobuf Payload，并转换为不依赖序列化库的只读领域消息。</summary>
        public static ServerWelcomeMessage DecodeServerWelcome(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ServerWelcomeMessage>();
        }

        /// <summary>解码客户端固定 Tick 输入 Protobuf Payload，并严格校验八方向离散输入范围。</summary>
        public static ClientInputMessage DecodeClientInput(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ClientInputMessage>();
        }

        /// <summary>解码服务器权威状态 Protobuf Payload，并转换为不依赖序列化库的只读领域消息。</summary>
        public static ServerSnapshotMessage DecodeServerSnapshot(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ServerSnapshotMessage>();
        }

        /// <summary>解码服务器拒绝 Protobuf Payload，并保留协议中稳定的数值原因。</summary>
        public static ServerRejectMessage DecodeServerReject(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ServerRejectMessage>();
        }

        /// <summary>解码客户端 Ping Protobuf Payload。</summary>
        public static ClientPingMessage DecodeClientPing(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ClientPingMessage>();
        }

        /// <summary>解码服务器 Pong Protobuf Payload。</summary>
        public static ServerPongMessage DecodeServerPong(byte[] payload)
        {
            return DecodeFrame(payload).GetMessage<ServerPongMessage>();
        }

        /// <summary>向 TCP 流写入仅用于传输分帧的四字节小端长度前缀和完整 Protobuf Envelope。</summary>
        public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            ValidatePayloadLength(payload);
            byte[] header = new byte[FrameHeaderLength];
            WriteInt32LittleEndian(header, payload.Length);
            await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>从 TCP 流读取一帧完整 Protobuf Envelope；对端在帧边界正常关闭时返回空。</summary>
        public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            byte[] header = new byte[FrameHeaderLength];
            bool hasFrame = await TryReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
            if (!hasFrame) return null;
            int payloadLength = ReadInt32LittleEndian(header);
            ValidatePayloadLength(payloadLength);
            byte[] payload = new byte[payloadLength];
            bool hasPayload = await TryReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            if (!hasPayload) throw new EndOfStreamException("Remote endpoint closed during a Protobuf battle frame.");
            return payload;
        }

        /// <summary>序列化唯一根 Envelope，并在进入网络队列前限制负载尺寸。</summary>
        private static byte[] SerializeEnvelope(BattleEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.PayloadCase == BattleEnvelope.PayloadOneofCase.None) throw new InvalidDataException("Battle Protobuf envelope does not contain a payload.");
            byte[] payload = envelope.ToByteArray();
            ValidatePayloadLength(payload);
            return payload;
        }

        /// <summary>解析一帧 Protobuf Envelope，并拒绝空负载或没有 oneof 分支的无效消息。</summary>
        private static BattleEnvelope ParseEnvelope(byte[] payload)
        {
            ValidatePayloadLength(payload);
            BattleEnvelope envelope;
            try
            {
                envelope = BattleEnvelope.Parser.ParseFrom(payload);
            }
            catch (InvalidProtocolBufferException exception)
            {
                throw new InvalidDataException("Battle Protobuf envelope is malformed.", exception);
            }
            if (envelope.PayloadCase == BattleEnvelope.PayloadOneofCase.None) throw new InvalidDataException("Battle Protobuf envelope does not contain a recognized payload.");
            return envelope;
        }

        /// <summary>把不依赖 Protobuf 的完整角色网络状态映射为生成的消息对象。</summary>
        private static CharacterStatePayload EncodeCharacterState(CharacterNetworkState state)
        {
            return new CharacterStatePayload { Tick = state.Tick, PositionX = state.PositionX, PositionY = state.PositionY, PositionZ = state.PositionZ, FacingXRaw = state.FacingXRaw, FacingZRaw = state.FacingZRaw, LocomotionState = state.LocomotionState, ActionKind = state.ActionKind, ActionElapsedTicks = state.ActionElapsedTicks, ActionDirectionXRaw = state.ActionDirectionXRaw, ActionDirectionZRaw = state.ActionDirectionZRaw, HorizontalRemainderX = state.HorizontalRemainderX, HorizontalRemainderZ = state.HorizontalRemainderZ, VerticalVelocityRaw = state.VerticalVelocityRaw, VerticalAccelerationRemainder = state.VerticalAccelerationRemainder, VerticalPositionRemainder = state.VerticalPositionRemainder, IsGrounded = state.IsGrounded, IsInvincible = state.IsInvincible, Hp = state.Hp, CoreEnergy = state.CoreEnergy, UltimateEnergy = state.UltimateEnergy, AttackChargeTicks = state.AttackChargeTicks, NextAttackComboIndex = state.NextAttackComboIndex, ComboTimeoutRemainingTicks = state.ComboTimeoutRemainingTicks, DodgeCooldownRemainingTicks = state.DodgeCooldownRemainingTicks, AttackCooldownRemainingTicks = state.AttackCooldownRemainingTicks, HeavyAttackCooldownRemainingTicks = state.HeavyAttackCooldownRemainingTicks, SkillCooldownRemainingTicks = state.SkillCooldownRemainingTicks, UltimateCooldownRemainingTicks = state.UltimateCooldownRemainingTicks, AttackHoldConsumed = state.AttackHoldConsumed, LightAttackBufferRemainingTicks = state.LightAttackBufferRemainingTicks, UsesMovingAttackVariant = state.UsesMovingAttackVariant };
        }

        /// <summary>把生成的完整角色状态消息映射为不依赖 Protobuf 的稳定领域值。</summary>
        private static CharacterNetworkState DecodeCharacterState(CharacterStatePayload state)
        {
            if (state == null) throw new InvalidDataException("Battle message does not contain the required character state.");
            return new CharacterNetworkState(state.Tick, state.PositionX, state.PositionY, state.PositionZ, state.FacingXRaw, state.FacingZRaw, state.LocomotionState, state.ActionKind, state.ActionElapsedTicks, state.ActionDirectionXRaw, state.ActionDirectionZRaw, state.HorizontalRemainderX, state.HorizontalRemainderZ, state.VerticalVelocityRaw, state.VerticalAccelerationRemainder, state.VerticalPositionRemainder, state.IsGrounded, state.IsInvincible, state.Hp, state.CoreEnergy, state.UltimateEnergy, state.AttackChargeTicks, state.AttackHoldConsumed, state.LightAttackBufferRemainingTicks, state.UsesMovingAttackVariant, state.NextAttackComboIndex, state.ComboTimeoutRemainingTicks, state.DodgeCooldownRemainingTicks, state.AttackCooldownRemainingTicks, state.HeavyAttackCooldownRemainingTicks, state.SkillCooldownRemainingTicks, state.UltimateCooldownRemainingTicks);
        }

        /// <summary>把只读领域战斗事件映射为生成的 Protobuf 事件消息。</summary>
        private static BattleEventPayload EncodeBattleEvent(BattleEventMessage message)
        {
            return new BattleEventPayload { Kind = (int)message.Kind, SourceEntityId = message.SourceEntityId, TargetEntityId = message.TargetEntityId, WorldTick = message.WorldTick, Ordinal = message.Ordinal, CharacterEventType = message.CharacterEventType, ActionKind = message.ActionKind, ActionId = message.ActionId, Value = message.Value, IsCritical = message.IsCritical };
        }

        /// <summary>把生成的事件列表复制为不依赖 Protobuf 集合实现的只读领域值。</summary>
        private static IReadOnlyList<BattleEventMessage> DecodeBattleEvents(IList<BattleEventPayload> payloads)
        {
            if (payloads == null || payloads.Count == 0) return Array.Empty<BattleEventMessage>();
            BattleEventMessage[] events = new BattleEventMessage[payloads.Count];
            for (int index = 0; index < payloads.Count; index++)
            {
                BattleEventPayload payload = payloads[index];
                if (payload == null) throw new InvalidDataException("Server snapshot contains a null battle event payload.");
                if (payload.Kind < (int)BattleEventKind.Character || payload.Kind > (int)BattleEventKind.HitResolved) throw new InvalidDataException($"Server snapshot contains unknown battle event kind {payload.Kind}.");
                events[index] = new BattleEventMessage((BattleEventKind)payload.Kind, payload.SourceEntityId, payload.TargetEntityId, payload.WorldTick, payload.Ordinal, payload.CharacterEventType, payload.ActionKind, payload.ActionId, payload.Value, payload.IsCritical);
            }
            return Array.AsReadOnly(events);
        }

        /// <summary>校验传输层移动模式采用当前协议定义的 Walk、Run 或 Sprint 稳定数值。</summary>
        private static int DecodeRequestedMoveMode(int value)
        {
            if (value < 0 || value > 2) throw new InvalidDataException($"Protobuf requested move mode must be between 0 and 2, but received {value}.");
            return value;
        }

        /// <summary>拒绝协议版本尚未定义的动作比特位，避免客户端和服务器对同一输入产生不同解释。</summary>
        private static CharacterInputButtons DecodeInputButtons(uint value)
        {
            const CharacterInputButtons allowed = CharacterInputButtons.JumpPressed | CharacterInputButtons.DodgePressed | CharacterInputButtons.DodgeBackward | CharacterInputButtons.AttackPressed | CharacterInputButtons.AttackHeld | CharacterInputButtons.AttackReleased | CharacterInputButtons.SkillPressed | CharacterInputButtons.UltimatePressed;
            CharacterInputButtons buttons = (CharacterInputButtons)value;
            if ((buttons & ~allowed) != 0U) throw new InvalidDataException($"Protobuf character input contains unsupported button bits 0x{value:X8}.");
            return buttons;
        }

        /// <summary>把生成代码中的 oneof 分支映射为稳定领域枚举。</summary>
        private static BattleMessageType GetMessageType(BattleEnvelope envelope)
        {
            switch (envelope.PayloadCase)
            {
                case BattleEnvelope.PayloadOneofCase.ClientHello: return BattleMessageType.ClientHello;
                case BattleEnvelope.PayloadOneofCase.ServerWelcome: return BattleMessageType.ServerWelcome;
                case BattleEnvelope.PayloadOneofCase.ClientInput: return BattleMessageType.ClientInput;
                case BattleEnvelope.PayloadOneofCase.ServerSnapshot: return BattleMessageType.ServerSnapshot;
                case BattleEnvelope.PayloadOneofCase.ServerReject: return BattleMessageType.ServerReject;
                case BattleEnvelope.PayloadOneofCase.ClientPing: return BattleMessageType.ClientPing;
                case BattleEnvelope.PayloadOneofCase.ServerPong: return BattleMessageType.ServerPong;
                default: throw new InvalidDataException($"Unsupported Protobuf battle payload case {envelope.PayloadCase}.");
            }
        }

        /// <summary>校验客户端离散轴只能为负一、零或一，并安全转换为领域层使用的有符号字节。</summary>
        private static sbyte DecodeMovementAxis(int value, string fieldName)
        {
            if (value < -1 || value > 1) throw new InvalidDataException($"Protobuf field {fieldName} must be -1, 0, or 1, but received {value}.");
            return (sbyte)value;
        }

        /// <summary>校验待发送或待解析的 Protobuf 字节数组存在且不超过协议上限。</summary>
        private static void ValidatePayloadLength(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            ValidatePayloadLength(payload.Length);
        }

        /// <summary>校验传输层声明的 Protobuf Envelope 长度，避免异常分配和拒绝服务。</summary>
        private static void ValidatePayloadLength(int payloadLength)
        {
            if (payloadLength < 1 || payloadLength > BattleProtocol.MaximumPayloadLength) throw new InvalidDataException($"Battle Protobuf payload length {payloadLength} is invalid.");
        }

        /// <summary>循环读取直到缓冲区填满，并正确区分帧边界关闭和帧中断。</summary>
        private static async Task<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int readCount = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
                if (readCount == 0)
                {
                    if (offset == 0) return false;
                    throw new EndOfStreamException("Remote endpoint closed during a Protobuf battle frame.");
                }
                offset += readCount;
            }
            return true;
        }

        /// <summary>将 TCP 分帧长度明确写为四字节小端整数，避免依赖运行平台字节序。</summary>
        private static void WriteInt32LittleEndian(byte[] destination, int value)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
        }

        /// <summary>从四字节小端 TCP 分帧头读取负载长度。</summary>
        private static int ReadInt32LittleEndian(byte[] source)
        {
            return source[0] | source[1] << 8 | source[2] << 16 | source[3] << 24;
        }
    }
}
