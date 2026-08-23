using UnityEngine;

namespace Xuan.Prometheus
{
    public class FaceCameraObject : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation;
        }
    }
}