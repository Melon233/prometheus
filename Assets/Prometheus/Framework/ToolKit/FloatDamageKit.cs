using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 将世界坐标中的伤害转换到屏幕 UI，并创建飘字实例。
    /// </summary>
    public class FloatTextKit : MonoSingleton<FloatTextKit>
    {
        private const string ConfigPath = "DmgConf";

        private FloatDamageConfig config;
        private Canvas canvas;

        protected override void OnAwake()
        {
            LoadConfig();
            ResolveCanvas();
        }

        public void CastNumberText(float number, Vector3 worldPosition, bool isHeal = false)
        {
            if (!EnsureReady())
                return;

            var worldCamera = Camera.main;
            if (worldCamera == null)
            {
                Debug.LogError("[FloatDamageKit] 场景中没有标记为 MainCamera 的相机。");
                return;
            }

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null)
            {
                Debug.LogError("[FloatDamageKit] Canvas 缺少 RectTransform。");
                return;
            }

            var instance = Instantiate(config.dmgComp, canvasRect, false);
            instance.gameObject.SetActive(true);
            instance.Initialize(number, worldPosition, worldCamera, canvas, config, isHeal);
        }

        private bool EnsureReady()
        {
            if (config == null)
                LoadConfig();

            if (canvas == null || !canvas.isActiveAndEnabled)
                ResolveCanvas();

            if (config == null)
            {
                Debug.LogError(
                    $"[FloatDamageKit] 无法加载 Resources/{ConfigPath}.asset。");
                return false;
            }

            if (config.dmgComp == null)
            {
                Debug.LogError("[FloatDamageKit] DmgConf 没有配置飘字预制体。");
                return false;
            }

            if (canvas == null)
            {
                Debug.LogError("[FloatDamageKit] 当前场景中没有可用的屏幕空间 Canvas。");
                return false;
            }

            return true;
        }

        private void LoadConfig()
        {
            config = Resources.Load<FloatDamageConfig>(ConfigPath);
        }

        private void ResolveCanvas()
        {
            canvas = null;
            Canvas screenSpaceCameraCanvas = null;

            foreach (var candidate in FindObjectsOfType<Canvas>())
            {
                if (!candidate.isActiveAndEnabled || !candidate.isRootCanvas)
                    continue;

                if (candidate.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas = candidate;
                    break;
                }

                if (candidate.renderMode == RenderMode.ScreenSpaceCamera &&
                    screenSpaceCameraCanvas == null)
                {
                    screenSpaceCameraCanvas = candidate;
                }
            }

            if (canvas == null)
                canvas = screenSpaceCameraCanvas;

            // 兼容旧场景：旧实现需要在 Canvas 下放一个 Dmg 模板。
            // 新实现直接从配置中的 prefab 创建，因此将旧模板隐藏。
            if (canvas != null)
            {
                var legacyTemplate = canvas.transform.Find("Dmg");
                if (legacyTemplate != null &&
                    legacyTemplate.TryGetComponent<FloatDmgComponent>(out _))
                {
                    legacyTemplate.gameObject.SetActive(false);
                }
            }
        }
    }
}
