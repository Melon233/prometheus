using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus.Component
{
    public class HpBar : MonoBehaviour
    {
        public Image hpImg;
        public Image chaserImg;
        public float reactionTime = 0.5f;
        public float chaseVelo = 10f;
        public float chaserLen = 1f;
        public float reactionTimer;
        public AnimationCurve chaseCurve;
        public float delta;
        public bool wasHpChangedThisFrame;
        public CanvasGroup canvasGroup;
        public EventComponent evtComp;
        public PropertyComponent propComp;

        /// <summary>记录当前血条是否已经订阅实体事件组件。</summary>
        private bool isEventBound;

        /// <summary>记录当前启用周期是否已经用属性组件同步过初始显示。</summary>
        private bool isInitialValueSynchronized;

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
        void Start()
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
            if (wasHpChangedThisFrame)
            {
                delta = chaserLen - hpImg.fillAmount;
                wasHpChangedThisFrame = false;
            }

            if (!Mathf.Approximately(hpImg.fillAmount, chaserLen))
            {
                if ((reactionTimer += Time.deltaTime) >= reactionTime)
                {
                    chaserLen = Mathf.MoveTowards(chaserLen, hpImg.fillAmount,
                        chaseVelo / chaserImg.rectTransform.rect.width * Time.deltaTime * chaseCurve.Evaluate(
                            (chaserLen - hpImg.fillAmount) / delta));
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
            hpImg.fillAmount = evt.newHp / evt.maxHp;
            wasHpChangedThisFrame = true;
            isInitialValueSynchronized = true;
        }

        /// <summary>
        /// 收到死亡事件后淡出整个血条画布组。
        /// </summary>
        private void OnDie(DieEvent evt)
        {
            DOVirtual.Float(1f, 0f, 1f, f => canvasGroup.alpha = f);
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
            if (!isEventBound || isInitialValueSynchronized || propComp.MaxHp <= 0f) return;
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
            if (!isEventBound || evtComp == null) return;
            evtComp.RemoveListener<HpChangedEvent>(SetHp);
            evtComp.RemoveListener<DieEvent>(OnDie);
            evtComp = null;
            isEventBound = false;
        }
    }
}
