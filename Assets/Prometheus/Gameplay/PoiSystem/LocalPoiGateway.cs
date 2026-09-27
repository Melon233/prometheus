using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.LocalAuthority;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 世界领域网关的单机实现：在进程内执行原服务器的 POI 权威规则，状态只保存在内存中。
    ///
    /// 规则逐条移植自 <c>Server/internal/service/service.go</c>：
    /// POI 静态定义来自策划导出的 PoiExport.json（与服务器播种同源）；
    /// 一次性操作（解锁/开箱/收集）重复请求返回失败，可刷新操作（采集/击败）总是成功并写入重生时间戳，
    /// 供奉消耗全部风神瞳推进神像进度。响应仍使用协议消息，PoiSystem 因此与联网版共用同一套处理逻辑。
    /// 单机版不做存档：每次进入游戏，全部 POI 都回到初始状态，玩家也从世界定义的出生点开始。
    /// </summary>
    internal sealed class LocalPoiGateway : XSystem, IPoiGateway
    {
        /// <summary>策划导出 POI 定义的 Resources 路径；与服务器 <c>-export</c> 参数指向同一份文件。</summary>
        private const string PoiExportResourcePath = "Config/PoiExport";

        /// <summary>采集物重生周期（毫秒），与服务器 GatheringRespawnSeconds 一致。</summary>
        private const long GatheringRespawnMs = 30L * 1000L;

        /// <summary>地图 Boss 重生周期（毫秒），与服务器 MapBossRespawnSeconds 一致。</summary>
        private const long MapBossRespawnMs = 300L * 1000L;

        /// <summary>单机版固定的房间与玩家标识，只用于填充协议响应。</summary>
        private const string LocalId = "local";

        /// <summary>与背包网关共享的背包；交互掉落与神像供奉经由它修改物品。</summary>
        private readonly LocalInventory inventory;

        /// <summary>导出中全部 POI 的运行时状态，按 Id 索引。</summary>
        private readonly Dictionary<string, PoiRecord> recordsById = new Dictionary<string, PoiRecord>();

        /// <summary>按区块分组的 POI 状态，服务区块拉取。</summary>
        private readonly Dictionary<int, List<PoiRecord>> recordsByChunk = new Dictionary<int, List<PoiRecord>>();

        /// <summary>创建单机 POI 网关。</summary>
        /// <param name="inventory">与背包网关共享的背包。</param>
        public LocalPoiGateway(LocalInventory inventory)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        /// <inheritdoc />
        public bool IsAvailable => true;

        /// <summary>单机数据源不会失效，因此该事件永不触发；保留空访问器只为满足契约。</summary>
        public event Action Unavailable
        {
            add { }
            remove { }
        }

        /// <summary>单机数据源没有服务端推送，因此该事件永不触发；保留空访问器只为满足契约。</summary>
        public event Action<PlayerPositionPush> PositionReceived
        {
            add { }
            remove { }
        }

        /// <summary>按导出定义为每个 POI 建立初始状态并按区块索引（对应服务器的 Seed 与玩家模板初始化）。</summary>
        public override void AfterNew()
        {
            TextAsset exportAsset = Resources.Load<TextAsset>(PoiExportResourcePath);
            PoiSeedList export = JsonUtility.FromJson<PoiSeedList>(exportAsset.text);
            Resources.UnloadAsset(exportAsset);
            foreach (PoiSeed seed in export.pois)
            {
                PoiRecord record = new PoiRecord(seed.Id, seed.PoiType, seed.ChunkId);
                // 导出里出现重复 Id 属于烘焙错误，Add 会直接抛出而不是静默覆盖。
                recordsById.Add(record.Id, record);
                if (!recordsByChunk.TryGetValue(record.ChunkId, out List<PoiRecord> chunk)) recordsByChunk[record.ChunkId] = chunk = new List<PoiRecord>();
                chunk.Add(record);
            }
        }

        /// <summary>进入默认世界；单机版不保存玩家位置，因此恒返回空位置，PoiSystem 据此保留世界定义的出生点。</summary>
        public UniTask<JoinRoomResponse> EnterWorldAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(new JoinRoomResponse { Success = true, RoomId = LocalId, PlayerId = LocalId });
        }

        /// <inheritdoc />
        public UniTask<PullChunkResponse> PullChunkAsync(int chunkId, CancellationToken cancellationToken = default)
        {
            PullChunkResponse response = new PullChunkResponse();
            if (recordsByChunk.TryGetValue(chunkId, out List<PoiRecord> chunk))
            {
                foreach (PoiRecord record in chunk) response.States.Add(record.ToState());
            }
            return UniTask.FromResult(response);
        }

        /// <inheritdoc />
        public UniTask<PullAllResponse> PullAllAsync(CancellationToken cancellationToken = default)
        {
            PullAllResponse response = new PullAllResponse();
            foreach (PoiRecord record in recordsById.Values) response.States.Add(record.ToState());
            return UniTask.FromResult(response);
        }

        /// <inheritdoc />
        public UniTask<InteractResponse> InteractAsync(string poiId, PoiOp op, CancellationToken cancellationToken = default)
        {
            if (!recordsById.TryGetValue(poiId, out PoiRecord record)) return UniTask.FromResult(new InteractResponse { Success = false });
            if (op == PoiOp.OfferStatue)
            {
                // 供奉与服务器一致：没有风神瞳时也返回成功，只是进度不变。
                OfferStatue(record);
                return UniTask.FromResult(new InteractResponse { Success = true, State = record.ToState() });
            }

            // 失败时仍返回最新状态，PoiSystem 据此消除场景过期表现。
            if (!ApplyOperation(record, op)) return UniTask.FromResult(new InteractResponse { Success = false, State = record.ToState() });
            GrantDrops(op);
            return UniTask.FromResult(new InteractResponse { Success = true, State = record.ToState() });
        }

        /// <summary>单机版不保存玩家位置，上传直接确认成功；保留该入口只为满足契约。</summary>
        public UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(new UpdatePositionResponse { Success = true });
        }

        /// <summary>会话结束时丢弃全部 POI 状态；下一次会话从导出重新建立初始状态。</summary>
        public override void Dispose()
        {
            recordsById.Clear();
            recordsByChunk.Clear();
        }

        /// <summary>
        /// 把一次交互落到记录对应的状态字段（对应服务器 applyToState）。
        /// </summary>
        /// <returns>产生了一次有效变更时返回 true；一次性操作重复执行返回 false。</returns>
        private static bool ApplyOperation(PoiRecord record, PoiOp op)
        {
            switch (op)
            {
                case PoiOp.Unlock:
                    switch (record.PoiType)
                    {
                        case PoiType.Statue: return SetOnce(ref record.StatueUnlocked);
                        case PoiType.TeleAnchor: return SetOnce(ref record.AnchorUnlocked);
                        case PoiType.Dungeon: return SetOnce(ref record.DungeonUnlocked);
                        default: return false;
                    }
                case PoiOp.OpenChest: return SetOnce(ref record.ChestOpened);
                case PoiOp.CollectCore: return SetOnce(ref record.SpiritCoreCollected);
                case PoiOp.Gather:
                    record.GatheringRespawnAt = NowMs() + GatheringRespawnMs;
                    return true;
                case PoiOp.Defeat:
                    record.MapBossRespawnAt = NowMs() + MapBossRespawnMs;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>把一次性标记置为 true；已经为 true 时不修改并返回 false。</summary>
        private static bool SetOnce(ref bool flag)
        {
            if (flag) return false;
            flag = true;
            return true;
        }

        /// <summary>按交互操作发放掉落物品（对应服务器 grantItems）。</summary>
        private void GrantDrops(PoiOp op)
        {
            switch (op)
            {
                case PoiOp.OpenChest:
                    inventory.Grant(LocalItemCatalog.Armor, 1);
                    inventory.Grant(LocalItemCatalog.ExpBook, 1);
                    break;
                case PoiOp.CollectCore:
                    inventory.Grant(LocalItemCatalog.Anemoculus, 1);
                    break;
                case PoiOp.Gather:
                    inventory.Grant(LocalItemCatalog.Apple, 1);
                    break;
            }
        }

        /// <summary>神像供奉：消耗全部风神瞳推进进度，每升一级发放一把长剑（对应服务器 offerStatue）。</summary>
        private void OfferStatue(PoiRecord record)
        {
            int count = inventory.ConsumeAll(LocalItemCatalog.Anemoculus);
            if (count == 0) return;
            record.StatueProgress += count * LocalItemCatalog.StatueProgressPerOculus;
            while (record.StatueProgress >= LocalItemCatalog.StatueLevelThreshold)
            {
                record.StatueProgress -= LocalItemCatalog.StatueLevelThreshold;
                record.StatueLevel++;
                inventory.Grant(LocalItemCatalog.Sword, 1);
            }
        }

        /// <summary>当前 Unix 毫秒时间戳；与 PoiMono 判断重生使用同一时间基准。</summary>
        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// 一个 POI 的运行时状态，字段与协议 PoiState 一一对应。
        /// 采用公开字段是为了让 <see cref="SetOnce"/> 能以 ref 直接改写标记；它只在本网关内部使用。
        /// </summary>
        private sealed class PoiRecord
        {
            /// <summary>POI 稳定 UUID，与场景 PoiConfig.Id 一致。</summary>
            public readonly string Id;

            /// <summary>POI 类型，决定解锁类操作写入哪个字段。</summary>
            public readonly PoiType PoiType;

            /// <summary>空间分区编号，用于按区块拉取。</summary>
            public readonly int ChunkId;

            /// <summary>七天神像：是否已激活。</summary>
            public bool StatueUnlocked;

            /// <summary>七天神像：供奉等级（对应服务器 initState）。</summary>
            public int StatueLevel;

            /// <summary>七天神像：当前等级内的供奉进度。</summary>
            public float StatueProgress;

            /// <summary>传送锚点：是否已激活。</summary>
            public bool AnchorUnlocked;

            /// <summary>宝箱：是否已开启。</summary>
            public bool ChestOpened;

            /// <summary>神瞳：是否已收集。</summary>
            public bool SpiritCoreCollected;

            /// <summary>采集物：下次重生的 Unix 毫秒时间戳，0 表示可用。</summary>
            public long GatheringRespawnAt;

            /// <summary>副本：是否已激活。</summary>
            public bool DungeonUnlocked;

            /// <summary>地图 Boss：下次重生的 Unix 毫秒时间戳，0 表示可用。</summary>
            public long MapBossRespawnAt;

            /// <summary>按类型建立初始状态（对应服务器 initState）：神像从 1 级开始，其余字段为零值。</summary>
            /// <param name="id">POI 稳定 UUID。</param>
            /// <param name="poiType">POI 类型。</param>
            /// <param name="chunkId">空间分区编号。</param>
            public PoiRecord(string id, PoiType poiType, int chunkId)
            {
                Id = id;
                PoiType = poiType;
                ChunkId = chunkId;
                StatueLevel = poiType == PoiType.Statue ? 1 : 0;
            }

            /// <summary>转换为协议状态（对应服务器 toProtoState）。</summary>
            public PoiState ToState()
            {
                return new PoiState
                {
                    Id = Id,
                    PoiType = (Protocol.PoiType)(int)PoiType,
                    StatueUnlocked = StatueUnlocked,
                    StatueLevel = StatueLevel,
                    StatueProgress = StatueProgress,
                    AnchorUnlocked = AnchorUnlocked,
                    ChestOpened = ChestOpened,
                    SpiritCoreCollected = SpiritCoreCollected,
                    GatheringRespawnAt = GatheringRespawnAt,
                    DungeonUnlocked = DungeonUnlocked,
                    MapBossRespawnAt = MapBossRespawnAt
                };
            }
        }

        /// <summary>
        /// PoiExport.json 中网关需要的最小字段集。
        /// 不直接反序列化为 PoiConfig：后者带有 ScriptableObject 引用字段，运行时无法从 JSON 还原；
        /// JsonUtility 会忽略这里未声明的字段，因此导出格式扩展不影响本结构。
        /// </summary>
        [Serializable]
        private sealed class PoiSeed
        {
            /// <summary>POI 稳定 UUID。</summary>
            public string Id;

            /// <summary>POI 类型。</summary>
            public PoiType PoiType;

            /// <summary>空间分区编号。</summary>
            public int ChunkId;
        }

        /// <summary>PoiExport.json 的根结构（JsonUtility 不支持以 List 为根）。</summary>
        [Serializable]
        private sealed class PoiSeedList
        {
            /// <summary>全部导出的 POI 定义。</summary>
            public List<PoiSeed> pois = new List<PoiSeed>();
        }
    }
}
