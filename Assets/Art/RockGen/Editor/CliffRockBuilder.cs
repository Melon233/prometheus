using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// 悬崖岩块生成（第 1 步：竖向节理 + 尖锥外凸）。
///
/// 流程：
/// 1. 从 ProBuilder 白盒中识别顶环（顶点 y 值之和最大的循环边）与底环（最小者），两环顶点数可以不同；
///    以连接两环的竖边为锚点，锚点之间按弧长比例对应，侧面视为两环之间的直纹面。
/// 2. 把侧面展开成首尾相接的带子：X = 沿周长的距离（每段取底环段长与顶环段长的平均），
///    v = 高度比例（0 底 → 1 顶）。两环所有顶点都作为断点插入，生成后两环形状保持不变。
///    计算距离时使用压缩坐标 Y = v · H · verticalScale（H 为平均侧面高度），使纵向更稀疏。
/// 3. 在 (X, Y) 上做周期泊松盘撒点，再做周期沃罗诺伊划分；单元边界在侧面转角处插入顶点。
/// 4. 每个单元以散点为锥顶做三角扇：锥顶沿该处外法线凸出（噪声 × 单元大小调制），
///    单元边界顶点按噪声向内凹陷形成节理缝；位于顶边、底边的顶点不动，保证顶面平整、与封口严丝合缝。
/// 5. 用侧面顶边、底边的顶点重建顶面与底面，网格闭合。输出为新的 ProBuilder 子物体，每个三角面硬边。
/// 6. 在展开带坐标上保存图：单元编号（含散点）、节理、高度（PNG + EXR）、噪声。网格 UV0 即展开带坐标，与图一一对应。
///
/// 结构：每个处理是一个 IRockStep（修改器），按 Pipeline() 列表顺序作用于共享的 RockGenContext。
/// 外凸方向场 displaceDir 默认为侧面外法线，节理深度场 jointOf 默认为 0；场修改器只改写这些场，移除即回到默认。
/// </summary>
public static class CliffRockBuilder
{
    const string OutRoot = "Assets/Art/RockGen/Generated";

    // ---------------- 内部数据 ----------------
    /// <summary>
    /// 侧面展开带：底环 B 与顶环 T 之间的直纹面（两环顶点数可以不同，侧面可以是任意多边形）。
    /// 锚点是连接底环顶点与顶环顶点的“竖边”，锚点之间按弧长比例把两环一一对应。
    /// 展开坐标 X ∈ [0, P)：每段锚点区间的宽度取底环段长与顶环段长的平均；v ∈ [0,1] 为从底环到顶环的比例。
    /// </summary>
    internal class Band
    {
        public Vector3[] B, T;            // 底环、顶环（白盒局部坐标），均为俯视逆时针，且第 0 个锚点在两环的起点
        float[] cumB, cumT;               // 两环的累计弧长（长度 n+1）
        float perB, perT;                 // 两环周长
        float[] aB, aT, aX;               // 锚点在底环、顶环上的弧长与在展开带上的 X（末尾追加回到起点的一项）
        public float P;                   // 展开带总长
        public float H;                   // 平均直纹线长度（米）
        public Vector3 center;            // 两环中心，用于判定外法线方向
        public List<float> Breaks;        // 断点：两环所有顶点在展开带上的 X，生成时必须插入顶点以保证两环形状不变
        public int anchorCount;
        public List<Vector3[]> TopTris = new List<Vector3[]>();                 // 白盒顶面区域（顶环所围的面）的三角形，即碰撞体的真实顶面
        public List<(Vector3 a, Vector3 b)> TopCreases = new List<(Vector3, Vector3)>();   // 顶面三角形之间的内部边（顶面不共面时的折痕）

        /// <summary>
        /// 白盒真实顶面在 q 处的高度：俯视下 q 在各顶面三角形中的重心坐标，取最“在内部”（最小重心坐标最大）的三角形，按其平面求高度。
        /// q 落在顶面外（例如顶边扰动外凸的点）时即沿最近三角形的平面外推。
        /// </summary>
        public float TopHeight(Vector3 q)
        {
            float best = float.MinValue, h = q.y;
            foreach (var t in TopTris)
            {
                float den = (t[1].z - t[2].z) * (t[0].x - t[2].x) + (t[2].x - t[1].x) * (t[0].z - t[2].z);
                if (Mathf.Abs(den) < 1e-8f) continue;
                float wa = ((t[1].z - t[2].z) * (q.x - t[2].x) + (t[2].x - t[1].x) * (q.z - t[2].z)) / den;
                float wb = ((t[2].z - t[0].z) * (q.x - t[2].x) + (t[0].x - t[2].x) * (q.z - t[2].z)) / den;
                float wc = 1f - wa - wb, mn = Mathf.Min(wa, Mathf.Min(wb, wc));
                if (mn > best) { best = mn; h = t[0].y * wa + t[1].y * wb + t[2].y * wc; }
            }
            return h;
        }

        /// <summary>q 竖直投影到白盒真实顶面上。</summary>
        public Vector3 OnTop(Vector3 q) => new Vector3(q.x, TopHeight(q), q.z);

        public Band(Vector3[] b, Vector3[] t, List<(int ib, int it)> anchors)
        {
            B = b; T = t;
            cumB = Cum(B, out perB); cumT = Cum(T, out perT);
            int m = anchors.Count; anchorCount = m;
            aB = new float[m + 1]; aT = new float[m + 1]; aX = new float[m + 1];
            for (int i = 0; i < m; i++) { aB[i] = cumB[anchors[i].ib]; aT[i] = cumT[anchors[i].it]; }
            aB[m] = perB; aT[m] = perT;                                    // 回到第 0 个锚点（弧长 0 + 周长）
            for (int i = 0; i < m; i++) aX[i + 1] = aX[i] + 0.5f * ((aB[i + 1] - aB[i]) + (aT[i + 1] - aT[i]));
            P = aX[m];
            // 断点：底环、顶环每个顶点映射到展开带的 X
            Breaks = new List<float>();
            for (int i = 0; i < B.Length; i++) Breaks.Add(XOfB(cumB[i]));
            for (int i = 0; i < T.Length; i++) Breaks.Add(XOfT(cumT[i]));
            Breaks = Breaks.Distinct().OrderBy(x => x).ToList();
            float hs = 0; int ns = 64; for (int i = 0; i < ns; i++) { var x = P * i / ns; hs += Vector3.Distance(Bottom(x), Top(x)); }
            H = hs / ns;
            center = (B.Aggregate(Vector3.zero, (s, v) => s + v) / B.Length + T.Aggregate(Vector3.zero, (s, v) => s + v) / T.Length) * 0.5f;
        }

        static float[] Cum(Vector3[] loop, out float per)
        {
            var c = new float[loop.Length + 1];
            for (int i = 0; i < loop.Length; i++) c[i + 1] = c[i] + Vector3.Distance(loop[i], loop[(i + 1) % loop.Length]);
            per = c[loop.Length]; return c;
        }

        /// <summary>环上弧长 s → 点（s 按周长折回）。</summary>
        static Vector3 OnLoop(Vector3[] loop, float[] cum, float per, float s)
        {
            s = Mathf.Repeat(s, per);
            int i = 0; while (i < loop.Length - 1 && cum[i + 1] <= s) i++;
            float len = cum[i + 1] - cum[i];
            return Vector3.Lerp(loop[i], loop[(i + 1) % loop.Length], len > 1e-8f ? (s - cum[i]) / len : 0f);
        }

        /// <summary>展开带 X → 锚点区间与区间内比例 f。</summary>
        void Seg(float X, out int k, out float f)
        {
            float x = Mathf.Repeat(X, P); k = 0;
            while (k < aX.Length - 2 && aX[k + 1] <= x) k++;
            float w = aX[k + 1] - aX[k]; f = w > 1e-8f ? (x - aX[k]) / w : 0f;
        }
        float XOfB(float s) { int k = 0; while (k < aB.Length - 2 && aB[k + 1] <= s) k++; float w = aB[k + 1] - aB[k]; return aX[k] + (w > 1e-8f ? (s - aB[k]) / w : 0f) * (aX[k + 1] - aX[k]); }
        float XOfT(float s) { int k = 0; while (k < aT.Length - 2 && aT[k + 1] <= s) k++; float w = aT[k + 1] - aT[k]; return aX[k] + (w > 1e-8f ? (s - aT[k]) / w : 0f) * (aX[k + 1] - aX[k]); }

        /// <summary>顶环第 k 个顶点在展开带上的 X。</summary>
        public float TopVertexX(int k) => XOfT(cumT[k]);

        /// <summary>展开带 X → 该处顶环点所在的顶环边（第 i 段，T[i]→T[i+1]）及段内比例 f。</summary>
        public void TopLocate(float X, out int i, out float f)
        {
            Seg(X, out int k, out float g);
            float s = Mathf.Repeat(Mathf.Lerp(aT[k], aT[k + 1], g), perT);
            i = 0; while (i < T.Length - 1 && cumT[i + 1] <= s) i++;
            float len = cumT[i + 1] - cumT[i]; f = len > 1e-8f ? (s - cumT[i]) / len : 0f;
        }

        public Vector3 Bottom(float X) { Seg(X, out int k, out float f); return OnLoop(B, cumB, perB, Mathf.Lerp(aB[k], aB[k + 1], f)); }
        public Vector3 Top(float X) { Seg(X, out int k, out float f); return OnLoop(T, cumT, perT, Mathf.Lerp(aT[k], aT[k + 1], f)); }

        /// <summary>展开带坐标 → 三维点：同一 X 的底环点与顶环点之间按 v 插值（直纹面）。</summary>
        public Vector3 Point(float X, float v) => Vector3.Lerp(Bottom(X), Top(X), v);

        /// <summary>外法线：沿 X 的中心差分切向 × 直纹线方向，朝远离中心的方向。</summary>
        public Vector3 Normal(float X, float v)
        {
            float e = P * 1e-3f;
            var du = Point(X + e, v) - Point(X - e, v);
            var dv = Top(X) - Bottom(X);
            var n = Vector3.Cross(du, dv).normalized;
            var p = Point(X, v);
            var c = new Vector3(center.x, p.y, center.z);
            return Vector3.Dot(n, p - c) >= 0 ? n : -n;
        }
    }

    internal class Cell
    {
        public Vector2 seed;               // 压缩坐标 (X, Y)
        public List<Vector2> poly;         // 单元多边形（压缩坐标，X 以散点为中心展开，可超出 [0, P)）
        public float bulge;                // 锥顶凸出量
        public float area;                 // 压缩坐标下的面积
    }

    /// <summary>面的角色：决定子网格、材质与贴图坐标。</summary>
    public enum FaceRole { Side, Top, Bevel, Bottom }

    // ---------------- 修改器栈 ----------------
    /// <summary>
    /// 所有步骤共享的生成上下文。场（displaceDir、jointOf）都有默认值，修改器只负责改写它们：
    /// 从流水线里移除某个修改器，相应的场就回到默认行为。
    /// </summary>
    public class RockGenContext
    {
        public CliffRockGenerator gen;
        internal Band band;
        public float Ymax;                                            // 压缩坐标下展开带的高度 = H · verticalScale
        public System.Random rng;
        internal PeriodicNoise noise;
        public List<Vector2> seeds = new List<Vector2>();             // 散点（压缩坐标 X, Y）
        internal List<Cell> cells = new List<Cell>();
        /// <summary>外凸 / 内陷方向场 (X, v) → 单位方向。默认 = 侧面外法线。</summary>
        public System.Func<float, float, Vector3> displaceDir;
        /// <summary>节理内陷深度场：展开带上的点（压缩坐标）→ 向内凹陷的米数。默认 = 0（无节理缝）。</summary>
        public System.Func<Vector2, float> jointOf = p => 0f;
        /// <summary>单元顶点吸附场：沃罗诺伊单元多边形的顶点（压缩坐标）→ 吸附后的位置，在插入转角切点之前应用。默认不变。</summary>
        public System.Func<Vector2, Vector2> cellVertexSnap = p => p;
        /// <summary>平滑组场：面序号 → ProBuilder 平滑组（0 = 不平滑 / 硬边；同一非零组的相邻面共享平均法线）。默认全部为 0（平直着色）。</summary>
        public System.Func<int, int> smoothingGroupOf = i => 0;
        /// <summary>转角棱线顶点 Q 的外凸上限：Q 到其所在竖线段两端的最近世界距离（米）→ 允许的最大外凸量。默认不限制。</summary>
        public System.Func<float, float> cornerBulgeLimit = d => float.MaxValue;
        /// <summary>侧面映射场 (X, v) → 侧面上的三维点（外凸、内陷之前）。默认 = 两环之间的直纹面。</summary>
        public System.Func<float, float, Vector3> surfacePoint;
        /// <summary>顶面外缘场 X → 顶面封口外缘上的点。默认 = 原顶环。与侧面顶边 surfacePoint(X, 1) 不重合时，封口步骤自动补一圈连接面。</summary>
        public System.Func<float, Vector3> topRing;
        /// <summary>顶边偏移场 X → 侧面顶边上的点水平移动的米数（正 = 外凸，负 = 内凹）。默认 0。</summary>
        public System.Func<float, float> topJag = x => 0f;
        /// <summary>未扰动的顶面外缘场（顶边扰动修改器写入扰动前的 topRing）。为空表示没有扰动，与 topRing 相同。封口用它求内环，保证内环是星形。</summary>
        public System.Func<float, Vector3> topRingSmooth;
        /// <summary>顶边偏移向量场 X → 侧面顶边点与顶面外缘点的偏移：水平部分垂直于该处所在的顶环边，竖直部分随白盒真实顶面的坡度升降。默认零向量。</summary>
        public System.Func<float, Vector3> topJagOffset = x => Vector3.zero;
        /// <summary>
        /// 顶边扰动 / 转角平滑的水平偏移，对任意“与顶环同参数化的基准环”R 求值：(R, X) → 偏移向量（水平）。
        /// 外缘用 R = 未扰动外缘；封口的草地边界环用 R = 未扰动外缘的内缩多边形，转角曲线随之按比例缩放，点链与外缘一样沿边单调。默认零偏移。
        /// </summary>
        public System.Func<System.Func<float, Vector3>, float, Vector3> topOffsetFor = (r, x) => Vector3.zero;
        /// <summary>顶环转角剪平使外缘相对原顶边向内的最大垂直距离（米）。封口的草地内环内缩量不小于它，保证外缘不越过内环。</summary>
        public float topCornerInward;
        /// <summary>
        /// 顶环转角剪平区（展开 X：转角 xc，半宽 a）。ConeFans 把区内的顶边顶点吸附到切点 A = xc − a / B = xc + a（转角点本身归 B），
        /// 原转角点消失，A、B 直接相连，原来连到区内顶点的转角竖线 / 节理线改接到较近的切点上。
        /// </summary>
        public List<(float xc, float a)> topChamfers = new List<(float xc, float a)>();
        // 网格输出（世界尺寸，白盒局部坐标轴）
        public List<Vector3> pos = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<float> grass = new List<float>();                 // 每顶点草地权重（与 pos 一一对应）：顶面内环以内 1，顶面外缘 0，其余 0
        public List<Face> faces = new List<Face>();
        public List<FaceRole> roles = new List<FaceRole>();          // 每个面的角色（侧面 / 顶面 / 倒角面 / 底面）
        public List<int> plates = new List<int>();                    // 每个面所属岩板（单元被转角线切开后的一块）编号；封口为 −1
        public int currentPlate = -1, plateCount;                     // 尖锥三角化时当前岩板编号 / 岩板总数
        /// <summary>顶点色（与 pos 一一对应），为空则不写。R 曲率（0.5 平，高 = 凸棱），G AO（1 = 无遮蔽），B 岩板色调（0.5 中性），A 草地遮罩（1 = 草）。</summary>
        public List<Color> colors;
        /// <summary>子网格场：面序号 → 子网格。默认（调试纹理）侧面为 0，其余为 1。</summary>
        public System.Func<int, int> submeshOf;
        /// <summary>按子网格顺序的材质。为空时 OutputMesh 用默认：[侧面调试材质（没有则灰色）, 灰色]。</summary>
        public Material[] materials;
        /// <summary>侧面顶边 / 底边上的顶点：键 → (展开坐标 X ∈ [0, P)，侧面三角形实际使用的三维坐标)。封口直接复用，保证坐标逐位一致。</summary>
        public Dictionary<long, (float x, Vector3 pos)> topPts = new Dictionary<long, (float, Vector3)>(), botPts = new Dictionary<long, (float, Vector3)>();
        public List<(Vector2 a, Vector2 b, Vector2 c, float ha, float hb, float hc)> paramTris = new List<(Vector2, Vector2, Vector2, float, float, float)>();
        public string folder;
        public Material sideMaterial;
        public List<string> log = new List<string>();

        /// <summary>加一个三角形；按 outward 方向统一绕序（只用于封口这类朝向明确的平面，侧面用 AddSideTri）。</summary>
        public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward, FaceRole role) => AddTri(a, b, c, ua, ub, uc, outward, role, 0f, 0f, 0f);

        /// <summary>同上，并给三个顶点指定草地权重 ga、gb、gc（翻转绕序时随顶点一起交换）。</summary>
        public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward, FaceRole role, float ga, float gb, float gc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); (gb, gc) = (gc, gb); }
            Push(a, b, c, ua, ub, uc, role, ga, gb, gc);
        }

        /// <summary>
        /// 加一个侧面三角形；按展开坐标 (X, v) 中的绕序统一朝向：展开平面上的有向面积符号乘以 sideWinding 必须为正。
        /// 侧面所有三角形来自同一个展开平面，这样相邻三角形的绕序天然一致，不依赖三维形状（三维法线估计在转角、强外凸处会误判）。
        /// </summary>
        public void AddSideTri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            float area = (ub.x - ua.x) * (uc.y - ua.y) - (ub.y - ua.y) * (uc.x - ua.x);
            if (area * sideWinding < 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); }
            Push(a, b, c, ua, ub, uc, FaceRole.Side, 0f, 0f, 0f);
        }

        /// <summary>展开平面上逆时针（+1）还是顺时针（−1）的三角形在三维中朝外。由 ConeFans 按侧面外法线投票确定。</summary>
        public int sideWinding = 1;

        void Push(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, FaceRole role, float ga, float gb, float gc)
        {
            int i = pos.Count; pos.Add(a); pos.Add(b); pos.Add(c); uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); grass.Add(ga); grass.Add(gb); grass.Add(gc);
            faces.Add(new Face(new[] { i, i + 1, i + 2 }) { manualUV = true });
            roles.Add(role); plates.Add(currentPlate);
        }
    }

    /// <summary>一个处理步骤（修改器）。只读写上下文，彼此独立。</summary>
    public interface IRockStep
    {
        string Name { get; }
        void Apply(RockGenContext ctx);
    }

    /// <summary>
    /// 默认流水线：按顺序执行。增加、移除或调换修改器只需改这里。
    /// 依赖关系：ScatterSeeds → [CornerSeedMargin] → [CornerVertexSnap] → VoronoiCells → NoiseBulge → [BulgeClamp] → [场修改器] → ConeFans → Caps → [WeldPositions] → 校验 / 出图 / 输出。
    /// 场修改器（JointInset、HorizontalDisplace、TopBevel）只改写上下文中的场，可单独移除。
    /// </summary>
    public static List<IRockStep> Pipeline() => new List<IRockStep>
    {
        new ScatterSeeds(),
        new CornerSeedMargin(),
        new CornerVertexSnap(),
        new VoronoiCells(),
        new TopEdgeMerge(),
        new NoiseBulge(),
        new BulgeClamp(),
        new JointInset(),
        new HorizontalDisplace(),
        new TopBevel(),
        new TopEdgeJag(),
        new ConeFans(),
        new Caps(),
        new WeldPositions(),
        new VertexColorBake(),
        new ClosedCheck(),
        new NormalSmoothing(),
        new SaveMapsStep(),
        new TextureMapping(),
        new OutputMesh(),
        new TopGrass(),
    };

    /// <summary>
    /// 实时预览流水线：默认流水线去掉出图（栅格化与写贴图是最耗时的部分），改为复用上次生成的调试材质。
    /// 预览不更新保存的图；点“生成岩石”时完整生成并更新。
    /// </summary>
    public static List<IRockStep> PreviewPipeline()
    {
        var list = Pipeline();
        int i = list.FindIndex(st => st is SaveMapsStep);
        list[i] = new ReuseMaterial();
        return list;
    }

    // ---------------- 入口 ----------------
    public static string Generate(CliffRockGenerator gen) => Generate(gen, Pipeline());

    /// <summary>实时预览：用预览流水线重建网格，不保存资源。</summary>
    public static string Preview(CliffRockGenerator gen) => Generate(gen, PreviewPipeline(), false);

    public static string Generate(CliffRockGenerator gen, List<IRockStep> pipeline, bool saveAssets = true)
    {
        var src = gen.GetComponent<ProBuilderMesh>();
        if (src == null) return "该物体上没有 ProBuilderMesh";
        var band = ExtractBand(src, out string err);
        if (band == null) return err;
        var ctx = new RockGenContext
        {
            gen = gen, band = band, Ymax = band.H * gen.verticalScale, rng = new System.Random(gen.seed),
            noise = new PeriodicNoise(gen.seed, Mathf.Max(1, Mathf.RoundToInt(band.P / gen.noiseScale)), Mathf.Max(1, Mathf.RoundToInt(band.H / gen.noiseScale))),
        };
        ctx.displaceDir = band.Normal;
        ctx.surfacePoint = band.Point;
        ctx.topRing = band.Top;
        ctx.submeshOf = i => ctx.roles[i] == FaceRole.Side ? 0 : 1;
        foreach (var step in pipeline) step.Apply(ctx);
        EditorUtility.SetDirty(gen);
        EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        if (saveAssets) AssetDatabase.SaveAssets();
        // 岩石变了：刷新场景里 RockGen 地面的撒草与接触 AO（预览时不刷新，太慢）
        if (saveAssets) foreach (var gr in Object.FindObjectsByType<RockGenGround>()) gr.Rebuild();
        return $"生成完成（世界尺寸，物体缩放 {gen.transform.lossyScale}）：底环 {band.B.Length} 点、顶环 {band.T.Length} 点、竖边锚点 {band.anchorCount} 条、展开长 {band.P:F2} m、平均高 {band.H:F2} m；" +
               $"散点 {ctx.seeds.Count}，单元 {ctx.cells.Count}，三角面 {ctx.faces.Count}；流水线 [{string.Join(" → ", pipeline.Select(s => s.Name))}]；{string.Join("；", ctx.log)}";
    }

    // ---------------- 步骤 ----------------
    /// <summary>撒点：X 周期的泊松盘采样。在纵向放大 verticalDensity 倍的坐标里采样再缩回，纵向间距 = spacing / verticalScale / verticalDensity。</summary>
    public class ScatterSeeds : IRockStep
    {
        public string Name => "撒点";
        public void Apply(RockGenContext c)
        {
            float vd = Mathf.Max(0.01f, c.gen.verticalDensity);
            c.seeds = PoissonPeriodic(c.band.P, c.Ymax * vd, c.gen.spacing, c.rng).Select(s => new Vector2(s.x, s.y / vd)).ToList();
        }
    }

    /// <summary>
    /// 撒点修改器：转角避让。离断点竖线（侧面转角）水平距离小于 cornerMargin × spacing 的散点，沿 X 推到该距离处（远离竖线一侧）。
    /// 否则单元被转角线切出极窄的碎块，锥顶与转角棱线顶点 Q 外凸方向不同，碎块在三维中被拧翻（看起来像破洞或褶皱）。
    /// </summary>
    public class CornerSeedMargin : IRockStep
    {
        public string Name => "转角避让";
        public void Apply(RockGenContext c)
        {
            float P = c.band.P, m = c.gen.cornerMargin * c.gen.spacing;
            if (m <= 0f) return;
            var lines = new List<float>();
            for (int k = -1; k <= 1; k++) foreach (var bx in c.band.Breaks) lines.Add(bx + k * P);
            for (int i = 0; i < c.seeds.Count; i++)
            {
                var sd = c.seeds[i];
                // 最近的转角线；相邻转角线间距不足 2m 时取两线中点，避免来回推
                float x = sd.x, best = lines.OrderBy(l => Mathf.Abs(l - x)).First();
                if (Mathf.Abs(x - best) >= m) continue;
                float side = x >= best ? 1f : -1f, nx = best + side * m;
                float other = lines.Where(l => (l - best) * side > 0).OrderBy(l => Mathf.Abs(l - best)).First();
                if (Mathf.Abs(other - best) < 2f * m) nx = (best + other) * 0.5f;
                c.seeds[i] = new Vector2(Mathf.Repeat(nx, P), sd.y);
            }
        }
    }

    /// <summary>
    /// 场修改器：转角吸附（需放在 VoronoiCells 之前）。单元多边形上离断点竖线（侧面转角）水平距离小于 0.5 × cornerMargin × spacing 的顶点，X 吸附到该竖线上。
    /// 否则沃罗诺伊顶点贴着转角线时会切出极窄的碎条：一侧是外凸的转角棱线顶点 Q，一侧是内陷的节理顶点，碎条在三维中被拧翻。
    /// 吸附在插入转角切点之前进行，相邻单元的共享边在吸附后完全一致，切点也一致，不会产生 T 形接缝或折返。
    /// </summary>
    public class CornerVertexSnap : IRockStep
    {
        public string Name => "转角吸附";
        public void Apply(RockGenContext c)
        {
            float P = c.band.P, th = 0.5f * c.gen.cornerMargin * c.gen.spacing;
            if (th <= 0f) return;
            var lines = new List<float>();
            for (int m = -2; m <= 2; m++) foreach (var bx in c.band.Breaks) lines.Add(bx + m * P);
            c.cellVertexSnap = pt =>
            {
                float near = lines.OrderBy(l => Mathf.Abs(l - pt.x)).First();
                return Mathf.Abs(pt.x - near) < th ? new Vector2(near, pt.y) : pt;
            };
        }
    }

    /// <summary>沃罗诺伊划分（压缩坐标，含 X 方向 ±P 的周期副本）；单元顶点经吸附场处理后，单元边与断点竖线的交点插入顶点。</summary>
    public class VoronoiCells : IRockStep
    {
        public string Name => "沃罗诺伊";
        public void Apply(RockGenContext c)
        {
            var band = c.band; float P = band.P;
            var corners = new List<float>();
            for (int m = -1; m <= 1; m++) foreach (var bx in band.Breaks) corners.Add(bx + m * P);
            c.cells.Clear();
            foreach (var s in c.seeds)
            {
                var poly = new List<Vector2> { new Vector2(s.x - P * 0.5f, 0), new Vector2(s.x + P * 0.5f, 0), new Vector2(s.x + P * 0.5f, c.Ymax), new Vector2(s.x - P * 0.5f, c.Ymax) };
                foreach (var o in c.seeds)
                    for (int w = -1; w <= 1; w++)
                    {
                        var q = new Vector2(o.x + w * P, o.y);
                        if ((q - s).sqrMagnitude < 1e-10f) continue;
                        poly = ClipHalfPlane(poly, s, q);
                        if (poly.Count < 3) break;
                    }
                if (poly.Count < 3) continue;
                // 单元顶点吸附（相邻单元共享的顶点按同一规则吸附，结果一致），合并吸附后重合的相邻点
                var snapped = new List<Vector2>();
                foreach (var pt in poly) { var q = c.cellVertexSnap(pt); if (snapped.Count == 0 || Key(snapped[snapped.Count - 1], P) != Key(q, P)) snapped.Add(q); }
                if (snapped.Count > 1 && Key(snapped[0], P) == Key(snapped[snapped.Count - 1], P)) snapped.RemoveAt(snapped.Count - 1);
                if (snapped.Count < 3) continue;
                poly = InsertVerticalCuts(snapped, corners);
                c.cells.Add(new Cell { seed = s, poly = poly, area = Mathf.Abs(SignedArea(poly)) });
            }
        }
    }

    /// <summary>
    /// 单元修改器：顶边点合并（放在 VoronoiCells 之后）。保证顶边上的单元顶点（顶环转角 + 节理与顶边的交点）相互间距 ≥ topMinGap。
    /// 小单元只擦到顶边时，夹着它的两条节理会在顶边上相距很近；之后再插峰点、加顶边扰动，极近的点之间稍有偏移就会把三角形拧坏。
    /// 做法：收集所有单元在顶边上的顶点 X（折回 [0, P)），反复合并最近的一对间距 < topMinGap 的点——含转角的一方不动（转角吸收对方），
    /// 否则按点数加权取中点；再把每个单元顶边顶点的 X 改为合并后的值（保持单元内的展开连续），去掉相邻重复点。
    /// 只沿顶边水平移动，共享同一 X 的相邻单元同步更新，不产生裂缝；只擦到顶边的小单元顶边段收缩为一点，不再接触顶边。
    /// </summary>
    public class TopEdgeMerge : IRockStep
    {
        public string Name => "顶边点合并";
        public void Apply(RockGenContext c)
        {
            float gap = c.gen.topMinGap, P = c.band.P, Ymax = c.Ymax, eps = 1e-4f;
            if (gap <= 0f) return;
            var corners = c.band.Breaks.Select(b => Mathf.Repeat(b, P)).ToList();
            bool IsCorner(float x) => corners.Any(b => Mathf.Abs(Wrap(x - b, P)) < 1e-3f);
            // 顶边上的点（去重），每个点记录：位置、代表的原始点数、是否转角
            var pts = new List<(float x, int w, bool corner)>();
            foreach (var cell in c.cells)
                foreach (var q in cell.poly)
                {
                    if (q.y < Ymax - eps) continue;
                    float x = Mathf.Repeat(q.x, P);
                    if (!pts.Any(t => Mathf.Abs(Wrap(t.x - x, P)) < 1e-3f)) pts.Add((x, 1, IsCorner(x)));
                }
            var orig = pts.Select(t => t.x).ToList();
            var target = orig.ToDictionary(x => x, x => x);   // 原始点 X → 合并后 X
            int merged = 0;
            while (pts.Count > 1)
            {
                pts = pts.OrderBy(t => t.x).ToList();
                int bi = -1; float bg = float.MaxValue;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    if (a.corner && b.corner) continue;                      // 两个转角都不能动
                    float g = Mathf.Repeat(b.x - a.x, P);
                    if (g < gap && g < bg) { bg = g; bi = i; }
                }
                if (bi < 0) break;
                var pa = pts[bi]; var pb = pts[(bi + 1) % pts.Count];
                float nx = pa.corner ? pa.x : pb.corner ? pb.x : Mathf.Repeat(pa.x + Mathf.Repeat(pb.x - pa.x, P) * pb.w / (pa.w + pb.w), P);
                var np = (nx, pa.w + pb.w, pa.corner || pb.corner);
                foreach (var k in orig) { var tx = target[k]; if (Mathf.Abs(Wrap(tx - pa.x, P)) < 1e-4f || Mathf.Abs(Wrap(tx - pb.x, P)) < 1e-4f) target[k] = nx; }
                int j = (bi + 1) % pts.Count; pts.RemoveAt(System.Math.Max(bi, j)); pts.RemoveAt(System.Math.Min(bi, j)); pts.Add(np);
                merged++;
            }
            if (merged == 0) return;
            float Map(float x) { float xm = Mathf.Repeat(x, P); var k = orig.OrderBy(o => Mathf.Abs(Wrap(o - xm, P))).First(); return x + Wrap(target[k] - xm, P); }
            foreach (var cell in c.cells)
            {
                var res = new List<Vector2>();
                foreach (var q in cell.poly)
                {
                    var r = q.y > Ymax - eps ? new Vector2(Map(q.x), q.y) : q;
                    if (res.Count == 0 || (res[res.Count - 1] - r).sqrMagnitude > 1e-10f) res.Add(r);
                }
                if (res.Count > 1 && (res[0] - res[res.Count - 1]).sqrMagnitude <= 1e-10f) res.RemoveAt(res.Count - 1);
                cell.poly = res; cell.area = Mathf.Abs(SignedArea(res));
            }
            c.log.Add($"顶边点合并：{merged} 次（最小间距 {gap:F2} m）");
        }

        /// <summary>周期差值折到 [−P/2, P/2)。</summary>
        static float Wrap(float d, float P) => Mathf.Repeat(d + P * 0.5f, P) - P * 0.5f;
    }

    /// <summary>每个单元的锥顶凸出量 = bulge × 噪声（±bulgeVariation）× 单元大小（相对平均面积，夹在 0.6～1.5）。</summary>
    public class NoiseBulge : IRockStep
    {
        public string Name => "噪声凸出量";
        public void Apply(RockGenContext c)
        {
            float meanArea = c.cells.Average(x => x.area);
            foreach (var cell in c.cells)
            {
                float n01 = c.noise.Sample(cell.seed.x / c.band.P, cell.seed.y / c.Ymax);
                float size = Mathf.Clamp(Mathf.Sqrt(cell.area / meanArea), 0.6f, 1.5f);
                cell.bulge = c.gen.bulge * Mathf.Lerp(1f - c.gen.bulgeVariation, 1f + c.gen.bulgeVariation, n01) * size;
            }
        }
    }

    /// <summary>
    /// 凸出量修改器：外凸限制。锥顶凸出量不超过 bulgeLimit × 散点到“本单元边界、顶边、底边、转角竖线”的最近世界距离。
    /// 贴着底边 / 顶边 / 转角的散点若照常外凸，锥顶到边界的三角形会被拉平甚至翻折。转角棱线顶点 Q 的外凸量取自单元凸出量，随之受限。
    /// 同时设置转角棱线顶点 Q 的外凸上限：不超过 bulgeLimit × Q 到所在竖线段两端（单元边界或顶 / 底边）的距离。
    /// 世界距离：X 为弧长（米），纵向 = 压缩坐标 Y / verticalScale。
    /// </summary>
    public class BulgeClamp : IRockStep
    {
        public string Name => "外凸限制";
        public void Apply(RockGenContext c)
        {
            float P = c.band.P, vs = c.gen.verticalScale, k = c.gen.bulgeLimit; int clamped = 0;
            var lines = new List<float>();
            for (int m = -1; m <= 1; m++) foreach (var bx in c.band.Breaks) lines.Add(bx + m * P);
            Vector2 W(Vector2 p) => new Vector2(p.x, p.y / vs);          // 压缩坐标 → 世界距离坐标
            foreach (var cell in c.cells)
            {
                var s = W(cell.seed);
                float d = Mathf.Min(s.y, c.Ymax / vs - s.y);                  // 到底边、顶边
                d = Mathf.Min(d, lines.Min(l => Mathf.Abs(l - cell.seed.x))); // 到转角竖线
                for (int i = 0; i < cell.poly.Count; i++) d = Mathf.Min(d, DistToSegment(s, W(cell.poly[i]), W(cell.poly[(i + 1) % cell.poly.Count])));
                float lim = k * d;
                if (cell.bulge > lim) { cell.bulge = lim; clamped++; }
            }
            c.cornerBulgeLimit = d => k * d;
            c.log.Add($"外凸限制 {clamped}/{c.cells.Count} 个单元");
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }

    /// <summary>场修改器：节理内陷深度 = jointDepth × (0.35～1 噪声)；顶边、底边上为 0，保证两环不动。由位置决定，相邻单元一致。</summary>
    public class JointInset : IRockStep
    {
        public string Name => "节理内陷";
        public void Apply(RockGenContext c)
        {
            float eps = 1e-4f, Ymax = c.Ymax, P = c.band.P, depth = c.gen.jointDepth; var noise = c.noise;
            c.jointOf = p => (p.y < eps || p.y > Ymax - eps) ? 0f : depth * (0.35f + 0.65f * noise.Sample(Mathf.Repeat(p.x, P) / P, p.y / Ymax));
        }
    }

    /// <summary>
    /// 场修改器：水平外凸。把当前外凸方向投影到水平面再归一化，与原方向按 normalKeep 混合。
    /// normalKeep = 0 时外凸只在水平方向，顶点高度等于其在原侧面上的高度，斜面上的锥顶不会被抬过顶边。
    /// 侧面接近水平（投影长度过小）时保留原方向。
    /// </summary>
    public class HorizontalDisplace : IRockStep
    {
        public string Name => "水平外凸";
        public void Apply(RockGenContext c)
        {
            var baseDir = c.displaceDir; float keep = c.gen.normalKeep;
            c.displaceDir = (x, v) =>
            {
                var n = baseDir(x, v); var h = new Vector3(n.x, 0f, n.z);
                if (h.sqrMagnitude < 1e-6f) return n;
                return Vector3.Lerp(h.normalized, n, keep).normalized;
            };
        }
    }

    /// <summary>
    /// 场修改器：顶边倒角（只作用于顶环）。在每条直纹线的横截面里，倒角面连接“顶面内收 b 的点”和“侧面顶边沿直纹线下移 a 的点”。
    /// - bevel：倒角面宽度 c = bevel × 平均侧面高度 H（横截面里倒角斜边的长度）。
    /// - bevelAngle：倒角面相对顶面的倾角 φ。设顶面内向方向与侧壁向下方向的夹角为 α（竖直侧壁 α = 90°），
    ///   由正弦定理：b = c · sin(α + φ) / sin α，a = b · sin φ / sin(α + φ)。外倾、悬挑的侧壁也得到同样的倒角坡度。
    ///   α 与 a 都在垂直于顶边的横截面里计算；直纹线相对顶边斜着走时，沿直纹线的实际下移量 = a / 直纹线方向垂直于顶边的分量。
    /// - 每条顶边取一个内收量 b（用该边上直纹线的平均 α 计算），顶环转角处按两侧不同的内收量求斜接点；下移量 a 按每条直纹线自己的 α 计算，保证坡度处处为 φ。
    /// - 顶面不共面时，内向方向取顶面整体平面（Newell 法）内的方向，内收后的外缘点再竖直投影到白盒真实顶面（碰撞体顶面），
    ///   外缘始终落在原顶面上；此时倒角坡度相对整体平面近似为 φ，局部会有偏差。
    /// - 约束：每条直纹线的有效角度 ≤ 180° − α − 3°（倒角不能比外倾侧壁还陡）；内收量 ≤ 顶面内切半径的 45%；沿直纹线的下移量 ≤ 直纹线长度的 25%。
    ///   之后沿展开带密集采样做合法性自检：倒角条带每个四边形朝向一致、外缘绕顶面中心单调前进（不自交、不折回）。
    ///   不合法（锐角转角、斜直纹线、不共面顶面叠加且倒角很宽时会出现）则宽度按 0.85 逐步等比缩小，角度不变。受限时日志写明实际宽度与比例。
    /// 侧面映射场沿直纹线把侧面压到 [0, 1 − a / 直纹线长]，节理与锥顶随之压缩；封口步骤发现外缘与侧面顶边不重合时自动补一圈倒角面。
    /// bevel = 0 或移除本修改器即为直角顶边。
    /// </summary>
    public class TopBevel : IRockStep
    {
        public string Name => "顶边倒角";
        public void Apply(RockGenContext c)
        {
            var band = c.band; var T = band.T; int n = T.Length;
            if (c.gen.bevel <= 0f) return;
            float phi = Mathf.Clamp(c.gen.bevelAngle, 5f, 85f) * Mathf.Deg2Rad;
            // 顶面整体平面法线（Newell 法，朝上）与中心
            var nT = Vector3.zero; var ctr = Vector3.zero;
            for (int i = 0; i < n; i++) { var a = T[i]; var b = T[(i + 1) % n]; nT += new Vector3((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)); ctr += a; }
            nT = nT.normalized; if (nT.y < 0) nT = -nT; ctr /= n;
            // 每条顶边的向内方向（在顶面整体平面内、垂直于边、指向中心一侧）与顶面内切半径。
            // 不能改用每条边的局部扇面：中心低于边缘（帐篷形顶面）时，局部扇面向内下倾，外缘内收后会低于侧面顶边，倒角面朝下翻转
            var inward = new Vector3[n];
            float inradius = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var a = T[i]; var b = T[(i + 1) % n]; var dir = Vector3.Cross(nT, (b - a).normalized).normalized;
                if (Vector3.Dot(dir, ctr - (a + b) * 0.5f) < 0) dir = -dir;
                inward[i] = dir;
                inradius = Mathf.Min(inradius, Vector3.Dot(ctr - a, dir));
            }
            // 直纹线 X 处的横截面：顶面内向方向与侧壁向下方向的夹角 α（限制在 20°～160°，避免极端侧壁让内收量发散），
            // 以及直纹线方向垂直于顶边的分量长度 perp（倒角坡度只由垂直于顶边的分量决定）
            float Alpha(float x, out int edge, out float perp)
            {
                band.TopLocate(x, out edge, out _);
                var e = (T[(edge + 1) % n] - T[edge]).normalized;
                var w = (band.Bottom(x) - band.Top(x)).normalized; var wp = w - e * Vector3.Dot(w, e);
                perp = Mathf.Max(0.2f, wp.magnitude);
                return Mathf.Clamp(Mathf.Acos(Mathf.Clamp(Vector3.Dot(inward[edge], wp.normalized), -1f, 1f)), 20f * Mathf.Deg2Rad, 160f * Mathf.Deg2Rad);
            }
            // 有效倒角角度：横截面三角形要求 α + φ < 180°；侧壁外倾（α 大）时倒角不能比侧壁还陡，限制在 180° − α − 3° 以内（至少 5°）
            float Phi(float al) => Mathf.Max(5f * Mathf.Deg2Rad, Mathf.Min(phi, Mathf.PI - al - 3f * Mathf.Deg2Rad));
            // 每条顶边的平均 α（沿展开带均匀采样，按所在顶边归组）→ 单位宽度下的内收量
            var sumA = new float[n]; var cnt = new int[n];
            for (int k = 0; k < 256; k++) { float al = Alpha(band.P * (k + 0.5f) / 256f, out int e, out _); sumA[e] += al; cnt[e]++; }
            var bUnit = new float[n];
            for (int i = 0; i < n; i++) { float al = cnt[i] > 0 ? sumA[i] / cnt[i] : Mathf.PI * 0.5f; bUnit[i] = Mathf.Sin(al + Phi(al)) / Mathf.Sin(al); }
            // 当前宽度下的内收量与斜接外缘顶点
            float width = c.gen.bevel * band.H;
            var bIn = new float[n]; var off = new Vector3[n];
            void Build(float w)
            {
                for (int i = 0; i < n; i++) bIn[i] = w * bUnit[i];
                for (int i = 0; i < n; i++)
                {
                    // 满足 dot(p − T, 前一边内向) = 前一边内收量、dot(p − T, 本边内向) = 本边内收量；两边接近共线时取平均内收，斜接长度限制在 1 / 0.3 倍内收量以内
                    int ip = (i - 1 + n) % n; var u0 = inward[ip]; var u1 = inward[i]; float b0 = bIn[ip], b1 = bIn[i];
                    float k = Vector3.Dot(u0, u1), det = 1f - k * k;
                    var dp = det < 1e-4f ? (u0 + u1).normalized * (b0 + b1) * 0.5f : u0 * ((b0 - k * b1) / det) + u1 * ((b1 - k * b0) / det);
                    off[i] = ProjectToTop(T[i] + Vector3.ClampMagnitude(dp, Mathf.Max(b0, b1) / 0.3f));
                }
            }
            // 竖直投影到白盒真实顶面（碰撞体顶面）：顶面不共面时，在整体平面内内收的点会高于或低于原顶面
            Vector3 ProjectToTop(Vector3 q) => band.OnTop(q);
            // 直纹线 X 处沿直纹线的下移量（横截面下移量 / perp）
            float Drop(float x)
            {
                float al = Alpha(x, out int e, out float perp); float ph = Phi(al);
                return bIn[e] * Mathf.Sin(ph) / Mathf.Sin(al + ph) / perp;
            }
            Vector3 SideTop(float x) { float len = Vector3.Distance(band.Top(x), band.Bottom(x)); return c.surfacePoint(x, 1f - Drop(x) / len); }   // 此时 c.surfacePoint 仍是被包装前的映射
            // 外缘点：两斜接点之间插值后再竖直投影到真实顶面（顶面不共面时两点连线会穿过折痕、偏离顶面）
            Vector3 Ring(float x) { band.TopLocate(x, out int i, out float f); return band.OnTop(Vector3.Lerp(off[i], off[(i + 1) % n], f)); }
            // 采样点：均匀 512 个 + 所有断点（顶环转角必须采到）
            var xs = Enumerable.Range(0, 512).Select(k => band.P * (k + 0.5f) / 512f).Concat(band.Breaks.Select(b => Mathf.Repeat(b, band.P))).OrderBy(x => x).ToList();
            // 合法性自检：倒角条带四边形 (s0, s1, r1, r0) 两个三角形的朝向与多数一致；外缘绕顶面中心单调前进
            bool Valid()
            {
                var S = xs.Select(SideTop).ToList(); var R = xs.Select(Ring).ToList(); int m = xs.Count;
                var rc = R.Aggregate(Vector3.zero, (a, b) => a + b) / m;
                int pos = 0, neg = 0, ringPos = 0, ringNeg = 0;
                for (int k = 0; k < m; k++)
                {
                    int j = (k + 1) % m;
                    var outN = c.band.Normal(xs[k], 1f) + nT;
                    if ((S[j] - S[k]).sqrMagnitude > 1e-10f || (R[j] - R[k]).sqrMagnitude > 1e-10f)
                    {
                        float t1 = Vector3.Dot(Vector3.Cross(S[j] - S[k], R[j] - S[k]), outN), t2 = Vector3.Dot(Vector3.Cross(R[j] - S[k], R[k] - S[k]), outN);
                        if (t1 > 1e-8f) pos++; else if (t1 < -1e-8f) neg++;
                        if (t2 > 1e-8f) pos++; else if (t2 < -1e-8f) neg++;
                    }
                    float tr = Vector3.Dot(Vector3.Cross(R[k] - rc, R[j] - rc), nT);
                    if (tr > 1e-8f) ringPos++; else if (tr < -1e-8f) ringNeg++;
                }
                return Mathf.Min(pos, neg) == 0 && Mathf.Min(ringPos, ringNeg) == 0;
            }
            // 先按内切半径与直纹线长度约束定初始宽度，再自检，不合法则逐步缩小
            float scale = Mathf.Min(1f, Mathf.Max(0f, inradius) * 0.45f / Mathf.Max(1e-6f, width * bUnit.Max()));
            Build(width * scale);
            for (int k = 0; k < 256; k++)
            {
                float x = band.P * (k + 0.5f) / 256f, len = Vector3.Distance(band.Top(x), band.Bottom(x));
                float d = Drop(x); if (d > 0.25f * len) { scale *= 0.25f * len / d; Build(width * scale); }
            }
            int tries = 0;
            while (!Valid() && tries < 30) { scale *= 0.85f; Build(width * scale); tries++; }
            var basePoint = c.surfacePoint;
            c.surfacePoint = (x, v) =>
            {
                float len = Vector3.Distance(band.Top(x), band.Bottom(x));
                float vt = len > 1e-5f ? 1f - Drop(x) / len : 1f;
                return basePoint(x, v * vt);
            };
            c.topRing = Ring;
            c.log.Add($"顶边倒角：宽度 {width * scale:F2} m{(scale < 0.999f ? $"（受限缩小为设定的 {scale:P0}）" : "")}、角度 {c.gen.bevelAngle:F0}°、内收 {bIn.Min():F2}～{bIn.Max():F2} m");
        }
    }

    /// <summary>
    /// 单元 / 场修改器：顶边扰动（放在 TopBevel 之后、ConeFans 之前）。让顶边轮廓顺应侧面节理：岩板顶部外凸、节理处内凹，俯视呈锯齿状。
    /// - 插点：每个摸到顶边的单元，其顶边段被转角竖线分成若干小段；长度 ≥ 2 × topMinGap 的小段插一个峰点
    ///   （含散点 X 的小段取散点 X，夹在小段中间 50% 且离两端 ≥ topMinGap 的范围内，其余取中点），topJagRound 时峰点到某端 ≥ 2 × topMinGap 才在该侧再插一点。
    ///   顶边段只属于一个单元，插点不会产生 T 形接缝。顶边原有点（转角、节理）的间距已由 TopEdgeMerge 保证 ≥ topMinGap，所以顶边所有点相互间距 ≥ topMinGap。
    /// - 偏移量：峰点外凸 topJag × 该岩板凸出量（凸出量已受外凸限制），且不超过峰点到所在小段较近端点距离的一半（避免贴着转角拉出尖刺）；
    ///   节理端点内凹 topNotch；转角竖线上的点为 0（顶环转角不动）；圆润点取端点与峰值之间 75% 处。
    ///   所有控制点再做坡度限制：偏移不超过到相邻控制点较近距离的一半（相邻控制点之间坡度 ≤ 1，避免顶边附近侧面三角形被横向拧翻）。
    ///   之后做锐角剪平：外凸且俯视夹角 < topSharpAngle 的峰点 / 圆润点，删去尖点、改为两条边上各一点（切角），剪平点不受 topMinGap 限制。
    ///   最后才把本修改器的点插入所属单元的顶边。顶边上任意 X 的偏移按这些控制点周期分段线性插值，写入顶边偏移场 topJag。
    /// - 偏移方向：水平、垂直于该处所在的顶环边、朝外，再随白盒真实顶面坡度升降（topJagOffset）。不能用侧面外法线场：转角附近外法线是两侧的平均、斜着指，
    ///   相邻点的偏移带沿边分量，会互相交叉使顶边折回。同一条直边上的点偏移方向相同，顺序不会改变。
    /// - 顶环转角剪平：俯视夹角 < topSharpAngle 的白盒凸转角，在与转角相连的两条顶边上各插一点 A、B（转角两侧 X 距离 a），区内不插峰点。
    ///   区间写入 topChamfers，ConeFans 把区内顶点吸附到 A / B：原转角点消失，A–B 直接相连，原连到转角的线改接较近的切点。
    ///   偏移场在区内取 A、B 连线。剪平造成的最大内缩写入 topCornerInward，封口内环不小于它。
    /// - 顶面外缘场同步按同样的方向与量偏移，开倒角时倒角带跟随锯齿，草地内环也随外缘自动跟随。
    /// 移除本修改器即为直顶边。
    /// </summary>
    public class TopEdgeJag : IRockStep
    {
        public string Name => "顶边扰动";
        public void Apply(RockGenContext c)
        {
            float jag = c.gen.topJag, notch = c.gen.topNotch, P = c.band.P, Ymax = c.Ymax, eps = 1e-4f, minGap = Mathf.Max(0f, c.gen.topMinGap);
            float sharpThr = c.gen.topSharpAngle, sharpStr = Mathf.Clamp01(c.gen.topSmoothStrength) * 0.45f;
            bool sharpOn = sharpThr > 0f && sharpStr > 0f;
            if (jag <= 0f && notch <= 0f && !sharpOn) return;
            // 白盒顶环转角剪平区：俯视夹角 < topSharpAngle 的凸转角，半宽 a = 0.45 × 强度 × min(两侧顶环边的 X 长度, 2 × spacing)（相邻转角互不重叠）。
            // 拓扑上剪平（ConeFans 里把区内顶点吸附到 A、B）：原转角点消失，A–B 直接相连。区内不插峰点，区内的控制点（转角、节理端点）不参与偏移插值
            var T = c.band.T; int nT = T.Length; var band = c.band;
            var zones = new List<(float xc, float a)>();
            float cornerInward = 0f;
            if (sharpOn)
                for (int k = 0; k < nT; k++)
                {
                    var Tk = T[k]; Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
                    var e0 = Flat(T[(k - 1 + nT) % nT] - Tk); var e1 = Flat(T[(k + 1) % nT] - Tk);
                    float th = Vector3.Angle(e0, e1);
                    bool convex = Vector3.Dot(e0.normalized + e1.normalized, Flat(c.band.center - Tk)) > 0f;
                    if (!convex || th >= sharpThr) continue;
                    float xc = Mathf.Repeat(band.TopVertexX(k), P);
                    float xp = Mathf.Repeat(xc - band.TopVertexX((k - 1 + nT) % nT), P), xn = Mathf.Repeat(band.TopVertexX((k + 1) % nT) - xc, P);
                    float a = sharpStr * Mathf.Min(Mathf.Min(xp, xn), 2f * c.gen.spacing);
                    if (a < 1e-3f) continue;
                    zones.Add((xc, a));
                    cornerInward = Mathf.Max(cornerInward, 0.5f * a * Mathf.Sin(th * Mathf.Deg2Rad));    // 切线中点到两边的垂直距离
                }
            float WrapD(float d) => Mathf.Repeat(d + P * 0.5f, P) - P * 0.5f;
            bool InZone(float x) => zones.Any(z => Mathf.Abs(WrapD(x - z.xc)) < z.a - 1e-4f);
            var lines = new List<float>();
            for (int m = -2; m <= 2; m++) foreach (var bx in c.band.Breaks) lines.Add(bx + m * P);
            bool OnCorner(float x) => lines.Any(l => Mathf.Abs(l - x) < 1e-4f);
            // 顶边偏移控制点：x 折回 [0, P)；cell ≥ 0 表示本修改器新插的点（峰点 / 圆润点 / 平滑点），需插入该单元，xu 为其在单元内的展开 X；
            // cell = −1 为单元原有的点（转角、节理端点），不插入
            var ctrl = new List<(float x, float d, int cell, float xu)>();
            int inserted = 0;
            for (int ci = 0; ci < c.cells.Count; ci++)
            {
                var cell = c.cells[ci];
                // 单元在顶边上的顶点（X 升序）
                var tops = cell.poly.Where(q => q.y > Ymax - eps).Select(q => q.x).Distinct().OrderBy(x => x).ToList();
                if (tops.Count < 2) continue;
                float peakMax = jag * cell.bulge;
                // 端点：转角上为 0，否则为节理（内凹）；中间的点只可能是转角
                float EndD(float x) => OnCorner(x) ? 0f : -notch;
                for (int k = 0; k + 1 < tops.Count; k++)
                {
                    float l = tops[k], r = tops[k + 1], len = r - l;
                    if (len < 1e-3f) continue;
                    float dl = k == 0 ? EndD(l) : 0f, dr = k + 2 == tops.Count ? EndD(r) : 0f;
                    // 小段短于 2 × topMinGap 不插峰点（两端之间直接线性过渡）；峰点夹在 [l + gap, r − gap] 与小段中间 50% 的交集内；
                    // 圆润点只在峰点到该端的距离 ≥ 2 × gap 时插入，保证顶边所有点相互间距 ≥ topMinGap
                    var pts = new List<(float x, float d)>();
                    if (jag > 0f && len >= 2f * minGap)
                    {
                        float lo = Mathf.Max(l + 0.25f * len, l + minGap), hi = Mathf.Min(r - 0.25f * len, r - minGap);
                        float px = cell.seed.x > l && cell.seed.x < r ? Mathf.Clamp(cell.seed.x, lo, hi) : (l + r) * 0.5f;
                        float peakD = Mathf.Min(peakMax, 0.5f * Mathf.Min(px - l, r - px));     // X 为弧长（米）
                        pts.Add((px, peakD));
                        if (c.gen.topJagRound && px - l >= 2f * minGap) pts.Add(((l + px) * 0.5f, Mathf.Lerp(dl, peakD, 0.75f)));
                        if (c.gen.topJagRound && r - px >= 2f * minGap) pts.Add(((px + r) * 0.5f, Mathf.Lerp(dr, peakD, 0.75f)));
                    }
                    ctrl.Add((Mathf.Repeat(l, P), dl, -1, l)); ctrl.Add((Mathf.Repeat(r, P), dr, -1, r));
                    foreach (var pt in pts) if (!InZone(pt.x)) ctrl.Add((Mathf.Repeat(pt.x, P), pt.d, ci, pt.x));
                }
            }
            // 同一 X 的控制点（相邻单元共享的节理点）偏移相同，去重后升序
            var cx = ctrl.GroupBy(t => Mathf.Round(t.x * 1e4f)).Select(g => g.First()).OrderBy(t => t.x).ToList();
            // 坡度限制：每个控制点的偏移不超过它到左右相邻控制点较近距离的一半（周期），相邻控制点之间坡度 ≤ 1。
            // 节理端点的内凹是固定值，离下一个控制点很近（例如 0.1 m）时轮廓会陡到 2:1 以上，顶边附近的侧面三角形被横向拧翻
            {
                int nc = cx.Count; var capped = new List<(float x, float d, int cell, float xu)>();
                for (int i = 0; i < nc; i++)
                {
                    float dl = cx[i].x - cx[(i - 1 + nc) % nc].x, dr = cx[(i + 1) % nc].x - cx[i].x;
                    if (dl <= 0f) dl += P; if (dr <= 0f) dr += P;
                    float lim = 0.5f * Mathf.Min(dl, dr);
                    capped.Add((cx[i].x, Mathf.Clamp(cx[i].d, -lim, lim), cx[i].cell, cx[i].xu));
                }
                cx = capped;
            }
            // 锐角剪平：本修改器插的点（峰点 / 圆润点）中，俯视轮廓上外凸且夹角 < topSharpAngle 的尖点，删去尖点，改为两条边上各一点（切角）：
            // 在 (X, 偏移) 平面里（直边上即俯视平面）取 A = 尖点向左邻点走 s、B = 向右邻点走 s（s = 0.45 × 剪平强度，两侧互不重叠），A、B 之间为直线。
            // 剪平点不受 topMinGap 限制。节理端点是单元结构点，不参与；白盒转角另行剪平（见下）
            int smoothed = 0;
            {
                float thr = c.gen.topSharpAngle, str = Mathf.Clamp01(c.gen.topSmoothStrength) * 0.45f;
                if (thr > 0f && str > 0f && cx.Count > 2)
                {
                    int nc = cx.Count; var outList = new List<(float x, float d, int cell, float xu)>();
                    for (int i = 0; i < nc; i++)
                    {
                        var q = cx[i]; var a = cx[(i - 1 + nc) % nc]; var b = cx[(i + 1) % nc];
                        float dxa = a.x - q.x, dxb = b.x - q.x; if (dxa >= 0f) dxa -= P; if (dxb <= 0f) dxb += P;
                        var u = new Vector2(dxa, a.d - q.d); var w = new Vector2(dxb, b.d - q.d);
                        bool convex = q.d > Mathf.Lerp(a.d, b.d, -dxa / (dxb - dxa));       // 高于两邻点连线 = 向外凸
                        if (q.cell < 0 || !convex || Vector2.Angle(u, w) >= thr) { outList.Add(q); continue; }
                        // A、B 的 x 为相对尖点的 X 偏移
                        foreach (var pt in new[] { new Vector2(0f, q.d) + u * str, new Vector2(0f, q.d) + w * str })
                            outList.Add((Mathf.Repeat(q.x + pt.x, P), pt.y, q.cell, q.xu + pt.x));
                        smoothed++;
                    }
                    cx = outList.OrderBy(t => t.x).ToList();
                }
            }
            // 剪平区内的控制点（转角、节理端点、落入区内的剪平点）去掉：区内顶点都会被吸附到 A、B
            cx = cx.Where(t => !InZone(t.x)).ToList();
            // 把本修改器的点插入所属单元的顶边
            foreach (var q in cx) if (q.cell >= 0) { c.cells[q.cell].poly = InsertOnTop(c.cells[q.cell].poly, new Vector2(q.xu, Ymax), Ymax); inserted++; }
            float Profile(float x)
            {
                if (cx.Count == 0) return 0f;
                x = Mathf.Repeat(x, P);
                int n = cx.Count, hi = 0;
                while (hi < n && cx[hi].x <= x) hi++;
                var a = cx[(hi - 1 + n) % n]; var b = cx[hi % n];
                float ax = a.x > x ? a.x - P : a.x, bx = b.x < x ? b.x + P : b.x;   // 跨接缝时展开
                return bx - ax > 1e-6f ? Mathf.Lerp(a.d, b.d, (x - ax) / (bx - ax)) : a.d;
            }
            // 偏移方向：该处所在顶环边的水平外法线（顶环为俯视逆时针，但这里按远离中心判定朝外，不依赖绕序）
            Vector3 Dir(float x)
            {
                band.TopLocate(x, out int i, out _);
                var e = T[(i + 1) % nT] - T[i]; var d = new Vector3(e.z, 0f, -e.x).normalized;
                var mid = (T[i] + T[(i + 1) % nT]) * 0.5f;
                return Vector3.Dot(d, new Vector3(mid.x - band.center.x, 0f, mid.z - band.center.z)) >= 0f ? d : -d;
            }
            // 偏移向量：水平移动 Dir × 偏移量，再加上白盒真实顶面在新旧位置的高度差（斜顶面上外凸的点要随坡面升降，否则顶边出现台阶）。
            // 侧面顶边点（开倒角时在顶面以下）与外缘点用同一偏移，二者相对位置不变
            var baseRing = c.topRing;
            // 对基准环 R 的水平偏移：顶边扰动；剪平区内为 A、B 两点（各含扰动）的连线，与吸附后的实际顶边 A–B 一致
            Vector3 OffFor(System.Func<float, Vector3> R, float x)
            {
                foreach (var z in zones)
                {
                    float dx = WrapD(x - z.xc);
                    if (Mathf.Abs(dx) >= z.a) continue;
                    float xa = z.xc - z.a, xb = z.xc + z.a;
                    var pa = R(xa) + Dir(xa) * Profile(xa); var pb = R(xb) + Dir(xb) * Profile(xb);
                    var d = Vector3.Lerp(pa, pb, (dx + z.a) / (2f * z.a)) - R(x); d.y = 0f;
                    return d;
                }
                return Dir(x) * Profile(x);
            }
            // 插入切点 A、B：插入 X 落在其顶边段内的单元
            foreach (var z in zones)
                foreach (var sgn in new[] { -1f, 1f })
                {
                    float x = Mathf.Repeat(z.xc + sgn * z.a, P);
                    foreach (var cell in c.cells)
                    {
                        var tops = cell.poly.Where(q => q.y > Ymax - eps).Select(q => q.x).ToList();
                        if (tops.Count < 2) continue;
                        float lo = tops.Min(), hi = tops.Max(), xu = lo + Mathf.Repeat(x - lo, P);
                        if (xu > lo + 1e-4f && xu < hi - 1e-4f) { cell.poly = InsertOnTop(cell.poly, new Vector2(xu, Ymax), Ymax); inserted++; break; }
                    }
                }
            c.topCornerInward = cornerInward;
            c.topChamfers = zones;
            Vector3 Off(float x)
            {
                var h = OffFor(baseRing, x); var b = baseRing(x);
                return h + Vector3.up * (band.TopHeight(b + h) - band.TopHeight(b));
            }
            c.topJag = Profile; c.topJagOffset = Off; c.topOffsetFor = OffFor;
            c.topRingSmooth = baseRing;
            c.topRing = x => baseRing(x) + Off(x);
            c.log.Add($"顶边扰动：插入 {inserted} 个点，外凸 ≤ {(cx.Count > 0 ? cx.Max(t => t.d) : 0f):F2} m，内凹 {notch:F2} m，锐角剪平 {smoothed} 处，转角剪平 {zones.Count} 处");
        }

        /// <summary>在多边形的顶边（两端 y 都在 Ymax 上的边）中插入点 q（q.y = Ymax，q.x 落在该边内部）。</summary>
        static List<Vector2> InsertOnTop(List<Vector2> poly, Vector2 q, float Ymax)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                if (a.y > Ymax - 1e-4f && b.y > Ymax - 1e-4f && (q.x - a.x) * (q.x - b.x) < 0)
                {
                    var res = new List<Vector2>(poly); res.Insert(i + 1, q); return res;
                }
            }
            return poly;
        }
    }

    /// <summary>
    /// 尖锥三角化：每个单元以散点为锥顶做三角扇，锥顶沿方向场外凸 bulge；单元边界顶点沿方向场反向内陷 jointOf。
    /// 跨断点竖线（侧面转角）的单元沿该线切块：线上取 Q（散点高度，夹在该段范围内），沿两侧方向场平均外凸 0.7 × bulge 形成棱线；
    /// 散点所在块以锥顶为中心，其余块以靠近散点一侧的 Q 为中心。
    /// 三角形绕序按展开平面上的绕序统一（AddSideTri），朝外的绕序方向由沿展开带均匀采样的侧面外法线投票决定一次。
    /// </summary>
    public class ConeFans : IRockStep
    {
        /// <summary>展开平面上线段 a0→a1 与 b0→b1 是否相交（含端点接触、共线重叠；端点重合的情况由调用方排除相邻边）。</summary>
        static bool SegCross2(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            float d1 = Cr(b0, b1, a0), d2 = Cr(b0, b1, a1), d3 = Cr(a0, a1, b0), d4 = Cr(a0, a1, b1);
            const float e = 1e-9f;
            if (((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e))) return true;
            bool On(Vector2 o, Vector2 a, Vector2 p, float d) => Mathf.Abs(d) <= e && Mathf.Min(o.x, a.x) - 1e-6f <= p.x && p.x <= Mathf.Max(o.x, a.x) + 1e-6f && Mathf.Min(o.y, a.y) - 1e-6f <= p.y && p.y <= Mathf.Max(o.y, a.y) + 1e-6f;
            return On(b0, b1, a0, d1) || On(b0, b1, a1, d2) || On(a0, a1, b0, d3) || On(a0, a1, b1, d4);
        }

        /// <summary>展开平面上简单多边形的耳切（任意绕序），输出与多边形同绕序的三角形；找不到耳时剩余部分退化为扇形。</summary>
        static List<(Vector2, Vector2, Vector2)> EarClip2D(List<Vector2> poly)
        {
            float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var v = new List<Vector2>(poly); var res = new List<(Vector2, Vector2, Vector2)>();
            float area = 0f; for (int i = 0; i < v.Count; i++) area += Cr(Vector2.zero, v[i], v[(i + 1) % v.Count]);
            float sg = Mathf.Sign(area);
            int guard = 0;
            while (v.Count > 3 && guard++ < 1000)
            {
                bool cut = false;
                for (int i = 0; i < v.Count; i++)
                {
                    Vector2 a = v[(i - 1 + v.Count) % v.Count], b = v[i], d = v[(i + 1) % v.Count];
                    float cr = Cr(a, b, d) * sg;
                    if (cr <= 1e-9f) continue;                                            // 反角或共线点不能作耳尖（共线点会作为其他耳的端点保留，不产生 T 形接缝）
                    bool inside = false;
                    for (int k = 0; k < v.Count && !inside; k++)
                    {
                        var q = v[k]; if (q == a || q == b || q == d) continue;
                        inside = Cr(a, b, q) * sg >= 0f && Cr(b, d, q) * sg >= 0f && Cr(d, a, q) * sg >= 0f;
                    }
                    if (inside) continue;
                    res.Add((a, b, d)); v.RemoveAt(i); cut = true; break;
                }
                if (!cut) break;
            }
            if (v.Count == 3 && Mathf.Abs(Cr(v[0], v[1], v[2])) > 1e-9f) res.Add((v[0], v[1], v[2]));
            else for (int i = 1; i + 1 < v.Count; i++) res.Add((v[0], v[i], v[i + 1]));
            return res;
        }
        public string Name => "尖锥三角化";
        public void Apply(RockGenContext c)
        {
            var band = c.band; float P = band.P, Ymax = c.Ymax, eps = 1e-4f;
            Vector2 UV(Vector2 p) => new Vector2(p.x / P, p.y / Ymax);       // 展开带坐标（X 不折回，保证单元内 UV 连续）
            // 同一展开坐标的边界点只计算一次，所有单元复用同一份三维坐标：
            // 相邻单元各自算出的展开坐标有 1e-6 量级的浮点误差，分别求值会得到相差 0.01 mm 左右的“同一点”，
            // 导致焊接 / ProBuilder 共享顶点失效（顶点编辑时会裂开）。
            // 规范键 CK：先按 Key（1e-4 取整）查；没有时再查相邻取整格里 1.5e-4 以内的已登记点，避免误差恰好跨过取整边界时同一点得到两个键
            var canon = new Dictionary<long, Vector2>();
            long CK(Vector2 p)
            {
                long k = Key(p, P);
                if (canon.ContainsKey(k)) return k;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        long nk = Key(p + new Vector2(dx, dy) * 1e-4f, P);
                        if (!canon.TryGetValue(nk, out var q)) continue;
                        float ddx = Mathf.Repeat(p.x - q.x + P * 0.5f, P) - P * 0.5f, ddy = p.y - q.y;
                        if (ddx * ddx + ddy * ddy < 1.5e-4f * 1.5e-4f) return nk;
                    }
                canon[k] = p; return k;
            }
            var boundaryCache = new Dictionary<long, Vector3>();
            Vector3 Boundary(Vector2 p)
            {
                long k = CK(p);
                if (!boundaryCache.TryGetValue(k, out var r))
                {
                    float v = p.y / Ymax;
                    r = c.surfacePoint(p.x, v) - c.displaceDir(p.x, v) * c.jointOf(p);
                    if (p.y > Ymax - eps) r += c.topJagOffset(p.x);   // 顶边扰动：顶边上的点外凸 / 内凹，并随真实顶面坡度升降
                    boundaryCache[k] = r;
                }
                return r;
            }
            var special = new Dictionary<long, (Vector3 pos, float h)>();    // 转角棱线顶点 Q：外凸而非内陷
            Vector3 B3(Vector2 p) => special.TryGetValue(CK(p), out var s) ? s.pos : Boundary(p);
            float BH(Vector2 p) => special.TryGetValue(CK(p), out var s) ? s.h : -c.jointOf(p) + (p.y > Ymax - eps ? c.topJag(p.x) : 0f);
            var cornerXs = new List<float>();
            for (int m = -1; m <= 1; m++) foreach (var bx in band.Breaks) cornerXs.Add(bx + m * P);
            cornerXs.Sort();
            // 展开平面上 +X × +v 对应的三维法线 (∂X × ∂v) 与外法线同向则逆时针朝外，否则顺时针朝外；多点投票，避免个别斜面误判
            int vote = 0;
            for (int i = 0; i < 32; i++)
            {
                float x = P * (i + 0.5f) / 32f, e = P * 1e-3f;
                var dX = band.Point(x + e, 0.5f) - band.Point(x - e, 0.5f); var dV = band.Top(x) - band.Bottom(x);
                vote += Vector3.Dot(Vector3.Cross(dX, dV), band.Normal(x, 0.5f)) >= 0 ? 1 : -1;
            }
            c.sideWinding = vote >= 0 ? 1 : -1;
            // 第一遍：每个单元按转角竖线切块、插入 Q
            var cellData = new List<(Cell cell, Vector3 apex, List<float> cuts, List<List<Vector2>> pieces, List<Vector2> qs)>();
            foreach (var cell in c.cells)
            {
                float v0 = cell.seed.y / Ymax;
                var apex = c.surfacePoint(cell.seed.x, v0) + c.displaceDir(cell.seed.x, v0) * cell.bulge;
                float minX = cell.poly.Min(p => p.x), maxX = cell.poly.Max(p => p.x);
                var cuts = cornerXs.Where(x => x > minX + 1e-4f && x < maxX - 1e-4f).ToList();
                var pieces = new List<List<Vector2>>(); var rest = cell.poly;
                foreach (var x in cuts) { pieces.Add(ClipAxis(rest, x, true)); rest = ClipAxis(rest, x, false); }
                pieces.Add(rest);
                var qs = new List<Vector2>();
                for (int i = 0; i < cuts.Count; i++)
                {
                    var onLine = pieces[i].Where(p => Mathf.Abs(p.x - cuts[i]) < 1e-4f).Select(p => p.y).ToList();
                    float lo = onLine.Min(), hi = onLine.Max(), pad = (hi - lo) * 0.1f;
                    var q = new Vector2(cuts[i], Mathf.Clamp(cell.seed.y, lo + pad, hi - pad));
                    float vq = q.y / Ymax;
                    var dq = (c.displaceDir(q.x - 1e-3f, vq) + c.displaceDir(q.x + 1e-3f, vq)).normalized;
                    float hq = Mathf.Min(cell.bulge * 0.7f, c.cornerBulgeLimit(Mathf.Min(q.y - lo, hi - q.y) / c.gen.verticalScale));
                    special[CK(q)] = (c.surfacePoint(q.x, vq) + dq * hq, hq);
                    qs.Add(q);
                    pieces[i] = InsertOnLine(pieces[i], q); pieces[i + 1] = InsertOnLine(pieces[i + 1], q);
                }
                cellData.Add((cell, apex, cuts, pieces, qs));
            }
            // 顶环转角剪平（拓扑）：剪平区内的顶边顶点吸附到切点（转角左侧 → A、右侧 → B，转角点本身按 mode 归 B 或 A），去掉相邻重复点。
            // 原转角点消失，A–B 直接相连，原来连到区内顶点的转角竖线 / 节理线改接到切点上。
            // 吸附后某块在展开平面上自相交或翻转（例如节理折点落在斜线 Q → B 上方）时，该转角改为转角点归 A；仍不行则该转角不吸附
            // （区内顶点保留，按偏移场落在 A–B 连线上，几何上仍是剪平的）
            var zones = c.topChamfers; var mode = zones.Select(_ => 1).ToArray();
            float ZoneDx(float x, (float xc, float a) z) => Mathf.Repeat(x - z.xc + P * 0.5f, P) - P * 0.5f;
            int ZoneOf(Vector2 p)
            {
                if (p.y <= Ymax - eps) return -1;
                for (int zi = 0; zi < zones.Count; zi++) if (Mathf.Abs(ZoneDx(p.x, zones[zi])) < zones[zi].a - 1e-4f) return zi;
                return -1;
            }
            List<Vector2> Snap(List<Vector2> piece)
            {
                var snapped = new List<Vector2>();
                foreach (var p0 in piece)
                {
                    var p = p0; int zi = ZoneOf(p);
                    if (zi >= 0 && mode[zi] != 0)
                    {
                        var z = zones[zi]; float dx = ZoneDx(p.x, z);
                        float side = dx < -1e-5f ? -1f : dx > 1e-5f ? 1f : mode[zi];
                        p = new Vector2(p.x - dx + side * z.a, Ymax);
                    }
                    if (snapped.Count == 0 || (snapped[snapped.Count - 1] - p).sqrMagnitude > 1e-10f) snapped.Add(p);
                }
                while (snapped.Count > 1 && (snapped[0] - snapped[snapped.Count - 1]).sqrMagnitude <= 1e-10f) snapped.RemoveAt(snapped.Count - 1);
                return snapped;
            }
            float Area(List<Vector2> pl) { float a = 0f; for (int i = 0; i < pl.Count; i++) { var u = pl[i]; var w = pl[(i + 1) % pl.Count]; a += u.x * w.y - u.y * w.x; } return a * 0.5f; }
            bool Valid(List<Vector2> before, List<Vector2> after)
            {
                if (after.Count < 3) return false;
                float a0 = Area(before), a1 = Area(after);
                if (a1 * Mathf.Sign(a0) < 1e-6f) return false;
                int n = after.Count;
                for (int i = 0; i < n; i++)
                    for (int k = i + 2; k < n; k++)
                    {
                        if (i == 0 && k == n - 1) continue;
                        if (SegCross2(after[i], after[(i + 1) % n], after[k], after[(k + 1) % n])) return false;
                    }
                return true;
            }
            // 全局一致性：所有块应无缝铺满展开带——内部边恰好被两块以相反方向各用一次，顶边、底边只用一次。
            // 逐块的简单性检查发现不了“两块吸附后互相重叠”（某块越过转角竖线多占的区域没有从邻块让出），这里按有向边统计：
            // 同向重复（重叠）或缺反向（缝隙）的边所在的块记为冲突。吸附前已存在的冲突边（基线）不计
            var allPieces = cellData.SelectMany(cd => cd.pieces).ToList();
            HashSet<(long, long)> ConflictEdges(System.Func<List<Vector2>, List<Vector2>> map)
            {
                var dirCount = new Dictionary<(long, long), int>();
                foreach (var pc in allPieces)
                {
                    var s = map(pc);
                    for (int i = 0; i < s.Count; i++)
                    {
                        var p = s[i]; var q = s[(i + 1) % s.Count];
                        if ((q - p).sqrMagnitude < 1e-10f) continue;
                        if ((p.y > Ymax - eps && q.y > Ymax - eps) || (p.y < eps && q.y < eps)) continue;   // 顶边、底边是边界
                        var key = (CK(p), CK(q)); if (key.Item1 == key.Item2) continue;
                        dirCount[key] = dirCount.TryGetValue(key, out int n0) ? n0 + 1 : 1;
                    }
                }
                var bad = new HashSet<(long, long)>();
                foreach (var kv in dirCount)
                    if (kv.Value > 1 || !dirCount.ContainsKey((kv.Key.Item2, kv.Key.Item1))) { bad.Add(kv.Key); bad.Add((kv.Key.Item2, kv.Key.Item1)); }
                return bad;
            }
            var baseline = zones.Count > 0 ? ConflictEdges(pc => pc) : new HashSet<(long, long)>();
            if (zones.Count > 0)
                for (int iter = 0; iter < 4; iter++)
                {
                    var bump = new HashSet<int>();
                    var conflicts = ConflictEdges(Snap); conflicts.ExceptWith(baseline);
                    foreach (var pc in allPieces)
                    {
                        var touched = pc.Select(ZoneOf).Where(zi => zi >= 0 && mode[zi] != 0).Distinct().ToList();
                        if (touched.Count == 0) continue;
                        var s = Snap(pc);
                        bool conflict = false;
                        for (int i = 0; i < s.Count && !conflict; i++) conflict = conflicts.Contains((CK(s[i]), CK(s[(i + 1) % s.Count])));
                        if (conflict || !Valid(pc, s)) foreach (var zi in touched) bump.Add(zi);
                    }
                    if (bump.Count == 0) break;
                    foreach (var zi in bump) mode[zi] = mode[zi] == 1 ? -1 : 0;
                }
            if (zones.Count > 0) c.log.Add($"转角剪平拓扑：{zones.Count} 处，转角点归 B {mode.Count(m => m == 1)}、归 A {mode.Count(m => m == -1)}、未吸附 {mode.Count(m => m == 0)}");
            foreach (var cd in cellData)
            {
                var cell = cd.cell; var apex = cd.apex; var cuts = cd.cuts; var qs = cd.qs;
                var pieceRange = cd.pieces.Select(pc => (lo: pc.Min(v => v.x), hi: pc.Max(v => v.x))).ToList();   // 吸附前每块的 X 范围（转角竖线之间）
                var pieces = zones.Count > 0 ? cd.pieces.Select(Snap).ToList() : cd.pieces;
                int seedPiece = cuts.Count(x => x < cell.seed.x);
                for (int j = 0; j < pieces.Count; j++)
                {
                    var piece = pieces[j];
                    c.currentPlate = c.plateCount++;                         // 每个单元被转角线切开的每一块是一块岩板
                    // 扇心候选：散点块用散点（顶点 apex），其余块用靠散点一侧的 Q；再依次试本块两侧的 Q。
                    // 剪平吸附后转角竖线变成斜线 Q → 切点，一侧的块让出了切线与 Q 之间的三角区，散点落在该区时从散点出发的扇形不是星形（三角形翻面），改用 Q
                    var cands = new List<(Vector2 ctr, bool isQ)>();
                    if (j == seedPiece) cands.Add((cell.seed, false)); else cands.Add((j < seedPiece ? qs[j] : qs[j - 1], true));
                    if (j >= 1) cands.Add((qs[j - 1], true));
                    if (j < qs.Count) cands.Add((qs[j], true));
                    bool Skip(Vector2 cc, bool isQ, Vector2 p, Vector2 q) =>
                        isQ && (CK(p) == CK(cc) || CK(q) == CK(cc) || (Mathf.Abs(p.x - cc.x) < 1e-4f && Mathf.Abs(q.x - cc.x) < 1e-4f));
                    float Cross2(Vector2 u, Vector2 w) => u.x * w.y - u.y * w.x;
                    float pieceArea = 0f; for (int i = 0; i < piece.Count; i++) pieceArea += Cross2(piece[i], piece[(i + 1) % piece.Count]);
                    bool StarFrom(Vector2 cc, bool isQ)
                    {
                        for (int i = 0; i < piece.Count; i++)
                        {
                            var p = piece[i]; var q = piece[(i + 1) % piece.Count];
                            if ((q - p).sqrMagnitude < 1e-10f || Skip(cc, isQ, p, q)) continue;
                            if (Cross2(p - cc, q - cc) * Mathf.Sign(pieceArea) <= 1e-8f) return false;
                        }
                        return true;
                    }
                    int pi = cands.FindIndex(t => StarFrom(t.ctr, t.isQ));
                    // 都不是星形时（转角竖线恰为节理、块内没有 Q）：在吸附前的 X 范围内（不跨转角竖线）网格搜索星形核内离散点最近的点作扇心，凸出量同散点
                    if (pi < 0)
                    {
                        float yLo = piece.Min(v => v.y), yHi = piece.Max(v => v.y); var rg = pieceRange[j];
                        float best = float.MaxValue; Vector2 bp = default(Vector2);
                        for (int gx = 1; gx < 24; gx++)
                            for (int gy = 1; gy < 24; gy++)
                            {
                                var cc = new Vector2(Mathf.Lerp(rg.lo, rg.hi, gx / 24f), Mathf.Lerp(yLo, yHi, gy / 24f));
                                float dd = (cc - cell.seed).sqrMagnitude;
                                if (dd < best && StarFrom(cc, false)) { best = dd; bp = cc; }
                            }
                        if (best < float.MaxValue) { cands.Add((bp, false)); pi = cands.Count - 1; }
                    }
                    if (pi < 0)
                    {
                        // 星形核为空（吸附后出现反角，例如右侧块只剩节理折点 + 斜线 Q → B）：不设扇心，在展开平面上耳切这一块
                        foreach (var v in piece)
                        {
                            if (v.y > Ymax - eps) c.topPts[CK(v)] = (Mathf.Repeat(v.x, P), B3(v));
                            if (v.y < eps) c.botPts[CK(v)] = (Mathf.Repeat(v.x, P), B3(v));
                        }
                        foreach (var (ta, tb, tc) in EarClip2D(piece))
                        {
                            c.AddSideTri(B3(ta), B3(tb), B3(tc), UV(ta), UV(tb), UV(tc));
                            c.paramTris.Add((ta, tb, tc, BH(ta), BH(tb), BH(tc)));
                        }
                        continue;
                    }
                    var pick = cands[pi];
                    Vector2 ctr = pick.ctr; Vector3 ctr3; float ctrH;
                    if (!pick.isQ && pick.ctr == cell.seed) { ctr3 = apex; ctrH = cell.bulge; }
                    else if (!pick.isQ) { float vv = ctr.y / Ymax; ctr3 = c.surfacePoint(ctr.x, vv) + c.displaceDir(ctr.x, vv) * cell.bulge; ctrH = cell.bulge; }
                    else { var sp = special[CK(ctr)]; ctr3 = sp.pos; ctrH = sp.h; }
                    bool ctrIsQ = pick.isQ;
                    long ck = CK(ctr);
                    for (int i = 0; i < piece.Count; i++)
                    {
                        var p = piece[i]; var q = piece[(i + 1) % piece.Count];
                        if ((q - p).sqrMagnitude < 1e-10f) continue;
                        if (p.y > Ymax - eps) c.topPts[CK(p)] = (Mathf.Repeat(p.x, P), B3(p));
                        if (p.y < eps) c.botPts[CK(p)] = (Mathf.Repeat(p.x, P), B3(p));
                        // 以 Q 为中心时跳过与 Q 共线的边：与 Q 相邻的两条边，以及两端都在 Q 所在竖线上的边（转角吸附后可能出现），否则得到零面积三角形
                        if (Skip(ctr, ctrIsQ, p, q)) continue;
                        c.AddSideTri(ctr3, B3(p), B3(q), UV(ctr), UV(p), UV(q));
                        c.paramTris.Add((ctr, p, q, ctrH, BH(p), BH(q)));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 封口（角色：顶面 Top、倒角面 Bevel、底面 Bottom）：侧面顶边、底边上的顶点按展开顺序排成环，直接复用侧面三角形里的三维坐标。
    /// 顶面：外缘取顶面外缘场 topRing(X)；若与侧面顶边不重合（例如倒角修改器内收了外缘），先在两者之间补一圈连接面，再以外缘中心做三角扇。
    /// 底面：直接以侧面底边为外缘做三角扇（假设顶面、底面为星形多边形）。
    /// </summary>
    public class Caps : IRockStep
    {
        public string Name => "封口";
        public void Apply(RockGenContext c)
        {
            c.currentPlate = -1;
            var top = c.topPts.Values.OrderBy(t => t.x).ToList();
            var side = top.Select(t => t.pos).ToList();
            var ring = top.Select(t => c.topRing(t.x)).ToList();
            bool strip = side.Zip(ring, (a, b) => (a - b).sqrMagnitude).Any(d2 => d2 > 1e-8f);   // 0.1 mm 以内视为重合（顶边扰动后两边各自求值有浮点差）
            if (strip)
            {
                // 倒角带：逐条顶环边做“拉链式”缝合。侧面顶边一段与外缘一段（都从转角 k 到转角 k+1）沿该顶环边方向都是单调的
                // （倒角下移只沿边滑动，顶边扰动只沿垂直于边偏移，都不改变顺序），按点在边方向上的投影参数归并两条链，每步推进参数较小的一侧，
                // 得到两条单调链之间的标准三角剖分，不会扭转。不能按 X 逐点配成四边形（滑动后配对扭转），也不宜耳切（细长且两侧大量共线点，容易卡住）
                var xs = top.Select(t => t.x).ToList(); var corner = CornerIndices(c, xs); int m = corner.Length, n = side.Count;
                for (int k = 0; k < m; k++)
                {
                    int a = corner[k], b = corner[(k + 1) % m];
                    var sChain = new List<Vector3>(); var rChain = new List<Vector3>(); var nrm = Vector3.zero;
                    for (int i = a; ; i = (i + 1) % n) { sChain.Add(side[i]); rChain.Add(ring[i]); nrm += c.band.Normal(xs[i], 1f) + Vector3.up; if (i == b) break; }
                    var T0 = c.band.T[k]; var e = (c.band.T[(k + 1) % m] - T0).normalized; nrm = nrm.normalized;
                    float Tp(Vector3 q) => Vector3.Dot(q - T0, e);
                    // 先按自然顺序生成三角形（绕序天然一致），整段统一判定一次是否翻转：不能逐个按平均法线定朝向，
                    // 侧面顶边相对外缘沿边滑动较多时会出现细长三角形，其实际法线可能与平均法线相差 90° 以上，单独翻转后与邻面绕序不一致
                    var tris = new List<(Vector3 a, Vector3 b, Vector3 d)>();
                    int si = 0, ri = 0;
                    while (si < sChain.Count - 1 || ri < rChain.Count - 1)
                    {
                        bool advS = ri >= rChain.Count - 1 || (si < sChain.Count - 1 && Tp(sChain[si + 1]) <= Tp(rChain[ri + 1]));
                        if (advS) { tris.Add((sChain[si], sChain[si + 1], rChain[ri])); si++; }
                        else { tris.Add((sChain[si], rChain[ri + 1], rChain[ri])); ri++; }
                    }
                    var sum = tris.Aggregate(Vector3.zero, (acc, t) => acc + Vector3.Cross(t.b - t.a, t.d - t.a));
                    float flip = Vector3.Dot(sum, nrm) >= 0f ? 1f : -1f;
                    foreach (var t in tris) AddCapTri(c, t.a, t.b, t.d, 0f, 0f, 0f, Vector3.Cross(t.b - t.a, t.d - t.a) * flip, FaceRole.Bevel);
                }
            }
            TopCap(c, strip ? ring : side, top.Select(t => t.x).ToList(), c.topRingSmooth ?? c.topRing, c.gen.grassBlend);
            Fan(c, c.botPts.Values.OrderBy(t => t.x).Select(t => t.pos).ToList(), Vector3.down, FaceRole.Bottom);
        }
        static Vector2 UVxz(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>
        /// 顶面：外缘环 →（草地过渡带）→ 草地边界环 →（纯草带）→ 内环 →（内部，按白盒顶面折痕切块后各块中心扇）。所有新点竖直投影到白盒真实顶面。
        /// - 草地边界环（blend > 0 时）：“未扰动外缘内缩 dG”的多边形 G 按与顶环相同的参数化取样（X → 所在顶环边与段内比例 → G 上同比例处），
        ///   再叠加对 G 求值的顶边扰动 / 转角平滑水平偏移（topOffsetFor(G, X)：扰动量相同、转角曲线按 G 缩放）。草地边界因此与锯齿外缘平行，
        ///   过渡带宽度处处约为 dG，草地纹理的轮廓随 jag 后的顶面形状变化。G 的边与外缘平行且参数化相同，点链与外缘一样沿边单调、不会折回。
        ///   （不能把外缘点垂直投影到 G 再加外缘的偏移：转角附近多个点投影到同一转角点后再加不同偏移，点链会折回。）
        /// - 内环点链：与外缘相同的参数化在内环第 k 段上取点（相邻边上的点夹到段端），再加上该段与白盒顶面折痕的交点，按段内参数排序去重。
        /// - 内环多边形 I = 未扰动外缘内缩 blend + margin（margin = max(最大内凹深度, 转角平滑最大内缩) + 0.1 m，转角斜接，受内缩上限即内切半径 45% 限制），
        ///   草地边界环基准内缩 = I 的实际内缩 − margin（不超过 blend；顶面太小时过渡带变窄并写日志），保证草地边界环始终在 I 之外；
        ///   I 是凸多边形（顶环为凸多边形时），内部可按白盒顶面三角形裁剪后做中心扇。内环点链加上该段与白盒顶面折痕的交点，按段内参数排序去重。
        /// - 两条带都用拉链式缝合：相邻两条点链沿顶环边方向都单调，按投影参数归并，整段按自然顺序生成后统一判定朝向。
        ///   不用耳切：每段内环只有两个端点时会拉出贯穿整条边的细长三角形，在斜而不共面的顶面上形成大量折痕。
        /// - 内部：内环点链首尾相接成多边形，用白盒顶面的每个三角形（俯视）裁剪，每块落在同一平面上，各自做中心扇；
        ///   内部折痕正好落在白盒折痕上，折痕交点已插入内环点链，与纯草带共享，没有 T 形接缝。
        /// - 草地权重：blend > 0 时外缘 0、草地边界环及以内 1，过渡带线性插值（着色器可再做平方平滑）；blend = 0 时没有过渡带，全为 1。
        /// </summary>
        static void TopCap(RockGenContext c, List<Vector3> ring, List<float> xs, System.Func<float, Vector3> smoothRing, float blend)
        {
            int n = ring.Count;
            if (n < 3) return;
            var band = c.band; int m = band.T.Length;
            blend = Mathf.Max(0f, blend);
            float inward = Mathf.Max(0f, -xs.Min(x => c.topJag(x)));
            float margin = Mathf.Max(inward, c.topCornerInward) + 0.1f;           // 草地边界环相对基准的最大内移 + 余量
            var S = Enumerable.Range(0, m).Select(k => smoothRing(band.TopVertexX(k))).ToArray();
            InsetPolygon(S, float.MaxValue, out _, out float maxD);                // 内缩上限（内切半径的 45%）
            // 内环内缩 = blend + margin，受上限限制；草地边界环内缩 = 内环内缩 − margin（不超过 blend）。
            // 顶面太小时两者都被上限截断会重合，中间的带退化成零面积三角形被跳过，留下开口；所以先定内环，再从内环往外让出 margin
            var I = InsetPolygon(S, Mathf.Min(blend + margin, maxD), out var inwardDir, out float d2);
            for (int k = 0; k < m; k++) I[k] = band.OnTop(I[k]);
            Vector3[] G = null; float dG = 0f;                                    // 未扰动外缘内缩 dG 的多边形（草地边界环的基准）
            if (blend > 0f)
            {
                dG = Mathf.Max(0.05f, Mathf.Min(blend, d2 - margin));
                if (dG < blend - 1e-3f) c.log.Add($"顶面太小，草地过渡带宽度由 {blend:F2} m 缩为 {dG:F2} m");
                G = InsetPolygon(S, dG, out _, out dG);
            }
            // 内缩多边形 G 按与顶环相同的参数化取样（X → 所在顶环边 i 与段内比例 f → G[i]→G[i+1] 同比例处）
            Vector3 Gmap(float x) { band.TopLocate(x, out int i, out float f); return Vector3.Lerp(G[i], G[(i + 1) % m], f); }
            var cornerIdx = CornerIndices(c, xs);
            var innerLoop = new List<Vector3>();
            for (int k = 0; k < m; k++)
            {
                int a = cornerIdx[k], b = cornerIdx[(k + 1) % m];
                Vector3 e0 = I[k], e1 = I[(k + 1) % m], seg = e1 - e0; float len2 = Mathf.Max(1e-12f, seg.sqrMagnitude);
                // 三条点链都按同一参数 f（X → 所在顶环边 k 上的段内比例，落在相邻边上的点夹到本段端点）取样，并以 f 作为拉链缝合的归并键：
                // 外缘（ring）、草地边界环（G）、内环（I）在同一 f 处一一对应。不能按三维投影到顶环边方向归并：
                // 开倒角时侧面顶边沿斜直纹线滑动、顶面倾斜且不共面，投影顺序与对应关系不一致，会产生翻面三角形
                var outer = new List<(Vector3 p, float f)>(); var mid = new List<(Vector3 p, float f)>(); var ts = new List<float> { 0f, 1f };
                for (int i = a; ; i = (i + 1) % n)
                {
                    band.TopLocate(xs[i], out int ie, out float fe);
                    float fk = i == a ? 0f : i == b ? 1f : ie == k ? fe : (ie == (k + 1) % m ? 1f : 0f);
                    outer.Add((ring[i], fk));
                    if (G != null)
                    {
                        // 两端（转角）直接取 G 的转角点：同一 X 从两条边取样结果一致，没有缝隙
                        // 按本段参数 fk 在 G 第 k 段上取点（落在相邻边上的点已夹到段端），点与归并键一致
                        // 转角剪平后链端是切点 A / B 而非转角：按其 X 在 G 上取样（G 也被剪平），否则 G 的尖角会越过外缘的剪平线
                        bool chamferEnd = (i == a || i == b) && Mathf.Abs(Mathf.DeltaAngle(xs[i] / band.P * 360f, band.TopVertexX(i == a ? k : (k + 1) % m) / band.P * 360f)) * band.P / 360f > 1e-3f;
                        var gp = chamferEnd ? Gmap(xs[i]) : Vector3.Lerp(G[k], G[(k + 1) % m], fk);
                        var q = band.OnTop(gp + c.topOffsetFor(Gmap, xs[i]));
                        // 与上一点重合时跳过；但链末（转角）要替换上一点，保证链两端都精确落在转角点上
                        if (mid.Count > 0 && (mid[mid.Count - 1].p - q).sqrMagnitude <= 1e-6f) { if (i == b && mid.Count > 1) mid[mid.Count - 1] = (q, fk); }
                        else mid.Add((q, fk));
                    }
                    ts.Add(fk);
                    if (i == b) break;
                }
                // 内环第 k 段与白盒顶面折痕的交点（俯视）
                foreach (var (ca, cb) in band.TopCreases)
                    if (SegIntersect(e0, e1, ca, cb, out float tt)) ts.Add(tt);
                ts.Sort();
                var inner = new List<(Vector3 p, float f)>(); float segLen = Mathf.Sqrt(len2), lastT = -1f;
                foreach (var t in ts) { if (inner.Count > 0 && (t - lastT) * segLen < 1e-3f) continue; inner.Add((band.OnTop(Vector3.Lerp(e0, e1, t)), t)); lastT = t; }
                // 拉链式缝合（按参数 f 归并）
                if (G != null) { Zip(c, outer, 0f, mid, 1f); Zip(c, mid, 1f, inner, 1f); }
                else Zip(c, outer, 1f, inner, 1f);
                for (int i = 0; i < inner.Count - 1; i++) innerLoop.Add(inner[i].p);   // 末点即下一段的首点
            }
            // 内部：按白盒顶面三角形裁剪，每块星形时中心扇、否则耳切。内环不一定是凸多边形（顶面倾斜不共面、开倒角各边内收量不同时，近乎平直的顶环顶点在俯视下
            // 可能是反角，内环随之有小凹口），中心扇会重叠；也不能把内环凸化（反角处内缩量小时凸化后的内环会越过外缘）
            foreach (var tri in band.TopTris)
            {
                var piece = ClipByTriangleXZ(innerLoop, tri);
                if (piece.Count < 3) continue;
                float area = 0f; for (int i = 0; i < piece.Count; i++) area += Area2(Vector3.zero, piece[i], piece[(i + 1) % piece.Count]);
                if (Mathf.Abs(area) < 1e-6f) continue;
                // 相对块中心是星形（各扇形三角形与整块同向或退化）时做中心扇：裁剪结果可能带沿裁剪线来回的零宽“毛刺”，中心扇把它们当退化三角形跳过即可；
                // 不是星形（内环在反角处有凹口）时才耳切
                var ctr = band.OnTop(piece.Aggregate(Vector3.zero, (acc, v) => acc + v) / piece.Count);
                bool star = true;
                for (int i = 0; i < piece.Count && star; i++) star = Area2(ctr, piece[i], piece[(i + 1) % piece.Count]) * area >= -1e-6f;
                if (star) for (int i = 0; i < piece.Count; i++) AddCapTri(c, ctr, piece[i], piece[(i + 1) % piece.Count], 1f, 1f, 1f, Vector3.up, FaceRole.Top);
                else EarClipXZ(c, piece);
            }
        }

        /// <summary>
        /// 拉链式缝合两条按参数 f 单调的点链 A（草地权重 wa）与 B（wb）：按 f 归并，每步推进下一点 f 较小的一侧；
        /// 整段按自然顺序生成后统一判定一次朝向（朝上），绕序一致。
        /// </summary>
        static void Zip(RockGenContext c, List<(Vector3 p, float f)> A, float wa, List<(Vector3 p, float f)> B, float wb)
        {
            var tris = new List<(Vector3 a, Vector3 b, Vector3 d, float ga, float gb, float gd)>();
            int ai = 0, bi = 0;
            while (ai < A.Count - 1 || bi < B.Count - 1)
            {
                bool advA = bi >= B.Count - 1 || (ai < A.Count - 1 && A[ai + 1].f <= B[bi + 1].f);
                if (advA) { tris.Add((A[ai].p, A[ai + 1].p, B[bi].p, wa, wa, wb)); ai++; }
                else { tris.Add((A[ai].p, B[bi + 1].p, B[bi].p, wa, wb, wb)); bi++; }
            }
            var sum = tris.Aggregate(Vector3.zero, (acc, t) => acc + Vector3.Cross(t.b - t.a, t.d - t.a));
            float flip = sum.y >= 0f ? 1f : -1f;
            foreach (var t in tris) AddCapTri(c, t.a, t.b, t.d, t.ga, t.gb, t.gd, Vector3.Cross(t.b - t.a, t.d - t.a) * flip, FaceRole.Top);
        }

        /// <summary>俯视下线段 p0→p1 与线段 q0→q1 相交（不含端点附近），输出交点在 p0→p1 上的参数 t。</summary>
        static bool SegIntersect(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, out float t)
        {
            t = 0f;
            float rx = p1.x - p0.x, rz = p1.z - p0.z, sx = q1.x - q0.x, sz = q1.z - q0.z;
            float den = rx * sz - rz * sx;
            if (Mathf.Abs(den) < 1e-9f) return false;
            float qx = q0.x - p0.x, qz = q0.z - p0.z;
            t = (qx * sz - qz * sx) / den; float u = (qx * rz - qz * rx) / den;
            // 端点容差用绝对距离 1 mm（与裁剪时“新交点贴近已有顶点即并入”的容差一致），否则两边判定不一致会产生 T 形接缝
            float len = Mathf.Sqrt(rx * rx + rz * rz), tol = 1e-3f / Mathf.Max(1e-6f, len);
            return t > tol && t < 1f - tol && u > -1e-4f && u < 1f + 1e-4f;
        }

        /// <summary>
        /// 俯视下用三角形 tri（凸）裁剪多边形 poly（Sutherland–Hodgman），保留三角形内部（含边界）。新交点竖直投影到 tri 所在平面。
        /// 落在裁剪线上的原有顶点（折痕交点）两侧都会保留，相邻两块共享；输出去掉相邻重复点。
        /// </summary>
        static List<Vector3> ClipByTriangleXZ(List<Vector3> poly, Vector3[] tri)
        {
            float orient = Mathf.Sign(Area2(tri[0], tri[1], tri[2]));
            var res = poly;
            for (int e = 0; e < 3 && res.Count > 0; e++)
            {
                Vector3 a = tri[e], b = tri[(e + 1) % 3];
                float F(Vector3 q) => Area2(a, b, q) * orient / Mathf.Max(1e-6f, new Vector2(b.x - a.x, b.z - a.z).magnitude);   // 到裁剪线的有向距离（内侧为正）
                var next = new List<Vector3>();
                for (int i = 0; i < res.Count; i++)
                {
                    var p0 = res[i]; var p1 = res[(i + 1) % res.Count]; float f0 = F(p0), f1 = F(p1);
                    bool in0 = f0 >= -1e-5f, in1 = f1 >= -1e-5f;
                    if (in0) next.Add(p0);
                    if (in0 != in1 && Mathf.Abs(f0) > 1e-5f && Mathf.Abs(f1) > 1e-5f)
                    {
                        var q = Vector3.Lerp(p0, p1, f0 / (f0 - f1));
                        // 交点离边端点 ≤ 1 mm 时并入该端点（与内环点链“端点 1 mm 内不插折痕交点”的容差一致，避免 T 形接缝）：
                        // 靠近内侧端点 p0 时它已加入，跳过；靠近外侧端点 p1 时直接用 p1
                        if ((q - p0).magnitude <= 1e-3f) continue;
                        if ((q - p1).magnitude <= 1e-3f) { next.Add(p1); continue; }
                        // 交点高度取三角形平面
                        float den = (tri[1].z - tri[2].z) * (tri[0].x - tri[2].x) + (tri[2].x - tri[1].x) * (tri[0].z - tri[2].z);
                        float wa = ((tri[1].z - tri[2].z) * (q.x - tri[2].x) + (tri[2].x - tri[1].x) * (q.z - tri[2].z)) / den;
                        float wb = ((tri[2].z - tri[0].z) * (q.x - tri[2].x) + (tri[0].x - tri[2].x) * (q.z - tri[2].z)) / den;
                        q.y = tri[0].y * wa + tri[1].y * wb + tri[2].y * (1f - wa - wb);
                        next.Add(q);
                    }
                }
                res = next;
            }
            var outp = new List<Vector3>();
            foreach (var q in res) if (outp.Count == 0 || (outp[outp.Count - 1] - q).sqrMagnitude > 1e-8f) outp.Add(q);
            if (outp.Count > 1 && (outp[0] - outp[outp.Count - 1]).sqrMagnitude <= 1e-8f) outp.RemoveAt(outp.Count - 1);
            return outp;
        }

        /// <summary>
        /// 俯视耳切一个简单多边形（顶面草地权重 1，朝上）。依次找“凸角且三角形内（含边界）没有其他顶点”的耳朵切掉。
        /// 共线判定用相对阈值（夹角正弦 &lt; 1e-4；绝对阈值会把浮点误差的共线角误当耳朵，切下的退化三角形被跳过、顶点丢失形成 T 形接缝）；
        /// 内部测试含边界（否则落在对角线上的共线点会被跨过，产生重叠）。共线顶点不删除，随其他耳朵被纳入非退化三角形。
        /// </summary>
        static void EarClipXZ(RockGenContext c, List<Vector3> poly)
        {
            var v = new List<Vector3>(poly);
            float total = 0f; for (int i = 0; i < v.Count; i++) total += Area2(Vector3.zero, v[i], v[(i + 1) % v.Count]);
            float sign = Mathf.Sign(total);
            int guard = v.Count * v.Count + 10;
            while (v.Count > 3 && guard-- > 0)
            {
                bool cut = false;
                for (int i = 0; i < v.Count && !cut; i++)
                {
                    int ia = (i - 1 + v.Count) % v.Count, id = (i + 1) % v.Count;
                    Vector3 a = v[ia], b = v[i], d = v[id];
                    var ab = new Vector2(b.x - a.x, b.z - a.z); var bd = new Vector2(d.x - b.x, d.z - b.z);
                    if (Area2(a, b, d) * sign <= 1e-4f * ab.magnitude * bd.magnitude) continue;   // 凹角或共线角
                    bool blocked = false;
                    for (int j = 0; j < v.Count && !blocked; j++)
                    {
                        if (j == i || j == ia || j == id) continue;
                        var q = v[j];
                        blocked = Area2(a, b, q) * sign >= -1e-9f && Area2(b, d, q) * sign >= -1e-9f && Area2(d, a, q) * sign >= -1e-9f;
                    }
                    if (blocked) continue;
                    AddCapTri(c, a, b, d, 1f, 1f, 1f, Vector3.up, FaceRole.Top); v.RemoveAt(i); cut = true;
                }
                if (!cut) { c.log.Add("顶面内部耳切未完成（多边形可能自交）"); break; }
            }
            if (v.Count == 3) AddCapTri(c, v[0], v[1], v[2], 1f, 1f, 1f, Vector3.up, FaceRole.Top);
        }

        /// <summary>俯视（y 朝上）有向面积的两倍。</summary>
        static float Area2(Vector3 a, Vector3 b, Vector3 d) => (b.x - a.x) * (d.z - a.z) - (b.z - a.z) * (d.x - a.x);

        /// <summary>外缘（按 X 升序）中顶环各顶点（转角）所在的下标：X 与顶环第 k 个顶点的 X 最接近者（周期比较）。转角竖线在单元上插了点，外缘必含转角。</summary>
        static int[] CornerIndices(RockGenContext c, List<float> xs)
        {
            var band = c.band; float P = band.P; int m = band.T.Length, n = xs.Count;
            var idx = new int[m];
            for (int k = 0; k < m; k++)
            {
                float xk = Mathf.Repeat(band.TopVertexX(k), P);
                idx[k] = Enumerable.Range(0, n).OrderBy(i => Mathf.Abs(Mathf.DeltaAngle(xs[i] / P * 360f, xk / P * 360f))).First();
            }
            return idx;
        }

        /// <summary>加一个朝 outward 一侧的封口三角形；面积几乎为 0 时跳过。</summary>
        static void AddCapTri(RockGenContext c, Vector3 a, Vector3 b, Vector3 d, float ga, float gb, float gd, Vector3 outward, FaceRole role)
        {
            if (Vector3.Cross(b - a, d - a).sqrMagnitude < 1e-10f) return;
            c.AddTri(a, b, d, UVxz(a), UVxz(b), UVxz(d), outward, role, ga, gb, gd);
        }

        /// <summary>
        /// 多边形在其 Newell 平面内向内等宽内缩 d：每条边向内平移 d，转角取两侧内缩方向的斜接点（长度限制在 d / 0.3 内）；d 限制在内切半径的 45% 内。
        /// 输出每条边的内缩方向 inward 与实际内缩距离 dOut。
        /// </summary>
        static Vector3[] InsetPolygon(Vector3[] S, float d, out Vector3[] inward, out float dOut)
        {
            int m = S.Length;
            var nT = Vector3.zero; var ctr = Vector3.zero;
            for (int i = 0; i < m; i++) { var a = S[i]; var b = S[(i + 1) % m]; nT += new Vector3((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)); ctr += a; }
            nT = nT.normalized; if (nT.y < 0) nT = -nT; ctr /= m;
            inward = new Vector3[m]; float inradius = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                var a = S[i]; var b = S[(i + 1) % m]; var dir = Vector3.Cross(nT, (b - a).normalized).normalized;
                if (Vector3.Dot(dir, ctr - (a + b) * 0.5f) < 0) dir = -dir;
                inward[i] = dir; inradius = Mathf.Min(inradius, Vector3.Dot(ctr - a, dir));
            }
            d = Mathf.Min(d, Mathf.Max(0f, inradius) * 0.45f); dOut = d;
            var I = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                var mid = (inward[(i - 1 + m) % m] + inward[i]).normalized;
                I[i] = S[i] + mid * (d / Mathf.Max(0.3f, Vector3.Dot(mid, inward[i])));
            }
            return I;
        }
        static void Fan(RockGenContext c, List<Vector3> ring, Vector3 outward, FaceRole role)
        {
            if (ring.Count < 3) return;
            var ctr = ring.Aggregate(Vector3.zero, (s, v) => s + v) / ring.Count;
            for (int i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; c.AddTri(ctr, a, b, UVxz(ctr), UVxz(a), UVxz(b), outward, role); }
        }
    }

    /// <summary>
    /// 网格修改器：焊接。距离小于 0.5 mm 的顶点改为同一份坐标（取先出现的那个）。亚毫米细缝两侧的点合并后，细缝三角形塌缩为零面积（闭合校验跳过）。
    /// 各步骤已尽量复用同一份三维坐标，但不同路径算出的“同一点”仍可能有 1e-6 m 级的浮点差；
    /// 差值恰好跨过焊接网格的取整边界时会被当成两个点，形成假开口，ProBuilder 也不会共享顶点。
    /// </summary>
    public class WeldPositions : IRockStep
    {
        public string Name => "焊接";
        public void Apply(RockGenContext c)
        {
            const float tol = 5e-4f, cell = 1e-3f;      // 与闭合校验的焊接精度（0.5 mm）一致；更近的点是同一点或亚毫米细缝
            var grid = new Dictionary<(int, int, int), List<int>>(); int merged = 0;
            for (int i = 0; i < c.pos.Count; i++)
            {
                var v = c.pos[i]; int gx = Mathf.FloorToInt(v.x / cell), gy = Mathf.FloorToInt(v.y / cell), gz = Mathf.FloorToInt(v.z / cell);
                int found = -1;
                for (int dx = -1; dx <= 1 && found < 0; dx++)
                    for (int dy = -1; dy <= 1 && found < 0; dy++)
                        for (int dz = -1; dz <= 1 && found < 0; dz++)
                            if (grid.TryGetValue((gx + dx, gy + dy, gz + dz), out var list))
                                foreach (var r in list) if ((c.pos[r] - v).sqrMagnitude < tol * tol) { found = r; break; }
                if (found >= 0) { if (c.pos[found] != v) merged++; c.pos[i] = c.pos[found]; continue; }
                var key = (gx, gy, gz);
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(i);
            }
            if (merged > 0) c.log.Add($"焊接合并 {merged} 个近重合顶点");
        }
    }

    /// <summary>校验：焊接后每条边恰好被两个三角形共用（闭合流形），结果写入日志。</summary>
    public class ClosedCheck : IRockStep
    {
        public string Name => "闭合校验";
        public void Apply(RockGenContext c) => c.log.Add(CheckClosed(c.pos));
    }

    /// <summary>出图：在展开带坐标上保存 ID / 节理 / 高度 / 噪声图，并生成以 ID 图为底色的调试材质（侧面子网格用）。</summary>
    public class SaveMapsStep : IRockStep
    {
        public string Name => "出图";
        public void Apply(RockGenContext c)
        {
            c.folder = $"{OutRoot}/{c.gen.gameObject.name}_{c.gen.seed}";
            EnsureFolder(c.folder);
            c.gen.outputFolder = c.folder;
            c.sideMaterial = SaveMaps(c.gen, c.band, c.cells, c.seeds, c.Ymax, c.noise, c.paramTris, c.folder);
            c.log.Add("图保存在 " + c.folder);
        }
    }

    /// <summary>
    /// 场修改器：法线平滑，按 normalMode 改写平滑组场 smoothingGroupOf，由 OutputMesh 写到 ProBuilder 面上，ProBuilder 按平滑组计算顶点法线。
    /// - Flat：全部为 0，每个三角形用自己的面法线（平直着色）。
    /// - Smooth：全部为组 1，所有位置重合的顶点共享相邻面的平均法线（全平滑），包括顶面、倒角面与侧面之间。
    /// - Plates：岩板分区平滑。每块岩板（单元被转角线切开后的一块）内部平滑，岩板之间（节理缝、转角棱线）硬边；
    ///   顶面一组、倒角面一组，与侧面之间硬边；底面不平滑。效果是“一块岩板一个大明暗”，配合分阶光照。
    ///   ProBuilder 按位置共享顶点法线，只要共享任一顶点的两个分区组号相同就会被平滑到一起，所以按“共享顶点”建邻接图，
    ///   贪心着色分配组号（1～30，按度数从大到小），保证相邻分区组号不同。
    /// 移除本修改器即为平直着色。
    /// </summary>
    public class NormalSmoothing : IRockStep
    {
        public string Name => "法线平滑";
        public void Apply(RockGenContext c)
        {
            var mode = c.gen.normalMode;
            if (mode == CliffRockGenerator.NormalMode.Smooth) c.smoothingGroupOf = i => 1;
            if (mode != CliffRockGenerator.NormalMode.Plates) return;
            // 分区编号：侧面 = 岩板编号；顶面 = plateCount；倒角面 = plateCount + 1；底面 = −1（不平滑）
            int topNode = c.plateCount, bevelNode = c.plateCount + 1, nodeCount = c.plateCount + 2;
            int NodeOf(int f) => c.roles[f] == FaceRole.Side ? c.plates[f] : c.roles[f] == FaceRole.Top ? topNode : c.roles[f] == FaceRole.Bevel ? bevelNode : -1;
            // 共享顶点（按位置）的分区互为邻接
            var atPos = new Dictionary<Vector3, HashSet<int>>();
            for (int f = 0; f < c.faces.Count; f++)
            {
                int node = NodeOf(f); if (node < 0) continue;
                foreach (var vi in c.faces[f].indexes) { var key = c.pos[vi]; if (!atPos.TryGetValue(key, out var set)) atPos[key] = set = new HashSet<int>(); set.Add(node); }
            }
            var adj = Enumerable.Range(0, nodeCount).Select(_ => new HashSet<int>()).ToArray();
            foreach (var set in atPos.Values) foreach (var a in set) foreach (var b in set) if (a != b) adj[a].Add(b);
            // 贪心着色（度数大的先着色），组号 1～30（ProBuilder 的可平滑组范围）；没有可用组号的分区记 0（硬边）并写日志
            var group = new int[nodeCount]; int failed = 0;
            foreach (var node in Enumerable.Range(0, nodeCount).OrderByDescending(n => adj[n].Count))
            {
                var used = new HashSet<int>(adj[node].Select(n => group[n]));
                int g = Enumerable.Range(1, 30).FirstOrDefault(x => !used.Contains(x));
                group[node] = g; if (g == 0) failed++;
            }
            c.smoothingGroupOf = f => { int node = NodeOf(f); return node < 0 ? 0 : group[node]; };
            c.log.Add($"岩板分区平滑：{c.plateCount} 块岩板，用 {group.Distinct().Count(x => x > 0)} 个平滑组{(failed > 0 ? $"，{failed} 个分区组号不够改为硬边" : "")}");
        }
    }

    /// <summary>
    /// 网格修改器：顶点色烘焙（供 GenshinRock 类风格化着色器使用，放在焊接之后，读取最终顶点位置与调试 UV）。
    /// - R 曲率：按位置焊接后，顶点法线 n 取相邻面面积加权平均，k = Σ dot(n, p − q) / Σ |p − q|（q 为一环邻点），
    ///   凸（锥顶、顶边、转角棱线）为正，凹（节理缝）为负；R = 0.5 + k × curvatureGain，夹在 [0, 1]。
    /// - G AO：凹处遮蔽，G = 1 − saturate(−k × curvatureGain × 2) × 0.6。底部接地变暗由着色器按高度计算，这里不重复。
    /// - B 岩板色调：每块岩板一个哈希值，0.5 ± 0.4；封口为 0.5。同一位置在不同岩板上可以不同（每个三角形有独立顶点）。
    /// - A 草地遮罩：取封口步骤记录的每顶点草地权重——顶面内环以内 1、顶面外缘 0（环带内线性过渡），倒角、侧面、底面为 0。
    ///   由 RockGen/GenshinCliff 着色器读取，草地只出现在顶面内，不随法线蔓延到侧面。
    /// </summary>
    public class VertexColorBake : IRockStep
    {
        public string Name => "顶点色烘焙";
        public void Apply(RockGenContext c)
        {
            int n = c.pos.Count; float gain = c.gen.curvatureGain;
            // 按位置焊接：位置 → 编号（焊接修改器已保证同一点坐标逐位一致）
            var id = new Dictionary<Vector3, int>(); var vid = new int[n];
            for (int i = 0; i < n; i++) { if (!id.TryGetValue(c.pos[i], out int k)) id[c.pos[i]] = k = id.Count; vid[i] = k; }
            int m = id.Count; var P = new Vector3[m]; foreach (var kv in id) P[kv.Value] = kv.Key;
            var nrm = new Vector3[m]; var nb = Enumerable.Range(0, m).Select(_ => new HashSet<int>()).ToArray();
            foreach (var f in c.faces)
            {
                var t = f.indexes; int a = vid[t[0]], b = vid[t[1]], d = vid[t[2]];
                var cr = Vector3.Cross(P[b] - P[a], P[d] - P[a]);      // 长度 = 2 × 面积，天然按面积加权
                nrm[a] += cr; nrm[b] += cr; nrm[d] += cr;
                nb[a].Add(b); nb[a].Add(d); nb[b].Add(a); nb[b].Add(d); nb[d].Add(a); nb[d].Add(b);
            }
            var curv = new float[m];
            for (int v = 0; v < m; v++)
            {
                var nv = nrm[v].normalized; float num = 0f, den = 0f;
                foreach (var q in nb[v]) { var e = P[v] - P[q]; num += Vector3.Dot(nv, e); den += e.magnitude; }
                curv[v] = den > 1e-6f ? num / den : 0f;
            }
            c.colors = new List<Color>(n);
            for (int i = 0; i < n; i++) c.colors.Add(Color.clear);
            for (int f = 0; f < c.faces.Count; f++)
            {
                var role = c.roles[f]; int plate = c.plates[f];
                float tone = plate >= 0 ? 0.5f + (Hash(plate) - 0.5f) * 0.8f : 0.5f;
                foreach (var i in c.faces[f].indexes)
                {
                    float k = curv[vid[i]];
                    float r = Mathf.Clamp01(0.5f + k * gain), g = 1f - Mathf.Clamp01(-k * gain * 2f) * 0.6f;
                    c.colors[i] = new Color(r, g, tone, c.grass[i]);
                }
            }
        }

        /// <summary>整数 → [0, 1) 的确定性哈希。</summary>
        static float Hash(int x) { uint h = (uint)x * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xFFFFFF) / (float)0x1000000; }
    }

    /// <summary>
    /// 材质修改器：贴图映射，按 textureMode 决定材质与贴图坐标。
    /// - Debug：不改动。侧面用调试材质（ID 图，UV0 = 展开带坐标 (X / 周长, v)，与保存的图一一对应），顶面、倒角、底面用灰色。
    /// - Genshin：两个子网格都用风格化材质 genshinMaterial（RockGen/GenshinCliff 着色器，读顶点色），顶面草地由顶点色 A 通道决定。
    /// - GrassRock：顶面用草地材质，侧面、倒角、底面用岩石材质；UV 改为世界尺寸平铺：
    ///   侧面 = (展开带弧长 X, 高度 y) / rockTileSize，沿侧面一圈连续、无拉伸；顶面 = (x, z) / grassTileSize；
    ///   倒角面、底面按面法线的主轴做盒状投影 / rockTileSize。
    /// 草地 / 岩石材质未配置时使用 Assets/Art/RockGen/Materials 下的默认材质并回填到组件上。
    /// </summary>
    public class TextureMapping : IRockStep
    {
        public string Name => "贴图映射";
        const string DefaultGrass = "Assets/Art/RockGen/Materials/M_RockGen_Grass.mat", DefaultRock = "Assets/Art/RockGen/Materials/M_RockGen_Rock.mat";
        const string DefaultGenshin = "Assets/Art/RockGen/Materials/M_RockGen_Genshin.mat";
        public void Apply(RockGenContext c)
        {
            var gen = c.gen;
            if (gen.textureMode == CliffRockGenerator.TextureMode.Genshin)
            {
                // 风格化着色器不采样贴图，UV0 用来传相对高度：x = 展开带弧长（米），y = 相对高度（侧面 0 底 → 1 顶，顶面 / 倒角 1，底面 0），
                // 着色器据此做高度渐变、顶边亮带与越靠上越明显的风化条纹。顶面草地由顶点色 A 通道决定；两个子网格用同一材质
                float perim = c.band.P;
                for (int f = 0; f < c.faces.Count; f++)
                {
                    var role = c.roles[f];
                    foreach (var i in c.faces[f].indexes)
                        c.uvs[i] = role == FaceRole.Side ? new Vector2(c.uvs[i].x * perim, c.uvs[i].y) : new Vector2(0f, role == FaceRole.Bottom ? 0f : 1f);
                }
                if (gen.genshinMaterial == null) gen.genshinMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultGenshin);
                c.materials = new[] { gen.genshinMaterial, gen.genshinMaterial };
                return;
            }
            if (gen.textureMode != CliffRockGenerator.TextureMode.GrassRock) return;
            if (gen.grassMaterial == null) gen.grassMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultGrass);
            if (gen.rockMaterial == null) gen.rockMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultRock);
            float P = c.band.P, rock = Mathf.Max(0.01f, gen.rockTileSize), grass = Mathf.Max(0.01f, gen.grassTileSize);
            for (int f = 0; f < c.faces.Count; f++)
            {
                var t = c.faces[f].indexes; var role = c.roles[f];
                var n = Vector3.Cross(c.pos[t[1]] - c.pos[t[0]], c.pos[t[2]] - c.pos[t[0]]);
                foreach (var i in t)
                {
                    var v = c.pos[i];
                    if (role == FaceRole.Side) c.uvs[i] = new Vector2(c.uvs[i].x * P, v.y) / rock;   // 侧面调试 UV 的 x = X / 周长（单元内不折回，保证连续）
                    else if (role == FaceRole.Top) c.uvs[i] = new Vector2(v.x, v.z) / grass;
                    else c.uvs[i] = BoxUV(v, n) / rock;
                }
            }
            c.submeshOf = i => c.roles[i] == FaceRole.Top ? 1 : 0;
            c.materials = new[] { gen.rockMaterial, gen.grassMaterial };
        }

        /// <summary>盒状投影：按法线绝对值最大的轴选投影平面。</summary>
        static Vector2 BoxUV(Vector3 v, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return new Vector2(v.x, v.z);
            return ax >= az ? new Vector2(v.z, v.y) : new Vector2(v.x, v.y);
        }
    }

    /// <summary>预览用：不出图，侧面材质复用上次完整生成保存的调试材质（没有则为灰色）。</summary>
    public class ReuseMaterial : IRockStep
    {
        public string Name => "复用材质";
        public void Apply(RockGenContext c)
        {
            if (!string.IsNullOrEmpty(c.gen.outputFolder)) c.sideMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{c.gen.outputFolder}/M_RockGen_Debug.mat");
        }
    }

    /// <summary>
    /// 输出修改器：顶面撒草（放在 OutputMesh 之后）。在顶面封口三角形（角色 Top）上按面积撒草叶，生成岩石的子物体 &lt;名字&gt;_Grass（随岩石一起删除重建）。
    /// - 数量 = 三角形面积 × grassDensity；位置取三角形内均匀随机点。
    /// - 只在纯草地上撒：三个顶点草地权重都为 1 的三角形（内环以内），草地到岩石的过渡带不撒。
    /// - 草叶：与 GenshinTerrain 相同的 5 顶点弯折叶片（高 grassHeight × 0.7～1.3，宽为高度的 12%～20%，随机朝向与弯曲），法线朝上；
    ///   顶点色 RGB = 线性固有色（根部取悬崖材质的草色 _GrassDeep ↔ _GrassLight 按大尺度噪声插值并略压暗，叶尖偏亮黄绿），A = 风动权重（根 0 → 尖 1）。
    ///   材质用 grassBladeMaterial（默认 M_GT_Grass：Stylized/GenshinToon，双面、带风动），不投影。
    /// - 随机数以 seed 为种子，同参数结果一致。计算在世界尺寸下进行，顶点除回物体缩放。
    /// </summary>
    public class TopGrass : IRockStep
    {
        public string Name => "顶面撒草";
        const string DefaultBladeMat = "Assets/Art/GenshinTree/Materials/M_GT_Grass.mat";
        public void Apply(RockGenContext c)
        {
            var gen = c.gen;
            if (!gen.grassScatter || gen.grassDensity <= 0f || gen.output == null) return;
            if (gen.grassBladeMaterial == null) gen.grassBladeMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultBladeMat);
            var rnd = new System.Random(gen.seed * 7919 + 17);
            float R() => (float)rnd.NextDouble();
            // 草色：取悬崖材质的草色（没有时用默认蒙德草色），叶尖偏亮
            var cliffMat = gen.genshinMaterial;
            Color deep = cliffMat != null && cliffMat.HasProperty("_GrassDeep") ? cliffMat.GetColor("_GrassDeep") : new Color(0.33f, 0.56f, 0.22f);
            Color light = cliffMat != null && cliffMat.HasProperty("_GrassLight") ? cliffMat.GetColor("_GrassLight") : new Color(0.70f, 0.80f, 0.30f);
            float scale = cliffMat != null && cliffMat.HasProperty("_GrassScale") ? cliffMat.GetFloat("_GrassScale") : 0.06f;
            var tip = new Color(0.78f, 0.86f, 0.38f);
            var v = new List<Vector3>(); var col = new List<Color>(); var nrm = new List<Vector3>(); var idx = new List<int>();
            var inv = gen.transform.lossyScale; inv = new Vector3(1f / inv.x, 1f / inv.y, 1f / inv.z);
            int blades = 0;
            for (int f = 0; f < c.faces.Count; f++)
            {
                if (c.roles[f] != FaceRole.Top) continue;
                var t = c.faces[f].indexes; Vector3 A = c.pos[t[0]], B = c.pos[t[1]], C = c.pos[t[2]];
                float ga = c.grass[t[0]], gb = c.grass[t[1]], gc = c.grass[t[2]];
                if (Mathf.Min(ga, Mathf.Min(gb, gc)) < 0.999f) continue;                  // 只在纯草地（内环以内）撒，过渡带不撒
                float area = Vector3.Cross(B - A, C - A).magnitude * 0.5f, want = area * gen.grassDensity;
                int count = Mathf.FloorToInt(want) + (R() < want - Mathf.Floor(want) ? 1 : 0);
                for (int k = 0; k < count; k++)
                {
                    float u = R(), w2 = R(); if (u + w2 > 1f) { u = 1f - u; w2 = 1f - w2; }
                    var pos = A + (B - A) * u + (C - A) * w2;
                    float n01 = Mathf.PerlinNoise(pos.x * scale + 31.7f, pos.z * scale + 11.3f);
                    var root = Color.Lerp(deep, light, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, n01))) * 0.88f;
                    RockGenGround.AddBlade(v, col, nrm, idx, pos, root.linear, tip.linear, gen.grassHeight, R, inv);
                    blades++;
                }
            }
            if (blades == 0) return;
            var mesh = new Mesh { name = gen.gameObject.name + "_Grass", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetColors(col); mesh.SetNormals(nrm); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds();
            var go = new GameObject(gen.gameObject.name + "_Grass");
            go.transform.SetParent(gen.output.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = gen.grassBladeMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.log.Add($"顶面撒草 {blades} 株");
        }
    }

    /// <summary>
    /// 输出 ProBuilder 子物体（替换上一次的结果）。子网格取子网格场 submeshOf，材质取 materials。顺序要求：先挂材质（ProBuilder 生成子网格时按渲染器材质数截断 submeshIndex），
    /// 再在 pb.faces 上重新标记手动 UV 与子网格（Create 后不保留），最后写 UV 并重建网格。计算在世界尺寸下进行，这里除回物体缩放。
    /// </summary>
    public class OutputMesh : IRockStep
    {
        public string Name => "输出网格";
        public void Apply(RockGenContext c)
        {
            var gen = c.gen;
            // 删除上一次的结果：output 引用，以及同名的 _Rock 子物体（撤销参数修改会把 output 恢复成已销毁的旧引用，只靠引用会留下重复的岩石）
            if (gen.output != null) Object.DestroyImmediate(gen.output);
            for (int i = gen.transform.childCount - 1; i >= 0; i--) { var ch = gen.transform.GetChild(i); if (ch.name == gen.gameObject.name + "_Rock") Object.DestroyImmediate(ch.gameObject); }
            var inv = gen.transform.lossyScale; inv = new Vector3(1f / inv.x, 1f / inv.y, 1f / inv.z);
            var pb = ProBuilderMesh.Create(c.pos.Select(v => Vector3.Scale(v, inv)), c.faces);
            pb.gameObject.name = gen.gameObject.name + "_Rock";
            pb.transform.SetParent(gen.transform, false);
            var grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Whitebox/M_Whitebox_Grey.mat");
            pb.GetComponent<MeshRenderer>().sharedMaterials = c.materials ?? new[] { c.sideMaterial != null ? c.sideMaterial : grey, grey };
            var pbFaces = pb.faces;
            for (int i = 0; i < pbFaces.Count; i++) { pbFaces[i].manualUV = true; pbFaces[i].submeshIndex = c.submeshOf(i); pbFaces[i].smoothingGroup = c.smoothingGroupOf(i); }
            pb.textures = c.uvs;
            if (c.colors != null) pb.colors = c.colors;
            pb.ToMesh(); pb.Refresh();
            gen.output = pb.gameObject;
            var srcRenderer = gen.GetComponent<MeshRenderer>(); if (srcRenderer) srcRenderer.enabled = !gen.hideSource;
        }
    }

    // ---------------- 白盒解析 ----------------
    /// <summary>
    /// 识别顶环与底环：
    /// - 候选循环边：每个面的边界环，以及每个面与其近似共面（法线夹角 &lt; 20°）的相连面合并后的外边界环（顶面被细分成多块时也能整体识别）。
    /// - 顶环 = 顶点 y 值之和最大的候选；底环 = y 值之和最小的候选，并列时取顶点多者。两环不得共用顶点。
    /// - 锚点 = 连接底环顶点与顶环顶点的边（竖边）。侧面可以是任意多边形，两环顶点数可以不同。
    /// 两环上的顶点在生成时保持不动。
    /// </summary>
    static Band ExtractBand(ProBuilderMesh pb, out string err)
    {
        err = null;
        // 按世界尺寸计算：顶点乘以物体缩放（输出时再除回去），参数的米数与物体缩放无关
        var scale = pb.transform.lossyScale;
        var p = pb.positions.Select(v => Vector3.Scale(v, scale)).ToList();
        var fs = pb.faces.ToList();
        // 位置键：按毫米取整坐标查表分配编号（不能用 XOR 哈希：对称白盒如 ProBuilder 圆柱的顶面点与底面点会碰撞成同一个键）
        var keyOf = new Dictionary<Vector3Int, long>();
        long PK(Vector3 v)
        {
            var q = new Vector3Int(Mathf.RoundToInt(v.x * 1000), Mathf.RoundToInt(v.y * 1000), Mathf.RoundToInt(v.z * 1000));
            if (!keyOf.TryGetValue(q, out long k)) { k = keyOf.Count; keyOf[q] = k; }
            return k;
        }
        Vector3 FaceN(Face f) { var t = f.indexes; var n = Vector3.zero; for (int i = 0; i < t.Count; i += 3) n += Vector3.Cross(p[t[i + 1]] - p[t[i]], p[t[i + 2]] - p[t[i]]); return n.normalized; }
        var pt = new Dictionary<long, Vector3>();
        foreach (var v in p) pt[PK(v)] = v;
        // 每个面的边界边（按位置键）
        var faceEdges = fs.Select(f => f.edges.Select(e => (PK(p[e.a]), PK(p[e.b]))).ToList()).ToList();
        // 把一组无向边串成一个闭合环；不是单一闭环时返回 null
        List<long> Chain(List<(long a, long b)> edges)
        {
            if (edges.Count < 3) return null;
            var adj = new Dictionary<long, List<long>>();
            foreach (var (a, b) in edges) { if (!adj.ContainsKey(a)) adj[a] = new List<long>(); if (!adj.ContainsKey(b)) adj[b] = new List<long>(); adj[a].Add(b); adj[b].Add(a); }
            if (adj.Values.Any(l => l.Count != 2)) return null;
            var loop = new List<long> { edges[0].a }; long prev = edges[0].a, cur = edges[0].b;
            while (cur != edges[0].a) { loop.Add(cur); var nx = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0]; prev = cur; cur = nx; if (loop.Count > edges.Count) return null; }
            return loop.Count == edges.Count ? loop : null;
        }
        // 候选循环边
        var candidates = new List<List<long>>(); var candFaces = new List<HashSet<int>>();   // 候选环及其所围的面（顶环所围的面即白盒顶面）
        for (int i = 0; i < fs.Count; i++)
        {
            var single = Chain(faceEdges[i]); if (single != null) { candidates.Add(single); candFaces.Add(new HashSet<int> { i }); }
            // 与面 i 近似共面且相连的面合并，取外边界（只出现一次的边）
            var n0 = FaceN(fs[i]); var group = new HashSet<int> { i }; var queue = new Queue<int>(); queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int f = queue.Dequeue(); var keys = new HashSet<long>(faceEdges[f].SelectMany(e => new[] { e.Item1, e.Item2 }));
                for (int g = 0; g < fs.Count; g++)
                {
                    if (group.Contains(g) || Vector3.Angle(FaceN(fs[g]), n0) > 20f) continue;
                    if (faceEdges[g].Count(e => keys.Contains(e.Item1) && keys.Contains(e.Item2)) == 0) continue;
                    group.Add(g); queue.Enqueue(g);
                }
            }
            if (group.Count > 1)
            {
                var cnt = new Dictionary<(long, long), int>();
                foreach (var g in group) foreach (var (a, b) in faceEdges[g]) { var key = a < b ? (a, b) : (b, a); cnt[key] = cnt.TryGetValue(key, out var c) ? c + 1 : 1; }
                var outer = Chain(cnt.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList()); if (outer != null) { candidates.Add(outer); candFaces.Add(group); }
            }
        }
        float SumY(List<long> l) => l.Sum(k => pt[k].y);
        int topIdx = Enumerable.Range(0, candidates.Count).OrderByDescending(i => SumY(candidates[i])).ThenByDescending(i => candidates[i].Count).First();
        var topK = candidates[topIdx];
        var botK = candidates.OrderBy(SumY).ThenByDescending(l => l.Count).First();
        if (topK.Intersect(botK).Any()) { err = "顶环与底环共用顶点，无法确定侧面"; return null; }
        // 统一为俯视逆时针
        Vector3[] Ccw(List<long> keys)
        {
            var a = keys.Select(k => pt[k]).ToArray(); float s = 0;
            for (int i = 0; i < a.Length; i++) { var u = a[i]; var w = a[(i + 1) % a.Length]; s += u.x * w.z - w.x * u.z; }
            if (s < 0) System.Array.Reverse(a); return a;
        }
        var B = Ccw(botK); var T = Ccw(topK);
        // 竖边锚点：网格中任一面的边，一端在底环、一端在顶环
        var bIdx = new Dictionary<long, int>(); for (int i = 0; i < B.Length; i++) bIdx[PK(B[i])] = i;
        var tIdx = new Dictionary<long, int>(); for (int i = 0; i < T.Length; i++) tIdx[PK(T[i])] = i;
        var anchors = new HashSet<(int, int)>();
        foreach (var fe in faceEdges) foreach (var (a, b) in fe)
        {
            if (bIdx.TryGetValue(a, out int ib) && tIdx.TryGetValue(b, out int it)) anchors.Add((ib, it));
            else if (bIdx.TryGetValue(b, out ib) && tIdx.TryGetValue(a, out it)) anchors.Add((ib, it));
        }
        var list = anchors.OrderBy(x => x.Item1).ToList();
        if (list.Count == 0)
        {
            // 没有竖边：取离底环起点最近的顶环顶点作为唯一锚点
            int j = Enumerable.Range(0, T.Length).OrderBy(i => (T[i] - B[0]).sqrMagnitude).First();
            list.Add((0, j));
        }
        // 两环都从第 0 个锚点开始编号；只保留在顶环上也按顺序递增的锚点，避免交叉
        int sb = list[0].Item1, st = list[0].Item2;
        B = B.Skip(sb).Concat(B.Take(sb)).ToArray(); T = T.Skip(st).Concat(T.Take(st)).ToArray();
        var kept = new List<(int ib, int it)>(); int lastT = -1;
        foreach (var (ib, it) in list.Select(x => ((x.Item1 - sb + B.Length) % B.Length, (x.Item2 - st + T.Length) % T.Length)).OrderBy(x => x.Item1))
            if (it > lastT || kept.Count == 0) { kept.Add((ib, it)); lastT = it; }
        var band = new Band(B, T, kept);
        // 白盒真实顶面：顶环所围各面的三角形；被两个三角形共用的边是内部边（顶面不共面时即折痕）
        var edgeUse = new Dictionary<(long, long), (Vector3 a, Vector3 b, int n)>();
        foreach (var fi in candFaces[topIdx])
        {
            var t = fs[fi].indexes;
            for (int i = 0; i < t.Count; i += 3)
            {
                var tri = new[] { p[t[i]], p[t[i + 1]], p[t[i + 2]] };
                band.TopTris.Add(tri);
                for (int e = 0; e < 3; e++)
                {
                    long ka = PK(tri[e]), kb = PK(tri[(e + 1) % 3]); var key = ka < kb ? (ka, kb) : (kb, ka);
                    edgeUse[key] = edgeUse.TryGetValue(key, out var u) ? (u.a, u.b, u.n + 1) : (tri[e], tri[(e + 1) % 3], 1);
                }
            }
        }
        foreach (var u in edgeUse.Values) if (u.n == 2) band.TopCreases.Add((u.a, u.b));
        return band;
    }

    // ---------------- 撒点与沃罗诺伊 ----------------
    /// <summary>Bridson 泊松盘采样：X 方向周期为 P，Y ∈ [0, Ymax]，最小间距 r。</summary>
    static List<Vector2> PoissonPeriodic(float P, float Ymax, float r, System.Random rng)
    {
        var pts = new List<Vector2>(); var active = new List<int>();
        float Rand() => (float)rng.NextDouble();
        bool Far(Vector2 c)
        {
            foreach (var q in pts) { float dx = Mathf.Abs(c.x - q.x); dx = Mathf.Min(dx, P - dx); float dy = c.y - q.y; if (dx * dx + dy * dy < r * r) return false; }
            return true;
        }
        var first = new Vector2(Rand() * P, Rand() * Ymax); pts.Add(first); active.Add(0);
        while (active.Count > 0)
        {
            int ai = (int)(Rand() * active.Count) % active.Count; var b = pts[active[ai]]; bool found = false;
            for (int t = 0; t < 30; t++)
            {
                float ang = Rand() * Mathf.PI * 2f, rad = r * (1f + Rand());
                var c = new Vector2(Mathf.Repeat(b.x + Mathf.Cos(ang) * rad, P), b.y + Mathf.Sin(ang) * rad);
                if (c.y < 0 || c.y > Ymax || !Far(c)) continue;
                pts.Add(c); active.Add(pts.Count - 1); found = true; break;
            }
            if (!found) active.RemoveAt(ai);
        }
        return pts;
    }

    /// <summary>Sutherland–Hodgman：保留离 s 比离 q 更近的一侧（两点的中垂线半平面）。</summary>
    static List<Vector2> ClipHalfPlane(List<Vector2> poly, Vector2 s, Vector2 q)
    {
        var m = (s + q) * 0.5f; var d = q - s; var res = new List<Vector2>();
        float F(Vector2 p) => Vector2.Dot(p - m, d);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count]; float fa = F(a), fb = F(b);
            if (fa <= 0) res.Add(a);
            if ((fa < 0 && fb > 0) || (fa > 0 && fb < 0)) res.Add(Vector2.Lerp(a, b, fa / (fa - fb)));
        }
        return res;
    }

    /// <summary>在多边形边与竖线 X = c 的交点处插入顶点（侧面转角）。</summary>
    static List<Vector2> InsertVerticalCuts(List<Vector2> poly, List<float> xs)
    {
        var res = new List<Vector2>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count]; res.Add(a);
            var cuts = new List<float>();
            foreach (var c in xs) if ((a.x - c) * (b.x - c) < 0) cuts.Add((c - a.x) / (b.x - a.x));
            cuts.Sort();
            foreach (var t in cuts) res.Add(Vector2.Lerp(a, b, t));
        }
        return res;
    }

    /// <summary>用竖线 X = c 切多边形：keepLeft 为真时保留 X ≤ c 的部分，否则保留 X ≥ c。交点的 X 精确取 c。</summary>
    static List<Vector2> ClipAxis(List<Vector2> poly, float c, bool keepLeft)
    {
        var res = new List<Vector2>();
        float F(Vector2 p) => keepLeft ? p.x - c : c - p.x;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count]; float fa = F(a), fb = F(b);
            if (fa <= 0) res.Add(a);
            if ((fa < 0 && fb > 0) || (fa > 0 && fb < 0)) { var p = Vector2.Lerp(a, b, fa / (fa - fb)); p.x = c; res.Add(p); }
        }
        return res;
    }

    /// <summary>把位于竖线上的点 q 插入多边形中同样位于该竖线、且跨过 q 的那条边。</summary>
    static List<Vector2> InsertOnLine(List<Vector2> poly, Vector2 q)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            if (Mathf.Abs(a.x - q.x) < 1e-4f && Mathf.Abs(b.x - q.x) < 1e-4f && (q.y - a.y) * (q.y - b.y) < 0)
            {
                var res = new List<Vector2>(poly); res.Insert(i + 1, q); return res;
            }
        }
        return poly;
    }

    static float SignedArea(List<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; } return a * 0.5f; }

    static long Key(Vector2 p, float P)
    {
        float x = Mathf.Repeat(p.x, P); if (P - x < 1e-4f) x = 0;
        return ((long)Mathf.RoundToInt(x * 10000) << 32) ^ (long)Mathf.RoundToInt(p.y * 10000);
    }

    // ---------------- 封口与校验 ----------------
    /// <summary>按位置焊接后统计每条边被几个三角形共用：全部为 2 即闭合流形。</summary>
    static string CheckClosed(List<Vector3> pos)
    {
        // 位置键：按 0.5 mm 取整坐标查表分配编号（XOR 哈希在对称形体上会碰撞）
        var keyOf = new Dictionary<Vector3Int, long>();
        long K(Vector3 v)
        {
            var q = new Vector3Int(Mathf.RoundToInt(v.x * 2000), Mathf.RoundToInt(v.y * 2000), Mathf.RoundToInt(v.z * 2000));
            if (!keyOf.TryGetValue(q, out long k)) { k = keyOf.Count; keyOf[q] = k; }
            return k;
        }
        var count = new Dictionary<(long, long), int>();
        for (int i = 0; i < pos.Count; i += 3)
        {
            long k0 = K(pos[i]), k1 = K(pos[i + 1]), k2 = K(pos[i + 2]);
            if (k0 == k1 || k1 == k2 || k2 == k0) continue;      // 焊接后两顶点重合的零面积三角形（亚毫米细缝塌缩），不参与拓扑、也不渲染
            for (int e = 0; e < 3; e++)
            {
                long a = K(pos[i + e]), b = K(pos[i + (e + 1) % 3]); var key = a < b ? (a, b) : (b, a);
                count[key] = count.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }
        int open = count.Values.Count(c => c == 1), nonManifold = count.Values.Count(c => c > 2);
        return $"闭合校验：开口边 {open}，非流形边 {nonManifold}";
    }

    // ---------------- 保存图 ----------------
    /// <summary>
    /// 在展开带坐标上保存四张图（x = X / P，y = v）：
    /// ID（单元随机色 + 白色散点）、Joint（离单元边界的距离，越暗越靠近节理）、Height（网格实际外凸/内凹量，PNG 与 EXR）、Noise（所用噪声）。
    /// 返回一个以 ID 图为底色的调试材质，网格 UV0 与图对齐。
    /// </summary>
    static Material SaveMaps(CliffRockGenerator gen, Band band, List<Cell> cells, List<Vector2> seeds, float Ymax, PeriodicNoise noise, List<(Vector2 a, Vector2 b, Vector2 c, float ha, float hb, float hc)> paramTris, string folder)
    {
        int W = Mathf.Max(64, gen.mapWidth), Hp = Mathf.Clamp(Mathf.RoundToInt(W * band.H / band.P), 32, 2048);
        var id = new Color[W * Hp]; var joint = new Color[W * Hp]; var nz = new Color[W * Hp];
        var height = new float[W * Hp]; for (int i = 0; i < height.Length; i++) height[i] = float.NaN;
        var rnd = new System.Random(gen.seed + 7);
        var cellColor = cells.Select(_ => Color.HSVToRGB((float)rnd.NextDouble(), 0.45f, 0.85f)).ToList();
        // ID、Joint、Noise：逐像素找最近与次近散点（周期 X）
        for (int y = 0; y < Hp; y++)
            for (int x = 0; x < W; x++)
            {
                var pp = new Vector2((x + 0.5f) / W * band.P, (y + 0.5f) / Hp * Ymax);
                float d1 = float.MaxValue, d2 = float.MaxValue; int best = 0;
                for (int i = 0; i < cells.Count; i++)
                {
                    float dx = Mathf.Abs(pp.x - cells[i].seed.x); dx = Mathf.Min(dx, band.P - dx); float dy = pp.y - cells[i].seed.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; best = i; } else if (d < d2) d2 = d;
                }
                int o = y * W + x;
                id[o] = d1 < gen.spacing * 0.06f ? Color.white : cellColor[best];
                float j = Mathf.Clamp01((d2 - d1) * 0.5f / (gen.spacing * 0.5f));
                joint[o] = new Color(j, j, j, 1);
                float n = noise.Sample(pp.x / band.P, pp.y / Ymax); nz[o] = new Color(n, n, n, 1);
            }
        // Height：直接光栅化网格侧面的每个三角形（展开带坐标 + 顶点高度），与网格完全一致：外凸为正、内凹为负
        foreach (var t in paramTris) RasterTri(t.a, t.b, t.c, t.ha, t.hb, t.hc, band.P, Ymax, W, Hp, height);
        float hmin = gen.jointDepth, hmax = Mathf.Max(1e-4f, cells.Max(c => c.bulge));
        var hcol = new Color[W * Hp]; var hf = new Color[W * Hp];
        for (int i = 0; i < height.Length; i++)
        {
            float h = float.IsNaN(height[i]) ? 0 : height[i];
            float g = Mathf.InverseLerp(-hmin, hmax, h);
            hcol[i] = new Color(g, g, g, 1); hf[i] = new Color(h, h, h, 1);
        }
        string Save(string name, Color[] px, bool exr = false)
        {
            string path = $"{folder}/{name}";
            var t = new Texture2D(W, Hp, exr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, !exr);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, exr ? t.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat) : t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; ti.sRGBTexture = name.Contains("_ID"); ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false;
            ti.SaveAndReimport();
            return path;
        }
        string idPath = Save("T_RockGen_ID.png", id);
        Save("T_RockGen_Joint.png", joint);
        Save("T_RockGen_Height.png", hcol);
        Save("T_RockGen_Height.exr", hf, true);
        Save("T_RockGen_Noise.png", nz);
        // 调试材质：URP Lit，底色为 ID 图（侧面显示单元划分，顶底面为 UV 外区域的颜色）
        string matPath = $"{folder}/M_RockGen_Debug.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(idPath)); mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        File.WriteAllText($"{folder}/README.txt",
            "悬崖岩块生成图（世界尺寸，已计入物体缩放；展开带坐标：x = 沿侧面周长，y = 侧面高度比例 0 底 → 1 顶；网格 UV0 与之对应）\n" +
            "T_RockGen_ID.png     单元随机色，白点为散点（锥顶）\n" +
            "T_RockGen_Joint.png  离单元边界的距离，越暗越靠近节理缝\n" +
            $"T_RockGen_Height.png 网格实际外凸/内凹量，灰度映射 [-{hmin:F3}, {hmax:F3}] m\n" +
            "T_RockGen_Height.exr 同上，32 位浮点，单位米\n" +
            "T_RockGen_Noise.png  调制凸出量与节理深度的噪声\n" +
            $"参数：seed={gen.seed} spacing={gen.spacing} verticalScale={gen.verticalScale} verticalDensity={gen.verticalDensity} bulge={gen.bulge} bulgeVariation={gen.bulgeVariation} jointDepth={gen.jointDepth} noiseScale={gen.noiseScale}\n" +
            $"展开带：周长 {band.P:F3} m，平均高 {band.H:F3} m，散点 {seeds.Count}\n");
        AssetDatabase.ImportAsset($"{folder}/README.txt");
        return mat;
    }

    /// <summary>在展开带上光栅化一个三角形（压缩坐标），按重心插值写入高度；X 越界时按周期平移重画。</summary>
    static void RasterTri(Vector2 a, Vector2 b, Vector2 c, float ha, float hb, float hc, float P, float Ymax, int W, int Hp, float[] buf)
    {
        for (int shift = -1; shift <= 1; shift++)
        {
            Vector2 A = new Vector2((a.x + shift * P) / P * W, a.y / Ymax * Hp), B = new Vector2((b.x + shift * P) / P * W, b.y / Ymax * Hp), C = new Vector2((c.x + shift * P) / P * W, c.y / Ymax * Hp);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), y1 = Mathf.Min(Hp - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
            if (x0 > x1 || y0 > y1) continue;
            float den = (B.y - C.y) * (A.x - C.x) + (C.x - B.x) * (A.y - C.y);
            if (Mathf.Abs(den) < 1e-9f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w1 = ((B.y - C.y) * (px - C.x) + (C.x - B.x) * (py - C.y)) / den;
                    float w2 = ((C.y - A.y) * (px - C.x) + (A.x - C.x) * (py - C.y)) / den;
                    float w3 = 1 - w1 - w2;
                    if (w1 < -1e-4f || w2 < -1e-4f || w3 < -1e-4f) continue;
                    buf[y * W + x] = w1 * ha + w2 * hb + w3 * hc;
                }
        }
    }

    // ---------------- 噪声 ----------------
    /// <summary>X 方向周期的值噪声分形（输入 u ∈ [0,1) 沿周长，v ∈ [0,1] 沿高度），输出约 [0,1]。</summary>
    internal class PeriodicNoise
    {
        readonly int seed, nx, ny;
        public PeriodicNoise(int seed, int nx, int ny) { this.seed = seed; this.nx = nx; this.ny = ny; }
        float Hash(int x, int y, int o)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + (seed + o * 101) * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
        public float Sample(float u, float v)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < 3; o++)
            {
                int fx = nx << o, fy = (ny << o) + 1;
                float gx = Mathf.Repeat(u, 1f) * fx, gy = Mathf.Clamp01(v) * (fy - 1);
                int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
                float tx = gx - ix, ty = gy - iy; tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                float a = Hash(ix % fx, iy, o), b = Hash((ix + 1) % fx, iy, o), c = Hash(ix % fx, iy + 1, o), d = Hash((ix + 1) % fx, iy + 1, o);
                sum += amp * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty); norm += amp; amp *= 0.5f;
            }
            return sum / norm;
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

/// <summary>CliffRockGenerator 的 Inspector：参数 + “生成岩石”按钮。</summary>
[CustomEditor(typeof(CliffRockGenerator))]
public class CliffRockGeneratorEditor : Editor
{
    void OnEnable() => Undo.undoRedoPerformed += SchedulePreview;
    void OnDisable() => Undo.undoRedoPerformed -= SchedulePreview;

    /// <summary>
    /// 实时预览：已生成过岩石（有 output 引用或同名 _Rock 子物体）且开启 livePreview 时，延迟到本帧 GUI 结束后用预览流水线重建。
    /// 参数改动（拖动滑条时每次变化都会触发）与撤销 / 重做后都会调用。预览不出图，单次几毫秒到几十毫秒。
    /// </summary>
    void SchedulePreview()
    {
        foreach (var t in targets)
        {
            var gen = (CliffRockGenerator)t;
            if (gen == null || !gen.livePreview) continue;
            if (gen.output == null && gen.transform.Find(gen.gameObject.name + "_Rock") == null) continue;
            EditorApplication.delayCall += () => { if (gen != null) CliffRockBuilder.Preview(gen); };
        }
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck()) SchedulePreview();
        EditorGUILayout.Space();
        if (GUILayout.Button("生成岩石", GUILayout.Height(28)))
            foreach (var t in targets) Debug.Log("[RockGen] " + CliffRockBuilder.Generate((CliffRockGenerator)t));
    }
}
