using UnityEngine;

namespace Polyart
{

    public class TorusFog : MonoBehaviour
    {
        [Range(0f, 360f)]
        public float angle = 360f;
        [Range(1,70)]
        public int spawnRate = 50;
        public float radius = 350;

        private void ChangeAngle()
        {
            ParticleSystem pS = GetComponent<ParticleSystem>();
            if (pS == null)
            {
                Debug.Log("Den yparxei ps");
                return;
            }

            var shape = pS.shape;
            shape.arc = angle;
            shape.radius = radius;

            var emmition = pS.emission;
            emmition.rateOverTime = spawnRate;
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            ChangeAngle();
        }
#endif
        private void Start()
        {
            ChangeAngle();
        }

    }

}