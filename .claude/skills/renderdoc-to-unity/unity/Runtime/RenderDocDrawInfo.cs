using UnityEngine;

namespace RenderDocToUnity
{
    /// <summary>
    /// Per-object provenance written by RenderDocSceneImporter, so any rebuilt object can be traced back to its
    /// RenderDoc event (open the capture, go to <see cref="eventId"/>). Runtime assembly so it can live on scene objects.
    /// </summary>
    [AddComponentMenu("")]
    public class RenderDocDrawInfo : MonoBehaviour
    {
        public int eventId;
        public int instance;
        public string mode;
        public string bakeReason;
        public string shader;
        public string marker;
        public float fitError;
        public string[] textures;
    }
}
