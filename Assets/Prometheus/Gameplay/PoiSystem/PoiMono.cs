using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Npc;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 场景中摆放的 POI 组件，同时承载该 POI 的配置、服务器权威状态与交互入口。
    ///
    /// POI 不使用 ELC：它的数量由场景摆放固定，表现对象先于运行时存在（不由实体创建），
    /// 状态完全来自服务器，且没有任何逐帧逻辑——ELC 的实体生命周期、组件组合与帧驱动
    /// 对它全是净负担。类型之间的差异只有三点：交互发哪个 PoiOp、读服务器状态的哪个字段、
    /// 消费判定用布尔还是重生时间戳，因此按 PoiType 分派即可，不需要继承层次。
    /// </summary>
    public class PoiMono : MonoBehaviour
    {
        /// <summary>该 POI 的配置数据，由烘焙工具写入，PoiSystem 扫描读取。</summary>
        public PoiConfig Config;

        /// <summary>一次性消费标记：宝箱已开启、神瞳已收集。</summary>
        private bool consumed;

        /// <summary>一次性解锁标记：传送锚点、神像、副本是否已解锁。</summary>
        private bool unlocked;

        /// <summary>可刷新类的下次重生 Unix 毫秒时间戳；零表示当前可用。</summary>
        private long respawnAtMs;

        /// <summary>该 POI 的稳定语义 Id；配置缺失时为空。</summary>
        public string PoiId => Config != null ? Config.Id : null;

        /// <summary>该 POI 是否已解锁（仅解锁类有意义）。</summary>
        public bool IsUnlocked => unlocked;

        /// <summary>
        /// 该 POI 当前是否应从世界与地图上消失。
        /// 一次性收集类看消费标记，可刷新类看重生时间戳，其余类型恒为 false。
        /// </summary>
        public bool IsConsumed => consumed || respawnAtMs != 0L;

        /// <summary>写入一次性解锁状态；由服务器状态同步调用。</summary>
        /// <param name="value">服务器下发的解锁标记。</param>
        public void SetUnlocked(bool value)
        {
            unlocked = value;
        }

        /// <summary>写入一次性消费状态并同步场景表现的显隐；由服务器状态同步调用。</summary>
        /// <param name="value">服务器下发的已开启/已收集标记。</param>
        public void SetConsumed(bool value)
        {
            consumed = value;
            SetVisible(!consumed);
        }

        /// <summary>
        /// 写入服务器下发的下次重生时间戳并同步显隐。
        /// 客户端不做本地倒计时：可用性只在收到服务器状态时判定一次，
        /// 因此离线期间流逝的时间由服务器时间戳天然覆盖。
        /// </summary>
        /// <param name="value">Unix 毫秒时间戳；零或已过期表示当前可用。</param>
        public void SetRespawnAt(long value)
        {
            respawnAtMs = value > NowMs() ? value : 0L;
            SetVisible(respawnAtMs == 0L);
        }

        /// <summary>
        /// 外部交互入口：按 POI 类型执行该类型的交互行为。
        /// 服务器权威类型统一提交一次交互请求，由服务器响应回写状态；
        /// 纯客户端类型（副本入口）直接打开界面，NPC 交由 NpcSystem 串行化会话。
        /// </summary>
        public void OnInteract()
        {
            if (Config == null) return;
            switch (Config.PoiType)
            {
                case PoiType.Npc:
                    // NPC 的可交互性由 NpcSystem 的会话串行化决定，POI 侧不再重复判定解锁。
                    // 整个交互（查绑定、加载剧情、进舞台、演出、上报、还原）都在 InteractAsync 内部完成。
                    if (Core.Gameplay.TryGetSystem(out INpcSystem npcSystem)) npcSystem.InteractAsync(this).Forget();
                    return;
                case PoiType.Dungeon:
                    // 副本入口是纯客户端行为，不请求服务器；当前尚无副本面板。
                    Debug.Log($"[交互] 打开副本 UI {Config.Id}");
                    return;
                case PoiType.MonsterCamp:
                    // 怪物营地不入库，也没有交互操作：营地敌人由 PoiSystem 监听死亡事件补刷。
                    return;
                default:
                    if (Core.Gameplay.TryGetSystem(out IPoiSystem poiSystem)) poiSystem.TryInteractAsync(this).Forget();
                    return;
            }
        }

        /// <summary>切换场景表现对象的显隐；状态未变化时不触发 Unity 调用。</summary>
        /// <param name="visible">目标显隐状态。</param>
        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        /// <summary>当前 Unix 毫秒时间戳。</summary>
        private static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
