using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.Npc;
using Xuan.Prometheus.Service;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 管理大世界 POI：以场景摆放的 PoiMono 为唯一数据源与状态宿主（POI 不使用 ELC）；
    /// 服务器权威：按需拉取玩家附近 chunk 的状态，交互经服务器确认（返回 true）后才做表现；
    /// POI 不再按玩家距离统一显隐，消费和重生显示由各类型自身状态逻辑负责。
    /// </summary>
    internal sealed class PoiSystem : XSystem, IPoiSystem
    {
        /// <summary>
        /// 获取当前上场的小队成员；小队尚未建立时为空。
        /// 按 ARCH-SYS-007，跨 System 依赖在使用点解析，不长期保存 TeamSystem 实例。
        /// </summary>
        private static Entity ActivePlayer => Core.Gameplay.TryGetSystem(out ITeamSystem teamSystem) ? teamSystem.ActiveMember : null;

        /// <summary>生命周期刷新间隔，避免每帧全量遍历。</summary>
        private const float TickInterval = 0.25f;

        /// <summary>玩家坐标上传间隔；服务器会以独立的 3 秒定时器将房间坐标写入数据库。</summary>
        private const float PositionUploadInterval = 1f;


        /// <summary>交互物触发体的统一 tag，玩家感应据此过滤。</summary>
        private const string PoiTag = "POI";

        /// <summary>交互物根节点球形触发体的半径（米）。</summary>
        private const float PoiTriggerRadius = 0.5f;

        private readonly List<PoiMono> allPois = new List<PoiMono>();
        private readonly Dictionary<string, PoiMono> poisById = new Dictionary<string, PoiMono>();
        private readonly HashSet<int> syncedChunks = new HashSet<int>(); // 当前邻域内已拉取状态的 chunkId
        /// <summary>复用的当前邻域 chunk 集合，避免每次同步分配。</summary>
        private readonly HashSet<int> nearbyChunks = new HashSet<int>();
        /// <summary>复用的离开邻域 chunk 缓冲，避免在遍历 syncedChunks 时直接删除。</summary>
        private readonly List<int> departedChunks = new List<int>();
        /// <summary>记录每只营地史莱姆实体对应的场景营地位置，死亡通知按实体编号反查营地。</summary>
        private readonly Dictionary<int, Vector3> monsterCampByEntityId = new Dictionary<int, Vector3>();
        /// <summary>缓存实体更新期间收到的死亡通知，在系统更新阶段结束后逐个执行补刷。</summary>
        private readonly Queue<Vector3> pendingMonsterCampRespawns = new Queue<Vector3>();
        /// <summary>在系统释放时取消初始化、坐标上传、区块同步和交互请求，阻止异步 continuation 修改已释放状态。</summary>
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private PlayerPositionPush pendingRestoredPosition;
        private float tickAccumulator;
        private float positionUploadAccumulator;
        private bool isAvailable;

        /// <summary>通过统一 Core 入口按需取得当前单局 ServiceSystem 接口，不保存或注入公共 System 实例。</summary>
        private static IServiceSystem ServiceSystem => Core.Gameplay.GetSystem<IServiceSystem>();

        /// <summary>本领域的网络适配器；按 ARCH-SYS-005 在使用点解析，不长期保存实例。</summary>
        private static IPoiGateway Gateway => Core.Gameplay.GetSystem<IPoiGateway>();

        /// <summary>已加载的 POI 数量（诊断）。</summary>
        public int PoiCount => allPois.Count;

        /// <summary>当前已经扫描到的全部 POI，面板只读遍历该集合，不直接修改 PoiSystem 生命周期。</summary>
        public IReadOnlyList<PoiMono> AllPois => allPois;

        /// <summary>读取当前上场玩家的世界位置，供大地图打开时立即定位到玩家。</summary>
        /// <param name="position">成功读取时写入当前玩家位置。</param>
        /// <returns>当前存在可绑定玩家实体时返回 true。</returns>
        public bool TryGetPlayerPosition(out Vector3 position)
        {
            if (ActivePlayer != null && ActivePlayer.bindGo != null)
            {
                position = ActivePlayer.bindGo.transform.position;
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>建立单局状态并通过 ServiceSystem 执行一次服务器探测，仅在连接成功后启用网络同步和交互逻辑。</summary>
        public override void AfterNew()
        {
            Core.Event.AddListener<EntityDiedEvent>(OnEntityDied);
            ServiceSystem.WorldUnavailable += OnWorldUnavailable;
            SpawnMonsterCampEnemies();
            InitializeAsync(lifetimeCancellation.Token).Forget();
        }

        /// <summary>按场景中的怪物营地实例各生成一只史莱姆；该一次性本地行为不依赖 POI 服务器或语义 Id 唯一性。</summary>
        private void SpawnMonsterCampEnemies()
        {
            PoiMono[] monos = UnityEngine.Object.FindObjectsOfType<PoiMono>(true);
            IEntitySystem entitySystem = Core.Gameplay.GetSystem<IEntitySystem>();
            foreach (PoiMono mono in monos)
            {
                if (mono == null || mono.Config == null || mono.Config.PoiType != PoiType.MonsterCamp) continue;
                SlimeEntity enemy = entitySystem.SpawnEnemy(mono.transform.position);
                monsterCampByEntityId[enemy.EntityId] = mono.transform.position;
            }
        }

        /// <summary>接收全局实体死亡通知；仅登记属于营地的史莱姆并在本帧安全阶段补刷。</summary>
        private void OnEntityDied(EntityDiedEvent evt)
        {
            if (evt == null || !monsterCampByEntityId.TryGetValue(evt.EntityId, out Vector3 campPosition)) return;
            monsterCampByEntityId.Remove(evt.EntityId);
            pendingMonsterCampRespawns.Enqueue(campPosition);
        }

        /// <summary>先加载本地静态 POI，再执行服务器连接检测；地图展示不依赖服务器，网络只控制状态同步和交互请求。</summary>
        private async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            LoadFromScene();
            try
            {
                JoinRoomResponse joinResponse = await ServiceSystem.EnterWorldAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (joinResponse != null) OnPositionRestored(joinResponse.Position);
                ApplyRestoredPosition();
                isAvailable = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[PoiSystem] 未检测到 POI 服务器，已暂停状态同步和交互请求，本地地图 POI 仍可显示：{e.Message}");
            }
        }

        /// <summary>应用服务器返回的最近持久化位置；连接和重连都经过此入口，确保玩家在正确位置生成。</summary>
        private void OnPositionRestored(PlayerPositionPush position)
        {
            pendingRestoredPosition = position;
            ApplyRestoredPosition();
        }

        /// <summary>在玩家 GameObject 已完成生成后应用待恢复坐标；网络回调可能早于玩家实体生成。</summary>
        private void ApplyRestoredPosition()
        {
            if (pendingRestoredPosition == null || ActivePlayer == null || ActivePlayer.bindGo == null) return;
            PlayerPositionPush position = pendingRestoredPosition;
            ActivePlayer.bindGo.transform.position = new Vector3(position.X, position.Y, position.Z);
            pendingRestoredPosition = null;
            Debug.Log($"[PoiSystem] 已恢复玩家位置 ({position.X}, {position.Y}, {position.Z})");
        }

        /// <summary>扫描场景全部 PoiMono 并按稳定 Id 建立索引；POI 不创建实体，状态直接保存在组件上。</summary>
        private void LoadFromScene()
        {
            PoiMono[] monos = UnityEngine.Object.FindObjectsByType<PoiMono>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (PoiMono mono in monos)
            {
                if (mono == null || mono.Config == null) continue;
                PoiConfig cfg = mono.Config;
                if (string.IsNullOrEmpty(cfg.Id) || poisById.ContainsKey(cfg.Id)) continue;
                allPois.Add(mono);
                poisById[cfg.Id] = mono;
                EnsurePoiTrigger(mono.gameObject);
            }
            Debug.Log($"PoiSystem: loaded {allPois.Count} POIs from scene.");
            PublishMapPoiChanged(null);
        }

        /// <summary>低频驱动世界同步：上传玩家位置并拉取附近 chunk 状态，不按距离切换 POI 显隐。</summary>
        public override void OnUpdate(float dt)
        {
            // 先处理死亡期间排队的营地补刷，避免在 EntitySystem 遍历期间修改实体集合。
            RespawnPendingMonsterCampEnemies();
            ApplyRestoredPosition();
            if (ActivePlayer == null || ActivePlayer.bindGo == null) return;
            Vector3 playerPos = ActivePlayer.bindGo.transform.position;
            tickAccumulator += dt;
            positionUploadAccumulator += dt;
            if (tickAccumulator < TickInterval) return;
            tickAccumulator = 0f;
            if (!isAvailable || !ServiceSystem.IsWorldAvailable)
            {
                isAvailable = false;
                return;
            }
            if (positionUploadAccumulator >= PositionUploadInterval)
            {
                positionUploadAccumulator = 0f;
                UploadPlayerPositionAsync(playerPos, lifetimeCancellation.Token).Forget();
            }
            SyncNearbyChunks(playerPos);
        }

        /// <summary>通知地图面板重新读取 PoiSystem 的 POI 集合或指定 POI 状态。</summary>
        private void PublishMapPoiChanged(string poiId)
        {
            Core.Event.Invoke(new WorldMapPoiChangedEvent(poiId));
        }

        /// <summary>上传玩家当前坐标，保证服务器 3 秒持久化周期使用的是最新移动位置。</summary>
        private async UniTask UploadPlayerPositionAsync(Vector3 position, CancellationToken cancellationToken)
        {
            try
            {
                await Gateway.UploadPositionAsync(position, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception e)
            {
                bool shouldLog = isAvailable;
                if (!ServiceSystem.IsWorldAvailable) isAvailable = false;
                if (shouldLog) Debug.LogWarning($"[PoiSystem] 玩家坐标上传失败：{e.Message}");
            }
        }

        /// <summary>在 EntitySystem 完成实体遍历后执行待处理营地补刷，确保新增实体不会修改遍历集合。</summary>
        private void RespawnPendingMonsterCampEnemies()
        {
            if (pendingMonsterCampRespawns.Count == 0) return;
            IEntitySystem entitySystem = Core.Gameplay.GetSystem<IEntitySystem>();
            while (pendingMonsterCampRespawns.Count > 0)
            {
                Vector3 campPosition = pendingMonsterCampRespawns.Dequeue();
                SlimeEntity enemy = entitySystem.SpawnEnemy(campPosition);
                monsterCampByEntityId[enemy.EntityId] = campPosition;
            }
        }

        /// <summary>
        /// 拉取玩家所在 chunk 及其 3×3 邻域内尚未同步的 chunk 状态。
        /// 离开邻域的 chunk 会解除已同步标记：玩家再次靠近时重新拉取一次最新状态，
        /// 使可刷新 POI 的重生结果能够进入客户端——重生判定完全由服务器时间戳驱动，
        /// 客户端不做本地倒计时，因此必须依赖这次重新同步才能观察到状态变化。
        /// </summary>
        private void SyncNearbyChunks(Vector3 playerPos)
        {
            int playerChunk = ChunkIdCodec.EncodeFromPosition(playerPos);
            int cx = ChunkIdCodec.ChunkX(playerChunk);
            int cy = ChunkIdCodec.ChunkY(playerChunk);
            nearbyChunks.Clear();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    // chunk 坐标非负（客户端只在正方向添加 chunk），邻域裁剪到非负。
                    int nx = Mathf.Max(0, cx + dx);
                    int nz = Mathf.Max(0, cy + dz);
                    nearbyChunks.Add(ChunkIdCodec.Encode(nx, nz));
                }

            departedChunks.Clear();
            foreach (int syncedChunk in syncedChunks)
            {
                if (!nearbyChunks.Contains(syncedChunk)) departedChunks.Add(syncedChunk);
            }
            for (int index = 0; index < departedChunks.Count; index++) syncedChunks.Remove(departedChunks[index]);

            foreach (int chunkId in nearbyChunks)
            {
                if (!syncedChunks.Add(chunkId)) continue;
                PullChunkAsync(chunkId, lifetimeCancellation.Token).Forget();
            }
        }

        /// <summary>异步拉取指定 chunk 的状态并按 Id 应用到本地实体。</summary>
        private async UniTask PullChunkAsync(int chunkId, CancellationToken cancellationToken)
        {
            try
            {
                PullChunkResponse response = await Gateway.PullChunkAsync(chunkId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (PoiState state in response.States)
                {
                    if (poisById.TryGetValue(state.Id, out PoiMono poi))
                    {
                        PoiStateApplier.Apply(poi, state);
                        PublishMapPoiChanged(state.Id);
                    }
                }
                Debug.Log($"PoiSystem: chunk {chunkId} synced {response.States.Count} states.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception e)
            {
                bool shouldLog = isAvailable;
                if (!ServiceSystem.IsWorldAvailable) isAvailable = false;
                if (shouldLog) Debug.LogError($"[PoiSystem] 拉取 chunk {chunkId} 失败：{e.Message}");
            }
        }

        /// <summary>
        /// 交互入口：向服务器发起请求，仅当服务器确认（返回 true）后才把交互应用到本地实体触发表现。
        /// </summary>
        /// <returns>服务器是否确认成功。</returns>
        public async UniTask<bool> TryInteractAsync(PoiMono poi)
        {
            if (!isAvailable || poi == null || poi.Config == null) return false;
            PoiOp op = GetInteractOp(poi.Config.PoiType);
            Debug.Log($"[交互] 请求服务器 {poi.Config.Id} op={op}");
            try
            {
                InteractResponse response = await Gateway.InteractAsync(poi.Config.Id, op, lifetimeCancellation.Token);
                lifetimeCancellation.Token.ThrowIfCancellationRequested();
                Debug.Log($"[交互] 服务器响应 {poi.Config.Id} => success={response.Success}");
                if (response.State != null)
                {
                    PoiStateApplier.Apply(poi, response.State); // 重复操作失败时仍应用服务器最新状态，消除客户端过期表现。
                    if (poi.IsConsumed) RemoveFromNearby(poi); // 服务器状态已消费时立即移除过期交互入口。
                    PublishMapPoiChanged(poi.Config.Id);
                }
                if (!response.Success) return false;
                return true;
            }
            catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested) { return false; }
            catch (Exception e)
            {
                bool shouldLog = isAvailable;
                if (!ServiceSystem.IsWorldAvailable) isAvailable = false;
                if (shouldLog) Debug.LogError($"[PoiSystem] 交互请求失败：{e.Message}");
                return false;
            }
        }

        /// <summary>把已消费（消失）的 POI 从玩家附近交互列表中移除。</summary>
        private void RemoveFromNearby(PoiMono poi)
        {
            if (ActivePlayer == null) return;
            if (ActivePlayer.TryGetComp(out InteractComponent interact))
            {
                interact.RemoveNearby(poi.Config);
                Debug.Log($"[交互] 移出交互列表 {poi.Config.Id}");
            }
        }

        /// <summary>按语义 Id 查询当前场景中的 PoiMono。</summary>
        public bool TryGetPoi(string poiId, out PoiMono poi) => poisById.TryGetValue(poiId, out poi);

        /// <summary>把当前玩家传送到已加载的神像或传送锚点位置；两个地图面板下一帧直接读取实体坐标。</summary>
        /// <param name="poiId">目标神像或传送锚点的语义 Id。</param>
        /// <returns>目标存在、类型允许且当前玩家可用时返回 true。</returns>
        public bool TryTeleportToPoi(string poiId)
        {
            if (!poisById.TryGetValue(poiId, out PoiMono poi) || poi == null || poi.Config == null) return false;
            if (poi.Config.PoiType != PoiType.Statue && poi.Config.PoiType != PoiType.TeleAnchor) return false;
            if (ActivePlayer == null || ActivePlayer.bindGo == null) return false;
            Vector3 targetPosition = poi.transform.position;
            Entity player = ActivePlayer;
            CharacterController characterController = player.bindGo.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;
            player.bindGo.transform.position = targetPosition;
            if (characterController != null) characterController.enabled = true;
            if (player.TryGetComp(out MotionComponent motionComponent))
            {
                // 传送后清空上一位置残留的速度和根运动，避免下一帧运动逻辑把角色拉回原位置。
                motionComponent.curVelo = Vector3.zero;
                motionComponent.ClearRootMotionDelta();
                motionComponent.landThisFrame = false;
                motionComponent.wasGroundedLastFrame = false;
            }
            Debug.Log($"[PoiSystem] 玩家已传送到 {poi.Config.PoiType} {poi.Config.Id} ({targetPosition.x}, {targetPosition.y}, {targetPosition.z})");
            return true;
        }

        /// <summary>按 POI 类型映射到对应的交互操作（MonsterCamp 暂无操作，返回 Unlock 占位）。</summary>
        public static PoiOp GetInteractOp(PoiType type)
        {
            switch (type)
            {
                case PoiType.TeleAnchor:
                case PoiType.Statue:
                case PoiType.Dungeon:
                    return PoiOp.Unlock;
                case PoiType.Chest:
                    return PoiOp.OpenChest;
                case PoiType.SpiritCore:
                    return PoiOp.CollectCore;
                case PoiType.Gathering:
                    return PoiOp.Gather;
                case PoiType.MapBoss:
                    return PoiOp.Defeat;
                default:
                    return PoiOp.Unlock;
            }
        }

        /// <summary>为 POI 根节点补齐交互触发体：半径 0.5 的球形 trigger 并打上统一 POI tag。</summary>
        private void EnsurePoiTrigger(GameObject poiRoot)
        {
            if (poiRoot == null) return;
            SphereCollider trigger = poiRoot.GetComponent<SphereCollider>();
            if (trigger == null) trigger = poiRoot.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = PoiTriggerRadius;
            poiRoot.tag = PoiTag;
        }

        /// <summary>释放世界事件和单局缓存；ServiceSystem 及其 Push 订阅由 GameplayKit 独立释放。</summary>
        public override void Dispose()
        {
            lifetimeCancellation.Cancel();
            ServiceSystem.WorldUnavailable -= OnWorldUnavailable;
            Core.Event.RemoveListener<EntityDiedEvent>(OnEntityDied);
            monsterCampByEntityId.Clear();
            pendingMonsterCampRespawns.Clear();
            pendingRestoredPosition = null;
            isAvailable = false;
        }

        /// <summary>在 ServiceSystem 报告连接失效时立即停止本局全部后续世界网络轮询和交互。</summary>
        private void OnWorldUnavailable() { isAvailable = false; }
    }
}
