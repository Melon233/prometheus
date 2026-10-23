using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RenderDocToUnity
{
    /// <summary>
    /// Offline replay of a <see cref="RenderDocFrame"/> exactly as captured (captured camera, objects, resolution) into
    /// its own render targets. Used by the RenderDoc Frame Replay window to step through passes and validate them
    /// against the capture. For a live scene rendered by Unity cameras use <see cref="RenderDocScene"/>.
    /// </summary>
    [ExecuteAlways]
    public class RenderDocFramePlayer : MonoBehaviour
    {
        public RenderDocFrame frame;
        [Tooltip("Execute operations up to and including this index (-1 = whole frame).")]
        public int stopAfterOp = -1;
        [Tooltip("Unity flips face culling when rendering into a RenderTexture on D3D-style APIs; replay keeps texel rows identical, so this undoes it. Validated: on.")]
        public bool invertCulling = true;
        [Tooltip("Unity converts clear depth on reversed-Z platforms; pass 1-d so the raw captured value lands in the buffer.")]
        public bool convertClearDepth = true;
        [Tooltip("Completeness check: start screen-sized targets cleared instead of with the contents they had when the capture began.")]
        public bool freshScreenTargets;

        RenderDocFrameRenderer renderer;
        public RenderTexture[] targets => renderer != null ? renderer.targets : Array.Empty<RenderTexture>();
        public RenderTexture Final => renderer?.Final;

        void OnEnable() => Replay();
        void OnDisable() => Release();

        public void Release()
        {
            renderer?.Dispose();
            renderer = null;
        }

        public void Replay()
        {
            if (frame == null || frame.resources == null) { Release(); return; }
            if (renderer == null || renderer.frame != frame) { Release(); renderer = new RenderDocFrameRenderer(frame); }
            renderer.invertCulling = invertCulling;
            renderer.convertClearDepth = convertClearDepth;
            renderer.freshScreenTargets = freshScreenTargets;
            renderer.Ensure();
            var cmd = new CommandBuffer { name = "RenderDoc frame replay" };
            renderer.Record(cmd, null, stopAfterOp);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }
    }
}
