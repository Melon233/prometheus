"""OpenGL captures: make the captured GLSL compilable to Vulkan SPIR-V (then HLSL via spirv-cross).

Shared by rd_frame.py (inside qrenderdoc: packs uniform values) and make_replay_shaders.py (rewrites the source), so
the default-block uniforms get the same std140 layout on both sides.

- Loose (default-block) uniforms become one std140 block `_RDGlobals`, in declaration order. Samplers stay as they are.
- Vertex inputs get the attribute locations the capture used; varyings get locations from their names (identical in
  both stages); fragment outputs keep theirs.
- `#version 330` -> `#version 450`; the `gl_PerVertex` redeclaration is dropped.
"""
import re

GLOBALS_BLOCK = "_RDGlobals"
OPAQUE = re.compile(r"^(sampler|isampler|usampler|image|iimage|uimage|atomic_uint)")
UNIFORM = re.compile(r"^[ \t]*uniform[ \t]+(?:(?:highp|mediump|lowp)[ \t]+)?(\w+)[ \t]+(\w+)[ \t]*(?:\[[ \t]*(\d+)[ \t]*\])?[ \t]*;[ \t]*$", re.M)

# base alignment / size in std140
_SCALAR = {"float", "int", "uint", "bool"}
_VEC = {"vec": "float", "ivec": "int", "uvec": "uint", "bvec": "bool"}


def type_info(t):
    """(size, align, columns, rows): columns > 1 for matrices (column-major, each column a vecN aligned to 16)."""
    if t in _SCALAR:
        return 4, 4, 1, 1
    m = re.fullmatch(r"(vec|ivec|uvec|bvec)([234])", t)
    if m:
        n = int(m.group(2))
        return 4 * n, (8 if n == 2 else 16), 1, n
    m = re.fullmatch(r"mat([234])(?:x([234]))?", t)
    if m:
        c = int(m.group(1))
        r = int(m.group(2) or m.group(1))
        return 16 * c, 16, c, r
    raise ValueError("unsupported uniform type " + t)


def loose_uniforms(src):
    """[(type, name, array_count or 0)] of non-opaque default-block uniforms, in declaration order."""
    out = []
    for m in UNIFORM.finditer(src):
        t, n, c = m.group(1), m.group(2), m.group(3)
        if OPAQUE.match(t):
            continue
        out.append((t, n, int(c) if c else 0))
    return out


def std140_layout(decls):
    """[(type, name, count, offset, stride)] and total size (rounded to 16)."""
    off, out = 0, []
    for t, n, c in decls:
        size, align, cols, rows = type_info(t)
        if c or cols > 1:
            align = 16
        if c:
            stride = (size + 15) // 16 * 16 if cols == 1 else 16 * cols
            off = (off + align - 1) // align * align
            out.append((t, n, c, off, stride))
            off += stride * c
        else:
            off = (off + align - 1) // align * align
            out.append((t, n, 0, off, 0))
            off += size
    return out, (off + 15) // 16 * 16


def rewrite(src, stage, attrib_locations=None, varying_names=None):
    """Return Vulkan-compilable GLSL. attrib_locations: {input name: location} (vertex stage);
    varying_names: sorted list of every varying name of the program (gives both stages the same locations)."""
    src = re.sub(r"^#version\s+\d+.*$", "#version 450", src, count=1, flags=re.M)
    src = re.sub(r"^\s*out\s+gl_PerVertex\s*\{[^}]*\}\s*;\s*$", "", src, flags=re.M)
    # GL built-ins renamed in Vulkan GLSL (GL's ids exclude the base instance/vertex; the replay draws with base 0)
    src = re.sub(r"\bgl_InstanceID\b", "gl_InstanceIndex", src)
    src = re.sub(r"\bgl_VertexID\b", "gl_VertexIndex", src)
    decls = loose_uniforms(src)
    if decls:
        src = UNIFORM.sub(lambda m: "" if not OPAQUE.match(m.group(1)) else m.group(0), src)
        block = "layout(std140) uniform %s {\n%s};\n" % (GLOBALS_BLOCK, "".join(
            "  %s %s%s;\n" % (t, n, "[%d]" % c if c else "") for t, n, c in decls))
        # after the #version line (and any extension lines)
        lines = src.split("\n")
        at = max(i for i, l in enumerate(lines[:20]) if l.startswith("#")) + 1 if any(l.startswith("#") for l in lines[:20]) else 0
        lines.insert(at, block)
        src = "\n".join(lines)
    varying_names = varying_names or []

    def locate(m):
        quals, io, rest = m.group(1) or "", m.group(2), m.group(3)
        if "layout" in quals:
            return m.group(0)
        name = re.search(r"(\w+)\s*(?:\[\s*\d+\s*\])?\s*;", rest).group(1)
        if stage == "vs" and io == "in":
            loc = (attrib_locations or {}).get(name)
        elif (stage == "vs" and io == "out") or (stage == "ps" and io == "in"):
            loc = varying_names.index(name) if name in varying_names else None
        else:
            loc = None
        if loc is None:
            return m.group(0)
        return "%slayout(location = %d) %s %s" % (quals, loc, io, rest)
    src = re.sub(r"^((?:(?:flat|smooth|noperspective|centroid|highp|mediump|lowp)\s+)*)(in|out)\s+(\w+\s+\w+\s*(?:\[\s*\d+\s*\])?\s*;)", locate, src, flags=re.M)
    return src


def varyings(src, io):
    """Names of `in`/`out` variables declared at global scope (no explicit layout)."""
    return [m.group(1) for m in re.finditer(r"^(?:(?:flat|smooth|noperspective|centroid|highp|mediump|lowp)\s+)*%s\s+\w+\s+(\w+)\s*(?:\[\s*\d+\s*\])?\s*;" % io, src, re.M)]
