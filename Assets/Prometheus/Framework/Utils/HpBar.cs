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

        private void Update()
        {
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

        public void SetHp(float p)
        {
            if (Mathf.Approximately(hpImg.fillAmount, p)) return;
            hpImg.fillAmount = p;
            wasHpChangedThisFrame = true;
        }
    }
}