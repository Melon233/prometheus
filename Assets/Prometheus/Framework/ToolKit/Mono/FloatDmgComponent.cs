using TMPro;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 驱动一个由 UIKit 世界 UI 对象池管理的伤害飘字实例，仅负责文本、颜色、缩放和上浮表现。
    /// 世界坐标、相机朝向、生命周期与实例回收均由 WorldUIHandle 及 UIKit 负责，本组件不会自行实例化或销毁 GameObject。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class FloatDmgComponent : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI text;

        private RectTransform rectTransform;
        private FloatDamageConfig config;
        private WorldUIHandle handle;
        private Vector3 spawnWorldPosition;
        private Color defaultTextColor;
        private float elapsedTime;
        private bool initialized;

        /// <summary>
        /// 缓存必需组件和 Prefab 默认文本颜色，默认颜色会在对象池每次复用时恢复为普通伤害颜色。
        /// </summary>
        private void Awake()
        {
            ResolveReferences();
            defaultTextColor = text.color;
        }

        /// <summary>
        /// 池实例启用时先隐藏旧文本，防止调用方初始化失败时短暂显示上一次租约留下的内容。
        /// </summary>
        private void OnEnable()
        {
            ResolveReferences();
            initialized = false;
            text.enabled = false;
            rectTransform.localScale = Vector3.one;
        }

        /// <summary>
        /// 使用本次世界 UI 租约初始化数值与动画状态，后续位置变化通过句柄同步回 UIKit 的固定坐标记录。
        /// </summary>
        /// <param name="damage">需要显示的伤害或治疗数值。</param>
        /// <param name="originWorldPosition">包含随机半径与起始高度的实际生成世界坐标。</param>
        /// <param name="worldUIHandle">UIKit 为当前池实例建立的有效租约。</param>
        /// <param name="damageConfig">定义上浮、缩放和持续时间的伤害飘字配置。</param>
        /// <param name="isHeal">为 true 时显示绿色治疗文本，否则使用 Prefab 默认文本颜色。</param>
        public void Initialize(float damage, Vector3 originWorldPosition, WorldUIHandle worldUIHandle, FloatDamageConfig damageConfig, bool isHeal = false)
        {
            ResolveReferences();
            if (worldUIHandle == null || !worldUIHandle.IsValid)
                throw new System.ArgumentException("Float damage text requires a valid WorldUIHandle.", nameof(worldUIHandle));

            if (worldUIHandle.Root != gameObject)
                throw new System.ArgumentException("The supplied WorldUIHandle does not own this FloatDmgComponent instance.", nameof(worldUIHandle));

            if (damageConfig == null)
                throw new System.ArgumentNullException(nameof(damageConfig));

            config = damageConfig;
            handle = worldUIHandle;
            spawnWorldPosition = originWorldPosition;
            elapsedTime = 0f;
            initialized = true;
            text.text = damage.ToString("0.#");
            text.color = isHeal ? Color.green : defaultTextColor;
            text.enabled = true;
            ApplyAnimation(0f);
        }

        /// <summary>
        /// 按配置生命周期推进动画；UIKit 会使用同一个生命周期自动停用并回收实例，因此到期时不调用 Destroy。
        /// </summary>
        private void Update()
        {
            if (!initialized || config == null)
                return;

            if (handle == null || !handle.IsValid)
            {
                initialized = false;
                return;
            }

            float duration = Mathf.Max(0.01f, config.lifeTime);
            elapsedTime += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsedTime / duration);
            ApplyAnimation(normalizedTime);
        }

        /// <summary>
        /// 计算当前曲线采样值，将本地 UI 高度转换成世界 Canvas 的世界向量，并通过句柄更新实例位置。
        /// </summary>
        /// <param name="normalizedTime">范围为零到一的动画归一化时间。</param>
        private void ApplyAnimation(float normalizedTime)
        {
            float verticalProgress = config.yCurve == null ? normalizedTime : config.yCurve.Evaluate(normalizedTime);
            float scale = config.scaleCurve == null ? 1f : Mathf.Max(0f, config.scaleCurve.Evaluate(normalizedTime));
            rectTransform.localScale = Vector3.one * scale;
            Transform worldUIRoot = rectTransform.parent;
            Vector3 localAnimationOffset = Vector3.up * (config.height * verticalProgress);
            Vector3 worldAnimationOffset = worldUIRoot != null ? worldUIRoot.TransformVector(localAnimationOffset) : localAnimationOffset;
            handle.SetWorldPosition(spawnWorldPosition + worldAnimationOffset);
        }

        /// <summary>
        /// 池实例停用时断开本次租约和配置引用，并复位可见状态，保证下一次复用不会继承旧动画数据。
        /// </summary>
        private void OnDisable()
        {
            initialized = false;
            elapsedTime = 0f;
            config = null;
            handle = null;
            if (text != null)
                text.enabled = false;

            if (rectTransform != null)
                rectTransform.localScale = Vector3.one;
        }

        /// <summary>
        /// 解析 RectTransform 与 TextMeshProUGUI 引用，使 Prefab 未手动填写 text 字段时仍可安全初始化和复用。
        /// </summary>
        private void ResolveReferences()
        {
            if (rectTransform == null)
                rectTransform = (RectTransform)transform;

            if (text == null)
                text = GetComponent<TextMeshProUGUI>();
        }
    }
}
