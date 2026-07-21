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
        private FloatDamageConfig config;
        private Vector2 startPosition;
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
            Vector2 anchoredPosition,
            FloatDamageConfig damageConfig)
        {
            config = damageConfig;
            startPosition = anchoredPosition;
            elapsedTime = 0f;
            initialized = true;

            rectTransform.anchoredPosition = startPosition;
            text.text = damage.ToString("0.#");
            ApplyAnimation(0f);
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

        private void ApplyAnimation(float normalizedTime)
        {
            var verticalProgress = config.yCurve == null
                ? normalizedTime
                : config.yCurve.Evaluate(normalizedTime);

            var scale = config.scaleCurve == null
                ? 1f
                : Mathf.Max(0f, config.scaleCurve.Evaluate(normalizedTime));

            rectTransform.anchoredPosition =
                startPosition + Vector2.up * (config.height * verticalProgress);
            rectTransform.localScale = Vector3.one * scale;
        }
    }
}
