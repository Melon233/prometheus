using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 切削岩石生成（FacetRockGenerator 的编辑器端）。所有计算在世界尺寸下进行（源网格顶点乘物体缩放），输出时除回局部坐标。
/// 流程：
/// 1. 原石：源网格顶点的凸包（增量凸包），共面三角形合并成凸多边形 facet。
/// 2. 大刀：bigCuts 个方向在球面上均匀分布（Fibonacci 球面 + 随机旋转 + 抖动），沿方向 d 在“原石最突出处 − 切深”处切一刀（保留内侧）。
/// 3. 崩口：chipCuts 刀，每刀按尖锐度（顶点处各面单位法线平均值的长度越小越尖）随机选一个顶点，沿其平均法线加抖动的方向切掉它。
///    每刀切完检查：新边不短于 minEdge、新切面与相邻面的转角不小于 minAngle、剩余体积不少于切前的 30%；不满足时重新随机方向与切深，最多 8 次，仍不行跳过这一刀。
/// 4. 清理：只属于两个面的顶点（共线点）从面中去掉。
/// 5. 法线平滑带（见 BuildMesh）与输出普通 Mesh。
/// 凸多面体与半空间的交仍是凸多面体：每刀的切口是单个凸多边形，每一步都闭合。
/// </summary>
public static class FacetRockBuilder
{
    const string DefaultMat = "Assets/Art/RockGen/Materials/M_RockGen_Facet.mat";
    const float Eps = 1e-4f;

    /// <summary>凸多面体：顶点 + 面（顶点下标按外法线方向逆时针，平面 dot(n, x) = d）。</summary>
    class Poly
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Face> f = new List<Face>();
        public Poly Clone() => new Poly { v = new List<Vector3>(v), f = f.Select(x => new Face { idx = new List<int>(x.idx), n = x.n, d = x.d }).ToList() };
    }
    class Face { public List<int> idx; public Vector3 n; public float d; }

    public static string Generate(FacetRockGenerator gen) => Generate(gen, true);
    public static string Preview(FacetRockGenerator gen) => Generate(gen, false);

    static string Generate(FacetRockGenerator gen, bool saveAsset)
    {
        var mf = gen.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return "该物体上没有 MeshFilter 网格";
        var scale = gen.transform.lossyScale;
        var src = mf.sharedMesh.vertices.Select(v => Vector3.Scale(v, scale)).ToList();
        var poly = Hull(src, out string err);
        if (poly == null) return err;
        var rng = new System.Random(gen.seed);
        float R() => (float)rng.NextDouble();
        var log = new List<string> { $"凸包 {poly.v.Count} 点 {poly.f.Count} 面" };

        // ---- 大刀 ----
        int bigOk = 0, bigSkip = 0;
        if (gen.bigCuts > 0)
        {
            var rot = Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f);
            for (int i = 0; i < gen.bigCuts; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / gen.bigCuts, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), phi = i * 2.39996323f;
                var baseDir = rot * new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
                if (TryCut(ref poly, () => Jitter(baseDir, gen.bigJitter, R), gen.bigDepth, gen, R)) bigOk++; else bigSkip++;
            }
        }
        // ---- 崩口 ----
        int chipOk = 0, chipSkip = 0;
        for (int i = 0; i < gen.chipCuts; i++)
        {
            var (vi, vn) = PickSharpVertex(poly, R);
            if (vi < 0) break;
            if (TryCut(ref poly, () => Jitter(vn, gen.chipJitter, R), gen.chipDepth, gen, R)) chipOk++; else chipSkip++;
        }
        log.Add($"大刀 {bigOk} 刀（跳过 {bigSkip}），崩口 {chipOk} 刀（跳过 {chipSkip}）");
        RemoveCollinear(poly);
        log.Add($"结果 {poly.v.Count} 点 {poly.f.Count} 个 facet");

        // ---- 输出 ----
        var mesh = BuildMesh(poly, gen, scale, new System.Random(gen.seed * 31 + 7), out int narrowed);
        if (narrowed > 0) log.Add($"{narrowed} 个 facet 的平滑带因面窄而收窄");
        log.Add($"三角面 {mesh.triangles.Length / 3}，{CheckClosed(mesh, scale)}");
        Output(gen, mesh, saveAsset);
        EditorUtility.SetDirty(gen);
        EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        return "切削岩石：" + string.Join("；", log);
    }

    // ---------------- 切削 ----------------
    /// <summary>方向 d 随机偏转不超过 deg 度（偏转角按 sqrt 分布，方向在球冠内近似均匀）。</summary>
    static Vector3 Jitter(Vector3 d, float deg, System.Func<float> R)
    {
        if (deg <= 0f) return d.normalized;
        var rnd = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f);
        var axis = Vector3.Cross(d, rnd); if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(d, Vector3.right);
        return (Quaternion.AngleAxis(deg * Mathf.Sqrt(R()), axis.normalized) * d).normalized;
    }

    /// <summary>按尖锐度随机选一个顶点：顶点处各面单位法线平均值 a，尖锐度 = 1 − |a|（越尖越小的 |a|），按尖锐度² 加权；返回顶点与其平均法线。</summary>
    static (int, Vector3) PickSharpVertex(Poly P, System.Func<float> R)
    {
        var sum = new Vector3[P.v.Count]; var cnt = new int[P.v.Count];
        foreach (var f in P.f) foreach (var i in f.idx) { sum[i] += f.n; cnt[i]++; }
        var w = new float[P.v.Count]; float tot = 0f;
        for (int i = 0; i < w.Length; i++) { if (cnt[i] < 3) continue; float s = 1f - (sum[i] / cnt[i]).magnitude; w[i] = s * s + 1e-6f; tot += w[i]; }
        if (tot <= 0f) return (-1, Vector3.zero);
        float x = R() * tot;
        for (int i = 0; i < w.Length; i++) { x -= w[i]; if (x <= 0f && w[i] > 0f) return (i, sum[i].normalized); }
        int last = System.Array.FindLastIndex(w, a => a > 0f);
        return (last, sum[last].normalized);
    }

    /// <summary>沿方向 dir() 在“最突出处 − 切深”处切一刀；不满足质量约束时重新随机，最多 8 次。成功时替换 P。</summary>
    static bool TryCut(ref Poly P, System.Func<Vector3> dir, Vector2 depth, FacetRockGenerator gen, System.Func<float> R)
    {
        float vol0 = Volume(P);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var n = dir();
            float sup = P.v.Max(v => Vector3.Dot(v, n));
            float dep = Mathf.Lerp(Mathf.Min(depth.x, depth.y), Mathf.Max(depth.x, depth.y), R());
            if (dep <= Eps) continue;
            var Q = Cut(P, n, sup - dep, out var newVerts, out int capFace);
            if (Q == null || capFace < 0) continue;
            if (Volume(Q) < 0.3f * vol0) continue;
            if (!Quality(Q, newVerts, capFace, gen.minEdge, gen.minAngle)) continue;
            P = Q; return true;
        }
        return false;
    }

    /// <summary>新边不短于 minEdge；切面与每个相邻面的转角不小于 minAngle。</summary>
    static bool Quality(Poly Q, HashSet<int> newVerts, int capFace, float minEdge, float minAngle)
    {
        foreach (var f in Q.f)
            for (int k = 0; k < f.idx.Count; k++)
            {
                int a = f.idx[k], b = f.idx[(k + 1) % f.idx.Count];
                if ((newVerts.Contains(a) || newVerts.Contains(b)) && (Q.v[a] - Q.v[b]).magnitude < minEdge) return false;
            }
        var cap = Q.f[capFace]; var capEdges = new HashSet<(int, int)>();
        for (int k = 0; k < cap.idx.Count; k++) { int a = cap.idx[k], b = cap.idx[(k + 1) % cap.idx.Count]; capEdges.Add((Mathf.Min(a, b), Mathf.Max(a, b))); }
        for (int fi = 0; fi < Q.f.Count; fi++)
        {
            if (fi == capFace) continue; var f = Q.f[fi];
            for (int k = 0; k < f.idx.Count; k++)
            {
                int a = f.idx[k], b = f.idx[(k + 1) % f.idx.Count];
                if (capEdges.Contains((Mathf.Min(a, b), Mathf.Max(a, b))) && Vector3.Angle(f.n, cap.n) < minAngle) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 半空间裁剪：保留 dot(n, x) − off ≤ 0 的部分。顶点到平面的有符号距离 |s| &lt; Eps 时吸附到平面上（视为 0）。
    /// 每个面按边逐段裁剪（跨平面的边求交点，同一条边的交点按端点对缓存，相邻面共用），切口点按平面内的极角排成逆时针（绕 n）补成新面。
    /// 平面没有切到任何顶点时返回 null；全部切掉时也返回 null。newVerts 为新产生的交点，capFace 为切口面的下标。
    /// </summary>
    static Poly Cut(Poly P, Vector3 n, float off, out HashSet<int> newVerts, out int capFace)
    {
        newVerts = new HashSet<int>(); capFace = -1;
        var s = P.v.Select(v => { float x = Vector3.Dot(n, v) - off; return Mathf.Abs(x) < Eps ? 0f : x; }).ToArray();
        if (!s.Any(x => x > 0f) || !s.Any(x => x < 0f)) return null;
        var Q = new Poly(); var map = new int[P.v.Count];
        for (int i = 0; i < P.v.Count; i++) { if (s[i] <= 0f) { map[i] = Q.v.Count; Q.v.Add(P.v[i]); } else map[i] = -1; }
        var cache = new Dictionary<(int, int), int>(); var onPlane = new HashSet<int>();
        for (int i = 0; i < P.v.Count; i++) if (s[i] == 0f) onPlane.Add(map[i]);
        int Cross(int a, int b)
        {
            var key = (Mathf.Min(a, b), Mathf.Max(a, b));
            if (cache.TryGetValue(key, out int id)) return id;
            int lo = key.Item1, hi = key.Item2;
            var p = Vector3.Lerp(P.v[lo], P.v[hi], s[lo] / (s[lo] - s[hi]));
            id = Q.v.Count; Q.v.Add(p); cache[key] = id; onPlane.Add(id);
            return id;
        }
        foreach (var f in P.f)
        {
            var o = new List<int>();
            for (int k = 0; k < f.idx.Count; k++)
            {
                int a = f.idx[k], b = f.idx[(k + 1) % f.idx.Count];
                if (s[a] <= 0f) o.Add(map[a]);
                if ((s[a] < 0f && s[b] > 0f) || (s[a] > 0f && s[b] < 0f)) o.Add(Cross(a, b));
            }
            var dd = new List<int>();
            foreach (var x in o) if (dd.Count == 0 || dd[dd.Count - 1] != x) dd.Add(x);
            while (dd.Count > 1 && dd[0] == dd[dd.Count - 1]) dd.RemoveAt(dd.Count - 1);
            if (dd.Count >= 3 && PolyArea(Q.v, dd, f.n) > 1e-6f) Q.f.Add(new Face { idx = dd, n = f.n, d = f.d });
        }
        // 切口：平面上的顶点（属于保留面的）按极角排序
        var used = new HashSet<int>(Q.f.SelectMany(f => f.idx));
        var capPts = onPlane.Where(used.Contains).ToList();
        if (capPts.Count >= 3)
        {
            var c = capPts.Aggregate(Vector3.zero, (acc, i) => acc + Q.v[i]) / capPts.Count;
            Basis(n, out var u, out var w);
            capPts.Sort((a, b) => Mathf.Atan2(Vector3.Dot(Q.v[a] - c, w), Vector3.Dot(Q.v[a] - c, u)).CompareTo(Mathf.Atan2(Vector3.Dot(Q.v[b] - c, w), Vector3.Dot(Q.v[b] - c, u))));
            if (PolyArea(Q.v, capPts, n) > 1e-6f) { capFace = Q.f.Count; Q.f.Add(new Face { idx = capPts, n = n, d = off }); }
        }
        if (Q.f.Count < 4) return null;
        Compact(Q, out var remap);
        newVerts = new HashSet<int>(cache.Values.Where(i => remap[i] >= 0).Select(i => remap[i]));
        return Q;
    }

    /// <summary>去掉没有被任何面引用的顶点并重排下标。</summary>
    static void Compact(Poly Q, out int[] remap)
    {
        remap = Enumerable.Repeat(-1, Q.v.Count).ToArray();
        var nv = new List<Vector3>();
        foreach (var f in Q.f) for (int k = 0; k < f.idx.Count; k++) { int i = f.idx[k]; if (remap[i] < 0) { remap[i] = nv.Count; nv.Add(Q.v[i]); } }
        foreach (var f in Q.f) for (int k = 0; k < f.idx.Count; k++) f.idx[k] = remap[f.idx[k]];
        Q.v = nv;
    }

    /// <summary>只属于两个面的顶点是某条棱上的共线点（真正的多面体顶点至少属于三个面），从面中去掉。</summary>
    static void RemoveCollinear(Poly P)
    {
        var deg = new int[P.v.Count];
        foreach (var f in P.f) foreach (var i in f.idx) deg[i]++;
        foreach (var f in P.f) f.idx = f.idx.Where(i => deg[i] >= 3).ToList();
        P.f = P.f.Where(f => f.idx.Count >= 3).ToList();
        Compact(P, out _);
    }

    static float PolyArea(List<Vector3> v, List<int> idx, Vector3 n)
    {
        var a = Vector3.zero;
        for (int k = 1; k + 1 < idx.Count; k++) a += Vector3.Cross(v[idx[k]] - v[idx[0]], v[idx[k + 1]] - v[idx[0]]);
        return Vector3.Dot(a, n) * 0.5f;
    }

    static float Volume(Poly P)
    {
        var o = P.v.Aggregate(Vector3.zero, (a, b) => a + b) / P.v.Count; float vol = 0f;
        foreach (var f in P.f)
            for (int k = 1; k + 1 < f.idx.Count; k++)
                vol += Vector3.Dot(P.v[f.idx[0]] - o, Vector3.Cross(P.v[f.idx[k]] - o, P.v[f.idx[k + 1]] - o)) / 6f;
        return vol;
    }

    /// <summary>平面内正交基 u、w，满足 u × w = n（按极角递增即绕 n 逆时针）。</summary>
    static void Basis(Vector3 n, out Vector3 u, out Vector3 w)
    {
        u = Vector3.Cross(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized;
        w = Vector3.Cross(n, u);
    }

    // ---------------- 凸包 ----------------
    /// <summary>增量凸包：初始四面体，逐点加入时删去可见面、用地平线边连新点。之后把共面三角形合并成凸多边形面。</summary>
    static Poly Hull(List<Vector3> input, out string err)
    {
        err = null;
        var pts = new List<Vector3>(); var seen = new HashSet<Vector3Int>();
        foreach (var p in input) if (seen.Add(Vector3Int.RoundToInt(p * 1e4f))) pts.Add(p);
        if (pts.Count < 4) { err = "网格顶点不足 4 个"; return null; }
        float size = 0f; foreach (var p in pts) size = Mathf.Max(size, (p - pts[0]).magnitude);
        float eps = Mathf.Max(1e-6f, size * 1e-6f);
        int i0 = 0; for (int i = 1; i < pts.Count; i++) if (pts[i].x < pts[i0].x) i0 = i;
        int i1 = Enumerable.Range(0, pts.Count).OrderByDescending(i => (pts[i] - pts[i0]).sqrMagnitude).First();
        var dir01 = (pts[i1] - pts[i0]).normalized;
        int i2 = Enumerable.Range(0, pts.Count).OrderByDescending(i => Vector3.Cross(pts[i] - pts[i0], dir01).sqrMagnitude).First();
        var nrm = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]).normalized;
        int i3 = Enumerable.Range(0, pts.Count).OrderByDescending(i => Mathf.Abs(Vector3.Dot(pts[i] - pts[i0], nrm))).First();
        if (Mathf.Abs(Vector3.Dot(pts[i3] - pts[i0], nrm)) < eps * 10f) { err = "网格是平面的，无法构成凸包"; return null; }
        var tris = new List<(int a, int b, int c, Vector3 n, float d, bool dead)>();
        var center = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) / 4f;
        void AddTri(int a, int b, int c)
        {
            var n = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]).normalized;
            if (Vector3.Dot(n, center - pts[a]) > 0f) { (b, c) = (c, b); n = -n; }
            tris.Add((a, b, c, n, Vector3.Dot(n, pts[a]), false));
        }
        AddTri(i0, i1, i2); AddTri(i0, i1, i3); AddTri(i0, i2, i3); AddTri(i1, i2, i3);
        var initial = new HashSet<int> { i0, i1, i2, i3 };
        for (int pi = 0; pi < pts.Count; pi++)
        {
            if (initial.Contains(pi)) continue;
            var p = pts[pi];
            var vis = new List<int>();
            for (int t = 0; t < tris.Count; t++) if (!tris[t].dead && Vector3.Dot(tris[t].n, p) - tris[t].d > eps) vis.Add(t);
            if (vis.Count == 0) continue;
            var edges = new HashSet<(int, int)>();
            foreach (var t in vis) { var x = tris[t]; edges.Add((x.a, x.b)); edges.Add((x.b, x.c)); edges.Add((x.c, x.a)); }
            foreach (var t in vis) { var x = tris[t]; x.dead = true; tris[t] = x; }
            foreach (var (a, b) in edges)
                if (!edges.Contains((b, a)))
                {
                    var n = Vector3.Cross(pts[b] - pts[a], p - pts[a]);
                    if (n.sqrMagnitude < 1e-20f) continue;
                    n.Normalize(); tris.Add((a, b, pi, n, Vector3.Dot(n, pts[a]), false));
                }
        }
        // 共面三角形合并：按平面分组，组内顶点按极角排序（凸多面体的一个面是凸多边形）
        var P = new Poly(); var vmap = new Dictionary<int, int>();
        int V(int i) { if (!vmap.TryGetValue(i, out int k)) { k = P.v.Count; P.v.Add(pts[i]); vmap[i] = k; } return k; }
        var groups = new List<(Vector3 n, float d, HashSet<int> vs)>();
        foreach (var t in tris)
        {
            if (t.dead) continue;
            int g = groups.FindIndex(x => Vector3.Dot(x.n, t.n) > 0.99999f && Mathf.Abs(x.d - t.d) < 1e-3f);
            if (g < 0) { groups.Add((t.n, t.d, new HashSet<int>())); g = groups.Count - 1; }
            groups[g].vs.Add(t.a); groups[g].vs.Add(t.b); groups[g].vs.Add(t.c);
        }
        foreach (var (n, d, vs) in groups)
        {
            var list = vs.ToList(); var c = list.Aggregate(Vector3.zero, (acc, i) => acc + pts[i]) / list.Count;
            Basis(n, out var u, out var w);
            list.Sort((a, b) => Mathf.Atan2(Vector3.Dot(pts[a] - c, w), Vector3.Dot(pts[a] - c, u)).CompareTo(Mathf.Atan2(Vector3.Dot(pts[b] - c, w), Vector3.Dot(pts[b] - c, u))));
            P.f.Add(new Face { idx = list.Select(V).ToList(), n = n, d = d });
        }
        RemoveCollinear(P);
        return P;
    }

    // ---------------- 法线平滑带与输出网格 ----------------
    /// <summary>
    /// 每个 facet（凸 k 边形，顶点 P_i 逆时针）生成：
    /// - 棱上点：每条棱两端各往里 s_e = min(W, 棱长 / 3) 处一点（只由棱决定，两侧 facet 一致，无 T 形接缝）。P_i 之后的为 A_i，P_{i+1} 之前的为 B_i。
    /// - 内缩环 Q_i：两条相邻棱各自向内平移 k·s_e 后的交点（到这两条棱的距离正好是各自的平滑宽度）；内缩环翻转或越过其他棱的平移线时 k 乘 0.8 重试。
    /// - 三角形：内缩环扇形（t = 0，facet 法线）；每条棱一条带 A_i B_i Q_{i+1} Q_i；每个转角一块 P_i A_i Q_i B_{i−1}（以 P_i–Q_i 为对角线）。
    /// 顶点属性：UV1 = (facet 法线, t)，t 在棱上点与转角为 1、内缩环为 0——带内两个三角形里 t 都正好是 1 − 到棱距离 / 宽度；
    /// UV2 = t × 目标法线（棱上点为两侧 facet 法线的平分方向，转角为各面法线按内角加权的顶点法线，内缩环为 0），插值后归一化即带内目标方向。
    /// 网格法线 = facet 法线与目标法线按 t 线性混合（着色器里另算过渡曲线）。每个 facet 的顶点独立（facet 法线不同），棱上 t = 1 处两侧法线都等于平分法线，没有接缝。
    /// UV0 = facet 平面内的世界尺寸投影 / uvTileSize；顶点色 R = facet 随机色调，G = 相对高度。
    /// </summary>
    static Mesh BuildMesh(Poly P, FacetRockGenerator gen, Vector3 scale, System.Random rng, out int narrowed)
    {
        narrowed = 0;
        float W = Mathf.Max(0f, gen.normalSmoothWidth), tile = Mathf.Max(0.01f, gen.uvTileSize);
        // 棱 → 两侧面；顶点法线（按内角加权）
        var edgeFaces = new Dictionary<(int, int), List<int>>();
        var vn = new Vector3[P.v.Count];
        for (int fi = 0; fi < P.f.Count; fi++)
        {
            var f = P.f[fi]; int k = f.idx.Count;
            for (int i = 0; i < k; i++)
            {
                int a = f.idx[i], b = f.idx[(i + 1) % k], c = f.idx[(i - 1 + k) % k];
                var key = (Mathf.Min(a, b), Mathf.Max(a, b));
                if (!edgeFaces.TryGetValue(key, out var l)) edgeFaces[key] = l = new List<int>();
                l.Add(fi);
                vn[a] += f.n * Vector3.Angle(P.v[b] - P.v[a], P.v[c] - P.v[a]) * Mathf.Deg2Rad;
            }
        }
        for (int i = 0; i < vn.Length; i++) vn[i] = vn[i].normalized;
        Vector3 EdgeN(int a, int b, Vector3 self)
        {
            var l = edgeFaces[(Mathf.Min(a, b), Mathf.Max(a, b))];
            var sum = Vector3.zero; foreach (var fi in l) sum += P.f[fi].n;
            return l.Count >= 2 ? sum.normalized : self;
        }
        float minY = P.v.Min(v => v.y), maxY = P.v.Max(v => v.y), hy = Mathf.Max(1e-4f, maxY - minY);

        var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv0 = new List<Vector2>(); var uv1 = new List<Vector4>(); var uv2 = new List<Vector3>();
        var col = new List<Color>(); var tri = new List<int>();
        Vector3 ToLocalN(Vector3 n) => Vector3.Scale(n, scale).normalized;
        foreach (var f in P.f)
        {
            int k = f.idx.Count; var n = f.n;
            Basis(n, out var bu, out var bw);
            float tone = (float)rng.NextDouble();
            var Pp = f.idx.Select(i => P.v[i]).ToArray();
            var e = new Vector3[k]; var len = new float[k]; var m = new Vector3[k]; var s = new float[k]; var Ne = new Vector3[k];
            for (int i = 0; i < k; i++)
            {
                var d = Pp[(i + 1) % k] - Pp[i]; len[i] = d.magnitude; e[i] = d / Mathf.Max(1e-9f, len[i]);
                m[i] = Vector3.Cross(n, e[i]);                                                   // 棱 i 的面内向内方向
                s[i] = Mathf.Min(W, len[i] / 3f);
                Ne[i] = EdgeN(f.idx[i], f.idx[(i + 1) % k], n);
            }
            // 内缩环：带宽 kk·s，不合法时收窄
            var Q = new Vector3[k]; float kk = 1.25f; bool ok = false;
            for (int iter = 0; iter < 30 && !ok; iter++)
            {
                kk *= 0.8f; ok = true;
                for (int i = 0; i < k && ok; i++)
                {
                    int ip = (i - 1 + k) % k;
                    // 解 X：dot(X − P_i, m_ip) = kk·s_ip，dot(X − P_i, m_i) = kk·s_i（平面内二元一次方程）
                    float a1 = Vector3.Dot(m[ip], bu), b1 = Vector3.Dot(m[ip], bw), a2 = Vector3.Dot(m[i], bu), b2 = Vector3.Dot(m[i], bw);
                    float det = a1 * b2 - a2 * b1;
                    if (Mathf.Abs(det) < 1e-8f) { ok = false; break; }
                    float r1 = kk * s[ip], r2 = kk * s[i];
                    float x = (r1 * b2 - r2 * b1) / det, y = (a1 * r2 - a2 * r1) / det;
                    Q[i] = Pp[i] + bu * x + bw * y;
                }
                if (!ok) continue;
                for (int i = 0; i < k && ok; i++)
                {
                    if (Vector3.Dot(Q[(i + 1) % k] - Q[i], e[i]) <= 2e-3f) ok = false;            // 内缩环的边与原棱同向且不短于 2 mm（否则出现退化的细长三角形）
                    for (int j = 0; j < k && ok; j++) if (Vector3.Dot(Q[i] - Pp[j], m[j]) < kk * s[j] - 1e-4f) ok = false;   // 不越过任何棱的平移线
                }
            }
            if (!ok) { kk = 0f; for (int i = 0; i < k; i++) Q[i] = Pp[i]; }
            if (kk < 0.999f) narrowed++;

            int Add(Vector3 p, float t, Vector3 target)
            {
                pos.Add(new Vector3(p.x / scale.x, p.y / scale.y, p.z / scale.z));
                var nb = t > 0f ? Vector3.Lerp(n, target, t).normalized : n;
                nrm.Add(ToLocalN(nb));
                var nf = ToLocalN(n);
                uv1.Add(new Vector4(nf.x, nf.y, nf.z, t));
                uv2.Add(t > 0f ? ToLocalN(target) * t : Vector3.zero);
                uv0.Add(new Vector2(Vector3.Dot(p, bu), Vector3.Dot(p, bw)) / tile);
                col.Add(new Color(tone, (p.y - minY) / hy, 0f, 1f));
                return pos.Count - 1;
            }
            void Tri(int a, int b, int c) { tri.Add(a); tri.Add(b); tri.Add(c); }
            float Orient(Vector3 a, Vector3 b, Vector3 c) => Vector3.Dot(Vector3.Cross(b - a, c - a), n);
            // 凸四边形 a b c d（逆时针）：选两个三角形都朝外的对角线
            void QuadTri(int a, int b, int c, int d)
            {
                if (Orient(pos[a], pos[b], pos[c]) > 0f && Orient(pos[a], pos[c], pos[d]) > 0f) { Tri(a, b, c); Tri(a, c, d); }
                else { Tri(a, b, d); Tri(b, c, d); }
            }

            if (W <= 0f)
            {
                // 不做平滑：整块 facet 扇形，全硬边
                var ids = Pp.Select(p => Add(p, 0f, n)).ToArray();
                for (int i = 1; i + 1 < k; i++) Tri(ids[0], ids[i], ids[i + 1]);
                continue;
            }
            if (kk <= 0f)
            {
                // 平滑带放不下：仍要带上棱上点，与相邻 facet 一致（避免 T 形接缝）
                FanWithEdgePoints(Pp, s, e, n, Add, Tri);
                continue;
            }
            // 内缩环（t = 0）
            var q0 = Q.Select(p => Add(p, 0f, n)).ToArray();
            for (int i = 1; i + 1 < k; i++) Tri(q0[0], q0[i], q0[i + 1]);
            for (int i = 0; i < k; i++)
            {
                int i1 = (i + 1) % k, ip = (i - 1 + k) % k;
                var A = Pp[i] + e[i] * s[i]; var B = Pp[i1] - e[i] * s[i];
                // 棱带：A_i B_i Q_{i+1} Q_i
                int a = Add(A, 1f, Ne[i]), b = Add(B, 1f, Ne[i]), qa = Add(Q[i], 0f, n), qb = Add(Q[i1], 0f, n);
                QuadTri(a, b, qb, qa);
                // 转角：P_i A_i Q_i B_{i−1}，以 P_i–Q_i 为对角线（两个三角形总是朝外）
                var Bp = Pp[i] - e[ip] * s[ip];
                int pc = Add(Pp[i], 1f, vn[f.idx[i]]), ca = Add(A, 1f, Ne[i]), cq = Add(Q[i], 0f, n), cb = Add(Bp, 1f, Ne[ip]);
                Tri(pc, ca, cq); Tri(pc, cq, cb);
            }
        }
        var mesh = new Mesh { name = gen.gameObject.name + "_FacetRock" };
        if (pos.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2); mesh.SetColors(col);
        mesh.SetTriangles(tri, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    /// <summary>平滑带无法放下时的退化：facet 边界按棱上点细分后扇形三角化（整块 t = 0，facet 硬边，但与相邻 facet 的棱上点一致）。</summary>
    static void FanWithEdgePoints(Vector3[] Pp, float[] s, Vector3[] e, Vector3 n, System.Func<Vector3, float, Vector3, int> Add, System.Action<int, int, int> Tri)
    {
        int k = Pp.Length; var ring = new List<Vector3>();
        for (int i = 0; i < k; i++) { ring.Add(Pp[i]); ring.Add(Pp[i] + e[i] * s[i]); ring.Add(Pp[(i + 1) % k] - e[i] * s[i]); }
        var c = Pp.Aggregate(Vector3.zero, (acc, p) => acc + p) / k;
        int ci = Add(c, 0f, n); var ids = ring.Select(p => Add(p, 0f, n)).ToArray();
        for (int i = 0; i < ids.Length; i++) Tri(ci, ids[i], ids[(i + 1) % ids.Length]);
    }

    /// <summary>按位置焊接后统计每条边被几个三角形共用：全部为 2 即闭合流形。</summary>
    static string CheckClosed(Mesh mesh, Vector3 scale)
    {
        var v = mesh.vertices.Select(p => Vector3.Scale(p, scale)).ToArray(); var t = mesh.triangles;     // 世界尺寸下焊接（局部坐标在大缩放下会把相距几毫米的点焊成一点）
        var keyOf = new Dictionary<Vector3Int, int>();
        int K(Vector3 p) { var q = Vector3Int.RoundToInt(p * 2000f); if (!keyOf.TryGetValue(q, out int k)) { k = keyOf.Count; keyOf[q] = k; } return k; }
        var cnt = new Dictionary<(int, int), int>();
        for (int i = 0; i < t.Length; i += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = K(v[t[i + e]]), b = K(v[t[i + (e + 1) % 3]]); if (a == b) continue;
                var key = (Mathf.Min(a, b), Mathf.Max(a, b)); cnt[key] = cnt.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        return $"闭合校验：开口边 {cnt.Values.Count(c => c == 1)}，非流形边 {cnt.Values.Count(c => c > 2)}";
    }

    static void Output(FacetRockGenerator gen, Mesh mesh, bool saveAsset)
    {
        string childName = gen.gameObject.name + "_FacetRock";
        if (gen.output != null) Object.DestroyImmediate(gen.output);
        var old = gen.transform.Find(childName); if (old != null) Object.DestroyImmediate(old.gameObject);
        if (saveAsset)
        {
            string folder = $"Assets/Art/RockGen/Generated/{gen.gameObject.name}_{gen.seed}";
            if (!AssetDatabase.IsValidFolder(folder)) { Directory.CreateDirectory(folder); AssetDatabase.Refresh(); }
            string path = folder + "/FacetRock.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
        }
        var go = new GameObject(childName);
        go.transform.SetParent(gen.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mat = gen.material != null ? gen.material : LoadDefaultMaterial();
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        gen.output = go;
        var srcR = gen.GetComponent<MeshRenderer>(); if (srcR != null) srcR.enabled = !gen.hideSource;
    }

    static Material LoadDefaultMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(DefaultMat);
        if (mat == null) { mat = new Material(Shader.Find("RockGen/FacetRock")) { name = "M_RockGen_Facet" }; AssetDatabase.CreateAsset(mat, DefaultMat); }
        return mat;
    }
}

[CustomEditor(typeof(FacetRockGenerator))]
public class FacetRockGeneratorEditor : Editor
{
    void OnEnable() => Undo.undoRedoPerformed += SchedulePreview;
    void OnDisable() => Undo.undoRedoPerformed -= SchedulePreview;

    /// <summary>实时预览：已生成过（有 output 引用或同名子物体）且开启 livePreview 时，延迟到本帧 GUI 结束后重建（不保存网格资源）。</summary>
    void SchedulePreview()
    {
        foreach (var t in targets)
        {
            var gen = (FacetRockGenerator)t;
            if (gen == null || !gen.livePreview) continue;
            if (gen.output == null && gen.transform.Find(gen.gameObject.name + "_FacetRock") == null) continue;
            EditorApplication.delayCall += () => { if (gen != null) FacetRockBuilder.Preview(gen); };
        }
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck()) SchedulePreview();
        EditorGUILayout.Space();
        if (GUILayout.Button("生成岩石", GUILayout.Height(28)))
            foreach (var t in targets) Debug.Log("[FacetRock] " + FacetRockBuilder.Generate((FacetRockGenerator)t));
    }
}
