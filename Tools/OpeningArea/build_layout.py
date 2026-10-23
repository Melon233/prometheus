# -*- coding: utf-8 -*-
"""
晨风原开局区布局生成器。

读取 Assets/Art/OpeningArea/Layout/layout.json（唯一数据源），生成：
  Generated/heightmap.r16          513x513 小端 uint16 高度图，行 = y（向北），列 = x（向东），按 height_min..height_max 线性编码
  Generated/heightmap_meta.json    高度图编码信息
  Generated/layout_map.png         彩色俯视图（2 倍分辨率，1 像素 = 0.5 m）
  Generated/layout_resolved.json   解析后的数据：路线采样、道路剖面、城镇地块、POI 地面高度等（白盒与文档共用）
  Generated/report.json            确定性校验结果（连通性、坡度、崖高、可见性等）

运行（需要 numpy，用 Blender 自带的 Python 即可）：
  "C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe" Tools/OpeningArea/build_layout.py
"""
import json, math, os, struct, sys, zlib
from collections import deque
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
LAYOUT = os.path.join(ROOT, "Assets", "Art", "OpeningArea", "Layout", "layout.json")
OUT = os.path.join(ROOT, "Assets", "Art", "OpeningArea", "Layout", "Generated")


# ---------------------------------------------------------------- 几何工具（全部向量化在 513x513 网格上）

def smoothstep(e0, e1, x):
    """标准 smoothstep；e0 > e1 时为反向过渡。"""
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def polyline_nearest(X, Y, pts):
    """到折线的最近距离。返回 (距离, 沿线弧长, 有向侧别)。侧别 > 0 表示在前进方向左侧（折线自西向东时即北侧）。"""
    pts = np.asarray(pts, dtype=np.float64)
    best_d = np.full(X.shape, np.inf); best_s = np.zeros(X.shape); best_side = np.zeros(X.shape)
    acc = 0.0
    for a, b in zip(pts[:-1, :2], pts[1:, :2]):
        ab = b - a; L2 = float(ab @ ab); L = math.sqrt(L2)
        t = np.clip(((X - a[0]) * ab[0] + (Y - a[1]) * ab[1]) / L2, 0.0, 1.0)
        px = a[0] + t * ab[0]; py = a[1] + t * ab[1]
        d = np.hypot(X - px, Y - py)
        cross = ab[0] * (Y - a[1]) - ab[1] * (X - a[0])
        m = d < best_d
        best_d = np.where(m, d, best_d); best_s = np.where(m, acc + t * L, best_s); best_side = np.where(m, np.sign(cross), best_side)
        acc += L
    return best_d, best_s, best_side


def polyline_lengths(pts):
    """折线各控制点的累计弧长。"""
    pts = np.asarray(pts, dtype=np.float64)[:, :2]
    seg = np.hypot(*(pts[1:] - pts[:-1]).T)
    return np.concatenate([[0.0], np.cumsum(seg)])


def sample_polyline(pts, step):
    """按固定步长在折线上采样，返回 (N,2) 坐标与弧长。"""
    pts = np.asarray(pts, dtype=np.float64)[:, :2]
    cum = polyline_lengths(pts); total = cum[-1]
    s = np.arange(0.0, total + 1e-6, step)
    x = np.interp(s, cum, pts[:, 0]); y = np.interp(s, cum, pts[:, 1])
    return np.stack([x, y], 1), s


def point_in_polygon(X, Y, poly):
    """奇偶规则判断点是否在多边形内。"""
    poly = np.asarray(poly, dtype=np.float64)
    inside = np.zeros(X.shape, dtype=bool)
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]; x2, y2 = poly[(i + 1) % n]
        cond = (y1 > Y) != (y2 > Y)
        xint = (x2 - x1) * (Y - y1) / ((y2 - y1) if y2 != y1 else 1e-12) + x1
        inside ^= cond & (X < xint)
    return inside


def gauss_interp(X, Y, controls, sigma):
    """高斯加权插值（Shepard 形式），在控制点之间产生平缓起伏。"""
    c = np.asarray(controls, dtype=np.float64)
    num = np.zeros(X.shape); den = np.full(X.shape, 1e-9)
    for x, y, h in c:
        w = np.exp(-((X - x) ** 2 + (Y - y) ** 2) / (2.0 * sigma * sigma))
        num += w * h; den += w
    return num / den


def value_noise(X, Y, scale, seed, octaves=2):
    """确定性值噪声，范围约 [-1, 1]，用于自然地表的轻微起伏。"""
    rng = np.random.default_rng(seed)
    out = np.zeros(X.shape); amp = 1.0; norm = 0.0
    for o in range(octaves):
        s = scale / (2 ** o)
        n = int(600.0 / s) + 3                          # 固定覆盖 600 m，保证不同采样网格得到同一套噪声
        lattice = rng.uniform(-1, 1, (n, n))
        gx = X / s; gy = Y / s
        ix = np.floor(gx).astype(int); iy = np.floor(gy).astype(int)
        fx = gx - ix; fy = gy - iy
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
        v00 = lattice[iy, ix]; v10 = lattice[iy, ix + 1]; v01 = lattice[iy + 1, ix]; v11 = lattice[iy + 1, ix + 1]
        out += amp * ((v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy)
        norm += amp; amp *= 0.5
    return out / norm


def rect_inside_dist(X, Y, rect):
    """到轴对齐矩形边界的“内部距离”：内部为正（离边越远越大），外部为负。"""
    x0, y0, x1, y1 = rect
    return np.minimum(np.minimum(X - x0, x1 - X), np.minimum(Y - y0, y1 - Y))


# ---------------------------------------------------------------- 地形生成

def build_terrain(L, X, Y, info):
    """按 layout 规则逐层合成高度图。info 用于回传水体掩码等中间结果，供校验和着色使用。"""
    bf = L["base_fields"]
    noise = value_noise(X, Y, bf["noise_scale"], L["seed"]) * bf["noise_amplitude"]
    lower = gauss_interp(X, Y, bf["lower"], bf["sigma"]) + noise
    upper = gauss_interp(X, Y, bf["upper"], bf["sigma"]) + noise

    # 悬崖带：北侧取高地场，南侧取低地场，half_width 控制崖面宽度（越小越陡）
    esc = L["escarpment"]
    d, s, side = polyline_nearest(X, Y, esc["points"])
    # 崖高控制点投影到折线得到弧长，再按弧长插值出每个格子对应的目标崖高
    hp = np.asarray(esc["heights"], dtype=np.float64)
    _, hs_, _ = polyline_nearest(hp[:, 0], hp[:, 1], esc["points"])
    order = np.argsort(hs_)
    target = np.interp(s, hs_[order], hp[order, 2])
    # 低地一侧靠近崖线时修正到 upper - target，离开 correction_falloff 后回到原低地场
    fall = 1.0 - smoothstep(0.0, esc["correction_falloff"], d)
    lower = lower + ((upper - target) - lower) * fall
    k = smoothstep(-esc["half_width"], esc["half_width"], d * side)
    h = lower + (upper - lower) * k
    info["lowland"] = k < 0.5

    # 岩脊：沿折线叠加平顶、陡侧的凸起
    for r in L["ridges"]:
        d, _, _ = polyline_nearest(X, Y, r["points"])
        bump = r["height"] - np.clip(d - r["top_half_width"], 0, None) * math.tan(math.radians(r["side_slope_deg"]))
        h = h + np.clip(bump, 0, None)

    # 神像高地：椭圆平顶 + 陡峭边缘，与周边取最大值
    p = L["plateau"]
    cx, cy = p["center"]; rx, ry = p["radii"]
    rr = np.sqrt(((X - cx) / rx) ** 2 + ((Y - cy) / ry) ** 2)
    top = p["height"] - np.clip(rr - 0.9, 0, 0.1) * 10.0
    outside = (p["height"] - 1.0) - np.clip(rr - 1.0, 0, None) * math.sqrt(rx * ry) * math.tan(math.radians(p["edge_slope_deg"]))
    h = np.maximum(h, np.where(rr <= 1.0, top, outside))
    info["plateau_mask"] = rr <= 1.0

    # 东侧山脊：山脚线以东按陡坡抬升
    m = L["mountain"]
    edge = np.asarray(m["edge"], dtype=np.float64)
    x_edge = np.interp(Y, edge[:, 1], edge[:, 0])
    rise = np.clip((X - x_edge) * math.tan(math.radians(m["face_slope_deg"])), 0, m["rise"])
    # 山体起伏：按抬升量加权的低频噪声，打破平整的斜面与平顶（山脚处权重为 0，不影响边界位置）
    bumpy = value_noise(X, Y, 22.0, L["seed"] + 5) * m["noise_amplitude"] * (rise / m["rise"])
    h = h + rise + bumpy
    info["mountain_mask"] = X > x_edge

    # 北侧密林边界：逐渐抬高
    nb = L["north_boundary"]
    nrise = np.clip(Y - nb["start"], 0, None) * nb["rise_per_m"]
    h = h + nrise + value_noise(X, Y, 18.0, L["seed"] + 6) * 4.0 * np.clip(nrise / 20.0, 0, 1)

    # 平地（出生空地、战斗空地、神像平台）
    for f in L["flats"]:
        dd = np.hypot(X - f["center"][0], Y - f["center"][1])
        w = 1.0 - smoothstep(f["radius"], f["radius"] + f["blend"], dd)
        h = h * (1 - w) + f["height"] * w

    # 海岸：岸线内侧 beach_width 范围过渡到 shore_height，海里逐渐加深到 sea_floor
    c = L["coastline"]
    sea_poly = c["points"] + c["sea_closure"]
    sea = point_in_polygon(X, Y, sea_poly)
    dc, _, _ = polyline_nearest(X, Y, c["points"])
    cc = c["cliff_coasts"]
    # 海崖权重：矩形边界两侧 8 m 内渐变，避免沙滩与海崖之间出现直线接缝
    wc = np.zeros(X.shape)
    for r in cc["rects"]:
        wc = np.maximum(wc, smoothstep(-8.0, 8.0, rect_inside_dist(X, Y, r)))
    # 沙滩段：beach_width 内缓降到 shore_height；海崖段：cliff_width 内陡降，崖下直接是深水
    land_w = smoothstep(0, c["beach_width"], dc) * (1 - wc) + smoothstep(0, cc["cliff_width"], dc) * wc
    land_h = c["shore_height"] + (h - c["shore_height"]) * land_w
    sea_start = (c["shore_height"] - 1.0) * (1 - wc) + cc["sea_start"] * wc
    sea_h = sea_start + (c["sea_floor"] - sea_start) * smoothstep(0, c["sea_floor_distance"], dc)
    h = np.where(sea, sea_h, land_h)
    info["sea"] = sea

    # 河流：U 形河床，河岸压到略高于水面
    rv = L["river"]
    pts = np.asarray(rv["points"], dtype=np.float64)
    d, s, _ = polyline_nearest(X, Y, pts)
    water = np.interp(s, polyline_lengths(pts), pts[:, 2])
    hw = rv["half_width"]
    bed = water - rv["depth"] * np.clip(1 - (d / hw) ** 2, 0, 1)
    bank = (water + 0.6) + (h - (water + 0.6)) * smoothstep(hw, hw + rv["bank_width"], d)
    h = np.where(d < hw, np.minimum(h, bed), np.minimum(h, bank))
    info["river_water"] = np.where(d < hw, water, np.nan)

    # 道路（非桥梁）：在河流之后施工，避免河岸下切挖低桥头路面。路面按控制点高度沿弧长插值，路肩向两侧过渡
    # 按等级施工：小径 → 隘口 → 主路，交汇处以高等级道路的路面为准
    rank = {"trail": 0, "corridor": 1, "main": 2}
    for r in sorted((r for r in L["roads"] if r["kind"] != "bridge"), key=lambda r: rank[r["kind"]]):
        h = apply_road(X, Y, h, r["points"], r["width"], r["bank"])
    # 道路路肩可能伸进河槽（桥头两侧），再把河槽主体挖一次，保证桥下过水；河岸不再处理，桥台由道路决定
    d, _, _ = polyline_nearest(X, Y, pts)
    h = np.where(d < hw, np.minimum(h, bed), h)

    # 溪沟：相对两岸下切，B 点附近收窄
    st = L["stream"]
    d, _, _ = polyline_nearest(X, Y, st["points"])
    nx, ny = st["narrow_point"]
    wn = smoothstep(st["narrow_radius"], 0, np.hypot(X - nx, Y - ny))
    half = (st["width"] + (st["narrow_width"] - st["width"]) * wn) / 2.0
    cut = st["depth"] * (1.0 - smoothstep(half - 0.25, half + st["bank_width"] * 0.25, d))
    h = h - cut
    info["stream"] = cut > st["depth"] * 0.5

    # 城镇：台地 + 广场 + 街道，覆盖城墙矩形内部
    h = apply_town(L["town"], X, Y, h)

    return np.clip(h, L["height_min"], L["height_max"])


def apply_road(X, Y, h, pts, width, bank):
    """把折线道路压到控制点给定的路面高度，路肩在 bank 宽度内过渡回原地形。"""
    pts = np.asarray(pts, dtype=np.float64)
    d, s, _ = polyline_nearest(X, Y, pts)
    road_h = np.interp(s, polyline_lengths(pts), pts[:, 2])
    w = 1.0 - smoothstep(width / 2.0, width / 2.0 + max(bank, 0.3), d)
    return h * (1 - w) + road_h * w


def apply_town(T, X, Y, h):
    """城墙矩形内：先用台地中心做平滑底面，再压平各台地（1.5 m 过渡即挡土墙），最后是广场与街道。"""
    wall = T["wall"]["rect"]
    inside = rect_inside_dist(X, Y, wall) > 0
    ctrl = [[(t["rect"][0] + t["rect"][2]) / 2, (t["rect"][1] + t["rect"][3]) / 2, t["height"]] for t in T["terraces"]]
    base = gauss_interp(X, Y, ctrl, 18.0)
    th = base.copy()
    for t in T["terraces"]:
        w = smoothstep(0.0, 1.5, rect_inside_dist(X, Y, t["rect"]))
        th = th * (1 - w) + t["height"] * w
    pz = T["plaza"]
    w = 1.0 - smoothstep(pz["radius"], pz["radius"] + 1.5, np.hypot(X - pz["center"][0], Y - pz["center"][1]))
    th = th * (1 - w) + pz["height"] * w
    for st in T["streets"]:
        th = apply_road(X, Y, th, st["points"], st["width"], 0.5)
    return np.where(inside, th, h)


# ---------------------------------------------------------------- 城镇地块

class TownGrid:
    """城镇占用栅格（0.5 m 精度），用于地块放置时的重叠判断。"""
    RES = 0.5

    def __init__(self, rect):
        self.x0, self.y0, x1, y1 = rect
        self.nx = int((x1 - self.x0) / self.RES); self.ny = int((y1 - self.y0) / self.RES)
        gx = self.x0 + (np.arange(self.nx) + 0.5) * self.RES; gy = self.y0 + (np.arange(self.ny) + 0.5) * self.RES
        self.X, self.Y = np.meshgrid(gx, gy)
        self.occ = np.zeros((self.ny, self.nx), dtype=bool)

    def poly_mask(self, poly, pad=0.0):
        """凸四边形（顺/逆时针均可）的栅格掩码，pad 为向外扩展距离。"""
        poly = np.asarray(poly, dtype=np.float64)
        c = poly.mean(0)
        m = np.ones(self.X.shape, dtype=bool)
        for i in range(len(poly)):
            a = poly[i]; b = poly[(i + 1) % len(poly)]
            e = b - a; n = np.array([e[1], -e[0]]) / np.hypot(*e)
            if n @ (c - a) > 0: n = -n                      # 让法线朝外
            m &= (self.X - a[0]) * n[0] + (self.Y - a[1]) * n[1] <= pad
        return m

    def free(self, poly):
        m = self.poly_mask(poly)
        return m.any() and not (self.occ & m).any()

    def mark(self, mask):
        self.occ |= mask


def oriented_rect(cx, cy, ux, uy, w, d):
    """以 (cx, cy) 为中心、朝向 (ux, uy)（门面方向）、面宽 w、进深 d 的矩形四角。"""
    vx, vy = -uy, ux
    hw, hd = w / 2.0, d / 2.0
    return [[cx + ux * hd + vx * hw, cy + uy * hd + vy * hw], [cx + ux * hd - vx * hw, cy + uy * hd - vy * hw],
            [cx - ux * hd - vx * hw, cy - uy * hd - vy * hw], [cx - ux * hd + vx * hw, cy - uy * hd + vy * hw]]


def build_lots(T, rng):
    """生成城镇建筑地块：先放地标建筑，再沿街两侧排门面房，最后在剩余空地补后排房屋。"""
    wall = T["wall"]["rect"]; F = T["filler"]
    g = TownGrid(wall)
    # 城墙内侧留空
    g.mark(rect_inside_dist(g.X, g.Y, wall) < F["wall_clearance"] + T["wall"]["thickness"])
    # 街道与广场留空
    for st in T["streets"]:
        d, _, _ = polyline_nearest(g.X, g.Y, st["points"])
        g.mark(d < st["width"] / 2.0 + F["street_clearance"])
    pz = T["plaza"]
    g.mark(np.hypot(g.X - pz["center"][0], g.Y - pz["center"][1]) < pz["radius"] + F["street_clearance"])
    lots = []
    for b in T["landmark_buildings"]:
        x0, y0, x1, y1 = b["rect"]
        poly = [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]
        lots.append(dict(name=b["name"], func=b["func"], poly=poly, floors=b.get("floors", 0),
                         height=b.get("height", b.get("floors", 2) * 3.5 + 3.0), interior=b.get("interior", False), landmark=True))
        g.mark(g.poly_mask(poly, pad=1.0))
    def add(poly, name, func, floors):
        lots.append(dict(name=name, func=func, poly=[list(map(float, q)) for q in poly], floors=floors, height=floors * 3.5 + 3.0, interior=False, landmark=False))
        g.mark(g.poly_mask(poly, pad=F["gap"]))

    def try_place(base, u, n, setback, s_from, name, func, floor_rng):
        """在 base + u*s 处、沿 n 方向后退 setback 的位置，从大到小尝试面宽与进深，返回占用的面宽（失败返回 0）。
        u 为沿街方向，n 为背离街道的方向（门面朝向 -n）。"""
        for w in (rng.uniform(F["min_lot"], F["max_lot"]), F["min_lot"], 5.5):
            for dep in (rng.uniform(9.0, 12.0), 8.0, 6.5, 5.5):
                c = base + u * (s_from + w / 2.0) + n * (setback + dep / 2.0)
                poly = oriented_rect(c[0], c[1], -n[0], -n[1], w, dep)
                if g.free(poly):
                    add(poly, name, func, int(rng.integers(floor_rng[0], floor_rng[1] + 1)))
                    return w
        return 0.0

    def frontage(pts, setback, sides, name_fn, floor_rng):
        """沿折线连续推进排房：每次在当前弧长处放一栋，放不下就前进 1.5 m 再试。sides 为 (1, -1) 中要排房的侧别。"""
        pts = np.asarray(pts, dtype=np.float64)[:, :2]
        cum = polyline_lengths(pts)
        for sgn in sides:
            s = 0.5
            while s < cum[-1] - 3.0:
                k = min(int(np.searchsorted(cum, s, side="right")) - 1, len(pts) - 2)
                u = (pts[k + 1] - pts[k]) / (cum[k + 1] - cum[k]); n = np.array([-u[1], u[0]]) * sgn
                base = pts[k] - u * cum[k]                       # 使 base + u*s 落在当前段上
                name, func = name_fn()
                w = try_place(base, u, n, setback, s, name, func, floor_rng)
                s += (w + F["gap"]) if w > 0 else 1.0

    shop = lambda: ("商铺", "shop") if rng.uniform() < 0.4 else ("民居", "house")
    house = lambda: ("民居", "house")
    # 1) 沿街门面房（阶梯两侧不排）
    for st in T["streets"]:
        if st.get("stairs"): continue
        frontage(st["points"], st["width"] / 2.0 + F["street_clearance"], (1, -1), shop if st["width"] >= 5 else house, F["floors"])
    # 2) 广场周边：以广场外缘为“街道”，门面朝向广场
    cx, cy = pz["center"]; R = pz["radius"] + F["street_clearance"]
    circle = [[cx + math.cos(a) * R, cy + math.sin(a) * R] for a in np.linspace(0, 2 * math.pi, 49)]
    frontage(circle, 0.0, (-1,), lambda: ("广场建筑", "shop"), (F["floors"][0] + 1, F["floors"][1] + 1))
    # 3) 沿城墙内侧留空边界排一圈背靠城墙的房子（逆时针绕行，左侧 sgn=1 为城内）
    x0, y0, x1, y1 = wall; inset = T["wall"]["thickness"] + F["wall_clearance"] + F["gap"]
    ring = [[x0 + inset, y0 + inset], [x1 - inset, y0 + inset], [x1 - inset, y1 - inset], [x0 + inset, y1 - inset], [x0 + inset, y0 + inset]]
    frontage(ring, 0.0, (1,), house, F["floors"])
    # 4) 剩余空地补少量后排小屋，其余作为院落
    for t in T["terraces"]:
        tx0, ty0, tx1, ty1 = t["rect"]
        for _ in range(F["backfill_tries"]):
            w = rng.uniform(6.0, 10.0); d = rng.uniform(6.0, 10.0)
            x = rng.uniform(tx0, tx1 - w); y = rng.uniform(ty0, ty1 - d)
            poly = [[x, y], [x + w, y], [x + w, y + d], [x, y + d]]
            if g.free(poly) and rng.uniform() > F["skip_ratio"]:
                add(poly, "民居", "house", F["floors"][0])
    return lots, g


# ---------------------------------------------------------------- 校验

def slope_deg(h):
    gy, gx = np.gradient(h)
    return np.degrees(np.arctan(np.hypot(gx, gy)))


def bilinear(h, x, y):
    """在高度图上双线性采样（x, y 可为数组）。"""
    x = np.clip(np.asarray(x, dtype=np.float64), 0, h.shape[1] - 1.001); y = np.clip(np.asarray(y, dtype=np.float64), 0, h.shape[0] - 1.001)
    ix = np.floor(x).astype(int); iy = np.floor(y).astype(int); fx = x - ix; fy = y - iy
    return (h[iy, ix] * (1 - fx) + h[iy, ix + 1] * fx) * (1 - fy) + (h[iy + 1, ix] * (1 - fx) + h[iy + 1, ix + 1] * fx) * fy


def label_regions(walk):
    """4 邻接连通域标记（BFS），返回标签图，0 表示不可行走。"""
    lab = np.zeros(walk.shape, dtype=np.int32)
    H, W = walk.shape; cur = 0
    wl = walk.tolist(); ll = lab.tolist()
    for sy in range(H):
        for sx in range(W):
            if not wl[sy][sx] or ll[sy][sx]: continue
            cur += 1; ll[sy][sx] = cur; q = deque([(sy, sx)])
            while q:
                y, x = q.popleft()
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < H and 0 <= nx < W and wl[ny][nx] and not ll[ny][nx]:
                        ll[ny][nx] = cur; q.append((ny, nx))
    return np.array(ll, dtype=np.int32)


def region_at(lab, p, search=3):
    """取点附近（search 米内）第一个可行走格子的区域编号，避免点刚好落在不可走格子上。"""
    x, y = int(round(p[0])), int(round(p[1]))
    for r in range(search + 1):
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                v = lab[min(max(y + dy, 0), lab.shape[0] - 1), min(max(x + dx, 0), lab.shape[1] - 1)]
                if v: return int(v)
    return 0


def line_of_sight(hs, a, b, step=0.5):
    """a、b 为 (x, y, z)。沿视线采样，地形（含建筑）高于视线则遮挡。"""
    n = max(2, int(math.dist(a[:2], b[:2]) / step))
    t = np.linspace(0, 1, n)[1:-1]
    x = a[0] + (b[0] - a[0]) * t; y = a[1] + (b[1] - a[1]) * t; z = a[2] + (b[2] - a[2]) * t
    return bool((bilinear(hs, x, y) < z - 0.05).all())


def run_checks(L, h, info, lots, X, Y):
    rep = {"checks": [], "metrics": {}}

    def check(name, ok, detail):
        rep["checks"].append(dict(name=name, ok=bool(ok), detail=detail))

    sl = slope_deg(h)
    # 可行走：坡度 ≤ 40°，非深水，非城墙/建筑
    deep_sea = info["sea"] & (h < -1.2)
    rw = info["river_water"]
    deep_river = ~np.isnan(rw) & (np.nan_to_num(rw) - h > 1.2)
    blocked = np.zeros(h.shape, dtype=bool)
    T = L["town"]; x0, y0, x1, y1 = T["wall"]["rect"]; th = T["wall"]["thickness"]
    di = rect_inside_dist(X, Y, T["wall"]["rect"])
    wall_ring = (di > -0.5) & (di < th)
    for gt in T["gates"]:
        if gt["open"]:
            wall_ring &= ~((np.abs(X - gt["pos"][0]) <= gt["width"] / 2.0) & (np.abs(Y - gt["pos"][1]) <= th + 1))
    blocked |= wall_ring
    bmask = np.zeros(h.shape, dtype=bool)
    for lt in lots:
        poly = np.asarray(lt["poly"])
        bmask |= point_in_polygon(X, Y, poly)
    blocked |= bmask
    # 溪沟：设计上两岸为陡岸（岩石模型），1 m 网格表达不出，按“需要跳过或攀爬上岸”处理为不可直接行走
    walk = (sl <= 40.0) & ~deep_sea & ~deep_river & ~blocked & ~info["stream"]
    # 桥面可走；hw 为“可站立表面”高度（地形 + 桥面），路线剖面和道路坡度都在它上面采样
    hw = h.copy()
    for r in L["roads"]:
        if r["kind"] == "bridge":
            pts = np.asarray(r["points"], dtype=np.float64)
            d, s_, _ = polyline_nearest(X, Y, pts)
            Lb = polyline_lengths(pts)[-1]
            deck = (d < r["width"] / 2.0) & (s_ > 0.0) & (s_ < Lb)          # 只覆盖桥身，不在两端外扩圆头
            walk |= deck
            hw = np.where(deck, np.interp(s_, polyline_lengths(pts), pts[:, 2]), hw)
    fpos = next(p["pos"] for p in L["pois"] if p["id"] == "F")
    bramble = np.hypot(X - fpos[0], Y - fpos[1]) < 3.5

    lab_closed = label_regions(walk & ~bramble)
    lab_open = label_regions(walk)
    P = {p["id"]: p["pos"] for p in L["pois"]}
    c_base, c_top = (165, 144), (178, 165)
    rA, rCb, rCt = region_at(lab_closed, P["A"]), region_at(lab_closed, c_base), region_at(lab_closed, c_top)
    check("出生区与崖下窄带不连通（必须跳过溪沟）", rA != rCb and rA and rCb, f"A 区域 {rA}，崖下 {rCb}")
    check("崖下与崖上不连通（必须攀爬）", rCb != rCt and rCt, f"崖下 {rCb}，崖上 {rCt}")
    for pid in ("D", "E", "chest"):
        r = region_at(lab_closed, P[pid])
        check(f"{pid} 与崖上同属一个可行走区域", r == rCt, f"{pid} 区域 {r}")
    rG_closed = region_at(lab_closed, P["G"])
    check("荆棘未烧时，崖上区域到不了神像高地", rG_closed != rCt, f"神像区域 {rG_closed}，崖上 {rCt}")
    rE_open = region_at(lab_open, P["E"])
    for pid in ("G", "H", "I", "J"):
        r = region_at(lab_open, P[pid])
        check(f"荆棘烧掉后 {pid} 可达", r == rE_open, f"{pid} 区域 {r}，战斗空地 {rE_open}")
    plaza = T["plaza"]["center"]
    check("荆棘烧掉后城镇广场可达", region_at(lab_open, plaza) == rE_open, f"广场区域 {region_at(lab_open, plaza)}")

    # 悬崖带高度：沿线每 2 m 量南北两侧 6 m 处的高差
    esc = L["escarpment"]
    pts, _ = sample_polyline(esc["points"], 2.0)
    cc = np.asarray(esc["climb_section"]["center"]); cr = esc["climb_section"]["radius"]
    climb_h, other_h = [], []
    epts = np.asarray(esc["points"], dtype=np.float64)
    for i, p in enumerate(pts):
        j = min(i + 1, len(pts) - 1); k = max(i - 1, 0)
        t = pts[j] - pts[k]; t /= np.hypot(*t); n = np.array([-t[1], t[0]])
        hn = bilinear(h, *(p + n * 4)); hs = bilinear(h, *(p - n * 4))
        if info["sea"][int(p[1]), int(p[0])] or hs < 1.5: continue
        dc_ = np.hypot(*(p - cc))
        if dc_ < cr: climb_h.append(float(hn - hs))
        elif dc_ > cr + 14: other_h.append(float(hn - hs))           # 教学崖两侧 14 m 为高度过渡段，不计入
    rep["metrics"]["climb_cliff_height_m"] = [round(min(climb_h), 2), round(max(climb_h), 2)]
    rep["metrics"]["escarpment_other_min_m"] = round(min(other_h), 2)
    check("攀爬教学崖高 5～6 m", 5.0 <= min(climb_h) and max(climb_h) <= 6.0, f"{min(climb_h):.1f}～{max(climb_h):.1f} m")
    check("悬崖带其余段高于教学崖（≥ 7 m）", min(other_h) >= 7.0, f"最低 {min(other_h):.1f} m")
    cs = sl[int(cc[1]) - 2:int(cc[1]) + 3, int(cc[0]) - 2:int(cc[0]) + 3].max()
    check("教学崖崖面坡度 > 50°（需要攀爬）", cs > 50, f"{cs:.0f}°")

    # 道路坡度
    limits = {"main": 12.0, "trail": 20.0, "corridor": 18.0}
    road_rep = []
    for r in L["roads"]:
        if r["kind"] == "bridge": continue
        sp, s = sample_polyline(r["points"], 1.0)
        hh = bilinear(hw, sp[:, 0], sp[:, 1])
        if len(hh) < 4: continue
        grade = np.degrees(np.arctan(np.abs(hh[3:] - hh[:-3]) / 3.0))
        mg = float(grade.max()); lim = limits[r["kind"]]
        road_rep.append(dict(id=r["id"], name=r["name"], max_grade_deg=round(mg, 1), limit=lim))
        check(f"道路坡度：{r['name']} ≤ {lim:.0f}°", mg <= lim, f"最大 {mg:.1f}°")
    for st in T["streets"]:
        sp, s = sample_polyline(st["points"], 1.0)
        hh = bilinear(h, sp[:, 0], sp[:, 1])
        grade = np.degrees(np.arctan(np.abs(hh[3:] - hh[:-3]) / 3.0)) if len(hh) > 3 else np.array([0.0])
        lim = 35.0 if st.get("stairs") else 10.0
        check(f"城镇街道坡度：{st['name']} ≤ {lim:.0f}°" + ("（阶梯）" if st.get("stairs") else ""), grade.max() <= lim, f"最大 {grade.max():.1f}°")
    rep["roads"] = road_rep

    # 平地与高地
    E = next(f for f in L["flats"] if f["id"] == "E")
    m = np.hypot(X - E["center"][0], Y - E["center"][1]) < E["radius"]
    check("战斗空地平整（半径内高差 ≤ 1 m）", np.ptp(h[m]) <= 1.0, f"高差 {np.ptp(h[m]):.2f} m")
    Gp = P["G"]; gh = float(h[Gp[1], Gp[0]])
    rep["metrics"]["statue_ground_m"] = round(gh, 2)

    # 主路线：跌落检查（相邻 1 m 采样下降 > 1.5 m 视为跌落，预期的跳溪除外）
    rp, rs = sample_polyline(L["route"]["points"], 1.0)
    rh = bilinear(hw, rp[:, 0], rp[:, 1])
    rep["route_profile"] = [[round(float(s), 1), round(float(z), 2)] for s, z in zip(rs[::2], rh[::2])]
    rep["metrics"]["route_length_m"] = round(float(rs[-1]), 1)
    drops = []
    for i in range(1, len(rh)):
        dz = rh[i - 1] - rh[i]
        near_expected = any(math.dist(rp[i], e["at"]) < 4 for e in L["route"]["expected_steps"])
        if dz > 1.5 and not near_expected: drops.append((round(float(rs[i]), 1), round(float(dz), 2)))
    check("主路线上没有非预期跌落（相邻 1 m 下降 ≤ 1.5 m）", not drops, f"{len(drops)} 处" + (f"，首个在 {drops[0][0]} m 处下降 {drops[0][1]} m" if drops else ""))

    # 可见性：地形 + 建筑
    def occluders(skip_func=None):
        """地形 + 建筑高度（建筑按地块最高地面 + 建筑高度）。skip_func 指定的建筑不参与遮挡（被看的目标本身）。"""
        out = h.copy()
        for lt in lots:
            if lt["func"] == skip_func: continue
            mk = point_in_polygon(X, Y, np.asarray(lt["poly"]))
            if mk.any(): out[mk] = np.maximum(out[mk], h[mk].max() + lt["height"])
        return out
    hs = occluders()
    hs_t = occluders("landmark")
    statue = (Gp[0], Gp[1], gh + next(p for p in L["pois"] if p["id"] == "G")["height_above"])
    tower = next(b for b in T["landmark_buildings"] if b["func"] == "landmark")
    tcx = (tower["rect"][0] + tower["rect"][2]) / 2; tcy = (tower["rect"][1] + tower["rect"][3]) / 2
    tower_top = (tcx, tcy, float(h[int(tcy), int(tcx)]) + tower["height"] - 0.5)
    # 神像：从 C 崖顶到 F 之前的路线点
    idx_c = int(np.argmin(np.hypot(rp[:, 0] - c_top[0], rp[:, 1] - c_top[1]))); idx_f = int(np.argmin(np.hypot(rp[:, 0] - P["F"][0], rp[:, 1] - P["F"][1])))
    vis = [line_of_sight(hs, (rp[i, 0], rp[i, 1], rh[i] + 1.6), statue) for i in range(idx_c, idx_f, 5)]
    frac = sum(vis) / max(1, len(vis))
    rep["metrics"]["statue_visible_C_to_F"] = round(frac, 2)
    check("C 崖顶到 F 之间，神像在 ≥ 70% 的路线点可见", frac >= 0.7, f"{frac * 100:.0f}%")
    # 钟楼：从神像高地边缘和下坡路
    cam = next(c for c in L["cameras"] if c["name"] == "神像全景揭示")
    cz = float(h[cam["pos"][1], cam["pos"][0]]) + cam["eye_height"]
    v = line_of_sight(hs_t, (cam["pos"][0], cam["pos"][1], cz), tower_top)
    check("神像全景机位能看到钟楼", v, "可见" if v else "被遮挡")
    idx_h = int(np.argmin(np.hypot(rp[:, 0] - P["H"][0], rp[:, 1] - P["H"][1]))); idx_j = int(np.argmin(np.hypot(rp[:, 0] - 400, rp[:, 1] - 352)))
    vis = [line_of_sight(hs_t, (rp[i, 0], rp[i, 1], rh[i] + 1.6), tower_top) for i in range(idx_h, idx_j, 5)]
    frac = sum(vis) / max(1, len(vis))
    check("下坡主路上，钟楼在 ≥ 70% 的路线点可见", frac >= 0.7, f"{frac * 100:.0f}%")
    gate_eye = (405, 377, float(h[377, 405]) + 1.6)
    v1 = line_of_sight(hs, gate_eye, (T["plaza"]["center"][0], T["plaza"]["center"][1], T["plaza"]["height"] + 1.0))
    v2 = line_of_sight(hs_t, gate_eye, tower_top)
    if not v2:
        n_ = int(math.dist(gate_eye[:2], tower_top[:2]) / 0.5); t_ = np.linspace(0, 1, n_)[1:-1]
        xs = gate_eye[0] + (tower_top[0] - gate_eye[0]) * t_; ys = gate_eye[1] + (tower_top[1] - gate_eye[1]) * t_; zs = gate_eye[2] + (tower_top[2] - gate_eye[2]) * t_
        hit = np.nonzero(bilinear(hs_t, xs, ys) >= zs - 0.05)[0]
        rep["metrics"]["gate_tower_block"] = [round(float(xs[hit[0]]), 1), round(float(ys[hit[0]]), 1), round(float(zs[hit[0]]), 1)]
    check("进南门第一眼能看到广场和钟楼", v1 and v2, f"广场{'可见' if v1 else '被挡'}，钟楼{'可见' if v2 else '被挡'}")

    # 东侧山脊陡度（边界）
    mm = info["mountain_mask"] & (X < np.interp(Y, np.asarray(L["mountain"]["edge"])[:, 1], np.asarray(L["mountain"]["edge"])[:, 0]) + 8) & (Y > 60) & (Y < 360)
    check("东侧山脊坡面 > 60°（不可行走）", np.percentile(sl[mm], 10) > 60, f"10 分位坡度 {np.percentile(sl[mm], 10):.0f}°")

    rep["metrics"]["walkable_ratio"] = round(float(walk.mean()), 3)
    rep["metrics"]["building_count"] = len(lots)
    rep["passed"] = sum(c["ok"] for c in rep["checks"]); rep["total"] = len(rep["checks"])
    return rep, sl


# ---------------------------------------------------------------- 着色与输出

def write_png(path, rgb):
    """最小 PNG 写出器（RGB8，无依赖）。"""
    H, W, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(H))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def render_region(L, h, info, lots, region, ppm):
    """渲染 region = (x0, y0, x1, y1) 范围的彩色俯视图，ppm 为每米像素数。
    地表按类型着色 + 西北光源山体阴影 + 5 m 等高线，叠加道路、水体、城墙与建筑（含投影）。输出北在上。"""
    def col(hexs): return np.array([int(hexs[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float64) / 255.0
    x0, y0, x1, y1 = region
    xs = x0 + (np.arange(int((x1 - x0) * ppm)) + 0.5) / ppm; ys = y0 + (np.arange(int((y1 - y0) * ppm)) + 0.5) / ppm
    X, Y = np.meshgrid(xs, ys)
    hf = bilinear(h, X, Y)
    iy = np.clip(np.rint(Y).astype(int), 0, h.shape[0] - 1); ix = np.clip(np.rint(X).astype(int), 0, h.shape[1] - 1)
    sea = info["sea"][iy, ix]; mountain = info["mountain_mask"][iy, ix]; stream = info["stream"][iy, ix]
    rwater = info["river_water"][iy, ix]
    gy, gx = np.gradient(hf, 1.0 / ppm)
    sl = np.degrees(np.arctan(np.hypot(gx, gy)))
    # 地表
    t = np.clip(hf / 45.0, 0, 1)[..., None]
    rgb = col("#9cc66a") * (1 - t) + col("#6f9e4c") * t
    rock = smoothstep(38, 52, sl)[..., None]
    rgb = rgb * (1 - rock) + col("#9a9488") * rock
    n = value_noise(X, Y, 3.0, 7, 1)
    for f in L["forests"]:
        m = point_in_polygon(X, Y, f["polygon"]) & (sl < 45)
        rgb[m] = rgb[m] * 0.35 + col("#3f6b3a") * (0.85 + 0.15 * n[m, None])
    rgb[(~sea) & (hf < 2.2) & ~mountain] = col("#e7d9a8")
    for r in L["roads"]:
        d, _, _ = polyline_nearest(X, Y, r["points"])
        rgb[d < r["width"] / 2.0] = col("#7d7468") if r["kind"] == "bridge" else (col("#a99a80") if r["kind"] == "corridor" else col("#c9a776"))
    T = L["town"]
    rgb[rect_inside_dist(X, Y, T["wall"]["rect"]) > 0] = col("#d8cdb8")
    for st in T["streets"]:
        d, _, _ = polyline_nearest(X, Y, st["points"])
        rgb[d < st["width"] / 2.0] = col("#bdb09a")
    pz = T["plaza"]
    rgb[np.hypot(X - pz["center"][0], Y - pz["center"][1]) < pz["radius"]] = col("#c9bda6")
    # 水体
    depth = np.clip(-hf / 6.0, 0, 1)[..., None]
    rgb = np.where(sea[..., None], col("#a8d4dc") * (1 - depth) + col("#5c9fb3") * depth, rgb)
    rgb[~np.isnan(rwater) & (hf < np.nan_to_num(rwater) - 0.05)] = col("#6fb3c8")
    rgb[stream] = col("#7cc0d0")
    # 山体阴影
    nrm = np.stack([-gx, -gy, np.ones_like(hf)], -1); nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    ld = np.array([-0.55, 0.55, 0.63]); ld /= np.linalg.norm(ld)
    rgb = rgb * (0.55 + 0.6 * np.clip(nrm @ ld, 0, 1)[..., None])
    # 建筑投影（光从西北来，影子落向东南，长度与建筑高度成正比）
    for lt in lots:
        poly = np.asarray(lt["poly"]); off = np.array([1.0, -1.0]) * lt["height"] * 0.18
        m = point_in_polygon(X, Y, poly + off) & ~point_in_polygon(X, Y, poly)
        rgb[m] *= 0.72
    # 城墙
    di = rect_inside_dist(X, Y, T["wall"]["rect"])
    wall = (di > -T["wall"]["thickness"] / 2) & (di < T["wall"]["thickness"] / 2)
    for gt in T["gates"]:
        if gt["open"]: wall &= ~((np.abs(X - gt["pos"][0]) <= gt["width"] / 2) & (np.abs(Y - gt["pos"][1]) <= 3))
    rgb[wall] = col("#8b8070")
    # 建筑：屋顶颜色按功能区分，外圈描边
    roof = {"landmark": "#4f6f8f", "church": "#5d7690", "knights": "#5a6f87", "guild": "#b8593b", "tavern": "#a8613a"}
    for lt in lots:
        poly = np.asarray(lt["poly"])
        m = point_in_polygon(X, Y, poly)
        c = col(roof.get(lt["func"], "#c4744b" if lt["floors"] <= 2 else "#b0603f"))
        rgb[m] = c * (0.9 + 0.1 * (lt["floors"] / 4.0))
        inner = point_in_polygon(X, Y, poly.mean(0) + (poly - poly.mean(0)) * (1 - 1.2 / max(4.0, np.ptp(poly, 0).min())))
        rgb[m & ~inner] *= 0.75
    # 等高线（每 5 m）
    lv = np.floor(hf / 5.0)
    edge = (np.abs(np.diff(lv, axis=0, prepend=lv[:1])) + np.abs(np.diff(lv, axis=1, prepend=lv[:, :1]))) > 0
    edge &= (hf > 0.5) & (sl < 50)
    rgb[edge] *= 0.75
    return (np.clip(rgb, 0, 1) * 255).astype(np.uint8)[::-1]


# ---------------------------------------------------------------- Unity 白盒导出

SURFACE = ["grass", "rock", "dirt", "sand", "town", "forest", "street", "underwater"]


def surface_classes(L, h, info, X, Y):
    """每个高度图格子的地表类别（索引见 SURFACE），供 Unity 地形绘制贴图层。后写的类别优先级更高。"""
    sl = slope_deg(h)
    c = np.zeros(h.shape, dtype=np.uint8)
    for f in L["forests"]:
        c[point_in_polygon(X, Y, f["polygon"])] = 5
    c[(sl > 42) | (info["mountain_mask"] & (sl > 30))] = 1
    c[(~info["sea"]) & (h < 2.2) & ~info["mountain_mask"] & (sl < 30)] = 3
    for r in L["roads"]:
        if r["kind"] == "bridge": continue
        d, _, _ = polyline_nearest(X, Y, r["points"])
        c[d < r["width"] / 2.0] = 2
    T = L["town"]
    c[rect_inside_dist(X, Y, T["wall"]["rect"]) > 0] = 4
    for st in T["streets"]:
        d, _, _ = polyline_nearest(X, Y, st["points"])
        c[d < st["width"] / 2.0] = 6
    pz = T["plaza"]
    c[np.hypot(X - pz["center"][0], Y - pz["center"][1]) < pz["radius"]] = 6
    g = next(f for f in L["flats"] if f["id"] == "G")
    c[np.hypot(X - g["center"][0], Y - g["center"][1]) < g["radius"]] = 6
    rw = info["river_water"]
    c[info["sea"] | (~np.isnan(rw) & (h < np.nan_to_num(rw))) | info["stream"]] = 7
    return c


def scatter_trees(L, h, info, lots, X, Y, rng):
    """树木占位散布：林地内抖动网格、林外稀疏。返回 [x, y, 地面, 高度, 树冠半径]。"""
    V = L["vegetation"]; sl = slope_deg(h)
    blocked = info["sea"] | info["stream"] | ~np.isnan(info["river_water"]) | info["plateau_mask"] | (sl > V["max_slope"])
    blocked |= rect_inside_dist(X, Y, L["town"]["wall"]["rect"]) > -6
    for r in L["roads"]:
        d, _, _ = polyline_nearest(X, Y, r["points"])
        blocked |= d < r["width"] / 2.0 + V["road_clearance"]
    d, _, _ = polyline_nearest(X, Y, L["route"]["points"])
    near_route = d < V["route_clearance"]
    for f in L["flats"]:
        blocked |= np.hypot(X - f["center"][0], Y - f["center"][1]) < f["radius"] + 4
    for it in L["props"]["items"]:
        blocked |= np.hypot(X - it["pos"][0], Y - it["pos"][1]) < 6
    for c in L["cameras"]:                                  # 镜头机位周围与视线前方 25 m 不种树
        blocked |= np.hypot(X - c["pos"][0], Y - c["pos"][1]) < 18
        d_, _, _ = polyline_nearest(X, Y, [c["pos"], [c["pos"][0] + (c["look_at"][0] - c["pos"][0]) * 0.6, c["pos"][1] + (c["look_at"][1] - c["pos"][1]) * 0.6]])
        blocked |= d_ < 5
    cs = L["escarpment"]["climb_section"]                   # 教学崖面前留出视线
    blocked |= np.hypot(X - cs["center"][0], Y - cs["center"][1]) < cs["radius"] + 8
    for p in L["pois"]:
        if p["kind"] in ("npc", "respawn"): blocked |= np.hypot(X - p["pos"][0], Y - p["pos"][1]) < 4
    forest = np.zeros(h.shape, dtype=bool)
    for f in L["forests"]:
        forest |= point_in_polygon(X, Y, f["polygon"])
    trees = []
    def try_add(x, y, dense):
        ix, iy = int(round(x)), int(round(y))
        if not (0 <= ix < h.shape[1] and 0 <= iy < h.shape[0]) or blocked[iy, ix]: return
        if not dense and near_route[iy, ix]: return
        ht = rng.uniform(7.0, 12.0) if dense else rng.uniform(8.0, 13.0)
        trees.append([round(x, 2), round(y, 2), round(float(bilinear(h, x, y)), 2), round(ht, 1), round(ht * rng.uniform(0.28, 0.36), 2)])
    sp = V["forest_spacing"]
    for gy in np.arange(sp / 2, L["size"], sp):
        for gx in np.arange(sp / 2, L["size"], sp):
            x, y = gx + rng.uniform(-0.4, 0.4) * sp, gy + rng.uniform(-0.4, 0.4) * sp
            ix, iy = int(min(max(round(x), 0), L["size"])), int(min(max(round(y), 0), L["size"]))
            if forest[iy, ix]: try_add(x, y, True)
    sp = V["meadow_spacing"]
    for gy in np.arange(sp / 2, L["size"], sp):
        for gx in np.arange(sp / 2, L["size"], sp):
            x, y = gx + rng.uniform(-0.45, 0.45) * sp, gy + rng.uniform(-0.45, 0.45) * sp
            ix, iy = int(min(max(round(x), 0), L["size"])), int(min(max(round(y), 0), L["size"]))
            if not forest[iy, ix] and rng.uniform() < V["meadow_prob"]: try_add(x, y, False)
    return trees


def export_whitebox(L, h, info, lots, pois, cams, X, Y):
    """写出 Unity 白盒构建器读取的数据：whitebox.json（JsonUtility 可直接解析的结构）与 surface.bytes（地表类别图）。"""
    rng = np.random.default_rng(L["seed"] + 1)
    surf = surface_classes(L, h, info, X, Y)
    surf.tofile(os.path.join(OUT, "surface.bytes"))
    v3 = lambda x, y, z: dict(x=round(float(x), 2), y=round(float(y), 2), z=round(float(z), 2))
    waters = [dict(name="河流", halfWidth=L["river"]["half_width"] + 1.0, points=[v3(x, y, w) for x, y, w in L["river"]["points"]])]
    st = L["stream"]
    sp, _ = sample_polyline(st["points"], 4.0)
    waters.append(dict(name="溪沟", halfWidth=st["width"] / 2.0 + 0.2, points=[v3(x, y, float(bilinear(h, x, y)) + 0.45) for x, y in sp]))
    bridges = [dict(name=r["name"], width=r["width"], points=[v3(*q) for q in r["points"]]) for r in L["roads"] if r["kind"] == "bridge"]
    T = L["town"]; x0, y0, x1, y1 = T["wall"]["rect"]
    wall = dict(x0=x0, y0=y0, x1=x1, y1=y1, thickness=T["wall"]["thickness"], height=T["wall"]["height"],
                ground=round(float(np.max(h[int(y0):int(y1) + 1, int(x0):int(x1) + 1][rect_inside_dist(X, Y, T["wall"]["rect"])[int(y0):int(y1) + 1, int(x0):int(x1) + 1] < 3])), 2))
    gates = [dict(name=g["name"], x=g["pos"][0], y=g["pos"][1], width=g["width"], open=g["open"]) for g in T["gates"]]
    buildings = [dict(name=lt["name"], func=lt["func"], landmark=lt["landmark"], interior=lt["interior"], floors=lt["floors"], height=lt["height"], base=lt["base"],
                      corners=[dict(x=q[0], y=q[1]) for q in lt["poly"]]) for lt in lots]
    road_by_id = {r["id"]: r for r in L["roads"]}
    props = []
    for it in L["props"]["items"]:
        x, y = it["pos"]; yaw = 0.0
        if "align_to" in it:
            pts = np.asarray(road_by_id[it["align_to"]]["points"], dtype=np.float64)[:, :2]
            k = int(np.argmin(np.hypot(pts[:, 0] - x, pts[:, 1] - y))); k = min(k, len(pts) - 2)
            dvec = pts[k + 1] - pts[k]; yaw = math.degrees(math.atan2(dvec[0], dvec[1]))      # 道路方向相对北的偏角（顺时针）
        props.append(dict(kind=it["kind"], name=it["name"], x=x, y=y, ground=round(float(bilinear(h, x, y)), 2), yaw=round(yaw, 1),
                          sx=it["size"][0], sy=it["size"][1], sz=it["size"][2]))
    trees = [dict(x=t[0], y=t[1], ground=t[2], height=t[3], radius=t[4]) for t in scatter_trees(L, h, info, lots, X, Y, rng)]
    poi_out = [dict(id=p["id"], name=p["name"], kind=p["kind"], x=p["pos"][0], y=p["pos"][1], ground=p["ground"]) for p in pois]
    cam_out = []
    for c in cams:
        lx, ly = c["look_at"]
        eye = c.get("eye_abs", c["ground"] + c["eye_height"])          # 站在桥面等非地形表面时用绝对眼高
        cam_out.append(dict(name=c["name"], x=c["pos"][0], y=c["pos"][1], eye=round(eye, 2), lookX=lx, lookY=ly, lookZ=round(float(bilinear(h, lx, ly)) + 1.5, 2)))
    data = dict(size=L["size"], resolution=L["size"] + 1, heightMin=L["height_min"], heightMax=L["height_max"], surfaceNames=SURFACE,
                waters=waters, bridges=bridges, wall=wall, gates=gates, buildings=buildings, props=props, trees=trees, pois=poi_out, cameras=cam_out,
                route=[v3(x, y, float(bilinear(h, x, y))) for x, y in sample_polyline(L["route"]["points"], 2.0)[0]])
    with open(os.path.join(OUT, "whitebox.json"), "w", encoding="utf-8") as f: json.dump(data, f, ensure_ascii=False)
    return len(trees)


def main():
    with open(LAYOUT, encoding="utf-8") as f: L = json.load(f)
    N = L["size"] + 1
    g = np.arange(N, dtype=np.float64)
    X, Y = np.meshgrid(g, g)                                         # h[y, x]
    info = {}
    h = build_terrain(L, X, Y, info)
    rng = np.random.default_rng(L["seed"])
    lots, _ = build_lots(L["town"], rng)
    rep, sl = run_checks(L, h, info, lots, X, Y)
    os.makedirs(OUT, exist_ok=True)
    lo, hi = L["height_min"], L["height_max"]
    enc = np.round((h - lo) / (hi - lo) * 65535).astype("<u2")
    enc.tofile(os.path.join(OUT, "heightmap.r16"))
    with open(os.path.join(OUT, "heightmap_meta.json"), "w", encoding="utf-8") as f:
        json.dump(dict(resolution=N, size_m=L["size"], height_min=lo, height_max=hi, encoding="uint16 little-endian, row = y (north), col = x (east)"), f, ensure_ascii=False, indent=2)
    write_png(os.path.join(OUT, "layout_map.png"), render_region(L, h, info, lots, (0, 0, L["size"], L["size"]), 2))
    wx0, wy0, wx1, wy1 = L["town"]["wall"]["rect"]
    write_png(os.path.join(OUT, "town_map.png"), render_region(L, h, info, lots, (wx0 - 8, wy0 - 16, wx1 + 8, wy1 + 6), 6))
    # 解析结果：POI 地面高度、道路剖面、地块
    pois = [dict(p, ground=round(float(h[p["pos"][1], p["pos"][0]]), 2)) for p in L["pois"]]
    cams = [dict(c, ground=round(float(h[c["pos"][1], c["pos"][0]]), 2)) for c in L["cameras"]]
    for lt in lots:
        poly = np.asarray(lt["poly"])
        lt["base"] = round(float(max(bilinear(h, poly[:, 0], poly[:, 1]))), 2)
        lt["poly"] = [[round(x, 2), round(y, 2)] for x, y in lt["poly"]]
        lt["height"] = round(lt["height"], 1)
    # 各区域在主路线上的里程（投影到路线采样点），用于剖面图标注
    rp, rs = sample_polyline(L["route"]["points"], 1.0)
    for p in pois:
        if p["kind"] == "zone":
            i = int(np.argmin(np.hypot(rp[:, 0] - p["pos"][0], rp[:, 1] - p["pos"][1])))
            p["route_s"] = round(float(rs[i]), 1)
    resolved = dict(pois=pois, cameras=cams, lots=lots, route_profile=rep["route_profile"], roads=rep["roads"])
    with open(os.path.join(OUT, "layout_resolved.json"), "w", encoding="utf-8") as f: json.dump(resolved, f, ensure_ascii=False, indent=1)
    with open(os.path.join(OUT, "report.json"), "w", encoding="utf-8") as f: json.dump(rep, f, ensure_ascii=False, indent=1)
    n_trees = export_whitebox(L, h, info, lots, pois, cams, X, Y)
    print(f"白盒数据：树木占位 {n_trees} 棵，道具 {len(L['props']['items'])} 个")
    print(f"高度范围 {h.min():.1f}～{h.max():.1f} m，建筑 {len(lots)} 栋，校验 {rep['passed']}/{rep['total']}")
    for c in rep["checks"]: print(("  通过 " if c["ok"] else "  失败 ") + c["name"] + " —— " + c["detail"])
    print("指标", json.dumps(rep["metrics"], ensure_ascii=False))


if __name__ == "__main__":
    main()
