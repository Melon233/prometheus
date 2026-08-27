using UnityEngine;

namespace Xuan.Prometheus.World
{
    // ===== 一次性解锁类 =====

    /// <summary>传送锚点逻辑：解锁后可用于传送（传送目标/表现由业务层扩展）。状态持久化。</summary>
    public sealed class TeleAnchorLogic : PoiLogic
    {
        /// <summary>当前是否已解锁。</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>解锁该锚点（幂等）。</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            EventHandler<PoiUnlockedEvent>.Invoke(new PoiUnlockedEvent { Id = Config?.Id });
        }

        /// <summary>从持久化状态恢复。</summary>
        public void SetState(bool unlocked) => IsUnlocked = unlocked;

        /// <summary>交互：请求服务器解锁锚点。</summary>
        public override void OnInteract() => RequestServerInteract(PoiOp.Unlock);
    }

    /// <summary>七天神像逻辑：解锁后可升级等级（回复/供奉等业务由业务层扩展）。状态持久化。</summary>
    public sealed class StatueLogic : PoiLogic
    {
        /// <summary>当前是否已解锁。</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>当前供奉等级。</summary>
        public int Level { get; private set; } = 1;

        /// <summary>当前供奉进度。</summary>
        public float Progress { get; private set; }

        /// <summary>解锁该神像（幂等）。</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            EventHandler<PoiUnlockedEvent>.Invoke(new PoiUnlockedEvent { Id = Config?.Id });
        }

        /// <summary>供奉升级，仅在已解锁时生效。</summary>
        public void Upgrade()
        {
            if (!IsUnlocked) return;
            Level++;
        }

        /// <summary>推进供奉进度。</summary>
        public void AddProgress(float amount)
        {
            if (!IsUnlocked) return;
            Progress += amount;
        }

        /// <summary>从持久化状态恢复。</summary>
        public void SetState(bool unlocked, int level, float progress)
        {
            IsUnlocked = unlocked;
            Level = level;
            Progress = progress;
        }

        /// <summary>交互：请求服务器供奉（消耗风神瞳推进进度，升级发长剑）。</summary>
        public override void OnInteract() => RequestServerInteract(PoiOp.OfferStatue);
    }

    /// <summary>副本逻辑：解锁后推进通关进度（进入副本场景由业务层扩展）。状态持久化。</summary>
    public sealed class DungeonLogic : PoiLogic
    {
        /// <summary>当前是否已解锁。</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>当前通关进度。</summary>
        public int Progress { get; private set; }

        /// <summary>解锁该副本（幂等）。</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            EventHandler<PoiUnlockedEvent>.Invoke(new PoiUnlockedEvent { Id = Config?.Id });
        }

        /// <summary>推进一段通关进度，仅在已解锁时生效。</summary>
        public void Advance()
        {
            if (!IsUnlocked) return;
            Progress++;
        }

        /// <summary>从持久化状态恢复。</summary>
        public void SetState(bool unlocked) => IsUnlocked = unlocked;

        /// <summary>交互：打开副本 UI（客户端行为，不请求服务器）。</summary>
        public override void OnInteract()
        {
            Debug.Log($"[交互] 打开副本 UI {Config?.Id}");
            // TODO: 打开副本 UI（当前无副本面板，预留）
        }
    }

    // ===== 一次性收集类 =====

    /// <summary>宝箱逻辑：可开启一次，重复开启无效。状态持久化。</summary>
    public sealed class ChestLogic : PoiLogic
    {
        /// <summary>当前是否已开启。</summary>
        public bool IsOpened { get; private set; }

        /// <summary>尝试开启宝箱；首次开启返回 true，重复开启返回 false。</summary>
        public bool Open()
        {
            if (IsOpened) return false;
            IsOpened = true;
            SetPoiVisible(false);
            Debug.Log($"[交互] 宝箱开启 {Config?.Id}");
            EventHandler<PoiOpenedEvent>.Invoke(new PoiOpenedEvent { Id = Config?.Id });
            return true;
        }

        /// <summary>宝箱开启后视为已消费，应消失。</summary>
        public override bool IsConsumed => IsOpened;

        /// <summary>从持久化状态恢复。</summary>
        public void SetState(bool opened)
        {
            IsOpened = opened;
            if (opened) SetPoiVisible(false);
        }

        /// <summary>交互：请求服务器开启宝箱。</summary>
        public override void OnInteract() => RequestServerInteract(PoiOp.OpenChest);
    }

    /// <summary>神瞳逻辑：可收集一次，重复收集无效。状态持久化。</summary>
    public sealed class SpiritCoreLogic : PoiLogic
    {
        /// <summary>当前是否已收集。</summary>
        public bool IsCollected { get; private set; }

        /// <summary>尝试收集神瞳；首次收集返回 true，重复收集返回 false。</summary>
        public bool Collect()
        {
            if (IsCollected) return false;
            IsCollected = true;
            SetPoiVisible(false);
            Debug.Log($"[交互] 神瞳收集 {Config?.Id}");
            EventHandler<PoiCollectedEvent>.Invoke(new PoiCollectedEvent { Id = Config?.Id });
            return true;
        }

        /// <summary>神瞳收集后视为已消费，应消失。</summary>
        public override bool IsConsumed => IsCollected;

        /// <summary>从持久化状态恢复。</summary>
        public void SetState(bool collected)
        {
            IsCollected = collected;
            if (collected) SetPoiVisible(false);
        }

        /// <summary>交互：请求服务器收集神瞳。</summary>
        public override void OnInteract() => RequestServerInteract(PoiOp.CollectCore);
    }

    // ===== 可刷新类 =====

    /// <summary>采集物逻辑：重生时间戳由服务器下发，基类处理重生与显示。</summary>
    public sealed class GatheringLogic : RespawnablePoiLogic
    {
        /// <summary>交互：请求服务器采集。</summary>
        public override void OnInteract() => RequestServerInteract(PoiOp.Gather);
    }

    /// <summary>地图 Boss 逻辑：重生时间戳由服务器下发，基类处理重生与显示。</summary>
    public sealed class MapBossLogic : RespawnablePoiLogic
    {
        /// <summary>交互：请求服务器击败 Boss。</summary>
        public override void OnInteract() => RequestServerInteract(PoiOp.Defeat);
    }

    /// <summary>怪物营地逻辑：刷新属边缘逻辑，不入库（无服务器重生时间戳，始终保持可用）。</summary>
    public sealed class MonsterCampLogic : RespawnablePoiLogic
    {
    }
}
