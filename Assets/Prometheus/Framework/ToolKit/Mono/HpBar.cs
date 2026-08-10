using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus.Component
{
    public class HpBar : MonoBehaviour
    {
        /// <summary>显示当前生命比例的主血条图片。</summary>
        public Image hpImg;
        /// <summary>延迟追赶主血条的受伤缓冲图片。</summary>
        public Image chaserImg;
        /// <summary>生命变化后开始追赶前的等待时间。</summary>
        public float reactionTime = 0.5f;
        /// <summary>延迟血条追赶主血条时使用的基础速度。</summary>
        public float chaseVelo = 10f;
        /// <summary>延迟血条当前显示的归一化长度。</summary>
        public float chaserLen = 1f;
        /// <summary>当前生命变化后的等待计时。</summary>
        public float reactionTimer;
        /// <summary>根据剩余差值调整延迟血条追赶速度的曲线。</summary>
        public AnimationCurve chaseCurve;
        /// <summary>本次生命变化开始时两层血条之间的差值。</summary>
        public float delta;
        /// <summary>标记当前帧是否刚收到生命变化事件。</summary>
        public bool wasHpChangedThisFrame;
        /// <summary>控制整个血条在死亡时淡出的画布组。</summary>
        public CanvasGroup canvasGroup;
        /// <summary>当前运行时绑定的实体事件组件。</summary>
        public EventComponent evtComp;
        /// <summary>当前运行时绑定的实体属性组件。</summary>
        public PropertyComponent propComp;

        /// <summary>记录当前血条是否已经订阅实体事件组件。</summary>
        private bool isEventBound;

        /// <summary>记录当前启用周期是否已经用属性组件同步过初始显示。</summary>
        private bool isInitialValueSynchronized;

        /// <summary>保存死亡淡出动画，实例回收到对象池时用于终止旧动画。</summary>
        private Tween deathFadeTween;

        /// <summary>
        /// 将对象池中的独立血条实例绑定到目标属性组件，并恢复颜色、透明度和初始生命显示。
        /// </summary>
        /// <param name="targetPropertyComponent">需要由当前血条观察的实体属性组件。</param>
        /// <param name="hpColor">主血条颜色。</param>
        /// <param name="chaserColor">受伤缓冲血条颜色。</param>
        public void Initialize(PropertyComponent targetPropertyComponent, Color hpColor, Color chaserColor)
        {
            if (targetPropertyComponent == null) throw new System.ArgumentNullException(nameof(targetPropertyComponent));
            if (hpImg == null || chaserImg == null || canvasGroup == null) throw new System.InvalidOperationException($"World HP bar '{name}' is missing required Image or CanvasGroup references.");
            UnbindEvents();
            deathFadeTween?.Kill();
            deathFadeTween = null;
            propComp = targetPropertyComponent;
            hpImg.color = hpColor;
            chaserImg.color = chaserColor;
            canvasGroup.alpha = 1f;
            reactionTimer = 0f;
            delta = 0f;
            wasHpChangedThisFrame = false;
            isInitialValueSynchronized = false;
            TryBindEvents();
            SynchronizeInitialValue();
        }

        /// <summary>
        /// 组件启用时尝试绑定；如果实体尚未由 Entry 和 GameplayKit 创建，Update 会继续重试。
        /// </summary>
        private void OnEnable()
        {
            isInitialValueSynchronized = false;
            TryBindEvents();
        }

        /// <summary>
        /// Start 阶段再次尝试绑定，以覆盖运行时实例化后才设置 PropertyComponent.Entity 的情况。
        /// </summary>
        private void Start()
        {
            TryBindEvents();
        }

        /// <summary>
        /// 每帧确保事件绑定有效，并更新立即血条和延迟追赶血条的表现。
        /// </summary>
        private void Update()
        {
            TryBindEvents();
            SynchronizeInitialValue();
            if (hpImg == null || chaserImg == null) return;
            if (wasHpChangedThisFrame)
            {
                delta = chaserLen - hpImg.fillAmount;
                wasHpChangedThisFrame = false;
            }

            if (!Mathf.Approximately(hpImg.fillAmount, chaserLen))
            {
                if ((reactionTimer += Time.deltaTime) >= reactionTime)
                {
                    float width = Mathf.Max(0.0001f, chaserImg.rectTransform.rect.width);
                    float normalizedDelta = Mathf.Approximately(delta, 0f) ? 0f : (chaserLen - hpImg.fillAmount) / delta;
                    float curveMultiplier = chaseCurve == null || chaseCurve.length == 0 ? 1f : chaseCurve.Evaluate(normalizedDelta);
                    chaserLen = Mathf.MoveTowards(chaserLen, hpImg.fillAmount, chaseVelo / width * Time.deltaTime * curveMultiplier);
                    chaserImg.fillAmount = chaserLen;
                }
            }
            else
            {
                reactionTimer = 0f;
            }

            wasHpChangedThisFrame = false;
        }

        /// <summary>
        /// 收到生命变化事件时立即更新主血条，并启动延迟血条追赶过程。
        /// </summary>
        public void SetHp(HpChangedEvent evt)
        {
            if (hpImg == null) return;
            hpImg.fillAmount = evt.maxHp <= 0f ? 0f : Mathf.Clamp01(evt.newHp / evt.maxHp);
            wasHpChangedThisFrame = true;
            isInitialValueSynchronized = true;
        }

        /// <summary>
        /// 收到死亡事件后淡出整个血条画布组。
        /// </summary>
        private void OnDie(DieEvent evt)
        {
            if (canvasGroup == null) return;
            deathFadeTween?.Kill();
            deathFadeTween = DOVirtual.Float(canvasGroup.alpha, 0f, 1f, alpha => canvasGroup.alpha = alpha);
        }

        /// <summary>
        /// 在属性组件完成实体绑定后订阅生命变化和死亡事件，避免依赖不同 MonoBehaviour 的 Start 调用顺序。
        /// </summary>
        private void TryBindEvents()
        {
            if (isEventBound || propComp == null || propComp.Entity == null) return;
            if (!propComp.Entity.TryGetComp(out EventComponent resolvedEventComponent)) return;
            evtComp = resolvedEventComponent;
            evtComp.AddListener<HpChangedEvent>(SetHp);
            evtComp.AddListener<DieEvent>(OnDie);
            isEventBound = true;
        }

        /// <summary>
        /// 所有 Start 回调结束后的首个 Update 使用真实生命值初始化两层血条，防止默认图片值与属性状态不一致。
        /// </summary>
        private void SynchronizeInitialValue()
        {
            if (!isEventBound || isInitialValueSynchronized || propComp == null || hpImg == null || chaserImg == null || propComp.MaxHp <= 0f) return;
            float normalizedHp = Mathf.Clamp01(propComp.Hp / propComp.MaxHp);
            hpImg.fillAmount = normalizedHp;
            chaserImg.fillAmount = normalizedHp;
            chaserLen = normalizedHp;
            isInitialValueSynchronized = true;
        }

        /// <summary>
        /// 组件禁用时注销事件，避免重复启用后累积监听器。
        /// </summary>
        private void OnDisable()
        {
            deathFadeTween?.Kill();
            deathFadeTween = null;
            UnbindEvents();
            propComp = null;
            isInitialValueSynchronized = false;
        }

        /// <summary>
        /// 注销当前实体的生命和死亡事件，并清理事件组件引用，保证对象池复用不会保留旧实体。
        /// </summary>
        private void UnbindEvents()
        {
            if (isEventBound && evtComp != null)
            {
                evtComp.RemoveListener<HpChangedEvent>(SetHp);
                evtComp.RemoveListener<DieEvent>(OnDie);
            }
            evtComp = null;
            isEventBound = false;
        }
    }
}
