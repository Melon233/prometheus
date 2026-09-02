using System;
using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 可刷新类 POI（采集物/地图Boss/怪物营地）共享的重生逻辑：
    /// 服务器存绝对重生时间戳（Unix 毫秒），客户端据此判断可用性并显示剩余秒数；无本地倒计时，天然支持离线重生。
    /// </summary>
    public abstract class RespawnablePoiLogic : PoiLogic
    {
        /// <summary>头顶剩余秒数文本在 POI 局部空间的高度。</summary>
        private const float CooldownTextHeight = 2f;

        private long respawnAtMs;             // 下次重生时间戳（Unix 毫秒，0=可用）
        private TextMesh cooldownText;        // 头顶剩余秒数文本
        private long lastShownSecond = -1;    // 上次显示的秒数，秒数变化才刷新文本

        /// <summary>当前是否可用（可采集/可战斗）。</summary>
        public bool Available => respawnAtMs <= NowMs();

        /// <summary>剩余重生秒数（可用时为 0，向上取整）。</summary>
        public long RemainingSeconds => Available ? 0 : Math.Max(0, (respawnAtMs - NowMs() + 999) / 1000);

        /// <inheritdoc />
        public override bool IsConsumed => !Available;

        /// <inheritdoc />
        public override void OnUpdate(float dt)
        {
            if (respawnAtMs == 0) { HideCooldownText(); return; }
            if (Available)
            {
                respawnAtMs = 0;
                SetPoiVisible(true); // 显隐 AOI 已移除，冷却结束后由可刷新 POI 自身恢复场景表现。
                HideCooldownText();
                Debug.Log($"[交互] 重生 {Config?.Id}");
                return;
            }
            UpdateCooldownText();
        }

        /// <summary>从服务器状态写入下次重生时间戳，并立即刷新显隐（冷却中隐藏、已重生显示）。</summary>
        public void SetRespawnAt(long respawnAtMs)
        {
            this.respawnAtMs = respawnAtMs;
            SetPoiVisible(respawnAtMs <= NowMs());
        }

        /// <summary>刷新头顶剩余秒数（仅在秒数变化时更新文本，避免每帧重设）。</summary>
        private void UpdateCooldownText()
        {
            long remaining = RemainingSeconds;
            if (remaining == lastShownSecond) return;
            lastShownSecond = remaining;
            EnsureCooldownText();
            cooldownText.text = $"{remaining}s";
            cooldownText.gameObject.SetActive(true);
        }

        /// <summary>隐藏头顶剩余秒数。</summary>
        private void HideCooldownText()
        {
            lastShownSecond = -1;
            if (cooldownText != null) cooldownText.gameObject.SetActive(false);
        }

        /// <summary>在 POI 头顶惰性创建剩余秒数文本。</summary>
        private void EnsureCooldownText()
        {
            if (cooldownText != null || Entity == null || Entity.bindGo == null) return;
            GameObject go = new GameObject("CooldownText");
            go.transform.SetParent(Entity.bindGo.transform, false);
            go.transform.localPosition = new Vector3(0f, CooldownTextHeight, 0f);
            cooldownText = go.AddComponent<TextMesh>();
            cooldownText.fontSize = 48;
            cooldownText.anchor = TextAnchor.MiddleCenter;
            cooldownText.alignment = TextAlignment.Center;
        }

        /// <summary>当前 Unix 毫秒时间戳。</summary>
        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
