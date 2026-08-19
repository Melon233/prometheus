using UnityEngine;

namespace ShinySSRR {
    public class ToggleEffect : MonoBehaviour {

        void Update() {
            if (InputProxy.GetKeyDown(KeyCode.Space)) {
                ShinySSRR.isEnabled = !ShinySSRR.isEnabled;
            }
        }
    }

}