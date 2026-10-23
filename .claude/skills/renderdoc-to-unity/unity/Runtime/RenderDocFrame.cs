using System;
using UnityEngine;

namespace RenderDocToUnity
{
    /// <summary>A captured frame recorded as ordered GPU operations (built by the editor importer from frame.json).</summary>
    public class RenderDocFrame : ScriptableObject
    {
        public string capture;
        public string sourceDir;                 // extraction folder, used for per-pass ground truth (validate/*.raw)
        public FrameResource[] resources = Array.Empty<FrameResource>();
        public Mesh[] meshes = Array.Empty<Mesh>();
        public Material[] materials = Array.Empty<Material>();
        public FrameOp[] ops = Array.Empty<FrameOp>();
        public int finalResource = -1;
        public bool rowsBottomUp;                // target memory row 0 is the bottom of the image (GL-translated captures)
        // capture camera (column-vector convention clip = P * V * world, V looks down -z); see make_camera_bindings.py
        public bool hasCamera;
        public Matrix4x4 captureView = Matrix4x4.identity, captureProj = Matrix4x4.identity;
        public int captureWidth, captureHeight;  // size of the final image (screen-relative buffers scale from it)
        public FrameObject[] objects = Array.Empty<FrameObject>();
    }

    /// <summary>A scene object: drawn by one or more ops (G-buffer, shadow caster, instanced slot...), placed by <see cref="model"/>.</summary>
    [Serializable]
    public class FrameObject
    {
        public Matrix4x4 model = Matrix4x4.identity;   // object-to-world in the capture
        public int[] ops = Array.Empty<int>();
        public bool movable;                            // its matrix was found in the constant buffers
    }

    [Serializable]
    public class FrameResource
    {
        public string id, format, unityFormat, kind;
        public int width, height, depth = 1, array = 1, mips = 1;
        public bool rt, isDepth;
        public int screenLevel = -1;              // screen-relative: size = screen size >> level (rounded like the capture)
        public bool screenRoundUp;
        public Texture init;                      // initial contents (exact raw data or decoded PNG)
        public Texture2D initDepth;               // initial depth as R32_SFloat (RG32_SFloat: g = stencil), written with SV_Depth
        public FilterMode filter = FilterMode.Bilinear;
        public TextureWrapMode wrapU = TextureWrapMode.Repeat, wrapV = TextureWrapMode.Repeat;
        public bool compareSampler;
    }

    /// <summary>Camera-, object- and screen-derived values found in constant buffers; rewritten for every rendered view.</summary>
    public enum CameraValue
    {
        ViewProj, InvViewProj, View, InvView, WorldToCamera, CameraToWorld, ViewProjRot, InvViewProjRot, ModelViewProj, CameraPos,
        Proj, InvProj, ObjectToWorld, WorldToObject, Screen
    }

    [Serializable]
    public class FrameCBPatch
    {
        public int offset;                        // bytes
        public CameraValue kind;
        public bool rowMajor;
        public Matrix4x4 model = Matrix4x4.identity;  // ModelViewProj without a known object
        public int obj = -1;                      // object index for ObjectToWorld / WorldToObject / ModelViewProj
        public int level, component;              // Screen: buffer level, 0 width 1 height 2 1/width 3 1/height
        public bool roundUp;                      // Screen: odd sizes round up at this level
    }

    [Serializable] public class FrameCB { public string name; public byte[] data; public FrameCBPatch[] patches = Array.Empty<FrameCBPatch>(); }
    /// <summary>
    /// A vertex attribute holding, per corner of a fullscreen draw, the world-space ray from the camera to the far plane
    /// (written by the game's CPU for its camera). Recomputed for every rendered view from the corner's NDC and depth.
    /// </summary>
    [Serializable]
    public class FrameViewRay
    {
        public int location;
        public float depth;                       // view-space depth of the ray end (far plane)
        public Vector2[] ndc = Array.Empty<Vector2>();
        public float[] w = Array.Empty<float>();
    }

    [Serializable]
    public class FrameTexBind
    {
        public string name;
        public int resource = -1;
        public bool depth;
        [Tooltip("Temporal history (previous frame at the captured viewpoint): for another viewpoint this current-frame target is bound instead.")]
        public int historyOf = -1;
    }

    public enum FrameOpType { Pass, Draw, Blit }

    [Serializable]
    public class FrameOp
    {
        public FrameOpType type;
        public int eid;
        // pass
        public int[] colors = Array.Empty<int>();
        public int depthTarget = -1;
        public string colorLoad = "load", depthLoad = "load", stencilLoad = "load";
        public Color[] clearColors = Array.Empty<Color>();
        public float clearDepth = 1f;
        public int clearStencil;
        public string validateFile;
        public int validateTarget = -1;
        // draw
        public int material = -1, mesh = -1, instances = 1, firstInstance, baseVertex;
        public float[] viewport = Array.Empty<float>(), scissor = Array.Empty<float>();
        public FrameCB[] cbuffers = Array.Empty<FrameCB>();
        public FrameTexBind[] textures = Array.Empty<FrameTexBind>();
        public int[] objects = Array.Empty<int>();    // scene objects this draw renders (empty: frame-level draw)
        public FrameViewRay[] viewRays = Array.Empty<FrameViewRay>();
        // blit
        public int src = -1, dst = -1;
    }
}
