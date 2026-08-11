using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus
{
    /// <summary>集中保存 HUD 大招区域的冷却遮罩、能量填充和冷却文本，并把原始状态转换为 UI 表现。</summary>
    public sealed class UltMono : MonoBehaviour
    {
        /// <summary>显示从一逐步归零的大招剩余冷却比例。</summary>
        public Image cooldownImg;

        /// <summary>显示从零逐步充满的大招能量比例。</summary>
        public Image energyImg;

        /// <summary>显示保留一位小数的剩余冷却秒数，冷却完成时隐藏文本。</summary>
        public TextMeshProUGUI cooldownTxt;

        /// <summary>使用当前能量、能量上限、剩余冷却和完整冷却同步两张 Fill Image 与冷却文本。</summary>
        public void ApplyState(float currentEnergy, float maxEnergy, float cooldownRemaining, float cooldownDuration)
        {
            if (cooldownImg == null || energyImg == null || cooldownTxt == null) throw new System.InvalidOperationException($"{nameof(UltMono)} on '{name}' requires cooldownImg, energyImg and cooldownTxt references.");
            energyImg.fillAmount = maxEnergy > 0f ? Mathf.Clamp01(currentEnergy / maxEnergy) : 0f;
            cooldownImg.fillAmount = cooldownDuration > 0f ? Mathf.Clamp01(cooldownRemaining / cooldownDuration) : 0f;
            cooldownTxt.text = cooldownRemaining > 0f ? $"{cooldownRemaining:0.0}" : string.Empty;
        }
    }
}
