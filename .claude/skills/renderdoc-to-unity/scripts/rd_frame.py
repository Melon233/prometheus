# -*- coding: utf-8 -*-
"""Frame-replay extraction (runs INSIDE qrenderdoc, called by rd_inside.py in `frame` mode).

Records the frame as an ordered op list that the Unity FrameReplay player executes with a CommandBuffer:
  pass  - render pass begin: colour/depth attachments, load ops and clear values
  draw  - one draw: shader family + full render state, viewport/scissor, raw constant buffers, texture bindings,
          raw vertex attributes, instances
  blit  - image blit/resolve/copy between textures
Every texture used by the frame is described (format, size, mips, kind); textures read before they are written in
the range are exported with their contents (raw bytes for uncompressed formats, RGB8 widened to RGBA8, decoded PNG
for block-compressed), and each pass's first colour target is exported at the end of the pass as ground truth.
Draws also keep the post-VS transform fit (or a few position -> clip samples) so make_camera_bindings.py can find
each object's matrix.

Vulkan: passes come from vkCmdBeginRenderPass (load ops, clear values), shaders are SPIR-V.
OpenGL: there are no passes - one is opened whenever the bound framebuffer changes (contents loaded) and at every
glClear (values parsed from the call name). Shaders are GLSL (rewritten by glsl_globals.py and compiled to SPIR-V
later); default-block uniforms are packed std140 in declaration order; attribute locations come from
glBindAttribLocation (reflection's regIndex is only name-sorted); inputs no buffer feeds take the GL generic value;
sampling state lives on the texture (the descriptor's sampler has no object); front faces flip (window y-up).
"""
import base64, collections, json, math, os, re, struct

# RenderDoc format name -> Unity GraphicsFormat enum name
UNITY_FORMAT = {
    "R8G8B8A8_UNORM": "R8G8B8A8_UNorm", "R8G8B8A8_SRGB": "R8G8B8A8_SRGB", "R8G8B8A8_SNORM": "R8G8B8A8_SNorm",
    "B8G8R8A8_UNORM": "B8G8R8A8_UNorm", "B8G8R8A8_SRGB": "B8G8R8A8_SRGB", "R8G8B8A8_UINT": "R8G8B8A8_UInt",
    "R8_UNORM": "R8_UNorm", "R8G8_UNORM": "R8G8_UNorm", "R8_UINT": "R8_UInt",
    "R16_FLOAT": "R16_SFloat", "R16G16_FLOAT": "R16G16_SFloat", "R16G16B16A16_FLOAT": "R16G16B16A16_SFloat",
    "R16_UNORM": "R16_UNorm", "R16G16_UNORM": "R16G16_UNorm", "R16G16B16A16_UNORM": "R16G16B16A16_UNorm",
    "R32_FLOAT": "R32_SFloat", "R32G32_FLOAT": "R32G32_SFloat", "R32G32B32A32_FLOAT": "R32G32B32A32_SFloat",
    "R32_UINT": "R32_UInt", "R16_UINT": "R16_UInt",
    "R11G11B10_FLOAT": "B10G11R11_UFloatPack32", "R10G10B10A2_UNORM": "A2B10G10R10_UNormPack32",
    "R9G9B9E5_FLOAT": "E5B9G9R9_UFloatPack32", "R5G6B5_UNORM": "B5G6R5_UNormPack16",
    "D16": "D16_UNorm", "D24S8": "D24_UNorm_S8_UInt", "D24": "D24_UNorm_S8_UInt", "D32": "D32_SFloat", "D32S8": "D32_SFloat_S8_UInt",
}
COMPRESSED = ("BC1", "BC2", "BC3", "BC4", "BC5", "BC6", "BC7", "ASTC", "ETC2", "EAC", "PVRTC")


PACKED4 = ("B4G4R4A4_UNORM", "R4G4B4A4_UNORM", "A4R4G4B4_UNORM")


def nibble_order(ctl, rd, td, data):
    """Bit shifts (r, g, b, a) of a 4-4-4-4 texture, chosen so the decode matches RenderDoc's own PickPixel."""
    import itertools
    w = td.width
    sub = rd.Subresource()
    sub.mip, sub.slice, sub.sample = 0, 0, 0
    n = len(data) // 2
    picks = []
    for k in range(min(n, 64)):
        i = (k * 7919) % n
        x, y = i % w, i // w
        if y >= td.height:
            continue
        pv = ctl.PickPixel(td.resourceId, x, y, sub, rd.CompType.Typeless).floatValue
        picks.append((struct.unpack_from("<H", data, i * 2)[0], [round(pv[c] * 15) for c in range(4)]))
    errs = {perm: sum(abs(((v >> perm[c]) & 15) - ref[c]) for v, ref in picks for c in range(4))
            for perm in itertools.permutations((12, 8, 4, 0))}
    best_err = min(errs.values())
    tied = [perm for perm, e in errs.items() if e == best_err]
    return tied[0], best_err, len(tied) == 1   # not unique: the texture cannot tell the channels apart (e.g. all zero)


def widen_4444(data, shifts):
    out = bytearray(len(data) // 2 * 4)
    for i, (v,) in enumerate(struct.iter_unpack("<H", data[:len(data) // 2 * 2])):
        for c in range(4):
            out[i * 4 + c] = ((v >> shifts[c]) & 15) * 17
    return bytes(out)


def child(obj, name):
    try:
        for i in range(obj.NumChildren()):
            c = obj.GetChild(i)
            if c.name == name:
                return c
    except Exception:
        pass
    return None


def children(obj):
    try:
        return [obj.GetChild(i) for i in range(obj.NumChildren())]
    except Exception:
        return []


def run(rd, ctl, sf, tex_desc, props, ARGS, OUT, log, helpers):
    make_decoder, read_indices, to_triangles = helpers["make_decoder"], helpers["read_indices"], helpers["to_triangles"]
    read_postvs, fit_mvp = helpers["read_postvs"], helpers["fit_mvp"]
    first, last = ARGS.get("event_first", 0), ARGS.get("event_last", 1 << 31)
    validate = ARGS.get("validate", True)
    FD = OUT
    for sub in ("meshes", "textures", "shaders", "validate"):
        os.makedirs(os.path.join(FD, sub), exist_ok=True)

    # image views -> images (render pass attachments are views)
    parents = collections.defaultdict(list)   # view -> [device, image, ...]
    for r in ctl.GetResources():
        for p in r.parentResources:
            parents[str(r.resourceId)].append(str(p))

    def image_of(rid, depth=0):
        rid = str(rid)
        if rid in tex_desc:
            return rid
        if depth > 3:
            return None
        for p in parents.get(rid, ()):
            hit = image_of(p, depth + 1)
            if hit:
                return hit
        return None

    def walk(acts):
        for a in acts:
            yield a
            yield from walk(a.children)

    actions = [a for a in walk(ctl.GetRootActions()) if first <= a.eventId <= last]
    ops, resources = [], collections.OrderedDict()   # rid -> {"written": bool, "first_read": eid or None, ...}
    families = collections.OrderedDict()
    meshes = collections.OrderedDict()
    mesh_raw = {}   # mesh id -> {location: [float4]} (kept in memory for the transform fit)
    fam_pos_loc = {}  # family id -> position input location found by a successful fit
    unsupported = collections.Counter()
    pass_open = None
    is_gl = props.pipelineType in (rd.GraphicsAPI.OpenGL,)
    import glsl_globals
    glsl_cache = {}

    def glsl_source(refl):
        """GLSL text of a shader (OpenGL captures)."""
        k = str(refl.resourceId)
        if k not in glsl_cache:
            if len(refl.debugInfo.files) > 0:
                glsl_cache[k] = refl.debugInfo.files[0].contents
            else:
                glsl_cache[k] = bytes(refl.rawBytes).decode("utf-8", "replace")
        return glsl_cache[k]

    def pack_glsl_globals(pipe, stage, refl, ci):
        """Default-block uniforms of a GLSL program packed std140 in declaration order (glsl_globals._RDGlobals)."""
        vals = ctl.GetCBufferVariableContents(pipe.GetGraphicsPipelineObject(), refl.resourceId, stage, pipe.GetShaderEntryPoint(stage), ci, rd.ResourceId.Null(), 0, 0)
        byname = {v.name: v for v in vals}
        layout, size = glsl_globals.std140_layout(glsl_globals.loose_uniforms(glsl_source(refl)))
        buf = bytearray(max(16, size))

        def put(off, words):
            if off + 4 * len(words) <= len(buf):
                struct.pack_into("<%dI" % len(words), buf, off, *words)

        def value(off, t, v):
            _, _, cols, rows = glsl_globals.type_info(t)
            r, c = max(1, v.rows), max(1, v.columns)
            words = list(v.value.u32v[:r * c])
            if cols > 1:     # matrix: RenderDoc gives rows x columns row-major; std140 stores columns
                for ci_ in range(min(cols, c)):
                    put(off + 16 * ci_, [words[ri * c + ci_] for ri in range(min(rows, r))])
            else:
                put(off, words[:rows])

        for t, n, count, off, stride in layout:
            v = byname.get(n)
            if v is None:
                continue
            if count:
                elems = list(v.members) if len(v.members) else [v]
                for e, m in enumerate(elems[:count]):
                    value(off + e * stride, t, m)
            else:
                value(off, t, v)
        return bytes(buf)

    gl_pass_key = [None]

    # OpenGL attribute locations come from glBindAttribLocation (reflection's regIndex is only a name-sorted index)
    attrib_binds = collections.defaultdict(dict)
    if is_gl:
        for c in sf.chunks:
            if c.name == "glBindAttribLocation":
                try:
                    attrib_binds[str(child(c, "program").AsResourceId())][child(c, "name").AsString()] = int(child(c, "index").AsInt())
                except Exception:
                    pass

    def attribute_locations(pipe, vs_refl):
        """{shader input name: location}; plus {location: generic constant} for inputs no buffer feeds (GL)."""
        # built-ins (gl_InstanceID, gl_VertexID...) appear in the signature but are not vertex attributes
        sig = {sg.varName: sg.regIndex for sg in vs_refl.inputSignature
               if not sg.varName.startswith("gl_") and sg.systemValue == rd.ShaderBuiltin.Undefined}
        if not is_gl:
            return sig, {}
        glp = ctl.GetGLPipelineState()
        binds = attrib_binds.get(str(glp.vertexShader.programResourceId)) or attrib_binds.get(str(pipe.GetGraphicsPipelineObject())) or {}
        locs = {n: binds.get(n, sig[n]) for n in sig}
        fed = {v.name for v in pipe.GetVertexInputs() if v.used and not getattr(v, "genericEnabled", False)}
        generic = {}
        for n, loc in locs.items():
            if n not in fed and 0 <= loc < len(glp.vertexInput.attributes):
                g = tuple(glp.vertexInput.attributes[loc].genericValue.floatValue)[:4]
                generic[loc] = tuple(g) + (0.0, 0.0, 0.0, 1.0)[len(g):]
        return locs, generic

    def close_gl_pass():
        nonlocal pass_open
        if pass_open is not None and validate and pass_open["draws"] > 0:
            pass_open["end_eid"] = pass_open["last_draw"]
        if pass_open is not None:
            pass_open.pop("last_draw", None)
        pass_open = None
        gl_pass_key[0] = None

    def gl_open_pass(eid, outs, depth, loads=None, clears=None):
        """OpenGL has no render passes: a pass starts whenever the bound framebuffer changes (contents loaded) or at a clear."""
        nonlocal pass_open
        close_gl_pass()
        atts = [o for o in outs if o] + ([depth] if depth else [])
        loads = loads or {"color": "load", "depth": "load", "stencil": "load"}
        for r in atts:
            is_depth = tex_desc[r].format.Name().startswith("D")
            if loads["depth" if is_depth else "color"] == "load":
                use(r, False, eid - 1)      # previous contents (state before this event)
            use(r, True, eid)
        pass_open = {"op": "pass", "eid": eid, "attachments": atts, "clears": clears or [{"color": [0, 0, 0, 0], "depth": 1.0, "stencil": 0}] * len(atts),
                     "loads": loads, "draws": 0, "colors": list(outs), "depth": depth}
        ops.append(pass_open)
        gl_pass_key[0] = (tuple(outs), depth)

    def gl_clear(a, name):
        ctl.SetFrameEvent(a.eventId, False)
        pipe = ctl.GetPipelineState()
        outs = [image_of(o.resource) if o.resource != rd.ResourceId.Null() else None for o in pipe.GetOutputTargets()]
        depth = image_of(pipe.GetDepthTarget().resource) if pipe.GetDepthTarget().resource != rd.ResourceId.Null() else None
        mc = re.search(r"Color\s*=\s*<([^>]*)>", name)
        md = re.search(r"Depth\s*=\s*<([^>]*)>", name)
        ms = re.search(r"Stencil\s*=\s*<([^>]*)>", name)
        col = [float(x) for x in mc.group(1).split(",")] if mc else None
        dval = float(md.group(1)) if md else 1.0
        sval = int(ms.group(1), 0) if ms else 0
        loads = {"color": "clear" if (a.flags & rd.ActionFlags.ClearColor and col) else "load",
                 "depth": "clear" if md else "load", "stencil": "clear" if ms else "load"}
        colors = [o for o in outs if o]
        clears = [{"color": (col or [0, 0, 0, 0]) + [0.0] * (4 - len(col or [])), "depth": dval, "stencil": sval} for _ in colors + ([depth] if depth else [])]
        gl_open_pass(a.eventId, outs, depth, loads, clears)

    def use(rid, write, eid):
        r = resources.setdefault(rid, {"written": False, "first_read": None})
        if write:
            r["written"] = True
        elif not r["written"] and r["first_read"] is None:
            r["first_read"] = eid

    # ------------------------------------------------------------------ helpers (shared with extract semantics)
    def spec_constants(pipe, stage, refl):
        out = {}
        for ci, cb in enumerate(refl.constantBlocks):
            if not cb.compileConstants:
                continue
            try:
                vals = ctl.GetCBufferVariableContents(pipe.GetGraphicsPipelineObject(), refl.resourceId, stage, pipe.GetShaderEntryPoint(stage), ci, rd.ResourceId.Null(), 0, 0)
            except Exception:
                continue
            for v in vals:
                m = re.match(r"specID(\d+)", v.name)
                if m:
                    out[int(m.group(1))] = int(v.value.u32v[0])
        return out

    def pack_inline(pipe, stage, refl, ci, cb):
        vals = ctl.GetCBufferVariableContents(pipe.GetGraphicsPipelineObject(), refl.resourceId, stage, pipe.GetShaderEntryPoint(stage), ci, rd.ResourceId.Null(), 0, 0)
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
                        for sc, sv in zip(t.members, member.members):
                            write(sc, sv, off + e * t.arrayByteStride)
                return
            for sc, sv in zip(t.members, var.members):
                write(sc, sv, off)
        for const, var in zip(cb.variables, vals):
            write(const, var, 0)
        if len(buf) % 16:
            buf.extend(b"\0" * (16 - len(buf) % 16))
        return bytes(buf)

    def hlsl_texture_name(name):
        m = re.fullmatch(r"res(\d+)", name or "")
        return "_" + m.group(1) if m else name

    def enum_name(x):
        return str(x).split(".")[-1]

    # ------------------------------------------------------------------ ops
    for a in actions:
        f = a.flags
        name = a.GetName(sf)
        if is_gl and f & rd.ActionFlags.Clear:
            gl_clear(a, name)
            continue
        if f & rd.ActionFlags.BeginPass and name.startswith("vkCmdBeginRenderPass"):
            m = re.search(r"\((.*)\)", name)
            loads = {"color": "load", "depth": "load", "stencil": "load"}
            spec = m.group(1) if m else ""
            if "=" not in spec:
                for k in loads:
                    loads[k] = "clear" if "Clear" in spec else ("dontcare" if "Care" in spec else "load")
            else:
                for part in spec.split(","):
                    k, _, v = part.strip().partition("=")
                    v = "clear" if "Clear" in v else ("dontcare" if ("Care" in v or "None" in v) else "load")
                    if k == "C":
                        loads["color"] = v
                    elif k in ("DS", "D"):
                        loads["depth"] = v
                        if k == "DS":
                            loads["stencil"] = v
                    elif k == "S":
                        loads["stencil"] = v
            chunk = sf.chunks[a.events[-1].chunkIndex]
            rpb = child(chunk, "RenderPassBegin")
            atts = []
            pnext = child(rpb, "pNext") if rpb is not None else None
            pa = child(pnext, "pAttachments") if pnext is not None else None
            for el in children(pa) if pa is not None else []:
                try:
                    atts.append(image_of(el.AsResourceId()))
                except Exception:
                    atts.append(None)
            clears = []
            pcv = child(rpb, "pClearValues") if rpb is not None else None
            for el in children(pcv) if pcv is not None else []:
                col = child(child(el, "color"), "float32")
                ds = child(el, "depthStencil")
                try:
                    clears.append({"color": [c.AsFloat() for c in children(col)], "depth": child(ds, "depth").AsFloat(), "stencil": child(ds, "stencil").AsInt()})
                except Exception:
                    clears.append({"color": [0, 0, 0, 0], "depth": 1.0, "stencil": 0})
            for r in atts:
                if not r:
                    continue
                is_depth = tex_desc[r].format.Name().startswith("D")
                if loads["depth" if is_depth else "color"] == "load":
                    use(r, False, a.eventId)   # loading previous contents is a read
                use(r, True, a.eventId)
            pass_open = {"op": "pass", "eid": a.eventId, "attachments": atts, "clears": clears, "loads": loads, "draws": 0}
            ops.append(pass_open)
            continue
        if f & rd.ActionFlags.EndPass and name.startswith("vkCmdEndRenderPass"):
            if pass_open is not None and validate and pass_open["draws"] > 0:
                pass_open["end_eid"] = ops[-1]["eid"] if ops and ops[-1]["op"] == "draw" else a.eventId
            pass_open = None
            continue
        if f & (rd.ActionFlags.Copy | rd.ActionFlags.Resolve):
            src, dst = image_of(a.copySource), image_of(a.copyDestination)
            if src and dst:
                use(src, False, a.eventId)
                use(dst, True, a.eventId)
                ops.append({"op": "blit", "eid": a.eventId, "src": src, "dst": dst, "name": name})
            continue
        if not (f & rd.ActionFlags.Drawcall):
            continue

        ctl.SetFrameEvent(a.eventId, False)
        pipe = ctl.GetPipelineState()
        vs_refl = pipe.GetShaderReflection(rd.ShaderStage.Vertex)
        ps_refl = pipe.GetShaderReflection(rd.ShaderStage.Pixel)
        topo = enum_name(pipe.GetPrimitiveTopology())
        want = rd.ShaderEncoding.GLSL if is_gl else rd.ShaderEncoding.SPIRV
        if vs_refl is None or vs_refl.encoding != want or (ps_refl is not None and ps_refl.encoding != want):
            unsupported["shader encoding %s" % (vs_refl.encoding if vs_refl else None)] += 1
            continue
        if topo not in ("TriangleList", "TriangleStrip", "TriangleFan"):
            unsupported["topology " + topo] += 1
            continue
        outs = [image_of(o.resource) if o.resource != rd.ResourceId.Null() else None for o in pipe.GetOutputTargets()]
        depth = image_of(pipe.GetDepthTarget().resource) if pipe.GetDepthTarget().resource != rd.ResourceId.Null() else None
        if is_gl and (pass_open is None or gl_pass_key[0] != (tuple(outs), depth)):
            gl_open_pass(a.eventId, outs, depth)
        for o in outs:
            if o:
                use(o, True, a.eventId)
        if depth:
            use(depth, True, a.eventId)
        if pass_open is not None:
            pass_open["draws"] += 1
            pass_open["last_draw"] = a.eventId
            pass_open.setdefault("colors", [o for o in outs])
            pass_open.setdefault("depth", depth)

        # family
        spec = spec_constants(pipe, rd.ShaderStage.Vertex, vs_refl)
        if ps_refl is not None:
            spec.update(spec_constants(pipe, rd.ShaderStage.Pixel, ps_refl))
        fkey = (str(vs_refl.resourceId), str(ps_refl.resourceId) if ps_refl else "", tuple(sorted(spec.items())))
        fam = families.get(fkey)
        if fam is None:
            fid = len(families)
            if is_gl:
                open(os.path.join(FD, "shaders", "fam%d_vs.glsl" % fid), "w", encoding="utf-8").write(glsl_source(vs_refl))
                if ps_refl is not None:
                    open(os.path.join(FD, "shaders", "fam%d_ps.glsl" % fid), "w", encoding="utf-8").write(glsl_source(ps_refl))
            else:
                open(os.path.join(FD, "shaders", "fam%d_vs.spv" % fid), "wb").write(bytes(vs_refl.rawBytes))
                if ps_refl is not None:
                    open(os.path.join(FD, "shaders", "fam%d_ps.spv" % fid), "wb").write(bytes(ps_refl.rawBytes))
            ext = "glsl" if is_gl else "spv"

            def block_names(refl):
                return [glsl_globals.GLOBALS_BLOCK if (is_gl and not cb.bufferBacked) else cb.name for cb in refl.constantBlocks]
            fam = families[fkey] = {"id": fid, "vs_spv": "fam%d_vs.%s" % (fid, ext), "ps_spv": "fam%d_ps.%s" % (fid, ext) if ps_refl else "",
                                    "lang": "glsl" if is_gl else "spirv",
                                    "vs_attribs": attribute_locations(pipe, vs_refl)[0],
                                    "vs_blocks": block_names(vs_refl),
                                    "ps_blocks": block_names(ps_refl) if ps_refl else [],
                                    "spec_constants": {str(k): v for k, v in spec.items()}, "states": collections.OrderedDict(), "draws": 0}
        fam["draws"] += 1

        # full render state
        blends = []
        for i, b in enumerate(pipe.GetColorBlends()[:len(outs)]):
            blends.append({"enabled": bool(b.enabled), "mask": int(b.writeMask),
                           "src": enum_name(b.colorBlend.source), "dst": enum_name(b.colorBlend.destination), "op": enum_name(b.colorBlend.operation),
                           "src_a": enum_name(b.alphaBlend.source), "dst_a": enum_name(b.alphaBlend.destination), "op_a": enum_name(b.alphaBlend.operation)})
        dts = pipe.GetDepthTestState()
        rs = pipe.GetRasterState()
        sfaces = pipe.GetStencilFaces()
        stencil = None
        if pipe.IsStencilTestEnabled() and sfaces:
            fr = sfaces[0]
            bk = sfaces[1] if len(sfaces) > 1 else sfaces[0]

            def face(x):
                return {"func": enum_name(x.function), "pass": enum_name(x.passOperation), "fail": enum_name(x.failOperation), "zfail": enum_name(x.depthFailOperation)}
            stencil = {"ref": int(fr.reference), "read": int(fr.compareMask), "write": int(fr.writeMask), "front": face(fr), "back": face(bk)}
        # Vulkan clip-space depth convention (VK_EXT_depth_clip_control: GL-translating drivers/emulators use -1..1)
        neg_one, depth_clamp = False, bool(getattr(rs, "depthClampEnable", False))
        try:
            vkp = ctl.GetVulkanPipelineState()
            if vkp is not None:
                neg_one = bool(vkp.viewportScissor.depthNegativeOneToOne)
                depth_clamp = bool(vkp.rasterizer.depthClampEnable)
        except Exception:
            pass
        if is_gl:
            # GL: clip z in -1..1; front faces are judged in window space whose y points up, i.e. with rows reversed
            # relative to the replay's memory-order targets (where Vulkan judges them): the winding flips.
            try:
                glp = ctl.GetGLPipelineState()
                neg_one = bool(glp.vertexProcessing.clipNegativeOneToOne)
            except Exception:
                neg_one = True
        front_ccw = (not bool(rs.frontCCW)) if is_gl else bool(rs.frontCCW)
        state = {"cull": enum_name(rs.cullMode), "front_ccw": front_ccw, "depth_test": bool(dts.depthEnable) and depth is not None,
                 "neg_one_to_one": neg_one, "depth_clamp": depth_clamp,
                 "depth_write": bool(dts.depthWrites) and depth is not None, "depth_func": enum_name(dts.depthFunction),
                 "blends": blends, "stencil": stencil, "outputs": len(outs)}
        skey = json.dumps(state, sort_keys=True)
        if skey not in fam["states"]:
            fam["states"][skey] = dict(state, key="k%d" % len(fam["states"]))
        st_key = fam["states"][skey]["key"]

        # constant buffers
        cbs = {"vs_cb": [], "ps_cb": []}
        for field, stage, refl in (("vs_cb", rd.ShaderStage.Vertex, vs_refl), ("ps_cb", rd.ShaderStage.Pixel, ps_refl)):
            if refl is None:
                continue
            for ci, cb in enumerate(refl.constantBlocks):
                if cb.compileConstants:
                    continue
                try:
                    if cb.bufferBacked:
                        d = pipe.GetConstantBlock(stage, ci, 0).descriptor
                        if d.resource == rd.ResourceId.Null():
                            continue
                        data = bytes(ctl.GetBufferData(d.resource, d.byteOffset, cb.byteSize if cb.byteSize > 0 else min(d.byteSize, 65536)))
                    elif is_gl:
                        data = pack_glsl_globals(pipe, stage, refl, ci)
                    else:
                        data = pack_inline(pipe, stage, refl, ci, cb)
                    cbs[field].append({"block": ci, "data": base64.b64encode(data).decode("ascii")})
                except Exception as e:
                    unsupported["cbuffer %s: %s" % (cb.name, e)] += 1

        # textures + samplers
        texs = []
        sampler_of = {}
        for stage, refl in ((rd.ShaderStage.Vertex, vs_refl), (rd.ShaderStage.Pixel, ps_refl)):
            if refl is None:
                continue
            try:
                for us in pipe.GetSamplers(stage, True):
                    sampler_of[(stage, us.access.index)] = us.sampler
            except Exception:
                pass
            for ud in pipe.GetReadOnlyResources(stage, True):
                idx = ud.access.index
                if idx >= len(refl.readOnlyResources):
                    continue
                d = ud.descriptor
                rid = image_of(d.resource)
                if rid is None:
                    continue
                use(rid, False, a.eventId)
                smp = ud.sampler if hasattr(ud, "sampler") else None
                # GL keeps sampling state on the texture object (no sampler object): the descriptor still carries it
                smp = smp if (smp is not None and (is_gl or smp.object != rd.ResourceId.Null())) else sampler_of.get((stage, idx))
                sinfo = None
                if smp is not None:
                    compare = enum_name(smp.compareFunction) if hasattr(smp, "compareFunction") else ""
                    if is_gl:
                        # GL reports a compare function even when compare mode is off: trust the shader's sampler type
                        tex_name = refl.readOnlyResources[idx].name
                        if not re.search(r"\bsampler\w*Shadow\s+%s\b" % re.escape(tex_name), glsl_source(refl)):
                            compare = ""
                    sinfo = {"filter": enum_name(smp.filter.minify) if hasattr(smp.filter, "minify") else str(smp.filter),
                             "mip": enum_name(smp.filter.mip) if hasattr(smp.filter, "mip") else "",
                             "u": enum_name(smp.addressU), "v": enum_name(smp.addressV), "w": enum_name(smp.addressW),
                             "compare": compare}
                swz = None
                try:
                    sw = d.swizzle
                    swz = [enum_name(sw.red), enum_name(sw.green), enum_name(sw.blue), enum_name(sw.alpha)]
                    if swz == ["Red", "Green", "Blue", "Alpha"]:
                        swz = None
                except Exception:
                    pass
                vs_prefix = "rdvt" if stage == rd.ShaderStage.Vertex else ""
                texs.append({"name": vs_prefix + hlsl_texture_name(refl.readOnlyResources[idx].name), "res": rid, "sampler": sinfo,
                             "depth": "D" in tex_desc[rid].format.Name()[:1], "swizzle": swz})

        # vertices: raw attributes by shader input location, triangle-list indices
        attrs = [v for v in pipe.GetVertexInputs() if v.used]
        if any(v.perInstance for v in attrs):
            unsupported["per-instance vertex attributes (instances use attribute of instance 0)"] += 1
        attrs = [v for v in attrs if not v.perInstance]
        vbs = pipe.GetVBuffers()
        idx = read_indices(rd, ctl, pipe, a)
        valid = [i for i in idx if i >= 0]
        if not valid:
            continue
        lo, hi = min(valid), max(valid)
        loc_of, generic_of = attribute_locations(pipe, vs_refl)
        key = (tuple((str(vbs[v.vertexBuffer].resourceId), vbs[v.vertexBuffer].byteOffset, v.byteOffset, v.format.Name(), loc_of.get(v.name))
                     for v in attrs if v.vertexBuffer < len(vbs)), str(pipe.GetIBuffer().resourceId) if a.flags & rd.ActionFlags.Indexed else "",
               a.indexOffset, a.numIndices, a.baseVertex, a.vertexOffset, topo,
               tuple(sorted(generic_of.items())))
        if key not in meshes:
            used = list(range(lo, hi + 1))   # contiguous so SV_VertexID = base + local index stays exact
            raw = {}
            cache = {}
            for loc, g in generic_of.items():
                if loc <= 7:
                    # input not fed by a buffer: every vertex reads the constant generic value (glVertexAttrib*)
                    raw[loc] = [g] * len(used)
            for v in attrs:
                loc = loc_of.get(v.name)
                if loc is not None and loc in generic_of:
                    continue
                dec = make_decoder(rd, v.format)
                if loc is None or dec is None or loc > 7 or v.vertexBuffer >= len(vbs):
                    continue
                size, fn = dec
                vb = vbs[v.vertexBuffer]
                if v.vertexBuffer not in cache:
                    cache[v.vertexBuffer] = bytes(ctl.GetBufferData(vb.resourceId, vb.byteOffset + lo * vb.byteStride, (hi - lo + 1) * vb.byteStride + 64))
                data = cache[v.vertexBuffer]
                vals = []
                for k in range(len(used)):
                    off = k * vb.byteStride + v.byteOffset
                    t = fn(data, off) if off + size <= len(data) else (0.0,)
                    t = tuple(t) + (0.0, 0.0, 0.0, 1.0)[len(t):] if len(t) < 4 else tuple(t[:4])
                    vals.append(t)
                raw[loc] = vals
            tris = to_triangles(topo, idx) or []
            tris = [i - lo for i in tris if i >= 0]
            tris = tris[:len(tris) - len(tris) % 3]
            mid = "f%d" % len(meshes)
            path = os.path.join(FD, "meshes", mid + ".bin")
            layout, off = [], 0
            with open(path, "wb") as fh:
                for loc, vals in sorted(raw.items()):
                    buf = bytearray()
                    for t in vals:
                        buf += struct.pack("<4f", *[x if math.isfinite(x) else 0.0 for x in t])
                    fh.write(buf)
                    layout.append({"location": loc, "offset": off})
                    off += len(buf)
                fh.write(struct.pack("<%dI" % len(tris), *tris))
            meshes[key] = {"id": mid, "file": "meshes/%s.bin" % mid, "vertex_count": hi - lo + 1, "raw": layout,
                           "index_offset": off, "index_count": len(tris), "base_vertex": lo}
            mesh_raw[mid] = raw
        mesh = meshes[key]

        # object transforms: least-squares fit clip = MVP * position over the post-VS output of every instance,
        # trying each input location as the position (the best one of instance 0 is kept for the others).
        # Planar meshes have no unique fit: a few (position, clip) samples are kept instead, so the scene builder can
        # still recognise the object matrix among the constant-buffer values (VP * M * p == clip).
        fit = None
        if depth is not None or any(outs):
            mvps, samples, pos_loc, err0 = [], [], None, None
            locs = mesh_raw.get(mesh["id"], {})
            # position input: named POSITION (GL), else the family's known one, else every location is tried
            named = [loc_of[n] for n in loc_of if "POSITION" in n.upper() and loc_of[n] in locs]
            guess = named[0] if named else fam_pos_loc.get(fam["id"])
            for inst in range(min(max(1, a.numInstances), 256)):
                try:
                    pv = read_postvs(rd, ctl, inst)
                except Exception:
                    pv = None
                if pv is None:
                    mvps.append(None)
                    samples.append(None)
                    continue
                out_idx, clip_of = pv
                best, keep = None, None
                for loc in ([pos_loc] if pos_loc is not None else ([guess] + [x for x in locs if x != guess] if guess is not None else list(locs))):
                    vals = locs.get(loc)
                    if vals is None:
                        continue
                    pairs, seen = [], set()
                    for k in range(min(len(idx), len(out_idx))):
                        vi = idx[k]
                        if vi < 0 or vi in seen or len(pairs) >= 256:
                            continue
                        seen.add(vi)
                        c = clip_of(out_idx[k])
                        if c is None or not (0 <= vi - lo < len(vals)):
                            continue
                        p = vals[vi - lo]
                        pairs.append(((p[0], p[1], p[2]), c))
                    if loc == (pos_loc if pos_loc is not None else guess) and keep is None:
                        step = max(1, len(pairs) // 6)
                        keep = [{"p": list(pp[0]), "c": list(pp[1])} for pp in pairs[::step][:6]]
                    m, rel, cond = fit_mvp(pairs)
                    if m is not None and cond and rel < 2e-3 and (best is None or rel < best[1]):
                        best = (m, rel, loc)
                samples.append(keep)
                if best is None:
                    mvps.append(None)
                    continue
                if pos_loc is None:
                    pos_loc, err0 = best[2], best[1]
                    fam_pos_loc.setdefault(fam["id"], pos_loc)
                mvps.append([x for row in best[0] for x in row])
            loc_out = pos_loc if pos_loc is not None else guess
            if loc_out is not None:
                fit = {"mvp": mvps[0] if mvps else None, "instance_mvps": mvps, "fit_error": err0, "position_location": loc_out,
                       "instance_samples": samples}
        vp = pipe.GetViewport(0)
        sc = pipe.GetScissor(0)
        ops.append({"op": "draw", "eid": a.eventId, "family": fam["id"], "state": st_key, "mesh": mesh["id"],
                    "colors": outs, "depth": depth, "instances": max(1, a.numInstances), "first_instance": a.instanceOffset,
                    "base_vertex": mesh["base_vertex"], "viewport": [vp.x, vp.y, vp.width, vp.height, vp.minDepth, vp.maxDepth],
                    "scissor": [sc.x, sc.y, sc.width, sc.height], "textures": texs, **cbs, **(fit or {})})

    # ------------------------------------------------------------------ resources: descriptions + contents
    res_out = []
    shift_cache = {}
    nibble_shifts_for = shift_cache.__getitem__
    for rid, info in resources.items():
        td = tex_desc.get(rid)
        if td is None:
            continue
        fmt = td.format.Name()
        kind = "cube" if td.cubemap else ({rd.TextureType.Texture3D: "3d", rd.TextureType.Texture2DArray: "2darray"}.get(td.type, "2d"))
        entry = {"id": rid, "format": fmt, "unity_format": UNITY_FORMAT.get(fmt, ""), "width": td.width, "height": td.height,
                 "depth": td.depth, "array": td.arraysize, "mips": td.mips, "kind": kind, "rt": info["written"],
                 "is_depth": fmt.startswith("D"), "init": None}
        need_contents = info["first_read"] is not None
        if need_contents and entry["is_depth"]:
            # Depth contents carried over from earlier frames (e.g. cached shadow maps): raw depth, written back with SV_Depth.
            ctl.SetFrameEvent(info["first_read"], True)
            sub = rd.Subresource()
            sub.mip, sub.slice, sub.sample = 0, 0, 0
            p = os.path.join(FD, "textures", rid.replace("::", "_") + "_depth.raw")
            data = bytes(ctl.GetTextureData(td.resourceId, sub))
            if is_gl and fmt in ("D24S8", "D24") and len(data) == td.width * td.height * 4:
                # GL returns UNSIGNED_INT_24_8 (depth in the high 24 bits); store Vulkan/D3D order (depth low, stencil high)
                v = memoryview(data).cast("I")
                data = struct.pack("<%dI" % len(v), *[(x >> 8) | ((x & 0xFF) << 24) for x in v])
            open(p, "wb").write(data)
            entry["init"] = {"encoding": "raw_depth", "files": [os.path.relpath(p, FD).replace("\\", "/")], "slices": 1}
        elif need_contents:
            ctl.SetFrameEvent(info["first_read"], True)
            base = os.path.join(FD, "textures", rid.replace("::", "_"))
            # 3-channel 8-bit textures (common on GL): exact raw data widened to RGBA, every mip
            rgb8 = fmt in ("R8G8B8_UNORM", "R8G8B8_SRGB")
            if rgb8:
                entry["unity_format"] = "R8G8B8A8_SRGB" if fmt.endswith("SRGB") else "R8G8B8A8_UNorm"
            p4 = fmt in PACKED4 and kind in ("2d", "cube", "2darray")
            if p4 and fmt not in shift_cache:
                # Decide the bit order once per format, from a texture whose contents tell the channels apart.
                cands = []
                for rid2, info2 in resources.items():
                    td2 = tex_desc.get(rid2)
                    if td2 is None or td2.format.Name() != fmt or info2["first_read"] is None:
                        continue
                    ctl.SetFrameEvent(info2["first_read"], True)
                    sub2 = rd.Subresource()
                    sub2.mip, sub2.slice, sub2.sample = 0, 0, 0
                    cands.append(nibble_order(ctl, rd, td2, bytes(ctl.GetTextureData(td2.resourceId, sub2))))
                good = [c for c in cands if c[2]]
                pick = min(good or cands, key=lambda c: c[1])
                shift_cache[fmt] = pick[0]
                print("%s nibble shifts (r,g,b,a) %s, mismatch %d, decided by %d of %d textures%s"
                      % (fmt, pick[0], pick[1], len(good), len(cands), "" if good else " - AMBIGUOUS"))
                ctl.SetFrameEvent(info["first_read"], True)
            if p4:
                entry["unity_format"] = "R8G8B8A8_UNorm"
                files, shifts = [], None
                slices = 6 if kind == "cube" else (td.arraysize if kind == "2darray" else 1)
                for s_i in range(slices):
                    for mip in range(td.mips):
                        sub = rd.Subresource()
                        sub.mip, sub.slice, sub.sample = mip, s_i, 0
                        data = bytes(ctl.GetTextureData(td.resourceId, sub))
                        if shifts is None:
                            shifts = nibble_shifts_for(fmt)
                            entry["nibble_shifts"] = list(shifts)
                        p = "%s_s%d_m%d.raw" % (base, s_i, mip)
                        open(p, "wb").write(widen_4444(data, shifts))
                        files.append(os.path.relpath(p, FD).replace("\\", "/"))
                entry["init"] = {"encoding": "raw", "files": files, "slices": slices}
            elif rgb8 and kind == "2d":
                files = []
                for mip in range(td.mips):
                    sub = rd.Subresource()
                    sub.mip, sub.slice, sub.sample = mip, 0, 0
                    data = bytes(ctl.GetTextureData(td.resourceId, sub))
                    rgba = bytearray(len(data) // 3 * 4)
                    rgba[0::4], rgba[1::4], rgba[2::4] = data[0::3], data[1::3], data[2::3]
                    rgba[3::4] = b"\xff" * (len(data) // 3)
                    p = "%s_s0_m%d.raw" % (base, mip)
                    open(p, "wb").write(bytes(rgba))
                    files.append(os.path.relpath(p, FD).replace("\\", "/"))
                entry["init"] = {"encoding": "raw", "files": files, "slices": 1}
            elif rgb8 or any(fmt.startswith(c) for c in COMPRESSED) or not entry["unity_format"]:
                files = []
                slices = 6 if kind == "cube" else max(1, td.arraysize if kind == "2darray" else (td.depth if kind == "3d" else 1))
                for s_i in range(slices if kind != "3d" else 1):
                    ts = rd.TextureSave()
                    ts.resourceId = td.resourceId
                    ts.destType = rd.FileType.PNG
                    ts.mip = 0
                    ts.slice.sliceIndex = s_i
                    ts.alpha = rd.AlphaMapping.Preserve
                    p = "%s_s%d.png" % (base, s_i)
                    if ctl.SaveTexture(ts, p).OK():
                        files.append(os.path.relpath(p, FD).replace("\\", "/"))
                entry["init"] = {"encoding": "png", "files": files, "srgb": fmt.endswith("SRGB")}
            else:
                files = []
                slices = 6 if kind == "cube" else (td.arraysize if kind == "2darray" else 1)
                for s_i in range(slices):
                    for mip in range(td.mips):
                        sub = rd.Subresource()
                        sub.mip, sub.slice, sub.sample = mip, s_i, 0
                        data = bytes(ctl.GetTextureData(td.resourceId, sub))
                        dm = max(1, td.depth >> mip)
                        if kind == "3d" and dm > 1:
                            # one call may return a single depth slice: gather all of them for Texture3D.SetPixelData
                            whole = max(1, td.width >> mip) * max(1, td.height >> mip) * dm * td.format.compByteWidth * td.format.compCount
                            if len(data) < whole:
                                parts = [data]
                                for z in range(1, dm):
                                    sub.slice = z
                                    parts.append(bytes(ctl.GetTextureData(td.resourceId, sub)))
                                data = b"".join(parts)
                        p = "%s_s%d_m%d.raw" % (base, s_i, mip)
                        open(p, "wb").write(data)
                        files.append(os.path.relpath(p, FD).replace("\\", "/"))
                entry["init"] = {"encoding": "raw", "files": files, "slices": slices}
        res_out.append(entry)

    if is_gl:
        close_gl_pass()

    # ------------------------------------------------------------------ validation: pass outputs as captured
    if validate:
        for o in ops:
            if o["op"] != "pass" or "end_eid" not in o:
                continue
            target = next((c for c in (o.get("colors") or []) if c), None) or o.get("depth")
            if not target or target not in tex_desc:
                continue
            td = tex_desc[target]
            ctl.SetFrameEvent(o["end_eid"], True)
            if td.format.Name() in UNITY_FORMAT and not td.format.Name().startswith("D"):
                sub = rd.Subresource()
                sub.mip, sub.slice, sub.sample = 0, 0, 0
                p = os.path.join(FD, "validate", "e%d_%s.raw" % (o["end_eid"], target.replace("::", "_")))
                open(p, "wb").write(bytes(ctl.GetTextureData(td.resourceId, sub)))
                o["validate"] = {"target": target, "file": os.path.relpath(p, FD).replace("\\", "/"), "eid": o["end_eid"]}

    # final image: target of the last present, else the last draw's first colour target
    final = None
    for a in reversed(actions):
        if a.flags & rd.ActionFlags.Present:
            final = image_of(a.copyDestination) if a.copyDestination != rd.ResourceId.Null() else None
            if final is None:
                m = re.search(r"ResourceId::\d+", a.GetName(sf))
                final = image_of(m.group(0)) if m else None
            break
    if final is None:
        final = next((c for o in reversed(ops) if o["op"] == "draw" for c in o["colors"] if c), None)

    fam_out = [dict(f, states=list(f["states"].values())) for f in families.values()]
    json.dump({"families": fam_out, "mode": "frame"}, open(os.path.join(FD, "shaders", "families.json"), "w", encoding="utf-8"), indent=1)
    frame = {"format": "renderdoc-to-unity-frame/1", "capture": ARGS["capture"], "api": str(props.pipelineType),
             "renderdoc": rd.GetVersionString(), "event_range": [first, last], "final": final,
             # GL-translated captures (ANGLE-style, depthNegativeOneToOne) keep GL's bottom-up rows in offscreen targets
             "rows_bottom_up": any(st.get("neg_one_to_one") for fam in fam_out for st in fam["states"]),
             "resources": res_out, "meshes": list(meshes.values()), "ops": ops, "unsupported": dict(unsupported)}
    json.dump(frame, open(os.path.join(FD, "frame.json"), "w", encoding="utf-8"), ensure_ascii=False)
    log("frame ops", len(ops), "draws", sum(1 for o in ops if o["op"] == "draw"), "passes", sum(1 for o in ops if o["op"] == "pass"),
        "blits", sum(1 for o in ops if o["op"] == "blit"), "resources", len(res_out), "families", len(fam_out),
        "states", sum(len(f["states"]) for f in fam_out), "meshes", len(meshes), "final", final)
    if unsupported:
        log("frame unsupported/approximated:", dict(unsupported))
    # Completeness hint: screen-sized targets the frame renders into but first uses with contents from before the range.
    # Normal for temporal history (TAA); for a G-buffer or main depth it means part of the frame was drawn before the
    # capture began (emulators split frames at the display thread's swap) - exact at the capture view, stale in a free
    # camera. Confirm with "Validate completeness" in Unity.
    fw, fh = (tex_desc[final].width, tex_desc[final].height) if final in tex_desc else (0, 0)
    carried = [e for e in res_out if e["rt"] and e["init"] and fw and e["width"] * 2 >= fw and e["height"] * 2 >= fh]
    if carried:
        log("carried over into the range (screen-sized, rendered here but first used with earlier contents):",
            ", ".join("%s %s %dx%d" % (e["id"], e["format"], e["width"], e["height"]) for e in carried),
            "- expected for TAA history; a G-buffer/depth here means the frame started before the capture (check completeness)")
