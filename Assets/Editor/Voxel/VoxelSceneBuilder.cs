#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prometheus.VoxelEditor
{
    /// <summary>
    /// 数据驱动的体素场景构建器：场景内容全部写在 JSON 里，本类只负责把 JSON 翻译成 Cube。
    ///
    /// JSON 顶层字段：
    ///   name        场景名（用于根节点命名）
    ///   voxelSize   单格世界尺寸，默认 0.5
    ///   palette     颜色表，语义键 -> "#RRGGBB"
    ///   modules     可复用建筑模板，名字 -> { size:[sx,sy,sz], ops:[...] }
    ///   build       有序元素数组，逐个处理、后写覆盖先写：
    ///                 { type:"part",     name, combine?, ops:[...] }            直接在世界坐标构建
    ///                 { type:"instance", module, name?, at:[x,y,z], rot?, mirror?, swap?, combine? }  实例化模板
    ///
    /// op 指令（写入以格为单位的体素）：
    ///   voxel   { at,   color }
    ///   box     { min, size, color, mode:"solid|shell|frameY|top|bottom", carve? }
    ///   roof    { min, size, color, ridge?, style:"gable|hip", axis:"x|z"(gable), solid? }
    ///   ramp    { min, size, color, axis:"x|z" }                      台阶楔形，沿 axis 正方向升高
    ///   line    { from, to, color }                                   3D 直线
    ///   scatter { min, size, colors:[...], chance, seed }             在 min.y 层按概率随机撒点
    ///
    /// carve:true 的 box 在“当前元素内”挖空（用于门窗洞）；不影响已写入的其它元素。
    /// part 若 combine:true，则该组按颜色合并为整块 Mesh（省 GameObject / draw call）；instance 默认逐 Cube 以便微调。
    /// 菜单：Prometheus/Voxel/Build Scene From JSON…（选文件）、Prometheus/Voxel/Rebuild Last、Prometheus/Voxel/Clear Built Scenes。
    /// </summary>
    public static class VoxelSceneBuilder
    {
        private const string RootPrefix = "VoxelScene:";
        private const string MaterialDir = "Assets/EditorRes/Voxel/Materials";
        private const string LastPathKey = "Prometheus.Voxel.LastJsonPath";
        private const string SceneDir = "Assets/EditorRes/Voxel/Scenes";

        // ---- 运行期状态 ----
        private static Dictionary<string, Color> _palette;
        private static Dictionary<string, JObject> _modules;
        private static Dictionary<string, Material> _matCache;
        private static readonly HashSet<string> _missingWarned = new HashSet<string>();
        private static GameObject _proto;
        private static float _vs;
        private static bool _combineDefault;

        private struct Cell { public string Color; public string Group; public bool Combine; }

        // ================= 菜单 =================

        [MenuItem("Prometheus/Voxel/Build Scene From JSON…")]
        private static void BuildPicked()
        {
            string start = Directory.Exists(SceneDir) ? SceneDir : "Assets";
            string path = EditorUtility.OpenFilePanel("选择体素场景 JSON", start, "json");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(LastPathKey, path);
            Build(path);
        }

        [MenuItem("Prometheus/Voxel/Rebuild Last")]
        private static void RebuildLast()
        {
            string path = EditorPrefs.GetString(LastPathKey, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Debug.LogWarning("[Voxel] 没有可重建的上次 JSON 路径。"); return; }
            Build(path);
        }

        [MenuItem("Prometheus/Voxel/Clear Built Scenes")]
        private static void ClearBuilt()
        {
            int n = 0;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.transform.parent == null && go.name.StartsWith(RootPrefix)) { Object.DestroyImmediate(go); n++; }
            Debug.Log($"[Voxel] 已清除 {n} 个已构建场景根。");
        }

        // ================= 主流程 =================

        /// <summary>读取 JSON → 解析调色板/模板 → 逐元素累积体素 → 生成 Cube / 合并 Mesh。</summary>
        public static void Build(string jsonPath)
        {
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(jsonPath)); }
            catch (System.Exception e) { Debug.LogError($"[Voxel] JSON 解析失败：{e.Message}"); return; }

            string sceneName = (string)root["name"] ?? Path.GetFileNameWithoutExtension(jsonPath);
            _vs = root["voxelSize"] != null ? (float)root["voxelSize"] : 0.5f;
            _combineDefault = root["combineDefault"] != null && (bool)root["combineDefault"]; // 默认是否合并为整块 Mesh，元素可用 combine 覆盖

            _palette = new Dictionary<string, Color>();
            foreach (JProperty p in ((JObject)root["palette"] ?? new JObject()).Properties())
                _palette[p.Name] = ColorUtility.TryParseHtmlString((string)p.Value, out Color c) ? c : Color.magenta;

            _modules = new Dictionary<string, JObject>();
            foreach (JProperty p in ((JObject)root["modules"] ?? new JObject()).Properties())
                _modules[p.Name] = (JObject)p.Value;

            _matCache = new Dictionary<string, Material>();
            _missingWarned.Clear();
            EnsureMaterialDir();

            // 1) 把所有元素累积进一张主体素表（后写覆盖先写）
            var master = new Dictionary<Vector3Int, Cell>();
            foreach (JToken elemTok in (JArray)root["build"] ?? new JArray())
            {
                JObject elem = (JObject)elemTok;
                string type = (string)elem["type"] ?? "part";
                if (type == "instance") AccumulateInstance(elem, master);
                else AccumulatePart(elem, master);
            }
            if (master.Count == 0) { Debug.LogWarning("[Voxel] 场景为空。"); return; }

            // 2) 清旧根、建新根，按 XZ 包围盒中心把场景摆到原点附近
            string rootName = RootPrefix + sceneName;
            GameObject old = GameObject.Find(rootName);
            if (old != null) Object.DestroyImmediate(old);

            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (Vector3Int k in master.Keys)
            {
                if (k.x < minX) minX = k.x; if (k.x > maxX) maxX = k.x;
                if (k.z < minZ) minZ = k.z; if (k.z > maxZ) maxZ = k.z;
            }
            Transform sceneRoot = new GameObject(rootName).transform;
            sceneRoot.position = new Vector3(-(minX + maxX) * 0.5f * _vs, 0f, -(minZ + maxZ) * 0.5f * _vs);

            // 3) 生成
            _proto = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(_proto.GetComponent<Collider>());
            _proto.transform.localScale = Vector3.one * _vs;
            _proto.hideFlags = HideFlags.HideAndDontSave;
            _proto.SetActive(false);

            // 全局占用集：合并 Mesh 时用它做面剔除（贴到实体的面不生成三角形）
            var occupied = new HashSet<Vector3Int>(master.Keys);

            // 按组分桶
            var groups = new Dictionary<string, (bool combine, Dictionary<string, List<Vector3Int>> byColor)>();
            foreach (KeyValuePair<Vector3Int, Cell> kv in master)
            {
                if (!groups.TryGetValue(kv.Value.Group, out var g))
                {
                    g = (kv.Value.Combine, new Dictionary<string, List<Vector3Int>>());
                    groups[kv.Value.Group] = g;
                }
                if (!g.byColor.TryGetValue(kv.Value.Color, out List<Vector3Int> list))
                    g.byColor[kv.Value.Color] = list = new List<Vector3Int>();
                list.Add(kv.Key);
            }

            int cubeCount = 0, meshCount = 0;
            foreach (KeyValuePair<string, (bool combine, Dictionary<string, List<Vector3Int>> byColor)> g in groups)
            {
                Transform groupRoot = new GameObject(g.Key).transform;
                groupRoot.SetParent(sceneRoot, false);

                foreach (KeyValuePair<string, List<Vector3Int>> cc in g.Value.byColor)
                {
                    if (g.Value.combine)
                    {
                        Mesh mesh = BuildCulledMesh(cc.Value, occupied, $"{g.Key}_{cc.Key}");
                        GameObject mo = new GameObject($"{g.Key}_{cc.Key}");
                        mo.transform.SetParent(groupRoot, false);
                        mo.AddComponent<MeshFilter>().sharedMesh = mesh;
                        mo.AddComponent<MeshRenderer>().sharedMaterial = Mat(cc.Key);
                        meshCount++;
                    }
                    else
                    {
                        Material mat = Mat(cc.Key);
                        foreach (Vector3Int cell in cc.Value)
                        {
                            GameObject cube = Object.Instantiate(_proto, groupRoot, false);
                            cube.hideFlags = HideFlags.None;
                            cube.SetActive(true);
                            cube.name = cc.Key;
                            cube.transform.localPosition = (Vector3)cell * _vs;
                            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
                            cubeCount++;
                        }
                    }
                }
            }

            Object.DestroyImmediate(_proto);
            _proto = null;
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(sceneRoot.gameObject.scene);
            Selection.activeObject = sceneRoot.gameObject;
            Debug.Log($"[Voxel] “{sceneName}” 构建完成：体素 {master.Count}，独立 Cube {cubeCount}，合并 Mesh {meshCount}，格尺寸 {_vs}。");
        }

        // ================= 元素累积 =================

        private static void AccumulatePart(JObject elem, Dictionary<Vector3Int, Cell> master)
        {
            string name = (string)elem["name"] ?? "Part";
            bool combine = elem["combine"] != null ? (bool)elem["combine"] : _combineDefault;
            var local = new Dictionary<Vector3Int, string>();
            foreach (JToken op in (JArray)elem["ops"] ?? new JArray()) ApplyOp((JObject)op, local);
            foreach (KeyValuePair<Vector3Int, string> kv in local)
                master[kv.Key] = new Cell { Color = kv.Value, Group = name, Combine = combine };
        }

        private static void AccumulateInstance(JObject elem, Dictionary<Vector3Int, Cell> master)
        {
            string moduleName = (string)elem["module"];
            if (moduleName == null || !_modules.TryGetValue(moduleName, out JObject module))
            { Debug.LogWarning($"[Voxel] 找不到模板：{moduleName}"); return; }

            int[] size = Ints(module["size"]);                       // [sx,sy,sz]
            int[] at = Ints(elem["at"]) ?? new[] { 0, 0, 0 };
            int rot = elem["rot"] != null ? ((int)elem["rot"] % 360 + 360) % 360 : 0;
            string mirror = (string)elem["mirror"] ?? "";
            bool combine = elem["combine"] != null ? (bool)elem["combine"] : _combineDefault;
            string groupName = (string)elem["name"] ?? $"{moduleName}@{at[0]}_{at[2]}";

            var swap = new Dictionary<string, string>();
            foreach (JProperty p in ((JObject)elem["swap"] ?? new JObject()).Properties()) swap[p.Name] = (string)p.Value;

            var local = new Dictionary<Vector3Int, string>();
            foreach (JToken op in (JArray)module["ops"] ?? new JArray()) ApplyOp((JObject)op, local);

            foreach (KeyValuePair<Vector3Int, string> kv in local)
            {
                int x = kv.Key.x, y = kv.Key.y, z = kv.Key.z;
                if (mirror.Contains("x")) x = size[0] - 1 - x;
                if (mirror.Contains("z")) z = size[2] - 1 - z;
                Vector3Int r = Rotate(x, y, z, rot, size[0], size[2]);
                Vector3Int world = new Vector3Int(r.x + at[0], r.y + at[1], r.z + at[2]);
                string key = swap.TryGetValue(kv.Value, out string s) ? s : kv.Value;
                master[world] = new Cell { Color = key, Group = groupName, Combine = combine };
            }
        }

        /// <summary>绕 Y 轴按 90° 步进旋转格坐标；sx/sz 为模板在 X/Z 上的尺寸。</summary>
        private static Vector3Int Rotate(int x, int y, int z, int rot, int sx, int sz) => rot switch
        {
            90 => new Vector3Int(z, y, sx - 1 - x),
            180 => new Vector3Int(sx - 1 - x, y, sz - 1 - z),
            270 => new Vector3Int(sz - 1 - z, y, x),
            _ => new Vector3Int(x, y, z),
        };

        // ================= op 执行 =================

        private static void ApplyOp(JObject op, Dictionary<Vector3Int, string> grid)
        {
            string kind = (string)op["op"];
            string color = (string)op["color"];
            switch (kind)
            {
                case "voxel":
                {
                    int[] a = Ints(op["at"]);
                    Set(grid, a[0], a[1], a[2], color);
                    break;
                }
                case "box":
                {
                    int[] m = Ints(op["min"]), s = Ints(op["size"]);
                    string mode = (string)op["mode"] ?? "solid";
                    bool carve = op["carve"] != null && (bool)op["carve"];
                    int x0 = m[0], y0 = m[1], z0 = m[2], x1 = m[0] + s[0] - 1, y1 = m[1] + s[1] - 1, z1 = m[2] + s[2] - 1;
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                            for (int z = z0; z <= z1; z++)
                            {
                                bool onFace = x == x0 || x == x1 || y == y0 || y == y1 || z == z0 || z == z1;
                                bool vertEdge = (x == x0 || x == x1) && (z == z0 || z == z1);
                                bool keep = mode switch
                                {
                                    "shell" => onFace,
                                    "frameY" => vertEdge,
                                    "top" => y == y1,
                                    "bottom" => y == y0,
                                    _ => true,
                                };
                                if (!keep) continue;
                                if (carve) grid.Remove(new Vector3Int(x, y, z));
                                else Set(grid, x, y, z, color);
                            }
                    break;
                }
                case "roof":
                {
                    int[] m = Ints(op["min"]), s = Ints(op["size"]);
                    string style = (string)op["style"] ?? "gable";
                    string axis = (string)op["axis"] ?? "x";
                    string ridge = (string)op["ridge"] ?? color;
                    bool solid = op["solid"] != null && (bool)op["solid"];
                    if (style == "hip")
                    {
                        for (int k = 0; ; k++)
                        {
                            int xLo = m[0] + k, xHi = m[0] + s[0] - 1 - k, zLo = m[2] + k, zHi = m[2] + s[2] - 1 - k, y = m[1] + k;
                            if (xLo > xHi || zLo > zHi || k > s[1]) break;
                            bool cap = xLo == xHi || zLo == zHi;
                            for (int x = xLo; x <= xHi; x++)
                                for (int z = zLo; z <= zHi; z++)
                                {
                                    bool border = x == xLo || x == xHi || z == zLo || z == zHi;
                                    if (border || cap || solid) Set(grid, x, y, z, cap ? ridge : color);
                                }
                        }
                    }
                    else // gable
                    {
                        bool alongX = axis == "x";
                        int span = alongX ? s[0] : s[2];              // 屋脊方向长度
                        int slope = alongX ? s[2] : s[0];             // 斜面方向宽度
                        for (int k = 0; k < s[1]; k++)
                        {
                            int lo = k, hi = slope - 1 - k;
                            if (lo > hi) break;
                            int y = m[1] + k;
                            for (int t = 0; t < span; t++)
                            {
                                if (alongX)
                                {
                                    Set(grid, m[0] + t, y, m[2] + lo, lo == hi ? ridge : color);
                                    Set(grid, m[0] + t, y, m[2] + hi, lo == hi ? ridge : color);
                                    if (solid) for (int u = lo + 1; u < hi; u++) Set(grid, m[0] + t, y, m[2] + u, color);
                                }
                                else
                                {
                                    Set(grid, m[0] + lo, y, m[2] + t, lo == hi ? ridge : color);
                                    Set(grid, m[0] + hi, y, m[2] + t, lo == hi ? ridge : color);
                                    if (solid) for (int u = lo + 1; u < hi; u++) Set(grid, m[0] + u, y, m[2] + t, color);
                                }
                            }
                        }
                    }
                    break;
                }
                case "ramp":
                {
                    int[] m = Ints(op["min"]), s = Ints(op["size"]);
                    bool alongX = ((string)op["axis"] ?? "z") == "x";
                    int run = alongX ? s[0] : s[2];
                    for (int j = 0; j < run; j++)
                    {
                        int top = 1 + j * s[1] / run;
                        for (int yy = 0; yy < top; yy++)
                            if (alongX) for (int z = 0; z < s[2]; z++) Set(grid, m[0] + j, m[1] + yy, m[2] + z, color);
                            else for (int x = 0; x < s[0]; x++) Set(grid, m[0] + x, m[1] + yy, m[2] + j, color);
                    }
                    break;
                }
                case "line":
                {
                    int[] a = Ints(op["from"]), b = Ints(op["to"]);
                    int dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
                    int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
                    for (int i = 0; i <= steps; i++)
                    {
                        float f = steps == 0 ? 0f : (float)i / steps;
                        Set(grid, Mathf.RoundToInt(a[0] + dx * f), Mathf.RoundToInt(a[1] + dy * f), Mathf.RoundToInt(a[2] + dz * f), color);
                    }
                    break;
                }
                case "scatter":
                {
                    int[] m = Ints(op["min"]), s = Ints(op["size"]);
                    string[] cols = ((JArray)op["colors"]).Select(t => (string)t).ToArray();
                    float chance = op["chance"] != null ? (float)op["chance"] : 0.05f;
                    var rng = new System.Random(op["seed"] != null ? (int)op["seed"] : 12345);
                    for (int x = 0; x < s[0]; x++)
                        for (int z = 0; z < s[2]; z++)
                            if (rng.NextDouble() < chance)
                                Set(grid, m[0] + x, m[1], m[2] + z, cols[rng.Next(cols.Length)]);
                    break;
                }
                case "cyl":       // XZ 平面圆柱：立塔、井圈、圆窗框
                {
                    int[] c = Ints(op["center"]);
                    float r = (float)op["r"];
                    int height = op["height"] != null ? (int)op["height"] : 1;
                    bool shell = ((string)op["mode"] ?? "solid") == "shell";
                    bool carve = op["carve"] != null && (bool)op["carve"];
                    int ir = Mathf.CeilToInt(r) + 1;
                    for (int y = 0; y < height; y++)
                        for (int dx = -ir; dx <= ir; dx++)
                            for (int dz = -ir; dz <= ir; dz++)
                            {
                                float d = Mathf.Sqrt(dx * dx + dz * dz);
                                bool hit = shell ? (d <= r + 0.5f && d >= r - 0.5f) : d <= r + 0.5f;
                                if (!hit) continue;
                                if (carve) grid.Remove(new Vector3Int(c[0] + dx, c[1] + y, c[2] + dz));
                                else Set(grid, c[0] + dx, c[1] + y, c[2] + dz, color);
                            }
                    break;
                }
                case "cone":      // 逐层收缩的圆锥：塔尖、圆锥顶
                {
                    int[] c = Ints(op["center"]);
                    float r = (float)op["r"];
                    int height = op["height"] != null ? (int)op["height"] : Mathf.CeilToInt(r);
                    string ridge = (string)op["ridge"] ?? color;
                    int ir = Mathf.CeilToInt(r) + 1;
                    for (int k = 0; k < height; k++)
                    {
                        float rr = r * (1f - (k + 0.5f) / height);
                        string col = k == height - 1 ? ridge : color;
                        for (int dx = -ir; dx <= ir; dx++)
                            for (int dz = -ir; dz <= ir; dz++)
                                if (Mathf.Sqrt(dx * dx + dz * dz) <= rr + 0.5f)
                                    Set(grid, c[0] + dx, c[1] + k, c[2] + dz, col);
                    }
                    break;
                }
                case "disc":      // 单层实心圆：圆形地台、平台边缘
                {
                    int[] c = Ints(op["center"]);
                    float r = (float)op["r"];
                    int ir = Mathf.CeilToInt(r) + 1;
                    for (int dx = -ir; dx <= ir; dx++)
                        for (int dz = -ir; dz <= ir; dz++)
                            if (Mathf.Sqrt(dx * dx + dz * dz) <= r + 0.5f)
                                Set(grid, c[0] + dx, c[1], c[2] + dz, color);
                    break;
                }
                case "arch":      // 拱形洞口/拱券：下部矩形 + 顶部半圆。thin 轴（size 为 1 的轴）为墙厚方向
                {
                    int[] m = Ints(op["min"]), s = Ints(op["size"]);
                    bool carve = op["carve"] != null && (bool)op["carve"];
                    bool zWall = s[2] <= s[0];                                   // 墙面法线沿 Z
                    int wide = zWall ? s[0] : s[2];                             // 洞口横向宽度
                    float rad = (wide - 1) * 0.5f;
                    int spring = m[1] + s[1] - 1 - Mathf.FloorToInt(rad);       // 起拱线：其上为半圆
                    for (int a = 0; a < wide; a++)
                        for (int y = m[1]; y < m[1] + s[1]; y++)
                        {
                            if (y > spring)
                            {
                                float da = a - rad, dy = y - spring;
                                if (da * da + dy * dy > (rad + 0.35f) * (rad + 0.35f)) continue;
                            }
                            for (int t = 0; t < (zWall ? s[2] : s[0]); t++)
                            {
                                int x = zWall ? m[0] + a : m[0] + t;
                                int z = zWall ? m[2] + t : m[2] + a;
                                if (carve) grid.Remove(new Vector3Int(x, y, z));
                                else Set(grid, x, y, z, color);
                            }
                        }
                    break;
                }
                default:
                    Debug.LogWarning($"[Voxel] 未知 op：{kind}");
                    break;
            }
        }

        private static void Set(Dictionary<Vector3Int, string> grid, int x, int y, int z, string color)
        {
            if (string.IsNullOrEmpty(color)) return;
            grid[new Vector3Int(x, y, z)] = color;
        }

        // ================= 合并网格（面剔除） =================

        private static readonly Vector3Int[] _dirs =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        // 每个方向面的 4 个角（单位半格符号，外法线朝外的 CCW 顺序），三角形固定为 (0,1,2)(0,2,3)
        private static readonly Vector3[][] _face =
        {
            new[] { new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(1,1,1), new Vector3(1,-1,1) },      // +X
            new[] { new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(-1,1,-1), new Vector3(-1,-1,-1) },   // -X
            new[] { new Vector3(-1,1,1), new Vector3(1,1,1), new Vector3(1,1,-1), new Vector3(-1,1,-1) },       // +Y
            new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,-1,1), new Vector3(-1,-1,1) },    // -Y
            new[] { new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1), new Vector3(-1,-1,1) },       // +Z
            new[] { new Vector3(-1,-1,-1), new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1) },    // -Z
        };

        /// <summary>把同色体素合并成一张网格，只保留朝向空位（不在 occ 内）的面。</summary>
        private static Mesh BuildCulledMesh(List<Vector3Int> cells, HashSet<Vector3Int> occ, string name)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float h = 0.5f * _vs;
            foreach (Vector3Int c in cells)
            {
                Vector3 cc = (Vector3)c * _vs;
                for (int d = 0; d < 6; d++)
                {
                    if (occ.Contains(c + _dirs[d])) continue;
                    Vector3 n = _dirs[d];
                    int b = verts.Count;
                    for (int i = 0; i < 4; i++) { verts.Add(cc + _face[d][i] * h); norms.Add(n); }
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ================= 资源 =================

        private static Material Mat(string key)
        {
            if (_matCache.TryGetValue(key, out Material cached)) return cached;
            string path = $"{MaterialDir}/Voxel_{key}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Color col = _palette.TryGetValue(key, out Color pc) ? pc : Color.magenta;
            if (!_palette.ContainsKey(key) && _missingWarned.Add(key)) Debug.LogWarning($"[Voxel] 调色板缺少键：{key}");
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", col);
            mat.SetColor("_Color", col);
            mat.SetFloat("_Smoothness", 0.05f);
            _matCache[key] = mat;
            return mat;
        }

        private static void EnsureMaterialDir()
        {
            if (!Directory.Exists(MaterialDir)) { Directory.CreateDirectory(MaterialDir); AssetDatabase.Refresh(); }
        }

        private static int[] Ints(JToken t) => t == null ? null : ((JArray)t).Select(v => (int)v).ToArray();
    }
}
#endif
