using System;
using PromeArchTrial.Core.Networking;
using PromeArchTrial.Core.Unity.Networking;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.Networking;
using UnityEngine;

namespace PromeArchTrial.Game.Unity
{
    /// <summary>
    /// 采集完整角色输入、以三十赫兹立即预测、上传 Protobuf 输入，并用服务器完整状态恢复后重放尚未确认的命令。
    /// </summary>
    public sealed class ClientBattleSession : MonoBehaviour
    {
        /// <summary>单个渲染帧最多补算的预测 Tick，防止长帧后无限追赶。</summary>
        private const int MaximumTicksPerUnityFrame = 10;

        /// <summary>收到权威受击事件后覆盖普通动作的表现 Tick 数。</summary>
        private const int HitReactionDurationTicks = 6;

        /// <summary>独立 Ping/Pong 消息发送间隔。</summary>
        private const double PingIntervalSeconds = 0.5d;

        /// <summary>新 RTT 样本进入指数平滑值的权重。</summary>
        private const double PingSmoothingFactor = 0.2d;

        /// <summary>客户端连接的战斗服务器地址。</summary>
        [SerializeField, Tooltip("战斗服务器主机名或 IP 地址。")] private string serverHost = "127.0.0.1";

        /// <summary>客户端连接的战斗服务器 TCP 端口。</summary>
        [SerializeField, Tooltip("战斗服务器 TCP 端口。")] private int serverPort = BattleProtocol.DefaultPort;

        // 配置、预测器和传输对象由组合根及 Unity 生命周期共同管理。
        private CharacterRuntimeConfig config;
        private CharacterPredictionController prediction;
        private TcpBattleClient networkClient;

        // Tick、身份和权威对账字段完整描述当前会话的预测时序。
        private double tickAccumulator;
        private int configuredCharacterId;
        private int playerId;
        private int entityId;
        private int clientTick = -1;
        private int serverTick = -1;
        private int acknowledgedClientTick = -1;
        private int rollbackCount;
        private int correctionCount;
        private int lastAuthoritativeHp;
        private int hitReactionUntilClientTick = int.MinValue;
        private int latestDamageAmount;
        private uint damageEventSequence;
        private bool latestDamageWasCritical;

        // 诊断字段只影响 Debug 面板，不参与确定性模拟。
        private double lastPositionError;
        private double pingMilliseconds = -1.0d;
        private double nextPingTime;
        private int pingSequence;

        // 离散输入边沿跨 Unity 渲染帧锁存，直到下一个固定 Tick 原子消费。
        private bool jumpPressedLatched;
        private bool dodgePressedLatched;
        private bool attackPressedLatched;
        private bool attackReleasedLatched;
        private bool skillPressedLatched;
        private bool ultimatePressedLatched;

        // 生命周期标记保证 Configure、Start、Welcome 和断线状态不会发生非法重入。
        private bool configured;
        private bool welcomed;
        private bool startInvoked;
        private string statusText = "Waiting for composition root";

        /// <summary>获取客户端恢复并重放后的当前完整预测状态。</summary>
        public CharacterState CurrentState => prediction == null ? default : prediction.CurrentState;

        /// <summary>获取当前会话是否已经收到服务器欢迎消息并拥有可展示状态。</summary>
        public bool HasAuthoritativeBaseline => welcomed && prediction != null;

        /// <summary>获取当前角色不可变运行时配置。</summary>
        public CharacterRuntimeConfig RuntimeConfig => config;

        /// <summary>获取服务器分配的玩家编号。</summary>
        public int PlayerId => playerId;

        /// <summary>获取服务器权威世界中的角色实体编号。</summary>
        public int EntityId => entityId;

        /// <summary>获取当前会话选择的 Luban 角色表编号。</summary>
        public int CharacterId => configuredCharacterId;

        /// <summary>获取最近服务器快照的服务器 Tick。</summary>
        public int ServerTick => serverTick;

        /// <summary>获取服务器最近确认并完成模拟的客户端输入 Tick。</summary>
        public int AcknowledgedClientTick => acknowledgedClientTick;

        /// <summary>获取客户端当前预测 Tick。</summary>
        public int ClientTick => clientTick;

        /// <summary>获取位置误差严格超过配置阈值的累计回滚次数。</summary>
        public int RollbackCount => rollbackCount;

        /// <summary>获取完整状态发生任一权威纠正的累计次数。</summary>
        public int CorrectionCount => correctionCount;

        /// <summary>获取最近一次完成历史比较的位置误差。</summary>
        public double LastPositionError => lastPositionError;

        /// <summary>获取独立 Ping/Pong 消息测得的平滑协议往返延迟；尚无样本时返回负一。</summary>
        public double PingMilliseconds => pingMilliseconds;

        /// <summary>获取最近一次权威命中事件的稳定表现序号。</summary>
        public uint DamageEventSequence => damageEventSequence;

        /// <summary>获取最近一次权威命中事件请求显示的非负伤害。</summary>
        public int LatestDamageAmount => latestDamageAmount;

        /// <summary>获取最近一次权威命中是否为暴击。</summary>
        public bool LatestDamageWasCritical => latestDamageWasCritical;

        /// <summary>获取当前是否应短暂播放受击动作。</summary>
        public bool IsHitReactionActive => clientTick <= hitReactionUntilClientTick;

        /// <summary>获取用于演示状态面板显示的连接文本。</summary>
        public string StatusText => statusText;

        /// <summary>获取网络连接的当前生命周期状态。</summary>
        public BattleClientConnectionState ConnectionState => networkClient == null ? BattleClientConnectionState.Disconnected : networkClient.ConnectionState;

        /// <summary>由组合根在 Start 前注入服务器地址、角色表编号和已经由 Luban 引用解析出的运行时配置。</summary>
        public void Configure(string host, int port, int characterId, CharacterRuntimeConfig runtimeConfig)
        {
            if (startInvoked) throw new InvalidOperationException("Client battle session cannot be configured after Start.");
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Battle server host cannot be empty.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
            serverHost = host;
            serverPort = port;
            configuredCharacterId = characterId;
            config = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            if (config.PredictionHistoryTicks < BattlePredictionPolicy.ClientInputLeadTicks) throw new ArgumentException($"Prediction history {config.PredictionHistoryTicks} must be at least the configured input lead {BattlePredictionPolicy.ClientInputLeadTicks}.", nameof(runtimeConfig));
            prediction = new CharacterPredictionController(config);
            clientTick = prediction.CurrentState.Tick;
            lastAuthoritativeHp = config.Stats.MaxHp;
            configured = true;
            statusText = "Ready to connect";
        }

        /// <summary>创建只负责后台传输的 TCP 客户端；角色配置由组合根随后注入。</summary>
        private void Awake()
        {
            networkClient = new TcpBattleClient();
        }

        /// <summary>连接战斗服务器并只发送协议版本与共享配置内容哈希。</summary>
        private async void Start()
        {
            startInvoked = true;
            if (!configured)
            {
                statusText = "ClientBattleSession was not configured by the composition root";
                Debug.LogError($"[PromeArchTrial] {statusText}.", this);
                return;
            }
            statusText = $"Connecting to {serverHost}:{serverPort}";
            try
            {
                await networkClient.ConnectAsync(serverHost, serverPort, new ClientHelloMessage(BattleProtocol.Version, config.ContentHash));
                statusText = "TCP connected, waiting for server welcome";
            }
            catch (Exception exception)
            {
                statusText = $"Connection failed: {exception.Message}";
                Debug.LogError($"[PromeArchTrial] {statusText}", this);
            }
        }

        /// <summary>锁存 Unity 帧级输入边沿，消费网络消息，并按固定累计器推进零个或多个角色预测 Tick。</summary>
        private void Update()
        {
            LatchInputEdges();
            ProcessServerMessages();
            RefreshConnectionStatus();
            if (!welcomed) return;
            MaintainInputLead();
            TrySendPing();
            tickAccumulator += Math.Min(Time.unscaledDeltaTime, 0.25f);
            int executedTicks = 0;
            while (tickAccumulator >= config.TickIntervalSeconds && executedTicks < MaximumTicksPerUnityFrame)
            {
                tickAccumulator -= config.TickIntervalSeconds;
                if (!ExecutePredictionTick(CreateNextCommand())) break;
                executedTicks++;
            }
            if (executedTicks == MaximumTicksPerUnityFrame && tickAccumulator >= config.TickIntervalSeconds) tickAccumulator = 0.0d;
        }

        /// <summary>读取后台网络队列，并确保预测状态和表现事件写入都发生在 Unity 主线程。</summary>
        private void ProcessServerMessages()
        {
            while (networkClient.TryDequeueWelcome(out ServerWelcomeMessage welcome)) AcceptWelcome(welcome);
            while (networkClient.TryDequeueSnapshot(out ServerSnapshotMessage snapshot)) AcceptSnapshot(snapshot);
            while (networkClient.TryDequeuePingSample(out PingRoundTripSample sample)) AcceptPingSample(sample);
            while (networkClient.TryDequeueReject(out ServerRejectMessage reject))
            {
                welcomed = false;
                statusText = $"Server rejected connection: {reject.Reason}";
                Debug.LogError($"[PromeArchTrial] {statusText}", this);
            }
        }

        /// <summary>校验角色、Tick 频率和配置哈希，并从服务器完整初始状态建立严格对齐的预测基线。</summary>
        private void AcceptWelcome(ServerWelcomeMessage welcome)
        {
            if (welcome.CharacterId != configuredCharacterId || welcome.TickRate != config.TickRate || welcome.ConfigHash != config.ContentHash)
            {
                statusText = "Server welcome contains a different character or runtime config snapshot";
                Debug.LogError($"[PromeArchTrial] {statusText}", this);
                networkClient.Dispose();
                return;
            }
            CharacterState initialState = CharacterNetworkMapper.ToCharacterState(welcome.InitialState);
            if (initialState.Tick != welcome.ServerTick)
            {
                statusText = "Server welcome state is not aligned with the advertised world tick";
                Debug.LogError($"[PromeArchTrial] {statusText}", this);
                networkClient.Dispose();
                return;
            }
            playerId = welcome.PlayerId;
            entityId = welcome.EntityId;
            serverTick = welcome.ServerTick;
            acknowledgedClientTick = initialState.Tick;
            prediction.Reset(initialState);
            clientTick = initialState.Tick;
            lastAuthoritativeHp = initialState.Hp;
            ClearLatchedInputEdges();
            welcomed = true;
            if (!PrimeNeutralInputLead())
            {
                welcomed = false;
                networkClient.Dispose();
                return;
            }
            tickAccumulator = config.TickIntervalSeconds;
            nextPingTime = Time.realtimeSinceStartupAsDouble;
            statusText = $"Connected as player {playerId}, entity {entityId}";
            Debug.Log($"[PromeArchTrial] {statusText}; Character={configuredCharacterId}, ServerTick={serverTick}, PredictedTick={clientTick}, InputLead={BattlePredictionPolicy.ClientInputLeadTicks}, ConfigHash=0x{config.ContentHash:X16}.", this);
        }

        /// <summary>消费权威战斗事件，再从完整权威状态恢复并重放确认 Tick 之后的全部本地命令。</summary>
        private void AcceptSnapshot(ServerSnapshotMessage snapshot)
        {
            CharacterState authoritativeState = CharacterNetworkMapper.ToCharacterState(snapshot.State);
            if (authoritativeState.Tick != snapshot.AcknowledgedClientTick)
            {
                statusText = "Server snapshot state tick does not equal its reconciliation tick";
                Debug.LogError($"[PromeArchTrial] {statusText}", this);
                networkClient.Dispose();
                welcomed = false;
                return;
            }
            serverTick = snapshot.ServerTick;
            ProcessAuthoritativeEvents(snapshot, authoritativeState);
            CharacterReconciliationResult result = prediction.Reconcile(snapshot.AcknowledgedClientTick, authoritativeState);
            if (!result.Accepted) return;
            acknowledgedClientTick = snapshot.AcknowledgedClientTick;
            clientTick = prediction.CurrentState.Tick;
            lastAuthoritativeHp = authoritativeState.Hp;
            if (result.Compared) lastPositionError = result.PositionErrorUnits;
            if (result.Corrected) correctionCount++;
            if (!result.PositionThresholdExceeded) return;
            rollbackCount++;
            Debug.LogWarning($"[PromeArchTrial] Prediction rollback at tick {result.AcknowledgedTick}; error={result.PositionErrorUnits:F4}, replayed={result.ReplayedCommandCount}, finalHash=0x{result.FinalStateHash:X16}.", this);
        }

        /// <summary>把服务器命中事件聚合为本地受击动画和飘字，并在旧服务器没有事件时以权威 HP 差作为安全回退。</summary>
        private void ProcessAuthoritativeEvents(ServerSnapshotMessage snapshot, CharacterState authoritativeState)
        {
            int aggregatedDamage = 0;
            int requestedDamage = 0;
            bool anyCritical = false;
            uint latestSequence = damageEventSequence;
            for (int index = 0; index < snapshot.Events.Count; index++)
            {
                BattleEventMessage battleEvent = snapshot.Events[index];
                if (battleEvent.Kind == BattleEventKind.HitResolved && battleEvent.TargetEntityId == entityId)
                {
                    requestedDamage = SaturatingAdd(requestedDamage, Math.Max(0, battleEvent.Value));
                    anyCritical |= battleEvent.IsCritical;
                    continue;
                }
                if (battleEvent.Kind != BattleEventKind.Character || battleEvent.SourceEntityId != entityId || battleEvent.CharacterEventType != (int)CharacterEventType.DamageTaken) continue;
                aggregatedDamage = SaturatingAdd(aggregatedDamage, Math.Max(0, battleEvent.Value));
                latestSequence = CreatePresentationEventSequence(battleEvent.WorldTick, battleEvent.Ordinal);
            }
            if (aggregatedDamage > 0 && requestedDamage > 0) aggregatedDamage = requestedDamage;
            if (aggregatedDamage == 0 && authoritativeState.Hp < lastAuthoritativeHp)
            {
                aggregatedDamage = lastAuthoritativeHp - authoritativeState.Hp;
                latestSequence = CreatePresentationEventSequence(snapshot.ServerTick, 0);
            }
            if (aggregatedDamage <= 0) return;
            latestDamageAmount = aggregatedDamage;
            latestDamageWasCritical = anyCritical;
            damageEventSequence = latestSequence == 0U ? 1U : latestSequence;
            hitReactionUntilClientTick = Math.Max(hitReactionUntilClientTick, clientTick + HitReactionDurationTicks);
        }

        /// <summary>执行本地完整角色逻辑后立即上传同 Tick 命令和完整预测状态。</summary>
        private bool ExecutePredictionTick(CharacterCommand command)
        {
            try
            {
                CharacterPredictionFrame frame = prediction.Predict(command);
                clientTick = frame.StateAfterTick.Tick;
                ClientInputMessage message = CharacterNetworkMapper.ToClientInputMessage(command, frame.StateAfterTick);
                if (!networkClient.TrySend(message))
                {
                    statusText = "Failed to queue character input because the network is not connected";
                    return false;
                }
                return true;
            }
            catch (InvalidOperationException exception)
            {
                statusText = $"Prediction paused: {exception.Message}";
                Debug.LogError($"[PromeArchTrial] {statusText}", this);
                return false;
            }
        }

        /// <summary>在 Welcome 初始状态之后立即预测并上传四个空命令，从连接首帧建立可吸收网络延迟的未来输入窗口。</summary>
        private bool PrimeNeutralInputLead()
        {
            for (int leadIndex = 0; leadIndex < BattlePredictionPolicy.ClientInputLeadTicks; leadIndex++)
            {
                if (!ExecutePredictionTick(CharacterCommand.Empty(clientTick + 1))) return false;
            }
            return true;
        }

        /// <summary>在最新权威快照跳跃后补齐至固定领先 Tick，第一个追赶命令仍消费当前帧的真实按键边沿以保持本地立即响应。</summary>
        private void MaintainInputLead()
        {
            long targetPredictedTick = (long)serverTick + BattlePredictionPolicy.ClientInputLeadTicks;
            while (clientTick < targetPredictedTick)
            {
                if (!ExecutePredictionTick(CreateNextCommand())) return;
            }
        }

        /// <summary>把当前连续按键与已经锁存的边沿输入量化为下一个严格连续角色命令。</summary>
        private CharacterCommand CreateNextCommand()
        {
            int x = 0;
            int z = 0;
            if (Input.GetKey(KeyCode.A)) x--;
            if (Input.GetKey(KeyCode.D)) x++;
            if (Input.GetKey(KeyCode.S)) z--;
            if (Input.GetKey(KeyCode.W)) z++;
            CharacterMoveMode moveMode = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? CharacterMoveMode.Walk : Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? CharacterMoveMode.Sprint : CharacterMoveMode.Run;
            int nextTick = clientTick + 1;
            CharacterCommand command = new CharacterCommand(nextTick, (sbyte)x, (sbyte)z, moveMode, jumpPressedLatched, dodgePressedLatched, dodgePressedLatched && z < 0, attackPressedLatched, Input.GetMouseButton(0), attackReleasedLatched, skillPressedLatched, ultimatePressedLatched);
            ClearLatchedInputEdges();
            return command;
        }

        /// <summary>在每个 Unity Update 锁存一次性按键，避免渲染帧没有执行固定 Tick 时丢失输入边沿。</summary>
        private void LatchInputEdges()
        {
            jumpPressedLatched |= Input.GetKeyDown(KeyCode.Space);
            dodgePressedLatched |= Input.GetMouseButtonDown(1);
            attackPressedLatched |= Input.GetMouseButtonDown(0);
            attackReleasedLatched |= Input.GetMouseButtonUp(0);
            skillPressedLatched |= Input.GetKeyDown(KeyCode.E);
            ultimatePressedLatched |= Input.GetKeyDown(KeyCode.R);
        }

        /// <summary>清除已经被一个固定 Tick 消费的全部离散输入边沿。</summary>
        private void ClearLatchedInputEdges()
        {
            jumpPressedLatched = false;
            dodgePressedLatched = false;
            attackPressedLatched = false;
            attackReleasedLatched = false;
            skillPressedLatched = false;
            ultimatePressedLatched = false;
        }

        /// <summary>每半秒发送一条独立 Ping，服务器立即返回 Pong 而不等待权威世界 Tick。</summary>
        private void TrySendPing()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextPingTime) return;
            nextPingTime = now + PingIntervalSeconds;
            networkClient.TrySend(new ClientPingMessage(++pingSequence));
        }

        /// <summary>使用后台网络线程已经完成计时的协议 RTT 样本更新平滑 Ping。</summary>
        private void AcceptPingSample(PingRoundTripSample sample)
        {
            pingMilliseconds = pingMilliseconds < 0.0d ? sample.Milliseconds : pingMilliseconds + (sample.Milliseconds - pingMilliseconds) * PingSmoothingFactor;
        }

        /// <summary>把权威 Tick 与 Tick 内序号组合成稳定且非零的三十二位表现去重键。</summary>
        private static uint CreatePresentationEventSequence(int worldTick, int ordinal)
        {
            return unchecked((uint)worldTick * 4096U + (uint)ordinal + 1U);
        }

        /// <summary>执行非负整数饱和加法，避免恶意事件值造成表现层溢出。</summary>
        private static int SaturatingAdd(int left, int right)
        {
            long sum = (long)left + right;
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>在后台任务断线或失败时更新可见状态，但不覆盖已收到的服务器拒绝原因。</summary>
        private void RefreshConnectionStatus()
        {
            if (networkClient.ConnectionState == BattleClientConnectionState.Faulted) statusText = $"Network error: {networkClient.LastError}";
            else if (networkClient.ConnectionState == BattleClientConnectionState.Disconnected && startInvoked) statusText = $"Disconnected: {networkClient.LastError}";
        }

        /// <summary>退出场景时取消后台网络任务并关闭套接字。</summary>
        private void OnDestroy()
        {
            networkClient?.Dispose();
        }
    }
}
