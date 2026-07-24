using TMPro;
using UnityEngine;

namespace Xuan.Prometheus
{
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class FloatDmgComponent : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI text;

        private RectTransform rectTransform;
        private RectTransform canvasRect;
        private FloatDamageConfig config;
        private Canvas canvas;
        private Camera worldCamera;
        private Vector3 worldPosition;
        private Vector2 animationOffset;
        private float elapsedTime;
        private bool initialized;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            if (text == null)
                text = GetComponent<TextMeshProUGUI>();
        }

        public void Initialize(
            float damage,
            Vector3 spawnWorldPosition,
            Camera camera,
            Canvas ownerCanvas,
            FloatDamageConfig damageConfig)
        {
            config = damageConfig;
            canvas = ownerCanvas;
            canvasRect = ownerCanvas.transform as RectTransform;
            worldCamera = camera;
            var randomPoint =
                Random.insideUnitCircle * Mathf.Max(0f, damageConfig.radius);
            worldPosition = spawnWorldPosition +
                            new Vector3(randomPoint.x, 0f, randomPoint.y) +
                            Vector3.up * damageConfig.startHeight;
            elapsedTime = 0f;
            initialized = true;

            text.text = damage.ToString("0.#");
            ApplyAnimation(0f);
            RefreshScreenPosition();
        }

        private void Update()
        {
            if (!initialized || config == null)
                return;

            var duration = Mathf.Max(0.01f, config.lifeTime);
            elapsedTime += Time.deltaTime;

            var normalizedTime = Mathf.Clamp01(elapsedTime / duration);
            ApplyAnimation(normalizedTime);

            if (elapsedTime >= duration)
                Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (!initialized || config == null)
                return;

            RefreshScreenPosition();
        }

        private void ApplyAnimation(float normalizedTime)
        {
            var verticalProgress = config.yCurve == null
                ? normalizedTime
                : config.yCurve.Evaluate(normalizedTime);

            var scale = config.scaleCurve == null
                ? 1f
                : Mathf.Max(0f, config.scaleCurve.Evaluate(normalizedTime));

            animationOffset =
                Vector2.up * (config.height * verticalProgress);
            rectTransform.localScale = Vector3.one * scale;
        }

        private void RefreshScreenPosition()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            if (worldCamera == null || canvas == null || canvasRect == null)
            {
                text.enabled = false;
                return;
            }

            var screenPosition =
                worldCamera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f)
            {
                text.enabled = false;
                return;
            }

            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPosition, uiCamera,
                    out var localPosition))
            {
                text.enabled = false;
                return;
            }

            text.enabled = true;
            rectTransform.anchoredPosition =
                localPosition + animationOffset;
        }
    }
}
