using UnityEngine;
using UnityEngine.UI;

namespace Xuan.Prometheus
{
    [AddComponentMenu("Prometheus/UI/Raycast Blocker")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RaycastBlocker : Graphic
    {
        public override bool raycastTarget
        {
            get => true;
            set { }
        }

        public override Texture mainTexture => null;
        public override Material materialForRendering => null;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
        }

        public override void SetAllDirty() { }
        public override void SetLayoutDirty() { }
        public override void SetVerticesDirty() { }
        public override void SetMaterialDirty() { }
    }
}
