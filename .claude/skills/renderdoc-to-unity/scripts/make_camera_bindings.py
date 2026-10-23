"""Find the camera-dependent values inside a replayed frame's constant buffers, so the frame can be re-rendered from a
different viewpoint (free camera).

Input : <frame dir>/frame.json (written by `run_in_renderdoc.py frame`)
Output: frame.json updated in place:
          "camera": {"view": [16], "proj": [16], "view_proj": [16]}   (row-major, column-vector convention clip = P*V*x)
          every vs_cb / ps_cb entry gets "patches": [{"offset", "kind", "layout", "model"?}]

How it works
  1. The capture camera's view-projection is the most frequent matrix in the frame's cbuffers that has the perspective
     signature (w row = unit view axis, z row parallel to it) and is not a bare projection. It is decomposed into
     P (perspective, possibly off-centre) and V (rigid view, GL convention: camera looks down -z).
  2. Every 16-byte aligned 64-byte window is tested, in column- and row-major layout, against values derived from
     the camera: VP, VP^-1, V, V^-1, F*V (Unity's worldToCamera, F = flip z) and its inverse, rotation-only VP (sky)
     and its inverse, and MVP (VP^-1 * W is affine: per-object matrix; the model M is stored). Then float3 runs equal
     to the camera position.
  3. At replay time the importer/player rewrites exactly those bytes from a new view matrix V'.

Normal Python 3.7+, no packages.
"""
import base64
import collections
import json
import math
import os
import struct
import sys

KINDS = ["view_proj", "inv_view_proj", "view", "inv_view", "world_to_camera", "camera_to_world",
         "view_proj_rot", "inv_view_proj_rot", "model_view_proj", "camera_pos"]


def mm(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def inv(m):
    a = [list(r) + [1.0 if i == j else 0.0 for j in range(4)] for i, r in enumerate(m)]
    for c in range(4):
        p = max(range(c, 4), key=lambda r: abs(a[r][c]))
        if abs(a[p][c]) < 1e-12:
            return None
        a[c], a[p] = a[p], a[c]
        d = a[c][c]
        a[c] = [x / d for x in a[c]]
        for r in range(4):
            if r != c and a[r][c] != 0.0:
                f = a[r][c]
                a[r] = [x - f * y for x, y in zip(a[r], a[c])]
    return [r[4:] for r in a]


def from_floats(f, layout):
    if layout == "col":
        return [[f[c * 4 + r] for c in range(4)] for r in range(4)]
    return [[f[r * 4 + c] for c in range(4)] for r in range(4)]


def flat(m):
    return [m[r][c] for r in range(4) for c in range(4)]


def norm3(v):
    return math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])


def dot3(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def close(a, b, tol=2e-3):
    scale = max(1.0, max(abs(x) for r in b for x in r))
    return all(abs(a[r][c] - b[r][c]) <= tol * scale for r in range(4) for c in range(4))


def finite(f):
    return all(math.isfinite(x) and abs(x) < 1e12 for x in f)


def perspective_signature(m):
    r2, r3 = m[2], m[3]
    n = norm3(r3)
    if abs(n - 1.0) > 2e-3:
        return False
    c = [r2[1] * r3[2] - r2[2] * r3[1], r2[2] * r3[0] - r2[0] * r3[2], r2[0] * r3[1] - r2[1] * r3[0]]
    return norm3(c) <= 1e-3 * max(1e-6, norm3(r2)) and norm3(r2) > 1e-6


def decompose(vp):
    """VP = P * V with P = [[a,0,b,0],[0,c,d,0],[0,0,e,f],[0,0,-1,0]] and V rigid (rows orthonormal)."""
    v2 = [-x for x in vp[3]]
    rows = []
    pp = []
    for r in (vp[0], vp[1]):
        b = dot3(r, v2)
        t = [x - b * y for x, y in zip(r, v2)]
        a = norm3(t)
        if a < 1e-9:
            return None
        rows.append([x / a for x in t])
        pp.append((a, b))
    e = dot3(vp[2], v2)
    f = vp[2][3] - e * v2[3]
    v = [rows[0], rows[1], v2, [0.0, 0.0, 0.0, 1.0]]
    p = [[pp[0][0], 0, pp[0][1], 0], [0, pp[1][0], pp[1][1], 0], [0, 0, e, f], [0, 0, -1.0, 0]]
    if abs(dot3(rows[0], rows[1])) > 2e-3 or not close(mm(p, v), vp, 1e-3):
        return None
    return p, v


def is_affine(m, tol=1e-3):
    s = max(1.0, max(abs(x) for r in m[:3] for x in r))
    return abs(m[3][0]) <= tol * s and abs(m[3][1]) <= tol * s and abs(m[3][2]) <= tol * s and abs(m[3][3] - 1.0) <= tol


def is_identity(m, tol=1e-4):
    return close(m, [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)], tol)


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: make_camera_bindings.py <frame dir>")
    path = os.path.join(sys.argv[1], "frame.json")
    frame = json.load(open(path, encoding="utf-8"))
    draws = [o for o in frame["ops"] if o["op"] == "draw"]

    def cbs(o):
        for field in ("vs_cb", "ps_cb"):
            for cb in o.get(field, []):
                raw = base64.b64decode(cb["data"])
                yield cb, struct.unpack("<%df" % (len(raw) // 4), raw[: len(raw) // 4 * 4])

    # 1. the camera VP
    votes, sample = collections.Counter(), {}
    for o in draws:
        seen = set()
        for cb, f in cbs(o):
            for off in range(0, len(f) - 15, 4):
                w = f[off:off + 16]
                if not finite(w):
                    continue
                for layout in ("col", "row"):
                    m = from_floats(w, layout)
                    if not perspective_signature(m):
                        continue
                    d = decompose(m)
                    if d is None or is_identity(d[1], 1e-3):
                        continue
                    key = tuple(round(x, 3) for x in flat(m))
                    if key not in seen:
                        seen.add(key)
                        votes[key] += 1
                        sample[key] = m
    if not votes:
        sys.exit("no perspective view-projection found in the frame's constant buffers")
    key, n = votes.most_common(1)[0]
    vp = sample[key]
    proj, view = decompose(vp)
    others = [(c, k) for k, c in votes.most_common(6)[1:]]
    print("camera view-projection found in %d draws; other perspective matrices: %s" % (n, [c for c, _ in others]))
    ivp, iv = inv(vp), inv(view)
    F = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, -1, 0], [0, 0, 0, 1]]
    w2c = mm(F, view)
    vrot = [r[:3] + [0.0] for r in view[:3]] + [[0, 0, 0, 1.0]]
    vprot = mm(proj, vrot)
    targets = [("view_proj", vp), ("inv_view_proj", ivp), ("view", view), ("inv_view", iv),
               ("world_to_camera", w2c), ("camera_to_world", inv(w2c)),
               ("view_proj_rot", vprot), ("inv_view_proj_rot", inv(vprot))]
    eye = [iv[0][3], iv[1][3], iv[2][3]]
    eye_ok = norm3(eye) > 0.5
    print("camera position %s%s" % ([round(x, 3) for x in eye], "" if eye_ok else " (too close to the origin: position matching disabled)"))

    # 2. scene objects: a draw whose fitted clip transform is VP * M with M affine renders an object placed by M.
    #    Objects are grouped by M; any draw (shadow casters included) holding M or M^-1 in a cbuffer belongs to it.
    objects = []            # {"model": M, "ops": [...]}
    obj_by_key = {}

    def mkey(m):
        return tuple(round(x, 2) for x in flat(m))

    op_index = {id(o): i for i, o in enumerate(frame["ops"])}
    for o in draws:
        fits = o.get("instance_mvps") or ([o["mvp"]] if o.get("mvp") else [])
        for inst, f16 in enumerate(fits):
            if not f16:
                continue
            model = mm(ivp, from_floats(f16, "row"))
            if not is_affine(model, 5e-3):
                continue
            model[3] = [0.0, 0.0, 0.0, 1.0]
            k = mkey(model)
            if k not in obj_by_key:
                obj_by_key[k] = len(objects)
                objects.append({"model": model, "inv": inv(model), "ops": [], "identity": is_identity(model, 1e-3), "exact": False})
            o.setdefault("_fit_objects", []).append(obj_by_key[k])

    # planar meshes / failed fits: find the object matrix among the vertex-stage cbuffer values from the kept
    # (position, clip) samples: VP * W * p must reproduce every sample's clip position.
    def mat_vec(m, v):
        return [sum(m[r][c] * v[c] for c in range(4)) for r in range(4)]

    n_sampled = 0
    for o in draws:
        smp = o.get("instance_samples") or []
        mv = o.get("instance_mvps") or []
        need = [i for i, sm in enumerate(smp) if sm and (i >= len(mv) or not mv[i])]
        if not need:
            continue
        cands = []
        for cb in o.get("vs_cb", []):
            raw = base64.b64decode(cb["data"])
            f = struct.unpack("<%df" % (len(raw) // 4), raw[: len(raw) // 4 * 4])
            for off in range(0, len(f) - 15, 4):
                w = f[off:off + 16]
                if not finite(w):
                    continue
                for layout in ("col", "row"):
                    m = from_floats(w, layout)
                    if is_affine(m, 1e-4) and not is_identity(m, 1e-4):
                        cands.append(m)
        for i in need:
            for m in cands:
                vpm = mm(vp, m)
                ok = True
                for sm in smp[i]:
                    c2 = mat_vec(vpm, sm["p"] + [1.0])
                    if any(abs(a - b) > 2e-3 * max(1.0, abs(b)) for a, b in zip(c2, sm["c"])):
                        ok = False
                        break
                if ok:
                    model = [list(r) for r in m]
                    model[3] = [0.0, 0.0, 0.0, 1.0]
                    k = mkey(model)
                    if k not in obj_by_key:
                        obj_by_key[k] = len(objects)
                        objects.append({"model": model, "inv": inv(model), "ops": [], "identity": is_identity(model, 1e-3), "exact": True})
                    o.setdefault("_fit_objects", []).append(obj_by_key[k])
                    n_sampled += 1
                    break
    print("objects found from samples (planar meshes, unfit instances): %d" % n_sampled)

    # 3. classify windows
    inv_proj = inv(proj)
    targets += [("proj", proj), ("inv_proj", inv_proj)]
    # screen-size constants: the final image and its power-of-two reductions (half-res buffers etc.)
    final = next((r for r in frame["resources"] if r["id"] == frame.get("final")), None)
    screen_vals = []
    if final:
        fw, fh = final["width"], final["height"]
        sizes = {(r["width"], r["height"]) for r in frame["resources"] if r.get("rt")}
        for lvl in range(6):
            for w in {fw >> lvl, (fw + (1 << lvl) - 1) >> lvl}:
                for h in {fh >> lvl, (fh + (1 << lvl) - 1) >> lvl}:
                    if (w, h) in sizes and w > 2 and h > 2:
                        screen_vals.append((lvl, w, h))
    by_key = collections.defaultdict(list)   # rounded matrix -> objects whose M or M^-1 it is (fast lookup)
    for i, ob in enumerate(objects):
        by_key[mkey(ob["model"])].append(i)
        by_key[mkey(ob["inv"])].append(i)
    stats = collections.Counter()
    for o in draws:
        own_list = o.get("_fit_objects", [])
        for cb, f in cbs(o):
            patches, covered = [], set()
            for off in range(0, len(f) - 15, 4):
                w = f[off:off + 16]
                if any(i in covered for i in range(off, off + 16)) or not finite(w):
                    continue
                hit = None
                for layout in ("col", "row"):
                    m = from_floats(w, layout)
                    for name, t in targets:
                        if close(m, t, 1e-3):
                            hit = {"offset": off * 4, "kind": name, "layout": layout}
                            break
                    if hit is None and not is_identity(m, 1e-4):
                        # object matrices: own fitted object first, then every known object (shadow casters etc.)
                        order = own_list + [i for i in by_key.get(mkey(m), ()) if i not in own_list]
                        for oi in order:
                            ob = objects[oi]
                            if ob["identity"]:
                                continue
                            if close(m, ob["model"], 2e-3):
                                hit = {"offset": off * 4, "kind": "object_to_world", "layout": layout, "object": oi}
                                ob["model"], ob["inv"], ob["exact"] = m, inv(m), True     # exact stored values
                            elif close(m, ob["inv"], 2e-3):
                                hit = {"offset": off * 4, "kind": "world_to_object", "layout": layout, "object": oi}
                            if hit:
                                break
                    if hit is None:
                        model = mm(ivp, m)
                        if is_affine(model) and not is_identity(model, 1e-3):
                            model[3] = [0.0, 0.0, 0.0, 1.0]
                            if close(mm(vp, model), m):
                                hit = {"offset": off * 4, "kind": "model_view_proj", "layout": layout, "model": flat(model)}
                                k = mkey(model)
                                if k in obj_by_key:
                                    hit["object"] = obj_by_key[k]
                    if hit:
                        break
                if hit:
                    patches.append(hit)
                    covered.update(range(off, off + 16))
                    stats[hit["kind"]] += 1
            if eye_ok:
                for off in range(0, len(f) - 2):
                    if any(i in covered for i in range(off, off + 3)):
                        continue
                    if all(abs(f[off + i] - eye[i]) <= 1e-3 * (1 + abs(eye[i])) for i in range(3)):
                        patches.append({"offset": off * 4, "kind": "camera_pos"})
                        covered.update(range(off, off + 3))
                        stats["camera_pos"] += 1
            # screen size values (width, height, 1/width, 1/height) of screen-relative buffers
            for off in range(len(f)):
                if off in covered or not math.isfinite(f[off]) or f[off] == 0:
                    continue
                x = f[off]
                for lvl, w, h in screen_vals:
                    comp = next((c for c, v in enumerate((w, h, 1.0 / w, 1.0 / h)) if abs(x - v) <= 1e-6 * abs(v)), None)
                    if comp is not None:
                        patches.append({"offset": off * 4, "kind": "screen", "level": lvl, "component": comp})
                        covered.add(off)
                        stats["screen"] += 1
                        break
            if patches:
                cb["patches"] = sorted(patches, key=lambda p: p["offset"])
            else:
                cb.pop("patches", None)

    # draw -> object (first object referenced by its patches; else the fitted one when its vertices are in world space)
    for o in draws:
        refs = [p["object"] for fl in ("vs_cb", "ps_cb") for cb in o.get(fl, []) for p in cb.get("patches", []) if "object" in p]
        fos = o.pop("_fit_objects", [])
        o.pop("objects", None)
        # fitted instances are exactly the objects drawn; cbuffer references alone (shadow casters) are the fallback,
        # since instancing arrays can hold stale matrices of other batches beyond the drawn instances
        objs = list(dict.fromkeys(fos)) if fos else list(dict.fromkeys(refs))
        if objs:
            o["objects"] = objs          # instanced draws render several objects
            for oi in objs:
                objects[oi]["ops"].append(frame["ops"].index(o))
    # 4. per-vertex view rays: fullscreen draws (Unity-style) carry, per corner, the world-space ray from the camera to
    #    the far plane, written by the CPU for the captured camera. Signature: in view space every vertex has the same
    #    depth and its projection lands on a fullscreen-triangle/quad corner. Recomputed per view at replay time.
    meshes = {m["id"]: m for m in frame["meshes"]}
    n_rays = 0
    for o in draws:
        o.pop("view_rays", None)
        m = meshes.get(o.get("mesh"))
        if m is None or m["vertex_count"] > 4:
            continue
        blob = open(os.path.join(sys.argv[1], m["file"]), "rb").read()
        for r in m["raw"]:
            vals = [struct.unpack_from("<4f", blob, r["offset"] + 16 * i) for i in range(m["vertex_count"])]
            us = [[sum(view[i][j] * v[j] for j in range(3)) for i in range(3)] for v in vals]
            depths = [-u[2] for u in us]
            if min(depths) <= 1e-4 or (max(depths) - min(depths)) > 1e-3 * max(depths):
                continue
            ndc = [(proj[0][0] * u[0] / -u[2] + proj[0][2], proj[1][1] * u[1] / -u[2] + proj[1][2]) for u in us]
            snapped = [(round(x), round(y)) for x, y in ndc]
            if any(abs(x - sx) > 0.02 or abs(y - sy) > 0.02 or sx not in (-1, 1, 3) or sy not in (-1, 1, 3) for (x, y), (sx, sy) in zip(ndc, snapped)):
                continue
            o.setdefault("view_rays", []).append({"location": r["location"], "depth": sum(depths) / len(depths),
                                                  "ndc": [{"x": sx, "y": sy} for sx, sy in snapped], "w": [v[3] for v in vals]})
            n_rays += 1
    print("per-vertex view rays: %d attributes in %d draws" % (n_rays, sum(1 for o in draws if "view_rays" in o)))

    # 5. temporal history: a screen-sized texture the frame never writes (the previous frame's TAA / accumulation
    #    output, i.e. the captured viewpoint) read by a draw that also reads a same-format, same-size target written
    #    this frame (the current colour). For a new viewpoint the current colour stands in for the history.
    res = {r["id"]: r for r in frame["resources"]}
    written = set()
    for o in frame["ops"]:
        if o["op"] == "draw":
            written.update(c for c in (o.get("colors") or []) if c)
            if o.get("depth"):
                written.add(o["depth"])
        elif o["op"] == "blit":
            written.add(o["dst"])
    n_hist = 0
    for o in draws:
        texs = o.get("textures", [])
        for t in texs:
            t.pop("history_of", None)
            h = res.get(t["res"])
            if not h or h["id"] in written or not final or (h["width"], h["height"]) != (final["width"], final["height"]):
                continue
            cur = next((u["res"] for u in texs if u["res"] in written and res[u["res"]]["format"] == h["format"]
                        and (res[u["res"]]["width"], res[u["res"]]["height"]) == (h["width"], h["height"])), None)
            if cur:
                t["history_of"] = cur
                n_hist += 1
                print("temporal history: draw %d texture %s (%s) <- current %s" % (o["eid"], t["name"], h["id"], cur))
    if not n_hist:
        print("temporal history: none")
    out_objects = []
    for i, ob in enumerate(objects):
        movable = any(p.get("object") == i and p["kind"] in ("object_to_world", "world_to_object", "model_view_proj")
                      for o in draws for fl in ("vs_cb", "ps_cb") for cb in o.get(fl, []) for p in cb.get("patches", []))
        out_objects.append({"model": flat(ob["model"]), "ops": sorted(set(ob["ops"])), "movable": movable})
    frame["objects"] = out_objects
    frame["camera"] = {"view": flat(view), "proj": flat(proj), "view_proj": flat(vp)}
    frame["screen_levels"] = [{"level": l, "width": w, "height": h} for l, w, h in screen_vals]
    json.dump(frame, open(path, "w", encoding="utf-8"))
    print("camera-dependent values: %s" % dict(stats))
    print("objects: %d (%d movable), draws assigned: %d" % (len(out_objects), sum(1 for x in out_objects if x["movable"]),
                                                          sum(1 for o in draws if "objects" in o)))
    print("screen-relative sizes: %s" % screen_vals)

if __name__ == "__main__":
    main()
