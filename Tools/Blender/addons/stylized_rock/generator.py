# 白盒 → 风格化岩石的几何生成算法（整体岩体 + 节理切面）。
#
# 本模块只负责"白盒 + 参数 → bmesh + 修改器配置"，不创建物体、不处理 UI；物体的挂载、更新、卸载由 __init__.py 负责。
#
# 思路：把白盒（可由多个互相重叠的封闭块拼成）当作一整块岩体的体量参考，而不是逐块拉伸：
#   1. 外包络：在一组高度上，从竖直中轴向四周打水平射线，记录白盒最外侧表面到中轴的距离 r(θ, z)；
#      中轴位置取各高度截面的形心并沿高度平滑，允许岩体倾斜。
#   2. 融合：沿高度对 r 做高斯平滑，白盒中的台阶、侧挑块被抹成连续的收分与外凸（融合度控制平滑程度）。
#   3. 粗网格：按切面尺寸在包络上铺一张稀疏的环形网格，顶点沿切线随机错位、沿径向随机内收，四边形随机对角线三角化，形成低模大切面。
#   4. 局部平切：随机挑选若干顶点，以其局部切平面（加随机倾斜）为刀，把附近顶点沿径向截到平面上，得到跨越多个三角面的大平切面。
#   5. 节理：若干条竖向节理（整列顶点内收形成 V 形裂缝）与按环的横向层理（整环轻微内收形成水平缝）。
#   6. 顶部：倒角带 + 顶环 + 扇形盖面；顶部起伏让顶环与盖面中心高低错落，形成参差的岩峰轮廓。只有盖面写入顶部遮罩。
#
# 所有顶点都在"本环中心沿方位角 θ 的水平射线"上移动，各环高度严格递增、同一环内方位角顺序不变，
# 因此每个高度的截面都是关于中心的星形多边形，生成的网格不会自交。
# 限制：每个高度的白盒截面应大致关于中轴呈星形（单块岩体）；同一高度上分开的两块（两根柱子、拱门）无法表达，需拆成两个白盒。
import bmesh, math, random
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

TOP_MASK = "TopMask"  # 顶点色属性名（面角域、字节颜色），导出 FBX 后即 Unity 网格的 vertex color
N_THETA = 96          # 外包络采样的方位角数量
DZ_FINE = 0.25        # 外包络采样的高度间隔（米）


# ---------------- 外包络 ----------------
def world_bmesh(ob):
    """白盒网格转为世界坐标下的 bmesh（已应用物体变换并更新法线）。"""
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.transform(ob.matrix_world); bm.normal_update()
    return bm


def gaussian(a, sigma, axis, wrap):
    """沿 axis 做高斯平滑（numpy）；wrap=True 时首尾相接（方位角方向），否则边缘按端点值延伸（高度方向）。sigma 以采样点为单位。"""
    if sigma < 0.3: return a
    rad = int(3 * sigma) + 1
    k = np.exp(-0.5 * (np.arange(-rad, rad + 1) / sigma) ** 2); k /= k.sum()
    a = np.moveaxis(a, axis, 0)
    pad = np.concatenate([a[-rad:], a, a[:rad]]) if wrap else np.concatenate([np.repeat(a[:1], rad, 0), a, np.repeat(a[-1:], rad, 0)])
    out = sum(k[i] * pad[i:i + len(a)] for i in range(len(k)))
    return np.moveaxis(out, 0, axis)


def sample_radii(bvh, centers, zs, reach):
    """在每个高度 zs[k]、以 centers[k] 为中心，从 reach 米外沿水平方向朝中心打射线，返回最外侧表面到中心的距离表 r[k, j]。
    射线长度限制为 reach，只在中心外侧找表面；该方向没有实体时距离为 0。"""
    th = np.linspace(0, 2 * math.pi, N_THETA, endpoint=False)
    r = np.zeros((len(zs), N_THETA))
    for k, z in enumerate(zs):
        cx, cy = centers[k]
        for j, t in enumerate(th):
            ux, uy = math.cos(t), math.sin(t)
            loc, _, _, dist = bvh.ray_cast(Vector((cx + ux * reach, cy + uy * reach, z)), Vector((-ux, -uy, 0)), reach)
            if loc is not None: r[k, j] = reach - dist
    return r


def row_centroids(r, centers):
    """把每个高度的 (中心, r[θ]) 星形多边形求面积形心；该高度没有实体（全为 0）时沿用原中心。"""
    th = np.linspace(0, 2 * math.pi, N_THETA, endpoint=False)
    out = centers.copy()
    for k in range(len(r)):
        x = centers[k, 0] + r[k] * np.cos(th); y = centers[k, 1] + r[k] * np.sin(th)
        x1, y1 = np.roll(x, -1), np.roll(y, -1)
        cr = x * y1 - x1 * y
        a = cr.sum() / 2
        if a > 1e-4: out[k] = ((x + x1) * cr).sum() / (6 * a), ((y + y1) * cr).sum() / (6 * a)
    return out


class Envelope:
    """白盒的外包络：中心 c(z) 与半径 r(θ, z)，可按任意 (θ, z) 双线性插值查询。"""

    def __init__(self, wb, blend):
        bm = world_bmesh(wb)
        bvh = BVHTree.FromBMesh(bm)
        xs = [v.co.x for v in bm.verts]; ys = [v.co.y for v in bm.verts]; zz = [v.co.z for v in bm.verts]
        bm.free()
        self.z_bot, self.z_top = min(zz), max(zz)
        reach = 2 * math.hypot(max(xs) - min(xs), max(ys) - min(ys)) + 1  # 足够从白盒外侧打进来
        n = max(4, int((self.z_top - self.z_bot) / DZ_FINE))
        self.zs = np.linspace(self.z_bot + 0.02, self.z_top - 0.02, n)  # 避开顶面/底面本身，射线不会擦着面走
        c = np.tile([(min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2], (n, 1))
        # 两遍：先以包围盒中心采样求各高度形心，平滑后作为中轴再采样一次（中心偏出截面时第一遍有方向打不到，第二遍修正）
        c = gaussian(row_centroids(sample_radii(bvh, c, self.zs, reach), c), 1.5 / DZ_FINE, 0, False)
        r = sample_radii(bvh, c, self.zs, reach)
        # 融合：高度方向按融合度平滑（1 = 0.2×总高），方位角方向固定轻微平滑去掉白盒棱角处的锯齿
        h = self.z_top - self.z_bot
        self.r = gaussian(gaussian(r, blend * 0.2 * h / DZ_FINE, 0, False), 1.0, 1, True)
        self.c = c

    def _z(self, z):
        """高度 → 采样行的浮点索引（夹在有效范围内）。"""
        return min(max((z - self.zs[0]) / (self.zs[-1] - self.zs[0]) * (len(self.zs) - 1), 0.0), len(self.zs) - 1.0)

    def center(self, z):
        """高度 z 处的中心 (x, y)。"""
        f = self._z(z); k = min(int(f), len(self.zs) - 2); t = f - k
        return Vector(self.c[k] * (1 - t) + self.c[k + 1] * t)

    def radius(self, theta, z):
        """方位角 theta、高度 z 处的包络半径（双线性插值，方位角首尾相接）。"""
        f = self._z(z); k = min(int(f), len(self.zs) - 2); t = f - k
        g = (theta % (2 * math.pi)) / (2 * math.pi) * N_THETA; j = int(g) % N_THETA; s = g - int(g); j1 = (j + 1) % N_THETA
        a = self.r[k, j] * (1 - s) + self.r[k, j1] * s
        b = self.r[k + 1, j] * (1 - s) + self.r[k + 1, j1] * s
        return a * (1 - t) + b * t

    def mean_perimeter(self):
        """各高度包络多边形周长的平均值（用于推算自动切面尺寸）。"""
        th = np.linspace(0, 2 * math.pi, N_THETA, endpoint=False)
        x = self.r * np.cos(th); y = self.r * np.sin(th)
        return float(np.mean(np.sum(np.hypot(np.roll(x, -1, 1) - x, np.roll(y, -1, 1) - y), axis=1)))


# ---------------- 粗网格 ----------------
class Grid:
    """环形粗网格：rings[i] 为第 i 环（自下而上），每环 M 个顶点，记录方位角 θ、高度 z、径向距离 ρ。
    顶点世界位置 = (center(z) + ρ·(cosθ, sinθ), z)。"""

    def __init__(self, env, M):
        self.env, self.M = env, M
        self.th, self.z, self.rho = [], [], []

    def add_ring(self, th, z, rho):
        self.th.append(th); self.z.append(z); self.rho.append(rho)

    def pos(self, i, j):
        """第 i 环第 j 个顶点的世界坐标。"""
        t, z = self.th[i][j], self.z[i][j]
        c = self.env.center(z)
        return Vector((c.x + math.cos(t) * self.rho[i][j], c.y + math.sin(t) * self.rho[i][j], z))


def build(wb, s):
    """为白盒 wb 按参数 s（StylizedRockSettings）生成岩石。
    返回 (bm, mods)：bm 为白盒局部坐标下的 bmesh（岩石作为白盒子物体、父逆矩阵为单位阵时与世界坐标结果重合），带 TopMask 顶点色；
    mods 为修改器配置 dict(bevel_width, bevel_segments, subsurf, harden)。facet / bevel 为 0 时按白盒尺寸自动推算。"""
    rng = random.Random(s.seed)
    env = Envelope(wb, s.blend)
    z_bot, z_top = env.z_bot, env.z_top
    H = z_top - z_bot
    perim = env.mean_perimeter()
    facet = s.facet or max(1.0, math.sqrt(perim * H) / 9)  # 自动切面尺寸：随岩体尺度增长，约 9 个切面撑满"周长×高"的几何平均
    M = min(max(round(perim / facet), 8), 48)              # 每环顶点数（竖向切面列数）
    R = min(max(round(H / (facet * 1.1)), 2), 24)          # 侧墙环数
    dth = 2 * math.pi / M
    phase = rng.uniform(0, dth)
    g = Grid(env, M)

    def ring(z_of, rho_of, jitter=0.3):
        """按每个顶点的 θ 生成一环：θ 在列中心 ±jitter×列宽内随机错位（<0.5 保证同环方位角顺序不变），z、ρ 由回调给出。"""
        th = [phase + (j + rng.uniform(-jitter, jitter)) * dth for j in range(M)]
        z = [z_of(t) for t in th]
        g.add_ring(th, z, [rho_of(t, zz) for t, zz in zip(th, z)])

    # 顶部倒角带高度与顶部起伏：顶环随机下沉不超过倒角带的 80%，保证顶环始终高于下面一环
    spacing = H / (R + 1)
    rim = min(0.45 * spacing, max(0.6, 1.2 * s.crown))
    crown = min(s.crown, 0.8 * rim)
    lift = min(0.4 * spacing, 0.5)  # 接地线起伏幅度
    cut = s.cut

    # 1. 地下环（平底，统一埋深）与接地环（高度随机起伏，部分收口露出地面）
    depth = max(0.5, 0.05 * H)
    ring(lambda t: z_bot - depth, lambda t, z: env.radius(t, z_bot) * rng.uniform(0.75, 0.9))
    ring(lambda t: z_bot + rng.uniform(-0.3, 1.0) * lift, lambda t, z: env.radius(t, z) * (1 - rng.uniform(0.0, cut)))
    # 2. 侧墙环：在接地线以上、倒角带以下均匀分布，整环高度抖动 ±30% 环距；层理让整环统一内收一点，形成水平缝
    body0, body1 = z_bot + lift, z_top - rim
    gap = (body1 - body0) / R
    for i in range(1, R):
        zi = body0 + gap * (i + rng.uniform(-0.3, 0.3))
        bed = 1 - rng.uniform(0.0, 0.12) * s.strata
        ring(lambda t, zi=zi: zi, lambda t, z, bed=bed: env.radius(t, z) * bed * (1 - rng.uniform(0.0, cut)))
    # 3. 倒角带底环（保持包络）与顶环（向内收 8%~18%，形成切面状的顶边；高度随顶部起伏下沉）
    ring(lambda t: body1, lambda t, z: env.radius(t, z) * (1 - 0.5 * rng.uniform(0.0, cut)))
    ring(lambda t: z_top - rng.uniform(0.0, 1.0) * crown, lambda t, z: env.radius(t, z_top - 0.05) * (1 - rng.uniform(0.08, 0.18)))
    n_ring = len(g.z)
    side = range(1, n_ring - 1)  # 参与平切与节理的环：接地环到倒角带底环（地下环和顶环不动）

    # 4. 局部平切：每把刀取种子顶点的局部切平面（相邻顶点差分）加随机倾斜，略向内推，作用于半径 facet×(0.9~1.6) 内的顶点
    pos = [[g.pos(i, j) for j in range(M)] for i in range(n_ring)]
    for _ in range(round(s.flatten * M * R / 3)):
        i, j = rng.randint(2, n_ring - 3), rng.randrange(M)
        n = (pos[i][(j + 1) % M] - pos[i][j - 1]).cross(pos[i + 1][j] - pos[i - 1][j]).normalized()
        u = Vector((math.cos(g.th[i][j]), math.sin(g.th[i][j]), 0))
        if n.dot(u) < 0: n = -n  # 朝外
        n = (n + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * s.tilt).normalized()
        p0 = pos[i][j] - n * rng.uniform(0.0, 0.25) * cut * g.rho[i][j]
        reach = facet * rng.uniform(0.9, 1.6)
        for a in side:
            for b in range(M):
                if (pos[a][b] - pos[i][j]).length > reach: continue
                ua = Vector((math.cos(g.th[a][b]), math.sin(g.th[a][b]), 0))
                nu = n.dot(ua)
                if nu < 0.25: continue  # 刀面几乎平行于该顶点的径向射线（或背向），截不到
                c = env.center(g.z[a][b])
                rho_p = (n.dot(p0) - n.dot(Vector((c.x, c.y, g.z[a][b])))) / nu  # 径向射线与刀面的交点距离
                g.rho[a][b] = min(g.rho[a][b], max(rho_p, 0.55 * g.rho[a][b]))  # 只削不补，最多削掉 45%

    # 5. 竖向节理：随机若干列，在一段连续的环上整列内收，形成竖向 V 形裂缝
    for col in rng.sample(range(M), min(s.joints, M)):
        a = rng.randint(1, max(1, n_ring // 3)); b = rng.randint(a + max(1, (n_ring - 2) // 2), n_ring - 2)
        for i in range(a, b + 1): g.rho[i][col] *= 1 - s.joint_depth * rng.uniform(0.6, 1.0)

    # 6. 去尖刺：平切和节理会把一圈邻点削进去而漏掉中间某个顶点，留下孤零零的尖角；
    #    每个侧墙顶点的 ρ 不超过上下左右四个邻点平均值的 1.12 倍（只削不补，基于削之前的值计算，结果与遍历顺序无关）
    old = [row[:] for row in g.rho]
    for i in side:
        for j in range(M):
            avg = (old[i][j - 1] + old[i][(j + 1) % M] + old[i - 1][j] + old[i + 1][j]) / 4
            g.rho[i][j] = min(old[i][j], 1.12 * avg)

    # ---------------- 建网格 ----------------
    bm = bmesh.new()
    vs = [[bm.verts.new(g.pos(i, j)) for j in range(M)] for i in range(n_ring)]
    for i in range(n_ring - 1):
        for j in range(M):
            k = (j + 1) % M
            a, b, c, d = vs[i][j], vs[i][k], vs[i + 1][k], vs[i + 1][j]
            # 四边形随机选对角线拆成两个三角形，切面朝向更杂乱
            if rng.random() < 0.5: bm.faces.new((a, b, c)); bm.faces.new((a, c, d))
            else: bm.faces.new((a, b, d)); bm.faces.new((b, c, d))
    bottom_c = env.center(z_bot - depth)
    vb = bm.verts.new((bottom_c.x, bottom_c.y, z_bot - depth))
    for j in range(M): bm.faces.new((vb, vs[0][(j + 1) % M], vs[0][j]))
    top_c = env.center(z_top)
    vt = bm.verts.new((top_c.x, top_c.y, z_top + rng.uniform(-0.3, 0.6) * crown))  # 盖面中心：起伏为 0 时与顶环同高（平顶）
    cap = [bm.faces.new((vt, vs[-1][j], vs[-1][(j + 1) % M])) for j in range(M)]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # 顶部遮罩：只有盖面写入（按面法线 Z 做 smoothstep），侧墙、外凸、层理缝一律为 0
    layer = bm.loops.layers.color.new(TOP_MASK)
    bm.normal_update()
    capset = set(cap)
    for f in bm.faces:
        m = 0.0
        if f in capset:
            t = min(max((f.normal.z - s.top_start) / max(s.top_full - s.top_start, 1e-3), 0.0), 1.0)  # 面板上两个阈值可被调成相等或颠倒，分母保底
            m = t * t * (3 - 2 * t)
        for lp in f.loops: lp[layer] = (m, 0.0, 0.0, 1.0)
    bm.transform(wb.matrix_world.inverted())  # 世界坐标 → 白盒局部坐标

    # 修改器：圆度越大倒角越宽；圆度 >0.5 加细分磨圆，否则用 WeightedNormal 保持切面平整
    rnd = s.roundness
    mods = dict(bevel_width=s.bevel or facet * (0.04 + 0.12 * rnd), bevel_segments=3, subsurf=2 if rnd > 0.5 else 0, harden=rnd <= 0.5)
    # 倒角修改器在局部空间工作，会被父物体（白盒）的缩放放大；除以平均缩放，让倒角宽度保持为世界单位（米）
    mods['bevel_width'] /= sum(wb.matrix_world.to_scale()) / 3
    return bm, mods
