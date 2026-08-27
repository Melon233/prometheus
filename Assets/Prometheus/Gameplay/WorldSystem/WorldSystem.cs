using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 管理大世界 POI 生命周期：以场景摆放的 PoiMono 为数据源，绑定 PoiEntity；
    /// AOI（3×3 邻域 + 兴趣半径）控制显隐（不销毁场景对象）；
    /// 服务器权威：按需拉取玩家附近 chunk 的状态，交互经服务器确认（返回 true）后才做表现。
    /// </summary>
    public sealed class WorldSystem : XSystem
    {
        /// <summary>生命周期刷新间隔，避免每帧全量遍历。</summary>
        private const float TickInterval = 0.25f;

        /// <summary>Go 服务器监听地址与端口，需与 Server/main.go 默认值及 Editor 启动脚本保持一致。</summary>
        private const string ServerHost = "127.0.0.1";
        private const int ServerPort = 9000;

        /// <summary>交互物触发体的统一 tag，玩家感应据此过滤。</summary>
        private const string PoiTag = "POI";

        /// <summary>交互物根节点球形触发体的半径（米）。</summary>
        private const float PoiTriggerRadius = 0.5f;

        private IGameplayKit gameplayKit;
        private readonly List<PoiConfig> persistentPois = new List<PoiConfig>(); // aoiExempt 常驻 POI
        private readonly List<PoiEntity> allPois = new List<PoiEntity>();
        private readonly Dictionary<string, PoiEntity> poisById = new Dictionary<string, PoiEntity>();
        private readonly HashSet<int> syncedChunks = new HashSet<int>(); // 已拉取状态的 chunkId
        private PoiNetworkClient client;
        private float tickAccumulator;
        private bool isAvailable;

        /// <summary>AOI 网格边长，与 chunk 尺寸一致。</summary>
        public float RegionSize { get; set; } = ChunkIdCodec.ChunkSize;

        /// <summary>AOI 兴趣半径。</summary>
        public float InterestRadius { get; set; } = 15f;

        /// <summary>已加载的 POI 数量（诊断）。</summary>
        public int PoiCount => allPois.Count;

        /// <summary>POI 网络客户端（诊断 / 测试用）。</summary>
        public PoiNetworkClient Client => client;

        /// <summary>建立单局状态：创建客户端并异步检测服务器，仅在连接成功后扫描场景和启用系统逻辑。</summary>
        public override void AfterNew(IGameplayKit ownerGameplayKit)
        {
            gameplayKit = ownerGameplayKit;
            SpawnMonsterCampEnemies();
            client = new PoiNetworkClient(ServerHost, ServerPort);
            InitializeAsync().Forget();
        }

        /// <summary>按场景中的怪物营地实例各生成一只史莱姆；该一次性本地行为不依赖 POI 服务器或语义 Id 唯一性。</summary>
        private void SpawnMonsterCampEnemies()
        {
            PoiMono[] monos = UnityEngine.Object.FindObjectsOfType<PoiMono>(true);
            EntitySystem entitySystem = gameplayKit.GetSystem<EntitySystem>();
            foreach (PoiMono mono in monos)
            {
                if (mono == null || mono.Config == null || mono.Config.PoiType != PoiType.MonsterCamp) continue;
                entitySystem.SpawnEnemy(mono.transform.position);
            }
        }

        /// <summary>执行一次初始化连接检测；服务器不可用时保持系统禁用，避免后续更新与交互持续发起失败请求。</summary>
        private async UniTask InitializeAsync()
        {
            try
            {
                await client.ConnectAsync();
                LoadFromScene();
                isAvailable = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WorldSystem] 未检测到 POI 服务器，已屏蔽 WorldSystem 逻辑：{e.Message}");
            }
        }

        /// <summary>扫描场景全部 PoiMono 作为数据源，为每个绑定 PoiEntity（按 Id 建立索引）。</summary>
        private void LoadFromScene()
        {
            PoiMono[] monos = UnityEngine.Object.FindObjectsOfType<PoiMono>(true);
            foreach (PoiMono mono in monos)
            {
                if (mono == null || mono.Config == null) continue;
                PoiConfig cfg = mono.Config;
                if (string.IsNullOrEmpty(cfg.Id) || poisById.ContainsKey(cfg.Id)) continue;
                if (cfg.aoiExempt) persistentPois.Add(cfg);
                PoiEntity entity = new PoiEntity(mono.gameObject, cfg);
                gameplayKit.GetSystem<EntitySystem>().AddEntity(entity);
                entity.AfterNew();
                allPois.Add(entity);
                poisById[cfg.Id] = entity;
                EnsurePoiTrigger(mono.gameObject);
            }
            Debug.Log($"WorldSystem: loaded {allPois.Count} POIs from scene, {persistentPois.Count} persistent.");
        }

        /// <summary>低频驱动生命周期：以玩家位置刷新 AOI 显隐，并拉取附近 chunk 状态。</summary>
        public override void OnUpdate(float dt)
        {
            if (!isAvailable || gameplayKit == null || gameplayKit.Player == null || gameplayKit.Player.bindGo == null) return;
            tickAccumulator += dt;
            if (tickAccumulator < TickInterval) return;
            tickAccumulator = 0f;
            Vector3 playerPos = gameplayKit.Player.bindGo.transform.position;
            RefreshAt(playerPos);
            SyncNearbyChunks(playerPos);
        }

        /// <summary>拉取玩家所在 chunk 及其 3×3 邻域内尚未同步的 chunk 状态。</summary>
        private void SyncNearbyChunks(Vector3 playerPos)
        {
            int playerChunk = ChunkIdCodec.EncodeFromPosition(playerPos);
            int cx = ChunkIdCodec.ChunkX(playerChunk);
            int cy = ChunkIdCodec.ChunkY(playerChunk);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                // chunk 坐标非负（客户端只在正方向添加 chunk），邻域裁剪到非负。
                int nx = Mathf.Max(0, cx + dx);
                int nz = Mathf.Max(0, cy + dz);
                int chunkId = ChunkIdCodec.Encode(nx, nz);
                if (syncedChunks.Contains(chunkId)) continue;
                syncedChunks.Add(chunkId);
                PullChunkAsync(chunkId).Forget();
            }
        }

        /// <summary>异步拉取指定 chunk 的状态并按 Id 应用到本地实体。</summary>
        private async UniTask PullChunkAsync(int chunkId)
        {
            try
            {
                PullChunkResponse response = await client.PullChunkAsync(chunkId);
                foreach (PoiState state in response.States)
                {
                    if (poisById.TryGetValue(state.Id, out PoiEntity entity)) PoiStateApplier.Apply(entity, state);
                }
                Debug.Log($"WorldSystem: chunk {chunkId} synced {response.States.Count} states.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WorldSystem] 拉取 chunk {chunkId} 失败：{e.Message}");
            }
        }

        /// <summary>
        /// 交互入口：向服务器发起请求，仅当服务器确认（返回 true）后才把交互应用到本地实体触发表现。
        /// </summary>
        /// <returns>服务器是否确认成功。</returns>
        public async UniTask<bool> TryInteractAsync(PoiEntity entity, PoiOp op)
        {
            if (!isAvailable || client == null || entity == null) return false;
            Debug.Log($"[交互] 请求服务器 {entity.Config.Id} op={op}");
            try
            {
                InteractResponse response = await client.InteractAsync(entity.Config.Id, op);
                Debug.Log($"[交互] 服务器响应 {entity.Config.Id} => success={response.Success}");
                if (!response.Success) return false;
                PoiStateApplier.Apply(entity, response.State); // 服务器确认后按最新状态做表现
                if (entity.IsConsumed) RemoveFromNearby(entity); // 已消失则移出交互列表
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[WorldSystem] 交互请求失败：{e.Message}");
                return false;
            }
        }

        /// <summary>把已消费（消失）的 POI 从玩家附近交互列表中移除。</summary>
        private void RemoveFromNearby(PoiEntity entity)
        {
            if (gameplayKit == null || gameplayKit.Player == null) return;
            if (gameplayKit.Player.TryGetComp(out InteractComponent interact))
            {
                interact.RemoveNearby(entity.Config);
                Debug.Log($"[交互] 移出交互列表 {entity.Config.Id}");
            }
        }

        /// <summary>按语义 Id 查询当前场景中的 PoiEntity。</summary>
        public bool TryGetPoiEntity(string poiId, out PoiEntity entity) => poisById.TryGetValue(poiId, out entity);

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

        /// <summary>AOI：aoiExempt 常驻显示；普通 POI 在 9 格邻域 + 兴趣半径内显示，否则隐藏（不销毁场景对象）。</summary>
        public void RefreshAt(Vector3 playerPos)
        {
            int centerX = Mathf.FloorToInt(playerPos.x / RegionSize);
            int centerY = Mathf.FloorToInt(playerPos.z / RegionSize);
            foreach (PoiEntity entity in allPois)
            {
                PoiConfig cfg = entity.Config;
                Vector3 poiPos = entity.bindGo != null ? entity.bindGo.transform.position : cfg.Position;
                bool visible = (cfg.aoiExempt || IsInAoi(poiPos, playerPos, centerX, centerY)) && !entity.IsConsumed;
                if (entity.bindGo != null && entity.bindGo.activeSelf != visible) entity.bindGo.SetActive(visible);
            }
        }

        /// <summary>判断 POI 是否位于玩家 3×3 邻域 + 兴趣半径内（位置取 GameObject 实际世界坐标）。</summary>
        private bool IsInAoi(Vector3 poiPos, Vector3 playerPos, int centerX, int centerY)
        {
            int px = Mathf.FloorToInt(poiPos.x / RegionSize);
            int py = Mathf.FloorToInt(poiPos.z / RegionSize);
            if (Mathf.Abs(px - centerX) > 1 || Mathf.Abs(py - centerY) > 1) return false;
            return Vector3.Distance(poiPos, playerPos) <= InterestRadius;
        }

        /// <summary>释放网络客户端。</summary>
        public override void Dispose()
        {
            isAvailable = false;
            client?.Dispose();
            client = null;
        }
    }
}
