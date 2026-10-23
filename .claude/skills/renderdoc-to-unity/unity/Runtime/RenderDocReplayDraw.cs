using System;
using System.Collections.Generic;
using UnityEngine;

namespace RenderDocToUnity
{
    /// <summary>
    /// Binds the captured per-draw data of a replayed draw: the raw bytes of every constant buffer (exact layout,
    /// including uint and matrix members) and the textures, through a MaterialPropertyBlock. The material carries a
    /// RenderDocReplay/* shader generated from the captured SPIR-V. Runs in edit mode so the scene is viewable without Play.
    /// </summary>
    [ExecuteAlways, AddComponentMenu("")]
    public class RenderDocReplayDraw : MonoBehaviour
    {
        [Serializable] public class ConstantBuffer { public string name; public byte[] data; }
        [Serializable] public class TextureBinding { public string name; public Texture texture; }

        public ConstantBuffer[] constantBuffers = Array.Empty<ConstantBuffer>();
        public TextureBinding[] textures = Array.Empty<TextureBinding>();
        public int instanceIndex;   // original SV_InstanceID of this captured instance

        readonly List<GraphicsBuffer> buffers = new List<GraphicsBuffer>();

        void OnEnable() => Apply();
        void OnDisable() => Release();

        public void Apply()
        {
            Release();
            var r = GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            foreach (var cb in constantBuffers)
            {
                if (cb == null || cb.data == null || cb.data.Length == 0 || string.IsNullOrEmpty(cb.name)) continue;
                int size = (cb.data.Length + 15) / 16 * 16;
                var floats = new float[size / 4];
                Buffer.BlockCopy(cb.data, 0, floats, 0, cb.data.Length);   // bit-exact: uint/int members stay intact
                var vecs = new Vector4[size / 16];
                for (int i = 0; i < vecs.Length; i++) vecs[i] = new Vector4(floats[i * 4], floats[i * 4 + 1], floats[i * 4 + 2], floats[i * 4 + 3]);
                var gb = new GraphicsBuffer(GraphicsBuffer.Target.Constant, vecs.Length, 16);
                gb.SetData(vecs);
                mpb.SetConstantBuffer(cb.name, gb, 0, size);
                buffers.Add(gb);
            }
            foreach (var t in textures)
                if (t != null && t.texture != null && !string.IsNullOrEmpty(t.name)) mpb.SetTexture(t.name, t.texture);
            mpb.SetInteger("rd_InstanceIndex", instanceIndex);
            r.SetPropertyBlock(mpb);
        }

        void Release()
        {
            foreach (var b in buffers) b?.Release();
            buffers.Clear();
        }
    }
}
