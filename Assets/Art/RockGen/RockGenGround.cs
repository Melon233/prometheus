using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RockGen 地面：挂在场景地面（平面网格，材质用 RockGen/GenshinCliff 并开启 Ground Mode）上，与岩顶草地同一风格、同一清晰度。
/// - 撒草：在所有 CliffRockGenerator 岩石的包围范围外扩 margin 的矩形内，按与岩顶相同的密度、高度、草色撒草叶（岩石脚下不撒），按 chunkSize 分块成多个网格便于视锥剔除。
/// - 接触 AO：每块岩石离岩底 aoHeight 以内的部分（按高度裁剪后俯视投影，含外凸的岩板）光栅化成占地遮罩，chamfer 距离变换得到地面上每点到轮廓的水平距离 d，
///   压暗 ao = 1 − aoStrength × (1 − smoothstep(0, aoRadius, d))（d ≥ aoRadius 时为 1），烘成一张覆盖撒草矩形的贴图，通过 MaterialPropertyBlock 交给地面着色器；草叶同样按 AO 压暗。
/// 生成的网格、贴图都不存进场景（HideAndDontSave），启用时按种子确定性重建；岩石点“生成岩石”后会自动刷新，只移动岩石时手动点“重建”。
/// </summary>
[ExecuteAlways, DisallowMultipleComponent]
public class RockGenGround : MonoBehaviour
{
    [Tooltip("随机种子")] public int seed = 1;
    [Tooltip("撒草与 AO 范围：岩石包围范围向外扩的距离（米）")] public float margin = 20f;
    [Tooltip("每平方米草叶数（与岩顶 grassDensity 相同时清晰度一致）")] public float grassDensity = 30f;
    [Tooltip("草叶基准高度（米），实际 0.7～1.3 倍")] public float grassHeight = 0.45f;
    [Tooltip("草叶材质，空时用岩石的 grassBladeMaterial")] public Material grassBladeMaterial;
    [Tooltip("分块边长（米）")] public float chunkSize = 16f;
    [Tooltip("接触 AO 影响半径（米）")] public float aoRadius = 4f;
    [Tooltip("接触 AO 强度：紧贴岩石处压暗的比例")] [Range(0f, 1f)] public float aoStrength = 0.65f;
    [Tooltip("占地轮廓取岩体离岩底多高以内的部分（米）：外凸的岩板也算进去")] public float aoHeight = 1.5f;
    [Tooltip("AO 贴图每像素对应的米数")] public float aoTexel = 0.2f;

    static readonly int AOTexId = Shader.PropertyToID("_ContactAOTex"), AORectId = Shader.PropertyToID("_ContactAORect");
    readonly List<Object> generated = new List<Object>();

    void OnEnable() => Rebuild();
    void OnDisable() => Clear();

    void Clear()
    {
        foreach (var o in generated) if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        generated.Clear();
        var r = GetComponent<Renderer>();
        if (r != null) r.SetPropertyBlock(null);
    }

    /// <summary>按当前场景里的岩石重建草叶与接触 AO。</summary>
    [ContextMenu("重建")]
    public void Rebuild()
    {
        Clear();
        var rocks = FindObjectsByType<CliffRockGenerator>(FindObjectsInactive.Exclude);
        // 每块岩石下部（离岩底 aoHeight 以内）的三角形：按高度裁剪后俯视投影（凸多边形），作为占地轮廓
        var pieces = new List<List<Vector2>>();
        Bounds? all = null; CliffRockGenerator first = null;
        foreach (var g in rocks)
        {
            if (g.output == null) continue;
            var mf = g.output.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh; var tr = g.output.transform;
            var vs = mesh.vertices; var w = new Vector3[vs.Length];
            float minY = float.MaxValue;
            for (int i = 0; i < vs.Length; i++) { w[i] = tr.TransformPoint(vs[i]); minY = Mathf.Min(minY, w[i].y); }
            float cut = minY + aoHeight;
            var tri = mesh.triangles;
            for (int i = 0; i < tri.Length; i += 3)
            {
                var poly = ClipBelow(w[tri[i]], w[tri[i + 1]], w[tri[i + 2]], cut);
                if (poly.Count >= 3) pieces.Add(poly);
            }
            var rb = g.output.GetComponent<Renderer>().bounds;
            if (all == null) all = rb; else { var t = all.Value; t.Encapsulate(rb); all = t; }
            if (first == null) first = g;
        }
        if (all == null) return;
        var bb = all.Value;
        float x0 = bb.min.x - margin, z0 = bb.min.z - margin, sx = bb.size.x + 2f * margin, sz = bb.size.z + 2f * margin;
        float groundY = transform.position.y;

        // ---- 占地遮罩：投影多边形光栅化到 AO 网格，两遍 chamfer 距离变换得到到占地轮廓的水平距离（米） ----
        int W = Mathf.Clamp(Mathf.CeilToInt(sx / aoTexel), 4, 4096), H = Mathf.Clamp(Mathf.CeilToInt(sz / aoTexel), 4, 4096);
        float tx = sx / W, tz = sz / H, td = Mathf.Sqrt(tx * tx + tz * tz);
        var dist = new float[W * H];
        for (int i = 0; i < dist.Length; i++) dist[i] = 1e9f;
        foreach (var pc in pieces)
        {
            Vector2 lo = pc[0], hi = pc[0];
            float area = 0f;
            for (int i = 0; i < pc.Count; i++) { lo = Vector2.Min(lo, pc[i]); hi = Vector2.Max(hi, pc[i]); area += Cross(pc[i], pc[(i + 1) % pc.Count]); }
            if (Mathf.Abs(area) < 1e-8f) continue;
            float sg = Mathf.Sign(area);
            int ix0 = Mathf.Max(0, Mathf.FloorToInt((lo.x - x0) / tx)), ix1 = Mathf.Min(W - 1, Mathf.FloorToInt((hi.x - x0) / tx));
            int iz0 = Mathf.Max(0, Mathf.FloorToInt((lo.y - z0) / tz)), iz1 = Mathf.Min(H - 1, Mathf.FloorToInt((hi.y - z0) / tz));
            for (int iz = iz0; iz <= iz1; iz++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    var c = new Vector2(x0 + (ix + 0.5f) * tx, z0 + (iz + 0.5f) * tz); bool inside = true;
                    for (int k = 0; k < pc.Count && inside; k++) inside = Cross(pc[(k + 1) % pc.Count] - pc[k], c - pc[k]) * sg >= 0f;
                    if (inside) dist[iz * W + ix] = 0f;
                }
        }
        for (int iz = 0; iz < H; iz++)
            for (int ix = 0; ix < W; ix++)
            {
                int i = iz * W + ix; float d = dist[i];
                if (ix > 0) d = Mathf.Min(d, dist[i - 1] + tx);
                if (iz > 0) { d = Mathf.Min(d, dist[i - W] + tz); if (ix > 0) d = Mathf.Min(d, dist[i - W - 1] + td); if (ix < W - 1) d = Mathf.Min(d, dist[i - W + 1] + td); }
                dist[i] = d;
            }
        for (int iz = H - 1; iz >= 0; iz--)
            for (int ix = W - 1; ix >= 0; ix--)
            {
                int i = iz * W + ix; float d = dist[i];
                if (ix < W - 1) d = Mathf.Min(d, dist[i + 1] + tx);
                if (iz < H - 1) { d = Mathf.Min(d, dist[i + W] + tz); if (ix < W - 1) d = Mathf.Min(d, dist[i + W + 1] + td); if (ix > 0) d = Mathf.Min(d, dist[i + W - 1] + td); }
                dist[i] = d;
            }
        float AOOf(float d) { float k = 1f - Mathf.SmoothStep(0f, 1f, d / Mathf.Max(1e-3f, aoRadius)); return 1f - aoStrength * k; }
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) { byte v = (byte)Mathf.RoundToInt(AOOf(dist[i]) * 255f); px[i] = new Color32(v, v, v, 255); }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true) { name = name + "_ContactAO", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixels32(px); tex.Apply(false, true);
        generated.Add(tex);
        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture(AOTexId, tex);
            mpb.SetVector(AORectId, new Vector4(x0, z0, 1f / sx, 1f / sz));
            rend.SetPropertyBlock(mpb);
        }
        // 双线性取距离（草叶避让岩脚、压暗都按它）
        float SampleDist(float x, float z)
        {
            float fx = Mathf.Clamp((x - x0) / tx - 0.5f, 0f, W - 1.001f), fz = Mathf.Clamp((z - z0) / tz - 0.5f, 0f, H - 1.001f);
            int ix = (int)fx, iz = (int)fz; float u = fx - ix, t = fz - iz; int i = iz * W + ix;
            return Mathf.Lerp(Mathf.Lerp(dist[i], dist[i + 1], u), Mathf.Lerp(dist[i + W], dist[i + W + 1], u), t);
        }

        // ---- 撒草：与岩顶相同的草色（取岩石悬崖材质的草色）、草叶形状 ----
        var mat = grassBladeMaterial != null ? grassBladeMaterial : first.grassBladeMaterial;
        if (mat == null || grassDensity <= 0f) return;
        var cliff = first.genshinMaterial;
        Color deep = cliff != null && cliff.HasProperty("_GrassDeep") ? cliff.GetColor("_GrassDeep") : new Color(0.33f, 0.56f, 0.22f);
        Color light = cliff != null && cliff.HasProperty("_GrassLight") ? cliff.GetColor("_GrassLight") : new Color(0.70f, 0.80f, 0.30f);
        float scale = cliff != null && cliff.HasProperty("_GrassScale") ? cliff.GetFloat("_GrassScale") : 0.06f;
        var tip = new Color(0.78f, 0.86f, 0.38f);
        var rnd = new System.Random(seed * 7919 + 101);
        float R() => (float)rnd.NextDouble();
        int nx = Mathf.Max(1, Mathf.CeilToInt(sx / chunkSize)), nz = Mathf.Max(1, Mathf.CeilToInt(sz / chunkSize));
        float cw = sx / nx, ch = sz / nz;
        for (int cz = 0; cz < nz; cz++)
            for (int cx = 0; cx < nx; cx++)
            {
                var v = new List<Vector3>(); var col = new List<Color>(); var nrm = new List<Vector3>(); var idx = new List<int>();
                var origin = new Vector3(x0 + cx * cw, groundY, z0 + cz * ch);
                float want = cw * ch * grassDensity; int count = Mathf.FloorToInt(want) + (R() < want - Mathf.Floor(want) ? 1 : 0);
                for (int k = 0; k < count; k++)
                {
                    float lx = R() * cw, lz = R() * ch, wx = origin.x + lx, wz = origin.z + lz;
                    float d = SampleDist(wx, wz);
                    if (d < 0.5f * aoTexel) continue;                                  // 岩石脚下不撒
                    float ao = AOOf(d);
                    float n01 = Mathf.PerlinNoise(wx * scale + 31.7f, wz * scale + 11.3f);
                    var root = Color.Lerp(deep, light, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, n01))) * 0.88f * ao;
                    var tipC = tip * Mathf.Lerp(1f, ao, 0.75f);
                    AddBlade(v, col, nrm, idx, new Vector3(lx, 0f, lz), root.linear, tipC.linear, grassHeight, R, Vector3.one);
                }
                if (v.Count == 0) continue;
                var mesh = new Mesh { name = name + "_Grass", hideFlags = HideFlags.HideAndDontSave };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetColors(col); mesh.SetNormals(nrm); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds();
                var go = new GameObject(name + "_Grass") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.position = origin;     // 世界坐标放置，不随地面缩放
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                generated.Add(mesh); generated.Add(go);
            }
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// <summary>三角形按 y ≤ cut 裁剪后的俯视投影（凸多边形，XZ）。</summary>
    static List<Vector2> ClipBelow(Vector3 p0, Vector3 p1, Vector3 p2, float cut)
    {
        var t = new[] { p0, p1, p2 }; var res = new List<Vector2>();
        for (int i = 0; i < 3; i++)
        {
            Vector3 a = t[i], b = t[(i + 1) % 3];
            bool ia = a.y <= cut, ib = b.y <= cut;
            if (ia) res.Add(new Vector2(a.x, a.z));
            if (ia != ib) { var p = Vector3.Lerp(a, b, (cut - a.y) / (b.y - a.y)); res.Add(new Vector2(p.x, p.z)); }
        }
        return res;
    }

    /// <summary>一株 5 顶点弯折草叶（世界尺寸计算后乘 inv 还原到物体局部）：根部两点、中段两点、叶尖一点；A = 风动权重（高度比例）。岩顶撒草（TopGrass）共用。</summary>
    public static void AddBlade(List<Vector3> v, List<Color> c, List<Vector3> n, List<int> idx, Vector3 b, Color root, Color tip, float height, System.Func<float> R, Vector3 inv)
    {
        float a = R() * 6.283f, h = height * (0.7f + R() * 0.6f), w = h * (0.12f + R() * 0.08f);   // 宽度随高度（约 12%～20%），远看能读出草叶
        var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        var bend = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * (R() - 0.5f) + new Vector3(R() - 0.5f, 0, R() - 0.5f) * 0.6f;
        b -= Vector3.up * 0.02f;                                                  // 根部略埋入地面，避免浮空
        int i0 = v.Count;
        var pts = new[]
        {
            b - side * w * 0.5f, b + side * w * 0.5f,
            b + side * w * 0.16f + Vector3.up * h * 0.55f + bend * h * 0.12f, b - side * w * 0.16f + Vector3.up * h * 0.55f + bend * h * 0.12f,
            b + Vector3.up * h + bend * h * 0.38f,
        };
        float[] hs = { 0, 0, 0.55f, 0.55f, 1f };
        for (int k = 0; k < 5; k++) { v.Add(Vector3.Scale(pts[k], inv)); var cc = Color.Lerp(root, tip, hs[k] * 0.75f); cc.a = hs[k]; c.Add(cc); n.Add(Vector3.up); }
        idx.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2, i0 + 3, i0 + 4, i0 + 2 });
    }
}
