# -*- coding: utf-8 -*-
"""Runs INSIDE qrenderdoc (`qrenderdoc --python rd_inside.py`). Do not run with a normal Python.

Launched by run_in_renderdoc.py, which passes parameters through the RD2U_ARGS environment variable
(a path to a JSON file). Two modes:

  survey  - list draws, render targets, shader families, vertex layouts; writes survey.json + survey.md
  extract - rebuild the scene: meshes (object space), per-draw world matrices recovered from
            post-VS data, camera from the view-projection matrix, textures, reference image;
            writes scene.json + meshes/*.bin + textures/*.png for the Unity importer.

Engine/name agnostic: shader reflection names are often stripped (SPIR-V "uniforms90", "_input0"),
so world matrices are recovered numerically:
  1. Fit MVP per draw (instance) by least squares: clip = MVP * [pos_object, 1].
  2. Find the camera VP among constant-buffer matrices shared by many draws such that
     VP^-1 * MVP is affine (last row 0,0,0,1) for most draws.  World = VP^-1 * MVP.
  3. Draws that do not fit a single matrix (skinning, vertex animation, planar meshes) are baked:
     world position = VP^-1 * clip, divided by w.

Tested against RenderDoc 1.46 (descriptor-based PipeState API).
"""
import base64, json, math, os, re, struct, time, traceback, collections

ARGS = json.load(open(os.environ["RD2U_ARGS"], "r", encoding="utf-8"))
OUT = ARGS["out"]
os.makedirs(OUT, exist_ok=True)
LOG = open(os.path.join(OUT, "rd2u.log"), "w", encoding="utf-8")


def log(*a):
    LOG.write(" ".join(str(x) for x in a) + "\n")
    LOG.flush()


# ----------------------------------------------------------------------------- small linear algebra (no numpy)

def mat_mul(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def mat_vec(m, v):
    return [m[r][0] * v[0] + m[r][1] * v[1] + m[r][2] * v[2] + m[r][3] * v[3] for r in range(4)]


def mat_inv(m):
    a = [list(row) + [1.0 if i == j else 0.0 for j in range(4)] for i, row in enumerate(m)]
    for col in range(4):
        piv = max(range(col, 4), key=lambda r: abs(a[r][col]))
        if abs(a[piv][col]) < 1e-12:
            return None
        a[col], a[piv] = a[piv], a[col]
        p = a[col][col]
        a[col] = [x / p for x in a[col]]
        for r in range(4):
            if r != col and a[r][col] != 0.0:
                f = a[r][col]
                a[r] = [x - f * y for x, y in zip(a[r], a[col])]
    return [row[4:] for row in a]


def solve4(ata, atb):
    inv = mat_inv(ata)
    if inv is None:
        return None
    return [sum(inv[r][k] * atb[k] for k in range(4)) for r in range(4)]


def fit_mvp(pairs):
    """pairs: list of (obj_xyz, clip_xyzw). Returns (matrix, rel_rms, conditioned)."""
    if len(pairs) < 4:
        return None, 1e9, False
    ata = [[0.0] * 4 for _ in range(4)]
    atb = [[0.0] * 4 for _ in range(4)]  # one rhs per clip component
    for p, c in pairs:
        row = (p[0], p[1], p[2], 1.0)
        for i in range(4):
            for j in range(4):
                ata[i][j] += row[i] * row[j]
            for comp in range(4):
                atb[comp][i] += row[i] * c[comp]
    # Conditioning: normalised determinant of the covariance of positions; ~0 for planar/linear meshes.
    n = float(len(pairs))
    mean = [ata[3][i] / n for i in range(3)]
    cov = [[ata[i][j] / n - mean[i] * mean[j] for j in range(3)] for i in range(3)]
    det = (cov[0][0] * (cov[1][1] * cov[2][2] - cov[1][2] * cov[2][1])
           - cov[0][1] * (cov[1][0] * cov[2][2] - cov[1][2] * cov[2][0])
           + cov[0][2] * (cov[1][0] * cov[2][1] - cov[1][1] * cov[2][0]))
    tr = cov[0][0] + cov[1][1] + cov[2][2]
    conditioned = tr > 0 and det / (tr / 3.0) ** 3 > 1e-6
    rows = []
    for comp in range(4):
        sol = solve4(ata, atb[comp])
        if sol is None:
            return None, 1e9, False
        rows.append(sol)
    err = 0.0
    mag = 0.0
    for p, c in pairs:
        pc = mat_vec(rows, (p[0], p[1], p[2], 1.0))
        err += sum((pc[i] - c[i]) ** 2 for i in range(4))
        mag += sum(c[i] ** 2 for i in range(4))
    rel = math.sqrt(err / max(mag, 1e-20))
    return rows, rel, conditioned


def is_affine(w, scale_hint=1.0):
    tol = 1e-3
    s = max(1.0, abs(w[3][3]))
    return abs(w[3][0]) < tol * s * scale_hint and abs(w[3][1]) < tol * s * scale_hint and abs(w[3][2]) < tol * s * scale_hint and abs(w[3][3] - 1.0) < 2e-3


def flat(m):
    return [m[r][c] for r in range(4) for c in range(4)]


def finite_matrix(m):
    return all(math.isfinite(x) for row in m for x in row)


# ----------------------------------------------------------------------------- vertex decoding

def comp_struct(rd, fmt):
    """Return (struct char, scale) for one component of a regular ResourceFormat, or None."""
    ct, w = fmt.compType, fmt.compByteWidth
    if ct == rd.CompType.Float:
        return {4: ("f", None), 2: ("e", None), 8: ("d", None)}.get(w)
    if ct in (rd.CompType.UNorm, rd.CompType.UNormSRGB):
        return {1: ("B", 255.0), 2: ("H", 65535.0)}.get(w)
    if ct == rd.CompType.SNorm:
        return {1: ("b", -127.0), 2: ("h", -32767.0)}.get(w)  # negative => snorm clamp
    if ct in (rd.CompType.UInt, rd.CompType.UScaled):
        return {1: ("B", 1.0), 2: ("H", 1.0), 4: ("I", 1.0)}.get(w)
    if ct in (rd.CompType.SInt, rd.CompType.SScaled):
        return {1: ("b", 1.0), 2: ("h", 1.0), 4: ("i", 1.0)}.get(w)
    return None


def make_decoder(rd, fmt):
    """Returns (byte_size, fn(buf, offset) -> tuple of floats) or None if unsupported."""
    if fmt.type == rd.ResourceFormatType.R10G10B10A2:
        snorm = fmt.compType == rd.CompType.SNorm

        def dec_1010102(buf, off):
            v = struct.unpack_from("<I", buf, off)[0]
            parts = [(v >> 0) & 1023, (v >> 10) & 1023, (v >> 20) & 1023, (v >> 30) & 3]
            if snorm:
                out = []
                for i, x in enumerate(parts):
                    bits = 10 if i < 3 else 2
                    if x >= 1 << (bits - 1):
                        x -= 1 << bits
                    out.append(max(-1.0, x / float((1 << (bits - 1)) - 1)))
                return tuple(out)
            return (parts[0] / 1023.0, parts[1] / 1023.0, parts[2] / 1023.0, parts[3] / 3.0)
        return 4, dec_1010102
    if fmt.type != rd.ResourceFormatType.Regular:
        return None
    cs = comp_struct(rd, fmt)
    if cs is None:
        return None
    ch, scale = cs
    n = fmt.compCount
    st = struct.Struct("<" + ch * n)
    bgra = fmt.BGRAOrder()

    def dec(buf, off):
        vals = st.unpack_from(buf, off)
        if scale is not None:
            if scale < 0:
                vals = tuple(max(-1.0, x / -scale) for x in vals)
            else:
                vals = tuple(x / scale for x in vals)
        if bgra and n >= 3:
            vals = (vals[2], vals[1], vals[0]) + tuple(vals[3:])
        return tuple(float(x) for x in vals)
    return st.size, dec


def read_indices(rd, ctl, pipe, a):
    """Returns list of vertex indices (already including baseVertex / vertexOffset)."""
    if a.flags & rd.ActionFlags.Indexed:
        ib = pipe.GetIBuffer()
        stride = ib.byteStride if ib.byteStride in (1, 2, 4) else 4
        data = ctl.GetBufferData(ib.resourceId, ib.byteOffset + a.indexOffset * stride, a.numIndices * stride)
        ch = {1: "B", 2: "H", 4: "I"}[stride]
        cnt = len(data) // stride
        idx = list(struct.unpack_from("<%d%s" % (cnt, ch), data, 0))
        restart = (1 << (8 * stride)) - 1
        return [i + a.baseVertex if i != restart else -1 for i in idx]
    return list(range(a.vertexOffset, a.vertexOffset + a.numIndices))


def to_triangles(topo_name, idx):
    """Convert an index stream to a triangle list; -1 means primitive restart."""
    if topo_name == "TriangleList":
        n = len(idx) - len(idx) % 3
        return [i for i in idx[:n]]
    if topo_name == "TriangleStrip":
        out, strip = [], []
        for i in idx + [-1]:
            if i < 0:
                for k in range(2, len(strip)):
                    a, b, c = strip[k - 2], strip[k - 1], strip[k]
                    if a == b or b == c or a == c:
                        continue
                    out += [a, b, c] if k % 2 == 0 else [b, a, c]
                strip = []
            else:
                strip.append(i)
        return out
    if topo_name == "TriangleFan":
        return [x for k in range(2, len(idx)) for x in (idx[0], idx[k - 1], idx[k])]
    return None


# ----------------------------------------------------------------------------- main

def main():
    import renderdoc as rd
    t0 = time.time()
    cap = rd.OpenCaptureFile()
    res = cap.OpenFile(ARGS["capture"], "", None)
    if not res.OK():
        raise RuntimeError("OpenFile failed: %s" % res.Message())
    if cap.LocalReplaySupport() != rd.ReplaySupport.Supported:
        log("WARNING local replay support:", cap.LocalReplaySupport())
    res, ctl = cap.OpenCapture(rd.ReplayOptions(), None)
    if ctl is None:
        raise RuntimeError("OpenCapture failed: %s" % res)
    props = ctl.GetAPIProperties()
    log("api", props.pipelineType, "renderdoc", rd.GetVersionString())
    sf = ctl.GetStructuredFile()

    def walk(acts, path=()):
        for a in acts:
            yield a, path
            yield from walk(a.children, path + (a.GetName(sf),))

    draws = [(a, path) for a, path in walk(ctl.GetRootActions()) if a.flags & rd.ActionFlags.Drawcall]
    log("draws", len(draws))
    tex_desc = {str(t.resourceId): t for t in ctl.GetTextures()}

    if ARGS["mode"] == "survey":
        survey(rd, ctl, sf, draws, tex_desc, props)
    elif ARGS["mode"] == "frame":
        import sys
        sys.path.insert(0, ARGS["script_dir"])
        import rd_frame
        rd_frame.run(rd, ctl, sf, tex_desc, props, ARGS, OUT, log,
                     {"make_decoder": make_decoder, "read_indices": read_indices, "to_triangles": to_triangles,
                      "read_postvs": read_postvs, "fit_mvp": fit_mvp})
    else:
        extract(rd, ctl, sf, draws, tex_desc, props)
    ctl.Shutdown()
    cap.Shutdown()
    log("total_seconds %.1f" % (time.time() - t0))


def draw_state(rd, pipe):
    outs = [str(o.resource) for o in pipe.GetOutputTargets() if o.resource != rd.ResourceId.Null()]
    depth = pipe.GetDepthTarget().resource
    return outs, (str(depth) if depth != rd.ResourceId.Null() else "")


def survey(rd, ctl, sf, draws, tex_desc, props):
    shaders, rows = {}, []
    for a, path in draws:
        ctl.SetFrameEvent(a.eventId, False)
        pipe = ctl.GetPipelineState()
        vs, ps = pipe.GetShader(rd.ShaderStage.Vertex), pipe.GetShader(rd.ShaderStage.Pixel)
        outs, depth = draw_state(rd, pipe)
        key = "%s|%s" % (vs, ps)
        if key not in shaders:
            info = {"id": len(shaders), "vs": str(vs), "ps": str(ps), "first_event": a.eventId, "draws": 0, "stages": {}}
            for st in (rd.ShaderStage.Vertex, rd.ShaderStage.Pixel):
                refl = pipe.GetShaderReflection(st)
                if refl is None:
                    continue
                info["stages"][str(st).split(".")[-1]] = {
                    "constant_blocks": [{"name": cb.name, "byte_size": cb.byteSize, "variables": [v.name for v in cb.variables][:40]} for cb in refl.constantBlocks],
                    "textures": [r.name for r in refl.readOnlyResources],
                }
            info["vertex_inputs"] = [{"name": v.name, "format": v.format.Name(), "buffer": v.vertexBuffer, "offset": v.byteOffset, "per_instance": v.perInstance} for v in pipe.GetVertexInputs() if v.used]
            shaders[key] = info
        shaders[key]["draws"] += 1
        rows.append({"event": a.eventId, "name": a.GetName(sf), "marker": " / ".join(path[-3:]), "indices": a.numIndices,
                     "instances": a.numInstances, "shader": shaders[key]["id"], "targets": outs, "depth": depth})
    stripped = sum(1 for s in shaders.values() for st in s["stages"].values() for cb in st["constant_blocks"]
                   if cb["name"].startswith("uniforms") or cb["name"].startswith("type_")) > 0
    groups = collections.OrderedDict()
    for r in rows:
        k = (",".join(r["targets"]), r["depth"])
        g = groups.setdefault(k, {"targets": r["targets"], "depth": r["depth"], "draws": 0, "first_event": r["event"], "last_event": r["event"], "markers": collections.Counter()})
        g["draws"] += 1
        g["last_event"] = r["event"]
        g["markers"][r["marker"]] += 1
    summary = {"capture": ARGS["capture"], "api": str(props.pipelineType), "draw_count": len(rows),
               "reflection_names_stripped": stripped,
               "target_groups": [dict(g, markers=g["markers"].most_common(5)) for g in groups.values()],
               "shaders": sorted(shaders.values(), key=lambda s: -s["draws"]), "draws": rows}
    json.dump(summary, open(os.path.join(OUT, "survey.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    md = ["# Capture survey", "", "- capture: `%s`" % ARGS["capture"], "- api: %s, draws: %d" % (summary["api"], len(rows)),
          "- reflection names stripped: %s" % stripped, "", "## Render target groups (in submission order)", "",
          "| first..last event | draws | color targets | depth | top markers |", "|---|---|---|---|---|"]
    for g in summary["target_groups"]:
        md.append("| %d..%d | %d | %s | %s | %s |" % (g["first_event"], g["last_event"], g["draws"], " ".join(g["targets"]) or "-",
                                                    g["depth"] or "-", "; ".join("%s(%d)" % (m or "-", c) for m, c in g["markers"][:3])))
    md += ["", "## Shader families (by draw count)", "", "| id | draws | first event | vertex inputs | VS constant blocks | PS textures |", "|---|---|---|---|---|---|"]
    for s in summary["shaders"]:
        vsb = s["stages"].get("Vertex", {}).get("constant_blocks", [])
        md.append("| %d | %d | %d | %s | %s | %s |" % (s["id"], s["draws"], s["first_event"],
                                                     ", ".join("%s:%s" % (v["name"], v["format"]) for v in s["vertex_inputs"]),
                                                     ", ".join("%s(%dB)" % (c["name"], c["byte_size"]) for c in vsb),
                                                     ", ".join(s["stages"].get("Pixel", {}).get("textures", []))))
    open(os.path.join(OUT, "survey.md"), "w", encoding="utf-8").write("\n".join(md) + "\n")
    log("survey done shaders", len(shaders), "groups", len(groups))


# ----------------------------------------------------------------------------- extraction

def pick_semantics(rd, attrs, sample):
    """Map attribute index -> semantic using names when present, otherwise layout heuristics.

    sample: dict attr_index -> list of decoded tuples (for length checks)."""
    sem = {}
    names = {i: (a.name or "").lower() for i, a in enumerate(attrs)}
    for i, n in names.items():
        if "position" in n or n in ("pos", "vertex", "in_position0", "a_position"):
            sem.setdefault("position", i)
        elif "normal" in n:
            sem.setdefault("normal", i)
        elif ("texcoord0" in n or n.endswith("uv0") or n in ("texcoord", "uv", "in_texcoord0", "a_texcoord0")):
            sem.setdefault("uv0", i)
        elif "color" in n:
            sem.setdefault("color", i)
    rest = [i for i in range(len(attrs)) if i not in sem.values()]
    if "position" not in sem:
        cand = [i for i in rest if attrs[i].format.compCount >= 3 and attrs[i].format.compType == rd.CompType.Float]
        if cand:
            sem["position"] = cand[0]
            rest.remove(cand[0])
    if "normal" not in sem:
        for i in rest:
            vals = sample.get(i) or []
            if attrs[i].format.compCount >= 3 and vals:
                lens = [math.sqrt(v[0] ** 2 + v[1] ** 2 + v[2] ** 2) for v in vals[:64]]
                if lens and abs(sum(lens) / len(lens) - 1.0) < 0.08 and min(lens) > 0.5:
                    sem["normal"] = i
                    rest.remove(i)
                    break
    if "uv0" not in sem:
        cand = [i for i in rest if attrs[i].format.compCount == 2 and attrs[i].format.compType in (rd.CompType.Float, rd.CompType.UNorm, rd.CompType.SNorm)]
        if cand:
            sem["uv0"] = cand[0]
            rest.remove(cand[0])
    if "color" not in sem:
        cand = [i for i in rest if attrs[i].format.compCount == 4 and attrs[i].format.compType in (rd.CompType.UNorm, rd.CompType.UNormSRGB) and attrs[i].format.compByteWidth == 1]
        if cand:
            sem["color"] = cand[0]
    return sem


def read_postvs(rd, ctl, instance):
    m = ctl.GetPostVSData(instance, 0, rd.MeshDataStage.VSOut)
    if m.vertexResourceId == rd.ResourceId.Null() or m.numIndices == 0:
        return None
    if m.indexResourceId != rd.ResourceId.Null() and m.indexByteStride in (2, 4):
        data = ctl.GetBufferData(m.indexResourceId, m.indexByteOffset, m.numIndices * m.indexByteStride)
        cnt = len(data) // m.indexByteStride
        out_idx = [i + m.baseVertex for i in struct.unpack_from("<%d%s" % (cnt, "H" if m.indexByteStride == 2 else "I"), data, 0)]
    else:
        out_idx = list(range(m.numIndices))
    if not out_idx:
        return None
    hi = max(out_idx)
    vbytes = ctl.GetBufferData(m.vertexResourceId, m.vertexByteOffset, (hi + 1) * m.vertexByteStride)
    dec = make_decoder(rd, m.format)
    if dec is None:
        return None
    size, fn = dec
    stride = m.vertexByteStride
    cache = {}

    def pos(v):
        if v not in cache:
            off = v * stride
            if off + size > len(vbytes):
                return None
            p = fn(vbytes, off)
            cache[v] = (p[0], p[1], p[2], p[3] if len(p) > 3 else 1.0)
        return cache[v]
    return out_idx, pos


def extract(rd, ctl, sf, draws, tex_desc, props):
    opt = ARGS
    first, last = opt.get("event_first", 0), opt.get("event_last", 1 << 31)
    max_draws = int(opt.get("max_draws", 100000))
    max_inst = int(opt.get("max_instances", 256))
    fit_tol = float(opt.get("fit_tolerance", 2e-3))
    mode = opt.get("transform_mode", "auto")  # auto | baked
    target_filter = set(opt.get("targets") or [])
    require_depth = opt.get("require_depth", True)
    save_textures = opt.get("textures", True)
    max_tex = int(opt.get("max_texture_size", 4096))
    os.makedirs(os.path.join(OUT, "meshes"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)

    meshes = collections.OrderedDict()   # key -> mesh record (object space)
    objects = []
    skipped = collections.Counter()
    window_ids = {}                       # 64-byte cbuffer window -> id
    window_floats = []                    # id -> 16 floats
    window_draws = collections.Counter()  # id -> number of draws whose cbuffers contain it
    target_count = collections.Counter()
    tex_ids = collections.OrderedDict()
    frame_targets = set()   # resources written as colour/depth output in this frame: never material textures
    keep_duplicates = opt.get("keep_duplicates", False)
    want_shaders = opt.get("shaders", True)
    families = collections.OrderedDict()   # (vs, ps, spec) -> family record
    replay_tex = set()
    skipped_push_constants = collections.Counter()
    if want_shaders:
        os.makedirs(os.path.join(OUT, "shaders"), exist_ok=True)

    def hlsl_texture_name(name):
        m = re.fullmatch(r"res(\d+)", name or "")
        return "_" + m.group(1) if m else name

    def spec_constants(pipe, stage, refl):
        out = {}
        for ci, cb in enumerate(refl.constantBlocks):
            if not cb.compileConstants:
                continue
            try:
                vals = ctl.GetCBufferVariableContents(pipe.GetGraphicsPipelineObject(), refl.resourceId, stage,
                                                      pipe.GetShaderEntryPoint(stage), ci, rd.ResourceId.Null(), 0, 0)
            except Exception:
                continue
            for v in vals:
                m = re.match(r"specID(\d+)", v.name)
                if m:
                    out[int(m.group(1))] = int(v.value.u32v[0])
        return out

    def pack_inline_block(pipe, stage, refl, ci, cb):
        """Rebuild raw bytes of a non-buffer-backed block (push constants) from its variables and reflection offsets."""
        vals = ctl.GetCBufferVariableContents(pipe.GetGraphicsPipelineObject(), refl.resourceId, stage,
                                              pipe.GetShaderEntryPoint(stage), ci, rd.ResourceId.Null(), 0, 0)
        buf = bytearray(max(16, cb.byteSize))

        def put(off, words):
            end = off + 4 * len(words)
            if end > len(buf):
                buf.extend(b"\0" * (end - len(buf)))
            struct.pack_into("<%dI" % len(words), buf, off, *words)

        def write(const, var, base):
            t = const.type
            off = base + const.byteOffset
            if len(var.members) == 0:
                rows, cols = max(1, var.rows), max(1, var.columns)
                words = list(var.value.u32v[:rows * cols])
                if rows > 1 and t.matrixByteStride:
                    for r in range(rows):
                        put(off + r * t.matrixByteStride, words[r * cols:(r + 1) * cols])
                else:
                    put(off, words)
                return
            if t.elements > 1 and t.arrayByteStride:
                for e, member in enumerate(var.members):
                    if len(member.members) == 0:
                        put(off + e * t.arrayByteStride, list(member.value.u32v[:max(1, member.rows) * max(1, member.columns)]))
                    else:
                        for sub_const, sub_var in zip(t.members, member.members):
                            write(sub_const, sub_var, off + e * t.arrayByteStride)
                return
            for sub_const, sub_var in zip(t.members, var.members):
                write(sub_const, sub_var, off)
        for const, var in zip(cb.variables, vals):
            write(const, var, 0)
        if len(buf) % 16:
            buf.extend(b"\0" * (16 - len(buf) % 16))
        return bytes(buf)

    def capture_replay(pipe, cull):
        """Everything needed to replay this draw with its own decompiled shaders in Unity."""
        vs_refl = pipe.GetShaderReflection(rd.ShaderStage.Vertex)
        ps_refl = pipe.GetShaderReflection(rd.ShaderStage.Pixel)
        if vs_refl is None or ps_refl is None or vs_refl.encoding != rd.ShaderEncoding.SPIRV or ps_refl.encoding != rd.ShaderEncoding.SPIRV:
            return None
        spec = spec_constants(pipe, rd.ShaderStage.Vertex, vs_refl)
        spec.update(spec_constants(pipe, rd.ShaderStage.Pixel, ps_refl))
        fkey = (str(vs_refl.resourceId), str(ps_refl.resourceId), tuple(sorted(spec.items())))
        fam = families.get(fkey)
        if fam is None:
            fid = len(families)
            open(os.path.join(OUT, "shaders", "fam%d_vs.spv" % fid), "wb").write(bytes(vs_refl.rawBytes))
            open(os.path.join(OUT, "shaders", "fam%d_ps.spv" % fid), "wb").write(bytes(ps_refl.rawBytes))
            fam = families[fkey] = {"id": fid, "vs_spv": "fam%d_vs.spv" % fid, "ps_spv": "fam%d_ps.spv" % fid,
                                    "vs_blocks": [cb.name for cb in vs_refl.constantBlocks],
                                    "ps_blocks": [cb.name for cb in ps_refl.constantBlocks],
                                    "spec_constants": {str(k): v for k, v in spec.items()}, "states": collections.OrderedDict(), "draws": 0}
        fam["draws"] += 1
        # colour output: prefer a float (HDR scene colour) target, then an sRGB target (colour is stored sRGB,
        # normals/parameters are not), else the first bound target. Override per family in families.json if needed.
        targets = list(pipe.GetOutputTargets())
        cidx = next((i for i, t in enumerate(targets) if t.resource != rd.ResourceId.Null()), 0)
        for want in (rd.CompType.Float, rd.CompType.UNormSRGB):
            hit = next((i for i, t in enumerate(targets) if tex_desc.get(str(t.resource)) is not None
                        and tex_desc[str(t.resource)].format.compType == want), None)
            if hit is not None:
                cidx = hit
                break
        blends = pipe.GetColorBlends()
        b = blends[cidx] if cidx < len(blends) else None
        dts = pipe.GetDepthTestState()
        dfunc = str(dts.depthFunction).split(".")[-1]
        st = {"cull": cull, "depth_write": bool(dts.depthWrites), "depth_test": bool(dts.depthEnable), "depth_func": dfunc,
              "reversed_z": dfunc in ("Greater", "GreaterEqual"), "blend": bool(b is not None and b.enabled), "color_output": cidx}
        if st["blend"]:
            st.update({"src": str(b.colorBlend.source).split(".")[-1], "dst": str(b.colorBlend.destination).split(".")[-1],
                       "src_a": str(b.alphaBlend.source).split(".")[-1], "dst_a": str(b.alphaBlend.destination).split(".")[-1]})
        skey_full = json.dumps(st, sort_keys=True)
        if skey_full not in fam["states"]:
            fam["states"][skey_full] = dict(st, key="s%d" % len(fam["states"]))
        skey = fam["states"][skey_full]["key"]
        cbs = {"vs_cb": [], "ps_cb": []}
        for field, stage, refl in (("vs_cb", rd.ShaderStage.Vertex, vs_refl), ("ps_cb", rd.ShaderStage.Pixel, ps_refl)):
            for ci, cb in enumerate(refl.constantBlocks):
                if cb.compileConstants:
                    continue
                if not cb.bufferBacked:
                    try:
                        data = pack_inline_block(pipe, stage, refl, ci, cb)
                        cbs[field].append({"block": ci, "data": base64.b64encode(data).decode("ascii")})
                    except Exception as e:
                        skipped_push_constants["%s: %s" % (cb.name, e)] += 1
                    continue
                d = pipe.GetConstantBlock(stage, ci, 0).descriptor
                if d.resource == rd.ResourceId.Null():
                    continue
                size = cb.byteSize if cb.byteSize > 0 else min(d.byteSize, 65536)
                data = ctl.GetBufferData(d.resource, d.byteOffset, size)
                cbs[field].append({"block": ci, "data": base64.b64encode(bytes(data)).decode("ascii")})
        texs = []
        for stage, refl in ((rd.ShaderStage.Vertex, vs_refl), (rd.ShaderStage.Pixel, ps_refl)):
            for ud in pipe.GetReadOnlyResources(stage, True):
                idx = ud.access.index
                if idx >= len(refl.readOnlyResources):
                    continue
                d = ud.descriptor
                kind = {rd.TextureType.TextureCube: "cube", rd.TextureType.Texture2D: "2d", rd.TextureType.TextureRect: "2d"}.get(d.textureType)
                rid = str(d.resource)
                if kind is None or rid not in tex_desc:
                    continue
                texs.append({"name": ("rdvt" if stage == rd.ShaderStage.Vertex else "") + hlsl_texture_name(refl.readOnlyResources[idx].name), "resource": rid, "kind": kind})
                replay_tex.add(rid)
        return dict({"family": fam["id"], "state": skey, "textures": texs}, **cbs)

    selected = 0

    for ordinal, (a, path) in enumerate(draws):
        if not (first <= a.eventId <= last):
            continue
        if selected >= max_draws:
            break
        ctl.SetFrameEvent(a.eventId, True)
        pipe = ctl.GetPipelineState()
        outs, depth = draw_state(rd, pipe)
        frame_targets.update(outs)
        if depth:
            frame_targets.add(depth)
        if target_filter and not (target_filter & set(outs)):
            skipped["target_filter"] += 1
            continue
        if require_depth and not depth:
            skipped["no_depth_target(UI/post)"] += 1
            continue
        if require_depth and not outs:
            skipped["depth_only(shadow/prepass)"] += 1
            continue
        group = ",".join(sorted(outs)) + "|" + depth
        topo = str(pipe.GetPrimitiveTopology()).split(".")[-1]
        if topo not in ("TriangleList", "TriangleStrip", "TriangleFan"):
            skipped["topology_" + topo] += 1
            continue
        selected += 1
        for o in outs:
            target_count[o] += 1

        # ---- source vertices
        attrs = [v for v in pipe.GetVertexInputs() if v.used and not v.perInstance]
        vbs = pipe.GetVBuffers()
        try:
            idx = read_indices(rd, ctl, pipe, a)
        except Exception as e:
            skipped["index_read_error"] += 1
            log("E", a.eventId, "index", e)
            continue
        valid = [i for i in idx if i >= 0]
        if not valid:
            skipped["empty"] += 1
            continue
        lo, hi = min(valid), max(valid)
        buf_cache = {}

        def vb_bytes(slot):
            if slot not in buf_cache:
                vb = vbs[slot]
                stride = vb.byteStride
                buf_cache[slot] = (ctl.GetBufferData(vb.resourceId, vb.byteOffset + lo * stride, (hi - lo + 1) * stride + 64), stride)
            return buf_cache[slot]

        decoders = {}
        for ai, at in enumerate(attrs):
            d = make_decoder(rd, at.format)
            if d is not None and at.vertexBuffer < len(vbs):
                decoders[ai] = d
        used_sorted = sorted(set(valid))
        sample = {}
        for ai, (size, fn) in decoders.items():
            data, stride = vb_bytes(attrs[ai].vertexBuffer)
            vals = []
            for v in used_sorted[:64]:
                off = (v - lo) * stride + attrs[ai].byteOffset
                if off + size <= len(data):
                    vals.append(fn(data, off))
            sample[ai] = vals
        sem = pick_semantics(rd, attrs, sample)
        if "position" not in sem or sem["position"] not in decoders:
            skipped["no_position_attribute"] += 1
            continue

        key = (tuple((str(vbs[at.vertexBuffer].resourceId), vbs[at.vertexBuffer].byteOffset, at.byteOffset, at.format.Name()) for at in attrs if at.vertexBuffer < len(vbs)),
               str(pipe.GetIBuffer().resourceId), a.indexOffset, a.numIndices, a.baseVertex, a.vertexOffset, topo)
        remap = {v: k for k, v in enumerate(used_sorted)}
        if key not in meshes:
            streams = {}
            for semname in ("position", "normal", "uv0", "color"):
                ai = sem.get(semname)
                if ai is None or ai not in decoders:
                    continue
                size, fn = decoders[ai]
                data, stride = vb_bytes(attrs[ai].vertexBuffer)
                vals = []
                for v in used_sorted:
                    off = (v - lo) * stride + attrs[ai].byteOffset
                    vals.append(fn(data, off) if off + size <= len(data) else (0.0, 0.0, 0.0, 0.0))
                streams[semname] = vals
            tris = to_triangles(topo, idx)
            tris = [remap[i] for i in tris if i >= 0] if tris is not None else []
            tris = tris[:len(tris) - len(tris) % 3]
            if want_shaders:
                vs_refl = pipe.GetShaderReflection(rd.ShaderStage.Vertex)
                loc_of = {sg.varName: sg.regIndex for sg in vs_refl.inputSignature} if vs_refl is not None else {}
                raw = {}
                for ai, at in enumerate(attrs):
                    loc = loc_of.get(at.name)
                    if loc is None or ai not in decoders or loc > 7:
                        continue
                    size, fn = decoders[ai]
                    data, stride = vb_bytes(at.vertexBuffer)
                    vals = []
                    for v in used_sorted:
                        off = (v - lo) * stride + at.byteOffset
                        t = fn(data, off) if off + size <= len(data) else (0.0,)
                        vals.append((tuple(t) + (0.0, 0.0, 0.0, 1.0))[:4] if len(t) < 4 else tuple(t[:4]))
                    raw[loc] = vals
                streams["raw"] = raw
            meshes[key] = {"id": "m%d" % len(meshes), "streams": streams, "indices": tris, "first_event": a.eventId,
                           "attribute_map": {k: "%s:%s" % (attrs[v].name, attrs[v].format.Name()) for k, v in sem.items()}}
        mesh = meshes[key]

        # ---- render state: transparent draws (blending) must not become opaque occluders in Unity
        try:
            blends = pipe.GetColorBlends()
            blend = any(b.enabled and b.writeMask != 0 for b in blends[:max(1, len(outs))])
        except Exception:
            blend = False
        try:
            dts = pipe.GetDepthTestState()
            depth_write = bool(dts.depthWrites)
        except Exception:
            depth_write = True
        try:
            cull = str(pipe.GetRasterState().cullMode).split(".")[-1]
        except Exception:
            cull = "Back"

        replay = None
        if want_shaders:
            try:
                replay = capture_replay(pipe, cull)
            except Exception as e:
                log("W", a.eventId, "replay capture", e)

        # ---- textures (pixel stage)
        bound = []
        try:
            for ud in pipe.GetReadOnlyResources(rd.ShaderStage.Pixel, True):
                d = ud.descriptor
                rid = str(d.resource)
                td = tex_desc.get(rid)
                if td is None or d.textureType not in (rd.TextureType.Texture2D, rd.TextureType.TextureRect):
                    continue
                if max(td.width, td.height) > max_tex:
                    continue
                bound.append(rid)
        except Exception as e:
            log("W", a.eventId, "textures", e)

        # ---- VP candidates: every 16-byte aligned 4x4 window of this draw's vertex-stage constant blocks
        draw_windows = set()
        if True:
            refl = pipe.GetShaderReflection(rd.ShaderStage.Vertex)
            if refl is not None:
                for ci, cb in enumerate(refl.constantBlocks):
                    if not cb.bufferBacked:
                        continue
                    try:
                        desc = pipe.GetConstantBlock(rd.ShaderStage.Vertex, ci, 0).descriptor
                        if desc.resource == rd.ResourceId.Null():
                            continue
                        size = desc.byteSize if 0 < desc.byteSize < 1 << 20 else cb.byteSize
                        raw = ctl.GetBufferData(desc.resource, desc.byteOffset, min(size, 4096))
                    except Exception:
                        continue
                    for off in range(0, len(raw) - 63, 16):
                        chunk = bytes(raw[off:off + 64])
                        wid = window_ids.get(chunk)
                        if wid is None:
                            f = struct.unpack("<16f", chunk)
                            if not all(math.isfinite(x) for x in f) or all(abs(x) < 1e-12 for x in f):
                                window_ids[chunk] = -1
                                continue
                            wid = window_ids[chunk] = len(window_floats)
                            window_floats.append(f)
                        if wid >= 0:
                            draw_windows.add(wid)
            for wid in draw_windows:
                window_draws[wid] += 1

        # ---- post-VS: fit MVP per instance, or keep clip positions for baking
        inst_count = max(1, min(a.numInstances, max_inst))
        if a.numInstances > max_inst:
            skipped["instances_truncated"] += a.numInstances - max_inst
        for inst in range(inst_count):
            try:
                pv = read_postvs(rd, ctl, inst)
            except Exception as e:
                pv = None
                log("W", a.eventId, "postvs", e)
            if pv is None:
                skipped["no_postvs"] += 1
                continue
            out_idx, clip_of = pv
            pairs, clip_by_vertex = [], {}
            n = min(len(idx), len(out_idx))
            pos_vals = mesh["streams"]["position"]
            for k in range(n):
                vi = idx[k]
                if vi < 0 or vi in clip_by_vertex:
                    continue
                c = clip_of(out_idx[k])
                if c is None:
                    continue
                clip_by_vertex[vi] = c
                if len(pairs) < 512:
                    p = pos_vals[remap[vi]]
                    pairs.append(((p[0], p[1], p[2]), c))
            mvp, rel, cond = fit_mvp(pairs)
            rigid = mode != "baked" and mvp is not None and cond and rel < fit_tol and finite_matrix(mvp)
            obj = {"event": a.eventId, "name": a.GetName(sf), "instance": inst, "instance_index": a.instanceOffset + inst, "mesh": mesh["id"], "targets": outs,
                   "shader": "%s|%s" % (pipe.GetShader(rd.ShaderStage.Vertex), pipe.GetShader(rd.ShaderStage.Pixel)),
                   "textures": bound, "fit_error": rel, "conditioned": cond, "marker": " / ".join(path[-2:]), "ordinal": ordinal, "group": group, "windows": draw_windows,
                   "blend": blend, "depth_write": depth_write, "cull": cull, "replay": replay}
            if rigid:
                obj["mvp"] = mvp
            obj["clip"] = {remap[v]: c for v, c in clip_by_vertex.items()}
            objects.append(obj)

    log("selected draws", selected, "objects", len(objects), "unique meshes", len(meshes), "skipped", dict(skipped))

    def camera_from_vp(vp):
        """Camera centre = point mapped to clip x=y=w=0; forward along the w row; right along the x row."""
        r0, r1, r3 = vp[0], vp[1], vp[3]
        M3 = [r0[:3] + [0.0], r1[:3] + [0.0], r3[:3] + [0.0], [0.0, 0.0, 0.0, 1.0]]
        inv3 = mat_inv(M3)
        rhs = [-r0[3], -r1[3], -r3[3], 1.0]
        centre = [sum(inv3[i][k] * rhs[k] for k in range(4)) for i in range(3)] if inv3 else [0.0, 0.0, 0.0]

        def nrm(v):
            l = math.sqrt(sum(x * x for x in v)) or 1.0
            return [x / l for x in v]
        forward, right = nrm(r3[:3]), nrm(r0[:3])
        up = nrm([forward[1] * right[2] - forward[2] * right[1], forward[2] * right[0] - forward[0] * right[2], forward[0] * right[1] - forward[1] * right[0]])
        l3 = math.sqrt(sum(x * x for x in r3[:3])) or 1.0
        p11 = math.sqrt(sum(x * x for x in r1[:3])) / l3
        p00 = math.sqrt(sum(x * x for x in r0[:3])) / l3
        return {"vp": flat(vp), "position": centre, "forward": forward, "up": up,
                "fov_y_deg": math.degrees(2 * math.atan(1.0 / p11)) if p11 > 0 else 60.0,
                "aspect": p11 / p00 if p00 > 0 else 1.0}

    # ---- cameras: the VP of a draw must live in that draw's own constant buffers.
    # For every rigid object, test only its own cbuffer windows; a window (+layout) that makes VP^-1*MVP affine
    # is a camera candidate. Vote across objects, then assign every object (rigid or not) to the best-voted
    # candidate present in its own draw.
    def window_matrix(wid, layout):
        f = window_floats[wid]
        return [[f[c * 4 + r] if layout == "col" else f[r * 4 + c] for c in range(4)] for r in range(4)]

    inv_cache = {}

    def cand_inv(key):
        if key not in inv_cache:
            m = window_matrix(*key)
            inv_cache[key] = (m, mat_inv(m))
        return inv_cache[key]

    def projective(key):
        m = cand_inv(key)[0]
        return not (abs(m[3][0]) < 1e-9 and abs(m[3][1]) < 1e-9 and abs(m[3][2]) < 1e-9) and cand_inv(key)[1] is not None

    votes = collections.Counter()
    fits = {}
    for oi, o in enumerate(objects):
        if "mvp" not in o:
            continue
        for wid in o["windows"]:
            if window_draws[wid] < 2:
                continue  # a per-draw window cannot be the shared camera matrix
            for layout in ("col", "row"):
                key = (wid, layout)
                if not projective(key):
                    continue
                w = mat_mul(cand_inv(key)[1], o["mvp"])
                if is_affine(w):
                    votes[key] += 1
                    fits.setdefault(oi, []).append(key)
    ranked = [k for k, _ in votes.most_common()]
    rank = {k: i for i, k in enumerate(ranked)}

    ref_vp = None
    if opt.get("reference_event"):
        ref = [o for o in objects if o["event"] == int(opt["reference_event"]) and "mvp" in o]
        if ref:
            ref_vp = ref[0]["mvp"]

    cameras, cam_by_key, baked_meshes, unresolved = [], {}, [], 0
    identity = flat([[1.0, 0, 0, 0], [0, 1.0, 0, 0], [0, 0, 1.0, 0], [0, 0, 0, 1.0]])

    def camera_for(key, vp, source):
        if key in cam_by_key:
            return cam_by_key[key]
        fvp = flat(vp)
        scale = max(1.0, max(abs(x) for x in fvp))
        for c in cameras:
            if max(abs(x - y) for x, y in zip(c["vp"], fvp)) < 1e-5 * scale:
                cam_by_key[key] = c
                return c
        c = camera_from_vp(vp)
        c.update({"index": len(cameras), "vp_source": source, "draws": 0, "targets": collections.Counter()})
        cameras.append(c)
        cam_by_key[key] = c
        return c

    keep_far = opt.get("keep_far", False)
    far_dropped = 0
    pending_bake = []

    def is_far_background(clip_vals, pos, cam_pos, scene_radius):
        """Sky domes / far cards: vertices pinned to the far plane, or placed far beyond the scene."""
        zs = [c[2] / c[3] for c in clip_vals if abs(c[3]) > 1e-9]
        if zs:
            at_far = sum(1 for z in zs if abs(z) < 1e-5 or abs(z - 1.0) < 1e-5) / float(len(zs))
            if at_far > 0.9:
                return True
        d = sorted(math.sqrt(sum((p[i] - cam_pos[i]) ** 2 for i in range(3))) for p in pos)
        return bool(d) and d[int(len(d) * 0.9)] > scene_radius

    for oi, o in enumerate(objects):
        key = None
        if oi in fits:
            key = min(fits[oi], key=lambda k: rank[k])
        else:
            present = [(wid, layout) for wid in o["windows"] for layout in ("col", "row") if (wid, layout) in rank]
            if present:
                key = min(present, key=lambda k: rank[k])
        if key is not None:
            vp, vp_inv = cand_inv(key)
            cam = camera_for(key, vp, "constant_buffer(%s-major window shared by %d draws, %d affine votes)" % (key[1], window_draws[key[0]], votes[key]))
        elif ref_vp is not None:
            vp, vp_inv = ref_vp, mat_inv(ref_vp)
            cam = camera_for(("ref", 0), vp, "reference_event %s" % opt["reference_event"])
        else:
            o["mode"] = "dropped"
            unresolved += 1
            continue
        cam["draws"] += 1
        for t in o["targets"]:
            cam["targets"][t] += 1
        o["camera"] = cam["index"]
        if "mvp" in o:
            w = mat_mul(vp_inv, o.pop("mvp"))
            if is_affine(w):
                w[3] = [0.0, 0.0, 0.0, 1.0]
                o["mode"] = "rigid"
                o["world"] = flat(w)
                o["mvp_world_t"] = [w[0][3], w[1][3], w[2][3]]
                o.pop("clip", None)
                continue
            o["bake_reason"] = "not_affine(billboard/sky/custom VS)"
        else:
            o["bake_reason"] = "planar_or_deformed" if o.get("conditioned") is False else "fit_error"
        pending_bake.append((o, vp_inv, cam))

    # Scene radius per camera from rigid objects (all assigned above); far-plane / far-away bakes are background.
    for cam in cameras:
        ds = sorted(math.sqrt(sum((x["mvp_world_t"][i] - cam["position"][i]) ** 2 for i in range(3)))
                    for x in objects if x.get("camera") == cam["index"] and "mvp_world_t" in x)
        cam["_radius"] = max(200.0, 20.0 * ds[int(len(ds) * 0.95)]) if ds else 1e5
    for o, vp_inv, cam in pending_bake:
        clip = o.pop("clip")
        src = next(m for m in meshes.values() if m["id"] == o["mesh"])
        pos = []
        for k in range(len(src["streams"]["position"])):
            c = clip.get(k)
            if c is None:
                pos.append((0.0, 0.0, 0.0))
                continue
            wv = mat_vec(vp_inv, c)
            ww = wv[3] if abs(wv[3]) > 1e-12 else 1e-12
            pos.append((wv[0] / ww, wv[1] / ww, wv[2] / ww))
        if not keep_far and is_far_background(list(clip.values()), pos, cam["position"], cam["_radius"]):
            o["mode"] = "dropped_far"
            far_dropped += 1
            continue
        bm = {"id": "b%d" % len(baked_meshes), "streams": {"position": pos}, "indices": src["indices"], "first_event": o["event"],
              "attribute_map": {"position": "baked_from_postvs"}}
        for st_name in ("uv0", "color", "raw"):
            if st_name in src["streams"]:
                bm["streams"][st_name] = src["streams"][st_name]
        baked_meshes.append(bm)
        o["mesh"], o["world"], o["mode"] = bm["id"], identity, "baked"

    if unresolved:
        log("W", unresolved, "draws had no camera matrix in their constant buffers -> dropped (use --reference-event)")
    if far_dropped:
        log("dropped", far_dropped, "far-plane / far-background draws (sky, distant cards); keep them with --keep-far")
    objects[:] = [o for o in objects if o.get("mode") not in ("dropped", "dropped_far")]
    for o in objects:
        o.pop("mvp_world_t", None)
        o.pop("mvp", None)
        o.pop("clip", None)
        o.pop("windows", None)
    for c in cameras:
        c.pop("_radius", None)
        c["targets"] = [t for t, _ in c["targets"].most_common()]
        log("camera", c["index"], "draws", c["draws"], "targets", c["targets"][:3], "VP:", c["vp_source"])
    cameras.sort(key=lambda c: -c["draws"])
    remap_cam = {c["index"]: i for i, c in enumerate(cameras)}
    for i, c in enumerate(cameras):
        c["index"] = i
    for o in objects:
        o["camera"] = remap_cam[o["camera"]]
    if not cameras:
        raise RuntimeError("Could not determine any camera view-projection matrix. Re-run with --reference-event <eid of a draw "
                           "whose object-to-world is identity (static batch / terrain / world-space mesh)>.")

    duplicates = 0
    if not keep_duplicates:
        seen, kept = set(), []
        for o in objects:
            k = (o["mesh"], tuple(round(x, 3) for x in o["world"])) if o["mode"] == "rigid" else None
            if k is not None and k in seen:
                duplicates += 1
                continue
            if k is not None:
                seen.add(k)
            kept.append(o)
        objects[:] = kept
    for o in objects:
        o["textures"] = [r for r in o["textures"] if r not in frame_targets]
        for r in o["textures"]:
            tex_ids.setdefault(r, tex_desc[r])
        if o.get("replay"):
            o["replay"]["textures"] = [t for t in o["replay"]["textures"] if t["resource"] not in frame_targets]
            for t in o["replay"]["textures"]:
                tex_ids.setdefault(t["resource"], tex_desc[t["resource"]])
    for o in objects:
        o.pop("mvp", None)
        o.pop("clip", None)
    if not cameras:
        raise RuntimeError("Could not determine any camera view-projection matrix. Re-run with --reference-event <eid of a draw "
                           "whose object-to-world is identity (static batch / terrain / world-space mesh)>.")

    # ---- write meshes
    mesh_out = []
    for m in list(meshes.values()) + baked_meshes:
        if not any(o["mesh"] == m["id"] for o in objects):
            continue
        path = os.path.join(OUT, "meshes", m["id"] + ".bin")
        vc = len(m["streams"]["position"])
        layout = {k: {"offset": -1, "components": 0} for k in ("position", "normal", "uv0", "color")}
        off = 0
        with open(path, "wb") as f:
            for sname, comps, ch in (("position", 3, "f"), ("normal", 3, "f"), ("uv0", 2, "f"), ("color", 4, "B")):
                vals = m["streams"].get(sname)
                if vals is None:
                    continue
                buf = bytearray()
                st = struct.Struct("<" + ch * comps)
                for v in vals:
                    t = (tuple(v) + (0.0,) * comps)[:comps]
                    if ch == "B":
                        t = tuple(max(0, min(255, int(round(x * 255)))) for x in t)
                    elif not all(math.isfinite(x) for x in t):
                        t = (0.0,) * comps
                    buf += st.pack(*t)
                f.write(buf)
                layout[sname] = {"offset": off, "components": comps}
                off += len(buf)
            raw_layout = []
            for loc, vals in sorted((m["streams"].get("raw") or {}).items()):
                buf = bytearray()
                st4 = struct.Struct("<4f")
                for v in vals:
                    buf += st4.pack(*[x if math.isfinite(x) else 0.0 for x in v])
                f.write(buf)
                raw_layout.append({"location": loc, "offset": off})
                off += len(buf)
            layout["raw"] = raw_layout
            ib = struct.pack("<%dI" % len(m["indices"]), *m["indices"])
            f.write(ib)
            layout["indices"] = {"offset": off, "components": len(m["indices"])}
        mesh_out.append({"id": m["id"], "file": "meshes/%s.bin" % m["id"], "vertex_count": vc, "layout": layout,
                         "first_event": m["first_event"], "attributes": ["%s=%s" % kv for kv in sorted(m["attribute_map"].items())],
                         "space": "world" if m["id"].startswith("b") else "object"})

    # ---- textures
    tex_out = {}
    if save_textures:
        for rid, td in tex_ids.items():
            tid = "t%d" % len(tex_out)
            if td.type in (rd.TextureType.TextureCube, rd.TextureType.TextureCubeArray) or td.cubemap:
                faces = []
                for face in range(6):
                    ts = rd.TextureSave()
                    ts.resourceId = td.resourceId
                    ts.destType = rd.FileType.PNG
                    ts.mip = 0
                    ts.slice.sliceIndex = face
                    ts.alpha = rd.AlphaMapping.Preserve
                    fpath = os.path.join(OUT, "textures", "%s_f%d.png" % (tid, face))
                    try:
                        if ctl.SaveTexture(ts, fpath).OK():
                            faces.append("textures/%s_f%d.png" % (tid, face))
                    except Exception as e:
                        log("W cube face", rid, face, e)
                if len(faces) == 6:
                    tex_out[rid] = {"id": tid, "file": faces[0], "faces": faces, "kind": "cube", "resource": rid, "width": td.width,
                                    "height": td.height, "format": td.format.Name(), "srgb": td.format.compType == rd.CompType.UNormSRGB}
                continue
            path = os.path.join(OUT, "textures", tid + ".png")
            ts = rd.TextureSave()
            ts.resourceId = td.resourceId
            ts.destType = rd.FileType.PNG
            ts.mip = 0
            ts.alpha = rd.AlphaMapping.Preserve
            try:
                r = ctl.SaveTexture(ts, path)
                ok = r.OK() if hasattr(r, "OK") else bool(r)
            except Exception as e:
                ok = False
                log("W texture", rid, e)
            if ok:
                tex_out[rid] = {"id": tid, "file": "textures/%s.png" % tid, "kind": "2d", "faces": [], "resource": rid, "width": td.width, "height": td.height,
                                "format": td.format.Name(), "srgb": td.format.compType == rd.CompType.UNormSRGB}
    for o in objects:
        if o.get("replay"):
            for t in o["replay"]["textures"]:
                t["texture"] = tex_out[t["resource"]]["id"] if t["resource"] in tex_out else ""
        o["textures"] = [tex_out[r]["id"] for r in o["textures"] if r in tex_out and tex_out[r]["kind"] == "2d"]
        o["albedo"] = next((tex_out[r]["id"] for r in tex_out if tex_out[r]["id"] in o["textures"] and tex_out[r]["srgb"]), o["textures"][0] if o["textures"] else "")
        if not o.get("replay"):
            o.pop("replay", None)
        o.setdefault("bake_reason", "")
        o["fit_error"] = float(o["fit_error"]) if math.isfinite(o["fit_error"]) else -1.0

    # ---- reference image: the presented swapchain image if the capture has one, else the main camera's colour target
    reference = None
    swap = [t for t in ctl.GetTextures() if t.creationFlags & rd.TextureCategory.SwapBuffer]
    cand = []
    if swap:
        cand.append((swap[0], draws[-1][0].eventId, "swapchain"))
    if cameras and cameras[0]["targets"]:
        main_target = cameras[0]["targets"][0]
        td = tex_desc.get(main_target)
        last_eid = max(o["event"] for o in objects if main_target in o["targets"])
        if td is not None:
            cand.append((td, last_eid, "main_camera_target(render targets may be vertically flipped)"))
    for td, eid, kind in cand:
        try:
            ctl.SetFrameEvent(eid, True)
            ts = rd.TextureSave()
            ts.resourceId = td.resourceId
            ts.destType = rd.FileType.PNG
            ts.alpha = rd.AlphaMapping.Discard
            r = ctl.SaveTexture(ts, os.path.join(OUT, "reference.png"))
            if r.OK():
                reference = {"file": "reference.png", "target": str(td.resourceId), "event": eid, "width": td.width, "height": td.height, "kind": kind}
                break
        except Exception as e:
            log("W reference", kind, e)

    if want_shaders and families:
        fam_out = []
        for f in families.values():
            fam_out.append(dict(f, states=list(f["states"].values())))
        json.dump({"families": fam_out}, open(os.path.join(OUT, "shaders", "families.json"), "w", encoding="utf-8"), indent=1)
        log("shader families", len(fam_out), "states", sum(len(f["states"]) for f in fam_out),
            "push-constant blocks not replayed" if skipped_push_constants else "", dict(skipped_push_constants) if skipped_push_constants else "")
    for o in objects:
        o.pop("ordinal", None)
    scene = {"format": "renderdoc-to-unity/1", "capture": ARGS["capture"], "api": str(props.pipelineType),
             "renderdoc": rd.GetVersionString(), "cameras": cameras, "reference": reference,
             "options": {k: v for k, v in ARGS.items() if k not in ("capture", "out")},
             "stats": {"selected_draws": selected, "objects": len(objects), "meshes": len(mesh_out), "textures": len(tex_out),
                       "modes": dict(collections.Counter(o["mode"] for o in objects)), "skipped": dict(skipped), "duplicates_removed": duplicates, "far_background_dropped": far_dropped,
                       "unresolved_camera_draws": unresolved},
             "meshes": mesh_out, "textures": list(tex_out.values()), "objects": objects}
    json.dump(scene, open(os.path.join(OUT, "scene.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    log("extract done", json.dumps(scene["stats"]))


try:
    main()
except Exception:
    log("ERROR", traceback.format_exc())
finally:
    LOG.close()
    os._exit(0)
