using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus
{
    /// <summary>显示单个持续型 Buff 的配置图标、当前层数和剩余持续时间比例。</summary>
    public sealed class BuffMono : MonoBehaviour
    {
        /// <summary>显示 EffectDefinition 配置的 Buff 图标。</summary>
        public Image icon;

        /// <summary>显示大于一层时的当前堆叠层数。</summary>
        public TextMeshProUGUI stackCnt;

        /// <summary>显示有限持续时间的剩余比例；永久 Buff 保持满值。</summary>
        public Image durationImg;
        public Image stacksBg;

        /// <summary>把活动 EffectInstance 当前状态写入列表项，不在 UI 内保存或修改玩法状态。</summary>
        public void Apply(EffectInstance instance)
        {
            if (instance == null) throw new System.ArgumentNullException(nameof(instance));
            if (icon == null || durationImg == null) throw new System.InvalidOperationException($"{nameof(BuffMono)} on '{name}' requires icon and durationImg references.");
            Sprite configuredIcon = instance.Definition.BuffIcon;
            icon.sprite = configuredIcon;
            icon.enabled = configuredIcon != null;
            if (stackCnt != null) stackCnt.text = instance.Stacks > 1 ? instance.Stacks.ToString() : string.Empty;
            durationImg.sprite = configuredIcon;
            durationImg.enabled = configuredIcon != null;
            durationImg.fillAmount = instance.Definition.DurationType == EffectDurationType.Duration && instance.Definition.Duration > 0f ? Mathf.Clamp01(instance.ElapsedTime / instance.Definition.Duration) : 0f;
            stacksBg.enabled = instance.Stacks > 1;
        }
    }
}
