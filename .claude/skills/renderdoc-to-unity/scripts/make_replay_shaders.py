#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Turn captured shaders into Unity .shader files that replay the original shading.

Normal Python 3.7+, no packages. Uses spirv-cross and, for OpenGL captures, glslangValidator (both shipped with
RenderDoc in plugins/spirv/): GLSL is rewritten (glsl_globals.py) and compiled to Vulkan SPIR-V first.

Frame mode (families.json "mode": "frame") writes RDF_fam<N>_<state>.shader: the exact captured render state, clip
space converted to D3D (y flipped, GL -1..1 depth remapped), pixel-shader inputs re-laid out like the vertex outputs
(D3D11 links by register), depth-compare sampling emulated (Unity mirrors comparison samplers on reversed-Z).
The notes below describe the older extract mode.

Input : <extract dir>/shaders/families.json + fam*_vs.spv / fam*_ps.spv (written by `extract --shaders`)
Output: <extract dir>/shaders/RD_fam<N>_<state>.shader + shaders.json (family/state -> shader file, cbuffer names)

How the generated shader works
- The original vertex shader runs unchanged and produces the interpolants for the pixel shader; only SV_Position is
  replaced by UnityObjectToClipPos(mesh position), so the object follows Unity's camera and Transform.
- Original vertex inputs at location k are read from TEXCOORDk (the importer writes attribute k into UV channel k).
- Constant buffers keep their exact packoffset layout and are renamed rdv_<id> / rdp_<id>; the importer binds the
  captured raw bytes per draw with MaterialPropertyBlock.SetConstantBuffer.
- Textures keep their SPIR-V names; samplers are renamed sampler<tex> (Unity's inline sampler convention).
- One pixel-shader output (the colour MRT) is returned; render state (blend, cull, zwrite, ztest) is per state key.
Target: Unity Editor on D3D11/D3D12 (HLSL with packoffset). Other graphics APIs may not compile these shaders.
"""
import json, os, re, shutil, subprocess, sys


def find_spirv_cross(explicit=None):
    cands = [explicit, os.environ.get("SPIRV_CROSS")]
    for root in (os.environ.get("ProgramFiles", r"C:\Program Files"), os.environ.get("ProgramFiles(x86)", "")):
        if root:
            cands.append(os.path.join(root, "RenderDoc", "plugins", "spirv", "spirv-cross.exe"))
    if os.environ.get("VULKAN_SDK"):
        cands.append(os.path.join(os.environ["VULKAN_SDK"], "Bin", "spirv-cross.exe"))
    cands += [shutil.which("spirv-cross") or "", "/usr/share/renderdoc/plugins/spirv/spirv-cross"]
    for c in cands:
        if c and os.path.isfile(c):
            return c
    return None


def find_glslang(spirv_cross):
    """glslangValidator ships next to spirv-cross in RenderDoc's plugins/spirv."""
    cands = [os.path.join(os.path.dirname(spirv_cross), "glslangValidator.exe"), os.path.join(os.path.dirname(spirv_cross), "glslangValidator"),
             shutil.which("glslangValidator") or ""]
    if os.environ.get("VULKAN_SDK"):
        cands.append(os.path.join(os.environ["VULKAN_SDK"], "Bin", "glslangValidator.exe"))
    return next((c for c in cands if c and os.path.isfile(c)), None)


def glsl_to_spirv(glslang, glsl_path, stage, spv_path):
    """Compile rewritten GLSL (see glsl_globals.rewrite) to Vulkan SPIR-V."""
    r = subprocess.run([glslang, "-V", "--auto-map-bindings", "--auto-map-locations", "-S", "vert" if stage == "vs" else "frag",
                        "-o", spv_path, glsl_path], capture_output=True, text=True)
    if r.returncode != 0 or not os.path.exists(spv_path):
        raise RuntimeError("glslang failed for %s: %s" % (glsl_path, (r.stdout + r.stderr).strip()[:600]))


def glsl_family_to_spirv(glslang, sdir, f):
    """OpenGL capture: rewrite + compile both stages of a family; returns (vs.spv, ps.spv or '')."""
    import glsl_globals
    vs = open(os.path.join(sdir, f["vs_spv"]), encoding="utf-8").read()
    ps = open(os.path.join(sdir, f["ps_spv"]), encoding="utf-8").read() if f.get("ps_spv") else ""
    names = sorted(set(glsl_globals.varyings(vs, "out")) | set(glsl_globals.varyings(ps, "in")))
    out = []
    for stage, src in (("vs", vs), ("ps", ps)):
        if not src:
            out.append("")
            continue
        base = os.path.join(sdir, f[stage + "_spv"].rsplit(".", 1)[0])
        text = glsl_globals.rewrite(src, stage, f.get("vs_attribs") if stage == "vs" else None, names)
        gpath = base + (".vert" if stage == "vs" else ".frag")
        open(gpath, "w", encoding="utf-8").write(text)
        glsl_to_spirv(glslang, gpath, stage, base + ".spv")
        out.append(base + ".spv")
    return out[0], out[1]


def spirv_to_hlsl(exe, spv, out):
    r = subprocess.run([exe, "--hlsl", "--shader-model", "50", spv, "--output", out], capture_output=True, text=True)
    if r.returncode != 0 or not os.path.exists(out):
        raise RuntimeError("spirv-cross failed for %s: %s" % (spv, r.stderr.strip()[:400]))
    return open(out, encoding="utf-8").read()


CB_RE = re.compile(r"^cbuffer\s+(\w+)(\s*:\s*register\(\w+\))?", re.M)
INT_TYPES = re.compile(r"^(u?int)(\d?)$")


def rename_cbuffers(code, prefix, block_names):
    """Rename SPIRV-Cross cbuffers to <prefix><id>; returns (code, {new_name: reflection block index})."""
    mapping = {}

    def block_index(name):
        m = re.search(r"_(\d+)$", name)
        for i, bn in enumerate(block_names):
            if bn == name:
                return i
            if m and bn.startswith("uniforms") and bn[len("uniforms"):] == m.group(1):
                return i
        return None

    def sub(m):
        old = m.group(1)
        idx = block_index(old)
        new = "%s%s" % (prefix, idx if idx is not None else old.strip("_"))
        mapping[new] = idx
        return "cbuffer " + new
    return CB_RE.sub(sub, code), mapping


def stage_code(code, stage, block_names):
    """Make one SPIRV-Cross HLSL stage embeddable: unique cbuffer names, Unity sampler names, renamed entry/structs."""
    prefix = "rdv_" if stage == "vs" else "rdp_"
    code, cbmap = rename_cbuffers(code, prefix, block_names)
    code = re.sub(r"\s*:\s*register\([tsbu]\d+\)", "", code)
    # SamplerState __61_sampler -> SamplerState sampler_61 (Unity: "sampler" + texture name)
    code = re.sub(r"\b__(\w+?)_sampler\b", lambda m: "sampler_" + m.group(1), code)
    # Separate sampler objects with other names are not bound by Unity: make them inline linear/repeat samplers.
    for name in re.findall(r"\bSamplerState\s+(\w+)\s*;", code):
        if not name.startswith("sampler_"):
            code = re.sub(r"\b%s\b" % re.escape(name), "rd_%s_linear_repeat" % name.strip("_"), code)
    if stage == "vs":
        # VS and PS are compiled from separate SPIR-V modules whose ids overlap (both may own a texture "_60"):
        # give vertex-stage textures their own names (the extractor binds them as rdvt<name>).
        for name in re.findall(r"\bTexture\w*(?:<[^>]*>)?\s+(\w+)\s*;", code):
            code = re.sub(r"\b%s\b" % re.escape(name), "rdvt" + name, code)
            code = re.sub(r"\bsampler%s\b" % re.escape(name), "samplerrdvt" + name, code)   # Unity: "sampler" + texture name
    tag = "V" if stage == "vs" else "P"
    code = code.replace("SPIRV_Cross_Input", "RD%sIn" % tag).replace("SPIRV_Cross_Output", "RD%sOut" % tag)
    code = re.sub(r"\b(\w+)\s+main\s*\(", lambda m: "%s rd_%s_main(" % (m.group(1), stage), code)
    if stage == "vs":
        # Integer vertex inputs arrive from UV channels as floats: declare float, cast on read.
        struct = re.search(r"struct RDVIn\s*\{(.*?)\};", code, re.S)
        if struct:
            body = struct.group(1)
            casts = {}

            def fix(m):
                ty, name, sem = m.group(1), m.group(2), m.group(3)
                im = INT_TYPES.match(ty)
                if im and sem.upper().startswith("TEXCOORD"):
                    casts[name] = ty
                    ty = "float" + im.group(2)
                return "    %s %s : %s;" % (ty, name, sem)
            body2 = re.sub(r"^\s*(\w+)\s+(\w+)\s*:\s*(\w+);", fix, body, flags=re.M)
            body2 += "    float4 rd_position : POSITION;\n"
            code = code.replace(body, body2)
            for name, ty in casts.items():
                code = code.replace("%s = stage_input.%s;" % (name, name), "%s = %s(stage_input.%s);" % (name, ty, name))
            # Every captured instance is its own Unity object: the original instance index comes from a per-renderer
            # property instead of SV_InstanceID (which is always 0 here).
            vid = re.search(r"^\s*\w+\s+(\w+)\s*:\s*SV_VertexID;", body2, re.M)
            if vid:
                code = code.replace("stage_input.%s" % vid.group(1), "(stage_input.%s + (uint)rd_BaseVertex)" % vid.group(1))
                code = "int rd_BaseVertex;\n" + code
            inst = re.search(r"^\s*\w+\s+(\w+)\s*:\s*SV_InstanceID;", body2, re.M)
            if inst:
                code = code.replace("stage_input.%s" % inst.group(1), "((uint)rd_InstanceIndex)")
                code = "int rd_InstanceIndex;\n" + code
    return code, cbmap


BLEND = {"Zero": "Zero", "One": "One", "SrcCol": "SrcColor", "InvSrcCol": "OneMinusSrcColor", "DstCol": "DstColor",
         "InvDstCol": "OneMinusDstColor", "SrcAlpha": "SrcAlpha", "InvSrcAlpha": "OneMinusSrcAlpha", "DstAlpha": "DstAlpha",
         "InvDstAlpha": "OneMinusDstAlpha", "SrcAlphaSat": "SrcAlphaSaturate"}
ZTEST = {"Never": "Never", "Less": "Less", "Equal": "Equal", "LessEqual": "LEqual", "Greater": "Greater",
         "NotEqual": "NotEqual", "GreaterEqual": "GEqual", "AlwaysTrue": "Always"}
CULL = {"NoCull": "Off", "Front": "Front", "Back": "Back", "FrontAndBack": "Off"}


def render_state(st, reversed_z):
    lines = ["Cull %s" % CULL.get(st.get("cull", "Back"), "Back")]
    lines.append("ZWrite %s" % ("On" if st.get("depth_write", True) else "Off"))
    zt = st.get("depth_func", "LessEqual")
    if reversed_z:  # the capture used reversed-Z; Unity's ZTest is expressed for conventional depth
        zt = {"Greater": "Less", "GreaterEqual": "LessEqual", "Less": "Greater", "LessEqual": "GreaterEqual"}.get(zt, zt)
    lines.append("ZTest %s" % (ZTEST.get(zt, "LEqual") if st.get("depth_test", True) else "Always"))
    if st.get("blend"):
        lines.append("Blend %s %s, %s %s" % (BLEND.get(st.get("src", "One"), "One"), BLEND.get(st.get("dst", "Zero"), "Zero"),
                                             BLEND.get(st.get("src_a", "One"), "One"), BLEND.get(st.get("dst_a", "Zero"), "Zero")))
    return "\n            ".join(lines)


def build_shader(name, vs_code, ps_code, color_out, state, spec_defines, queue):
    defines = "\n".join("#define %s %s" % kv for kv in sorted(spec_defines.items()))
    return """// Generated by renderdoc-to-unity/make_replay_shaders.py. Replays a captured shader pair; do not edit by hand.
Shader "RenderDocReplay/%(name)s"
{
    SubShader
    {
        Tags { "Queue"="%(queue)s" "RenderType"="%(rtype)s" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            %(state)s
            HLSLPROGRAM
            #pragma vertex rd_vert
            #pragma fragment rd_frag
            #pragma target 5.0
            #pragma exclude_renderers gles gles3 glcore metal vulkan switch
            #include "UnityCG.cginc"
%(defines)s
#if defined(SHADER_STAGE_VERTEX)
%(vs)s
RDVOut rd_vert(RDVIn i)
{
    RDVOut o = rd_vs_main(i);
    o.gl_Position = UnityObjectToClipPos(i.rd_position.xyz);
    return o;
}
#endif
#if defined(SHADER_STAGE_FRAGMENT)
%(ps)s
float4 rd_frag(RDPIn i) : SV_Target
{
    RDPOut o = rd_ps_main(i);
    return o.%(color)s;
}
#endif
            ENDHLSL
        }
    }
}
""" % {"name": name, "queue": queue, "rtype": "Transparent" if state.get("blend") else "Opaque", "state": render_state(state, state.get("reversed_z", False)),
       "defines": defines, "vs": vs_code, "ps": ps_code, "color": color_out}


BLEND_OP = {"Add": "Add", "Subtract": "Sub", "ReversedSubtract": "RevSub", "Minimum": "Min", "Maximum": "Max"}
STENCIL_OP = {"Keep": "Keep", "Zero": "Zero", "Replace": "Replace", "IncSat": "IncrSat", "DecSat": "DecrSat", "Invert": "Invert",
              "IncWrap": "IncrWrap", "DecWrap": "DecrWrap"}
COMPARE = {"Never": "Never", "Less": "Less", "Equal": "Equal", "LessEqual": "LEqual", "Greater": "Greater", "NotEqual": "NotEqual",
           "GreaterEqual": "GEqual", "AlwaysTrue": "Always"}
SWAP_DEPTH = {"Less": "Greater", "LessEqual": "GreaterEqual", "Greater": "Less", "GreaterEqual": "LessEqual"}


def frame_state(st):
    """Exact GPU state of a captured draw expressed in ShaderLab (D3D, reversed-Z editor).

    - Unity flips ZTest on reversed-Z platforms, so the captured function is written mirrored.
    - Unity's front face is clockwise on screen; Vulkan's front face is given by frontFace, evaluated in the same
      (y-down) framebuffer space the replay keeps, so CCW-front captures swap Front/Back for Cull and Stencil.
    """
    swap_faces = st.get("front_ccw", False)
    cull = st.get("cull", "NoCull")
    cull_u = {"NoCull": "Off", "FrontAndBack": "Off", "Back": "Back", "Front": "Front"}[cull] if cull in ("NoCull", "FrontAndBack", "Back", "Front") else "Off"
    if swap_faces and cull_u in ("Back", "Front"):
        cull_u = "Front" if cull_u == "Back" else "Back"
    lines = ["Cull %s" % cull_u, "ZWrite %s" % ("On" if st.get("depth_write") else "Off")]
    if st.get("depth_clamp"):
        lines.append("ZClip False")
    if st.get("depth_test"):
        f = st.get("depth_func", "LessEqual")
        lines.append("ZTest %s" % COMPARE.get(SWAP_DEPTH.get(f, f), "LEqual"))
    else:
        lines.append("ZTest Always")
    for i, b in enumerate(st.get("blends", [])):
        mask = b.get("mask", 15)
        letters = "".join(c for bit, c in ((1, "R"), (2, "G"), (4, "B"), (8, "A")) if mask & bit) or "0"
        lines.append("ColorMask %s %d" % (letters, i))
        if b.get("enabled"):
            lines.append("Blend %d %s %s, %s %s" % (i, BLEND.get(b["src"], "One"), BLEND.get(b["dst"], "Zero"), BLEND.get(b["src_a"], "One"), BLEND.get(b["dst_a"], "Zero")))
            lines.append("BlendOp %d %s, %s" % (i, BLEND_OP.get(b.get("op", "Add"), "Add"), BLEND_OP.get(b.get("op_a", "Add"), "Add")))
        else:
            lines.append("Blend %d Off" % i)
    sten = st.get("stencil")
    if sten:
        fr, bk = (sten["back"], sten["front"]) if swap_faces else (sten["front"], sten["back"])
        body = ["Ref %d" % sten["ref"], "ReadMask %d" % sten["read"], "WriteMask %d" % sten["write"]]
        for tag, fc in (("Front", fr), ("Back", bk)):
            body += ["Comp%s %s" % (tag, COMPARE.get(fc["func"], "Always")), "Pass%s %s" % (tag, STENCIL_OP.get(fc["pass"], "Keep")),
                     "Fail%s %s" % (tag, STENCIL_OP.get(fc["fail"], "Keep")), "ZFail%s %s" % (tag, STENCIL_OP.get(fc["zfail"], "Keep"))]
        lines.append("Stencil { %s }" % " ".join(body))
    return "\n            ".join(lines)


CMP_FUNC = {"Never": 0, "Less": 1, "Equal": 2, "LessEqual": 3, "Greater": 4, "NotEqual": 5, "GreaterEqual": 6, "AlwaysTrue": 7, "Always": 7}

CMP_HELPERS = """
// Depth-compare sampling emulated in the shader: Unity mirrors comparison samplers on reversed-Z platforms, but the
// replay keeps the capture's own depth values, so the hardware compare would be inverted. Bilinear PCF = 2x2 Gather.
SamplerState rd_point_clamp_sampler;
float4 rd_cmp_op(float ref, float4 d, int func)
{
    switch (func)
    {
        case 0: return 0;
        case 1: return float4(ref < d);
        case 2: return float4(ref == d);
        case 3: return float4(ref <= d);
        case 4: return float4(ref > d);
        case 5: return float4(ref != d);
        case 6: return float4(ref >= d);
        default: return 1;
    }
}
float rd_cmp(Texture2D<float4> t, float2 uv, float ref, int func, bool bilinear)
{
    float w, h;
    t.GetDimensions(w, h);
    if (!bilinear)
        return rd_cmp_op(ref, t.SampleLevel(rd_point_clamp_sampler, uv, 0).rrrr, func).x;
    float2 p = uv * float2(w, h) - 0.5;
    float2 fr = frac(p);
    float4 r = rd_cmp_op(ref, t.GatherRed(rd_point_clamp_sampler, (floor(p) + 1.0) / float2(w, h)), func);
    return lerp(lerp(r.w, r.z, fr.x), lerp(r.x, r.y, fr.x), fr.y);   // Gather order: (0,1) (1,1) (1,0) (0,0)
}
"""


def split_args(text, start):
    """Parse a parenthesised argument list starting at text[start] == '('. Returns (args, index after ')')."""
    depth, args, cur, i = 0, [], "", start
    while i < len(text):
        ch = text[i]
        if ch == "(":
            depth += 1
            if depth > 1:
                cur += ch
        elif ch == ")":
            depth -= 1
            if depth == 0:
                args.append(cur.strip())
                return args, i + 1
            cur += ch
        elif ch == "," and depth == 1:
            args.append(cur.strip())
            cur = ""
        else:
            cur += ch
        i += 1
    raise ValueError("unbalanced call")


def emulate_compare_sampling(code, samplers):
    """Replace tex.SampleCmp[LevelZero](s, uv, ref) on Texture2D with rd_cmp(); samplers: texture name -> sampler info."""
    tex2d = set(re.findall(r"\bTexture2D(?:<[^>]*>)?\s+(\w+)\s*;", code))
    out, pos, used = [], 0, False
    for m in re.finditer(r"\b(\w+)\.SampleCmp(?:LevelZero)?\s*(?=\()", code):
        if m.start() < pos or m.group(1) not in tex2d:
            continue
        args, end = split_args(code, m.end())
        if len(args) < 3:
            continue
        info = samplers.get(m.group(1)) or {}
        func = CMP_FUNC.get(info.get("compare") or "LessEqual", 3)
        linear = "Point" not in (info.get("filter") or "Linear")
        out.append(code[pos:m.start()])
        out.append("rd_cmp(%s, %s, %s, %d, %s)" % (m.group(1), args[1], args[2], func, "true" if linear else "false"))
        pos, used = end, True
    if not used:
        return code
    out.append(code[pos:])
    code = "".join(out)
    # helpers go after the last texture declaration so they can be called from every function below
    decl = list(re.finditer(r"^\s*Texture\w*(?:<[^>]*>)?\s+\w+\s*;\s*$", code, re.M))
    at = decl[-1].end() if decl else 0
    return code[:at] + "\n" + CMP_HELPERS + code[at:]


STRUCT_MEMBER = re.compile(r"^\s*([^:;{}]+?)\s+(\w+)\s*:\s*(\w+)\s*;\s*$")


def align_ps_inputs(vs_code, ps_code):
    """D3D11 links VS outputs to PS inputs by register, assigned in declaration order: a pixel shader that reads only
    some varyings (or reads them in another order) would see shifted data. Rebuild RDPIn with the VS output layout,
    keeping the PS's own declarations and padding the varyings it does not read."""
    mv = re.search(r"struct RDVOut\s*\{(.*?)\};", vs_code, re.S)
    mp = re.search(r"struct RDPIn\s*\{(.*?)\};", ps_code, re.S)
    if not mv or not mp:
        return ps_code

    def members(body):
        out = []
        for line in body.split("\n"):
            m = STRUCT_MEMBER.match(line)
            if m:
                out.append((m.group(1), m.group(2), m.group(3).upper(), line.strip()))
        return out
    vs_m, ps_m = members(mv.group(1)), members(mp.group(1))
    by_sem = {sem: line for _, _, sem, line in ps_m}
    lines, used = [], set()
    for decl, name, sem, _ in vs_m:
        if sem in by_sem:
            lines.append(by_sem[sem])
            used.add(sem)
        else:
            lines.append("%s rd_pad_%s : %s;" % (decl, sem.lower(), sem))
    lines += [line for _, _, sem, line in ps_m if sem not in used]
    body = "\n" + "".join("    %s\n" % l for l in lines)
    return ps_code[:mp.start(1)] + body + ps_code[mp.end(1):]


def build_frame_shader(name, vs_code, ps_code, state, spec_defines):
    if ps_code:
        ps_code = align_ps_inputs(vs_code, ps_code)
        if state.get("front_ccw"):
            # Faces are swapped for Cull/Stencil (see frame_state): what the capture called front, Unity rasterizes as
            # back, so SV_IsFrontFace must be inverted too (two-sided materials pick normal sign / layer from it).
            ps_code, n = re.subn(r"gl_FrontFacing\s*=\s*stage_input\.gl_FrontFacing\s*;",
                                 "gl_FrontFacing = !stage_input.gl_FrontFacing;", ps_code)
    defines = "\n".join("#define %s %s" % kv for kv in sorted(spec_defines.items()))
    # a pixel shader that reads no varyings has no RDPIn (spirv-cross emits rd_ps_main() without a parameter)
    has_in = bool(ps_code) and re.search(r"struct RDPIn\s*\{", ps_code) is not None
    frag = """%s
RDPOut rd_frag(%s)
{
    return rd_ps_main(%s);
}""" % (ps_code, "RDPIn i" if has_in else "", "i" if has_in else "") if ps_code else "void rd_frag() { }"
    return """// Generated by renderdoc-to-unity/make_replay_shaders.py (frame replay). Exact captured state; do not edit by hand.
Shader "RenderDocFrame/%(name)s"
{
    SubShader
    {
        Pass
        {
            %(state)s
            HLSLPROGRAM
            #pragma vertex rd_vert
            #pragma fragment rd_frag
            #pragma target 5.0
            #pragma exclude_renderers gles gles3 glcore metal vulkan switch
%(defines)s
#if defined(SHADER_STAGE_VERTEX)
%(vs)s
RDVOut rd_vert(RDVIn i)
{
    RDVOut o = rd_vs_main(i);
    o.gl_Position.y = -o.gl_Position.y;   // Vulkan NDC y-down -> D3D y-up, same texel rows in every target
%(zfix)s    return o;
}
#endif
#if defined(SHADER_STAGE_FRAGMENT)
%(frag)s
#endif
            ENDHLSL
        }
    }
}
""" % {"name": name, "state": frame_state(state), "defines": defines, "vs": vs_code, "frag": frag,
       "zfix": "    o.gl_Position.z = (o.gl_Position.z + o.gl_Position.w) * 0.5;   // depthNegativeOneToOne: GL clip z -> 0..1\n"
               if state.get("neg_one_to_one") else ""}


def pick_color_member(ps_code, index):
    m = re.search(r"struct RDPOut\s*\{(.*?)\};", ps_code, re.S)
    if not m:
        return None
    members = re.findall(r"(\w+)\s+(\w+)\s*:\s*SV_Target(\d+);", m.group(1))
    for ty, nm, idx in members:
        if int(idx) == index:
            return nm
    return members[0][1] if members else None


def main():
    import argparse
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("extract_dir")
    ap.add_argument("--spirv-cross")
    a = ap.parse_args()
    sdir = os.path.join(a.extract_dir, "shaders")
    fam = json.load(open(os.path.join(sdir, "families.json"), encoding="utf-8"))
    exe = find_spirv_cross(a.spirv_cross)
    if not exe:
        sys.exit("spirv-cross not found (RenderDoc ships it in plugins/spirv). Pass --spirv-cross <path>.")
    out = {"shaders": [], "failed": []}
    frame_mode = fam.get("mode") == "frame"
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    glslang = find_glslang(exe)
    # sampler state per family/texture (compare function, filter) from the frame description
    fam_samplers = {}
    frame_json = os.path.join(a.extract_dir, "frame.json")
    if frame_mode and os.path.exists(frame_json):
        for op in json.load(open(frame_json, encoding="utf-8")).get("ops", []):
            if op.get("op") == "draw":
                for t in op.get("textures", []):
                    if t.get("sampler"):
                        fam_samplers.setdefault(op["family"], {}).setdefault(t["name"], t["sampler"])
    for f in fam["families"]:
        try:
            if frame_mode:
                if f.get("lang") == "glsl":
                    if not glslang:
                        raise RuntimeError("glslangValidator not found (RenderDoc ships it in plugins/spirv)")
                    vs_spv, ps_spv = glsl_family_to_spirv(glslang, sdir, f)
                else:
                    vs_spv = os.path.join(sdir, f["vs_spv"])
                    ps_spv = os.path.join(sdir, f["ps_spv"]) if f.get("ps_spv") else ""
                vs = spirv_to_hlsl(exe, vs_spv, vs_spv + ".hlsl")
                vs_code, vs_map = stage_code(vs, "vs", f["vs_blocks"])
                ps_code, ps_map = "", {}
                if ps_spv:
                    ps = spirv_to_hlsl(exe, ps_spv, ps_spv + ".hlsl")
                    ps_code, ps_map = stage_code(ps, "ps", f["ps_blocks"])
                smp = fam_samplers.get(f["id"], {})
                vs_code = emulate_compare_sampling(vs_code, smp)
                ps_code = emulate_compare_sampling(ps_code, smp) if ps_code else ps_code
                spec = {"SPIRV_CROSS_CONSTANT_ID_%d" % int(k): "%du" % int(v) for k, v in f.get("spec_constants", {}).items()}
                for st in f["states"]:
                    name = "fam%d_%s" % (f["id"], st["key"])
                    path = os.path.join(sdir, "RDF_%s.shader" % name)
                    open(path, "w", encoding="utf-8").write(build_frame_shader(name, vs_code, ps_code, st, spec))
                    out["shaders"].append({"family": f["id"], "state": st["key"], "file": "shaders/RDF_%s.shader" % name,
                                           "shader_name": "RenderDocFrame/" + name})
                continue
            vs = spirv_to_hlsl(exe, os.path.join(sdir, f["vs_spv"]), os.path.join(sdir, f["vs_spv"] + ".hlsl"))
            ps = spirv_to_hlsl(exe, os.path.join(sdir, f["ps_spv"]), os.path.join(sdir, f["ps_spv"] + ".hlsl"))
            vs_code, vs_map = stage_code(vs, "vs", f["vs_blocks"])
            ps_code, ps_map = stage_code(ps, "ps", f["ps_blocks"])
            spec = {"SPIRV_CROSS_CONSTANT_ID_%d" % int(k): "%du" % int(v) for k, v in f.get("spec_constants", {}).items()}
            for st in f["states"]:
                color = pick_color_member(ps_code, st.get("color_output", 0))
                if color is None:
                    raise RuntimeError("pixel shader has no colour output")
                name = "fam%d_%s" % (f["id"], st["key"])
                queue = "Transparent" if st.get("blend") else "Geometry"
                path = os.path.join(sdir, "RD_%s.shader" % name)
                open(path, "w", encoding="utf-8").write(build_shader(name, vs_code, ps_code, color, st, spec, queue))
                out["shaders"].append({"family": f["id"], "state": st["key"], "file": "shaders/RD_%s.shader" % name,
                                       "shader_name": "RenderDocReplay/" + name,
                                       "vs_cbuffers": [{"name": k, "block": v} for k, v in vs_map.items() if v is not None],
                                       "ps_cbuffers": [{"name": k, "block": v} for k, v in ps_map.items() if v is not None]})
        except Exception as e:
            out["failed"].append({"family": f["id"], "error": str(e)})
    json.dump(out, open(os.path.join(sdir, "shaders.json"), "w", encoding="utf-8"), indent=1)
    print("replay shaders: %d generated, %d families failed" % (len(out["shaders"]), len(out["failed"])))
    for x in out["failed"][:10]:
        print("  family %s: %s" % (x["family"], x["error"]))


if __name__ == "__main__":
    main()
