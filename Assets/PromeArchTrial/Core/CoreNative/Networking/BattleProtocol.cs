using System;
using System.Collections.Generic;

namespace PromeArchTrial.Core.Networking
{
    /// <summary>
    /// 声明客户端与权威战斗服务器共同使用的协议常量。
    /// </summary>
    public static class BattleProtocol
    {
        /// <summary>当前 Protobuf 消息协议版本；版本五加入与世界 Tick 对齐的握手初始状态和只读权威战斗事件。</summary>
        public const int Version = 5;

        /// <summary>本地演示服务器默认监听端口。</summary>
        public const int DefaultPort = 7777;

        /// <summary>允许接收的单帧最大负载长度，用于拒绝异常或恶意数据。</summary>
        public const int MaximumPayloadLength = 4096;
    }

    /// <summary>
    /// 标识一帧 Protobuf Envelope 携带的业务类型。
    /// </summary>
    public enum BattleMessageType : byte
    {
        ClientHello = 1,
        ServerWelcome = 2,
        ClientInput = 3,
        ServerSnapshot = 4,
        ServerReject = 5,
        ClientPing = 6,
        ServerPong = 7
    }

    /// <summary>
    /// 描述服务器拒绝客户端建立战斗会话的原因。
    /// </summary>
    public enum ServerRejectReason : byte
    {
        Unknown = 0,
        ProtocolMismatch = 1,
        ConfigMismatch = 2,
        ServerFull = 3,
        InvalidMessage = 4
    }

    /// <summary>
    /// 使用稳定比特位描述一个客户端固定 Tick 内发生的角色动作输入。
    /// </summary>
    [Flags]
    public enum CharacterInputButtons : uint
    {
        /// <summary>当前 Tick 没有离散动作输入。</summary>
        None = 0U,

        /// <summary>当前 Tick 按下跳跃键。</summary>
        JumpPressed = 1U << 0,

        /// <summary>当前 Tick 按下闪避键。</summary>
        DodgePressed = 1U << 1,

        /// <summary>当前 Tick 请求向面向反方向闪避。</summary>
        DodgeBackward = 1U << 2,

        /// <summary>当前 Tick 首次按下普通攻击键。</summary>
        AttackPressed = 1U << 3,

        /// <summary>当前 Tick 持续按住普通攻击键。</summary>
        AttackHeld = 1U << 4,

        /// <summary>当前 Tick 释放普通攻击键。</summary>
        AttackReleased = 1U << 5,

        /// <summary>当前 Tick 按下角色技能键。</summary>
        SkillPressed = 1U << 6,

        /// <summary>当前 Tick 按下终结技键。</summary>
        UltimatePressed = 1U << 7
    }

    /// <summary>
    /// 标识服务器快照事件载荷来自角色模拟核心还是权威世界命中结算。
    /// </summary>
    public enum BattleEventKind
    {
        /// <summary>事件来自单个角色固定 Tick 模拟。</summary>
        Character = 0,

        /// <summary>事件来自权威世界的空间命中、暴击和伤害结算。</summary>
        HitResolved = 1
    }

    /// <summary>
    /// 保存一个不依赖 GameNative 或 Unity 的只读权威战斗事件，客户端仅可用它驱动音画反馈。
    /// </summary>
    public readonly struct BattleEventMessage
    {
        /// <summary>创建一个已经由服务器分配稳定 Tick 与序号的战斗事件。</summary>
        public BattleEventMessage(BattleEventKind kind, int sourceEntityId, int targetEntityId, int worldTick, int ordinal, int characterEventType, int actionKind, int actionId, int value, bool isCritical)
        {
            if (sourceEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(sourceEntityId), "Battle event source entity id must be positive.");
            if (targetEntityId < 0) throw new ArgumentOutOfRangeException(nameof(targetEntityId), "Battle event target entity id cannot be negative.");
            if (worldTick < 0) throw new ArgumentOutOfRangeException(nameof(worldTick), "Battle event world tick cannot be negative.");
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal), "Battle event ordinal cannot be negative.");
            Kind = kind;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            WorldTick = worldTick;
            Ordinal = ordinal;
            CharacterEventType = characterEventType;
            ActionKind = actionKind;
            ActionId = actionId;
            Value = value;
            IsCritical = isCritical;
        }

        /// <summary>获取事件载荷种类。</summary>
        public BattleEventKind Kind { get; }

        /// <summary>获取产生事件的角色实体编号。</summary>
        public int SourceEntityId { get; }

        /// <summary>获取命中目标实体编号；普通角色事件为零。</summary>
        public int TargetEntityId { get; }

        /// <summary>获取事件产生的权威世界 Tick。</summary>
        public int WorldTick { get; }

        /// <summary>获取事件在当前世界 Tick 内的稳定零起始序号。</summary>
        public int Ordinal { get; }

        /// <summary>获取角色事件枚举数值；命中结算事件固定为零。</summary>
        public int CharacterEventType { get; }

        /// <summary>获取事件关联的角色动作枚举数值。</summary>
        public int ActionKind { get; }

        /// <summary>获取事件关联的 Luban 动作表编号。</summary>
        public int ActionId { get; }

        /// <summary>获取角色事件数值或命中结算的请求伤害值。</summary>
        public int Value { get; }

        /// <summary>获取本次命中是否由服务器确定性判定为暴击。</summary>
        public bool IsCritical { get; }
    }

    /// <summary>
    /// 保存一次 Protobuf Envelope 解析得到的稳定领域消息，使接收循环无需为了类型判断重复反序列化同一数据帧。
    /// </summary>
    public readonly struct DecodedBattleMessage
    {
        private readonly object value;

        /// <summary>由协议编解码器创建已经完成类型映射的领域消息。</summary>
        internal DecodedBattleMessage(BattleMessageType messageType, object value)
        {
            MessageType = messageType;
            this.value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>获取当前消息对应的稳定业务类型。</summary>
        public BattleMessageType MessageType { get; }

        /// <summary>取得当前消息的强类型领域值，并在调用方请求了错误类型时立即失败。</summary>
        public T GetMessage<T>()
        {
            if (value is T typedValue) return typedValue;
            throw new InvalidOperationException($"Decoded battle message {MessageType} cannot be read as {typeof(T).Name}.");
        }
    }

    /// <summary>
    /// 客户端连接后发送协议版本与全部共享 Luban 配置生成物的稳定内容哈希。
    /// </summary>
    public readonly struct ClientHelloMessage
    {
        /// <summary>创建客户端握手消息。</summary>
        public ClientHelloMessage(int protocolVersion, ulong configHash)
        {
            ProtocolVersion = protocolVersion;
            ConfigHash = configHash;
        }

        /// <summary>获取客户端协议版本。</summary>
        public int ProtocolVersion { get; }

        /// <summary>获取客户端加载的全部共享 Luban 配置内容哈希。</summary>
        public ulong ConfigHash { get; }
    }

    /// <summary>
    /// 服务器确认连接后返回玩家身份、默认角色、服务器 Tick 与权威配置哈希。
    /// </summary>
    public readonly struct ServerWelcomeMessage
    {
        /// <summary>创建服务器握手确认消息。</summary>
        public ServerWelcomeMessage(int playerId, int entityId, int characterId, int serverTick, int tickRate, ulong configHash, CharacterNetworkState initialState)
        {
            PlayerId = playerId;
            EntityId = entityId;
            CharacterId = characterId;
            ServerTick = serverTick;
            TickRate = tickRate;
            ConfigHash = configHash;
            InitialState = initialState;
        }

        /// <summary>获取当前连接对应的服务器玩家编号。</summary>
        public int PlayerId { get; }

        /// <summary>获取当前连接拥有的权威世界角色实体编号。</summary>
        public int EntityId { get; }

        /// <summary>获取当前会话使用的 Luban 角色配置编号。</summary>
        public int CharacterId { get; }

        /// <summary>获取握手完成时的服务器 Tick。</summary>
        public int ServerTick { get; }

        /// <summary>获取服务器固定 Tick 频率。</summary>
        public int TickRate { get; }

        /// <summary>获取服务器锁定的全部共享 Luban 配置内容哈希。</summary>
        public ulong ConfigHash { get; }

        /// <summary>获取与服务器当前世界 Tick 完全对齐的初始完整角色状态。</summary>
        public CharacterNetworkState InitialState { get; }
    }

    /// <summary>
    /// 客户端每个固定 Tick 上传量化输入、动作比特位与该 Tick 结束后的完整预测状态。
    /// </summary>
    public readonly struct ClientInputMessage
    {
        /// <summary>创建一条客户端预测输入消息。</summary>
        public ClientInputMessage(int clientTick, sbyte moveX, sbyte moveZ, int requestedMoveMode, CharacterInputButtons inputButtons, CharacterNetworkState predictedState)
        {
            if (moveX < -1 || moveX > 1) throw new ArgumentOutOfRangeException(nameof(moveX), "Movement X input must be -1, 0, or 1.");
            if (moveZ < -1 || moveZ > 1) throw new ArgumentOutOfRangeException(nameof(moveZ), "Movement Z input must be -1, 0, or 1.");
            ClientTick = clientTick;
            MoveX = moveX;
            MoveZ = moveZ;
            RequestedMoveMode = requestedMoveMode;
            InputButtons = inputButtons;
            PredictedState = predictedState;
        }

        /// <summary>获取客户端本地输入 Tick。</summary>
        public int ClientTick { get; }

        /// <summary>获取横向离散输入，合法值为负一、零或一。</summary>
        public sbyte MoveX { get; }

        /// <summary>获取纵向离散输入，合法值为负一、零或一。</summary>
        public sbyte MoveZ { get; }

        /// <summary>获取客户端请求的稳定移动模式枚举数值。</summary>
        public int RequestedMoveMode { get; }

        /// <summary>获取当前 Tick 的跳跃、闪避、攻击、技能和终结技输入比特位。</summary>
        public CharacterInputButtons InputButtons { get; }

        /// <summary>获取客户端执行该命令后的完整预测角色状态，仅用于偏差诊断和对账。</summary>
        public CharacterNetworkState PredictedState { get; }
    }

    /// <summary>
    /// 保存客户端和服务器之间传输的完整角色模拟状态；该类型只包含纯数据，不依赖游戏或 Unity 程序集。
    /// </summary>
    public readonly struct CharacterNetworkState
    {
        /// <summary>创建一份包含动作、位移、资源和冷却的完整可回滚角色状态。</summary>
        public CharacterNetworkState(int tick, long positionX, long positionY, long positionZ, long facingXRaw, long facingZRaw, int locomotionState, int actionKind, int actionElapsedTicks, long actionDirectionXRaw, long actionDirectionZRaw, long horizontalRemainderX, long horizontalRemainderZ, long verticalVelocityRaw, long verticalAccelerationRemainder, long verticalPositionRemainder, bool isGrounded, bool isInvincible, int hp, int coreEnergy, int ultimateEnergy, int attackChargeTicks, bool attackHoldConsumed, int lightAttackBufferRemainingTicks, bool usesMovingAttackVariant, int nextAttackComboIndex, int comboTimeoutRemainingTicks, int dodgeCooldownRemainingTicks, int attackCooldownRemainingTicks, int heavyAttackCooldownRemainingTicks, int skillCooldownRemainingTicks, int ultimateCooldownRemainingTicks)
        {
            Tick = tick;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
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

        /// <summary>获取角色状态对应的模拟 Tick。</summary>
        public int Tick { get; }

        /// <summary>获取角色 X 轴定点坐标。</summary>
        public long PositionX { get; }

        /// <summary>获取角色 Y 轴定点坐标。</summary>
        public long PositionY { get; }

        /// <summary>获取角色 Z 轴定点坐标。</summary>
        public long PositionZ { get; }

        /// <summary>获取角色面向方向的定点 X 分量。</summary>
        public long FacingXRaw { get; }

        /// <summary>获取角色面向方向的定点 Z 分量。</summary>
        public long FacingZRaw { get; }

        /// <summary>获取共享角色移动状态枚举数值。</summary>
        public int LocomotionState { get; }

        /// <summary>获取共享角色动作种类枚举数值。</summary>
        public int ActionKind { get; }

        /// <summary>获取当前动作已经推进的固定 Tick 数量。</summary>
        public int ActionElapsedTicks { get; }

        /// <summary>获取动作锁定方向的定点 X 分量。</summary>
        public long ActionDirectionXRaw { get; }

        /// <summary>获取动作锁定方向的定点 Z 分量。</summary>
        public long ActionDirectionZRaw { get; }

        /// <summary>获取水平 X 轴定点积分余数。</summary>
        public long HorizontalRemainderX { get; }

        /// <summary>获取水平 Z 轴定点积分余数。</summary>
        public long HorizontalRemainderZ { get; }

        /// <summary>获取垂直方向定点速度。</summary>
        public long VerticalVelocityRaw { get; }

        /// <summary>获取重力积分保留的定点余数。</summary>
        public long VerticalAccelerationRemainder { get; }

        /// <summary>获取垂直位置积分保留的定点余数。</summary>
        public long VerticalPositionRemainder { get; }

        /// <summary>获取角色是否处于地面接触状态。</summary>
        public bool IsGrounded { get; }

        /// <summary>获取角色当前是否处于配置定义的无敌窗口。</summary>
        public bool IsInvincible { get; }

        /// <summary>获取角色当前生命值。</summary>
        public int Hp { get; }

        /// <summary>获取角色当前核心能量。</summary>
        public int CoreEnergy { get; }

        /// <summary>获取角色当前终结技能量。</summary>
        public int UltimateEnergy { get; }

        /// <summary>获取普通攻击键已经持续按住的 Tick 数量。</summary>
        public int AttackChargeTicks { get; }

        /// <summary>获取当前物理按住是否已经消费为一次重击并等待释放。</summary>
        public bool AttackHoldConsumed { get; }

        /// <summary>获取尚未消费的单槽轻击输入剩余保留 Tick 数。</summary>
        public int LightAttackBufferRemainingTicks { get; }

        /// <summary>获取当前普攻是否冻结为移动起手动画变体。</summary>
        public bool UsesMovingAttackVariant { get; }

        /// <summary>获取下一次普通攻击应使用的连击段索引。</summary>
        public int NextAttackComboIndex { get; }

        /// <summary>获取普通攻击连击尚可衔接的剩余 Tick 数量。</summary>
        public int ComboTimeoutRemainingTicks { get; }

        /// <summary>获取闪避冷却剩余 Tick 数量。</summary>
        public int DodgeCooldownRemainingTicks { get; }

        /// <summary>获取普通攻击冷却剩余 Tick 数量。</summary>
        public int AttackCooldownRemainingTicks { get; }

        /// <summary>获取蓄力重击冷却剩余 Tick 数量。</summary>
        public int HeavyAttackCooldownRemainingTicks { get; }

        /// <summary>获取角色技能冷却剩余 Tick 数量。</summary>
        public int SkillCooldownRemainingTicks { get; }

        /// <summary>获取终结技冷却剩余 Tick 数量。</summary>
        public int UltimateCooldownRemainingTicks { get; }
    }

    /// <summary>
    /// 服务器每个固定 Tick 推送玩家的最新完整权威模拟状态。
    /// </summary>
    public readonly struct ServerSnapshotMessage
    {
        /// <summary>创建服务器权威状态快照。</summary>
        public ServerSnapshotMessage(int serverTick, int acknowledgedClientTick, CharacterNetworkState state, IReadOnlyList<BattleEventMessage> events)
        {
            ServerTick = serverTick;
            AcknowledgedClientTick = acknowledgedClientTick;
            State = state;
            if (events == null) throw new ArgumentNullException(nameof(events));
            BattleEventMessage[] eventCopy = new BattleEventMessage[events.Count];
            for (int index = 0; index < events.Count; index++) eventCopy[index] = events[index];
            Events = Array.AsReadOnly(eventCopy);
        }

        /// <summary>创建一个不携带事件的服务器权威状态快照。</summary>
        public ServerSnapshotMessage(int serverTick, int acknowledgedClientTick, CharacterNetworkState state) : this(serverTick, acknowledgedClientTick, state, Array.Empty<BattleEventMessage>())
        {
        }

        /// <summary>获取产生本快照的服务器 Tick。</summary>
        public int ServerTick { get; }

        /// <summary>获取服务器已经处理的最后一个客户端输入 Tick；负一表示尚未处理输入。</summary>
        public int AcknowledgedClientTick { get; }

        /// <summary>获取服务器计算得到的完整权威角色状态。</summary>
        public CharacterNetworkState State { get; }

        /// <summary>获取本快照 Tick 内按服务器稳定序号排列的只读战斗事件。</summary>
        public IReadOnlyList<BattleEventMessage> Events { get; }
    }

    /// <summary>
    /// 服务器在握手或协议校验失败时通知客户端拒绝原因。
    /// </summary>
    public readonly struct ServerRejectMessage
    {
        /// <summary>创建服务器拒绝消息。</summary>
        public ServerRejectMessage(ServerRejectReason reason)
        {
            Reason = reason;
        }

        /// <summary>获取服务器拒绝连接的原因。</summary>
        public ServerRejectReason Reason { get; }
    }

    /// <summary>
    /// 客户端定期发送独立序号以测量不包含权威模拟排队时间的纯协议往返延迟。
    /// </summary>
    public readonly struct ClientPingMessage
    {
        /// <summary>创建一条客户端 Ping 消息。</summary>
        public ClientPingMessage(int sequence)
        {
            Sequence = sequence;
        }

        /// <summary>获取客户端生成的递增 Ping 序号。</summary>
        public int Sequence { get; }
    }

    /// <summary>
    /// 服务器收到 Ping 后立即回送相同序号，不等待 30 Hz 权威模拟 Tick。
    /// </summary>
    public readonly struct ServerPongMessage
    {
        /// <summary>创建一条服务器 Pong 消息。</summary>
        public ServerPongMessage(int sequence)
        {
            Sequence = sequence;
        }

        /// <summary>获取与客户端 Ping 完全相同的序号。</summary>
        public int Sequence { get; }
    }
}
