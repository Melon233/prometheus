using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class DmgShower : Singleton<DmgShower>
    {
        private GameObject canvas;
        private RectTransform dmg;

        public void ShowDamage(string dmgText, Vector3 pos)
        {
            if (dmg == null)
            {
                dmg = GameObject.Find("Dmg").GetComponent<RectTransform>();
                canvas = GameObject.Find("Canvas");
                dmg.gameObject.SetActive(false);
            }

            var go = GameObject.Instantiate(dmg.gameObject, pos, Quaternion.identity, canvas.transform);
            go.SetActive(true);
            var screenPos = Camera.main.WorldToScreenPoint(pos);
            go.GetComponent<RectTransform>().position = screenPos;
            GameObject.Destroy(go.gameObject, 2f);
        }
    }
}