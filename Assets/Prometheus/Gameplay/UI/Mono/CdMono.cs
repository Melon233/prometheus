using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus
{
    /// <summary>集中保存 HUD 通用技能区域的冷却遮罩和冷却文本，并把原始状态转换为 UI 表现。</summary>
    public sealed class CdMono : MonoBehaviour
    {
        /// <summary>显示从一逐步归零的剩余冷却比例。</summary>
        public Image cooldownImg;

        /// <summary>显示保留一位小数的剩余冷却秒数，冷却完成时隐藏文本。</summary>
        public TextMeshProUGUI cooldownTxt;

        /// <summary>使用剩余冷却和完整冷却同步 Fill Image 与冷却文本。</summary>
        public void ApplyState(float cooldownRemaining, float cooldownDuration)
        {
            if (cooldownImg == null || cooldownTxt == null) throw new System.InvalidOperationException($"{nameof(CdMono)} on '{name}' requires cooldownImg and cooldownTxt references.");
            cooldownImg.fillAmount = cooldownDuration > 0f ? Mathf.Clamp01(cooldownRemaining / cooldownDuration) : 0f;
            cooldownTxt.text = cooldownRemaining > 0f ? $"{cooldownRemaining:0.0}" : string.Empty;
        }
    }
}
