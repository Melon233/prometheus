using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class FloatingDmgText : MonoBehaviour
    {
        public FloatingDmgTextConfig config;

        public TMP_Text text;
        public string textContent;

        private void Start()
        {
            text = gameObject.AddComponent<TMP_Text>();
            text.text = textContent;
            var oriPos = transform.position;
            DOVirtual.Float(0, 1, config.duration, nt =>
            {
                transform.position = oriPos + config.upCurve.Evaluate(nt) * config.upOffset * Vector3.up;
                transform.localScale = (1 + config.scaleCurve.Evaluate(nt)) * config.scaleOffset * Vector3.one;
            });
            Destroy(gameObject, config.duration);
        }
    }
}