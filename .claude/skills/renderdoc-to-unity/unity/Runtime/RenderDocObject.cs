using UnityEngine;

namespace RenderDocToUnity
{
    /// <summary>
    /// A reconstructed scene object. Its Transform drives the object matrix the captured shaders read (G-buffer,
    /// shadow caster and instanced draws alike): move, rotate, scale, disable or delete it like any GameObject.
    /// The child "Draw" renderers only serve selection and bounds; clicking them in the Scene view selects this object.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase]
    public class RenderDocObject : MonoBehaviour
    {
        [Tooltip("Index into RenderDocFrame.objects.")]
        public int index = -1;
        [Tooltip("Transform matrix this object was created with; the captured object matrix is applied relative to it.")]
        public Matrix4x4 initialLocalToWorld = Matrix4x4.identity;
        [Tooltip("False when the capture did not expose this object's matrix (e.g. world-space static batch): it renders but cannot move.")]
        public bool movable = true;

        /// <summary>Object-to-world for the shaders: the captured matrix carried along by the Transform's change since import.</summary>
        public Matrix4x4 ObjectMatrix(Matrix4x4 captured) => transform.localToWorldMatrix * initialLocalToWorld.inverse * captured;

        void OnEnable() => RenderDocScene.MarkDirty();
        void OnDisable() => RenderDocScene.MarkDirty();
    }
}
