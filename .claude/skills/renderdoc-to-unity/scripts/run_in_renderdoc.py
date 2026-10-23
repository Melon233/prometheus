#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Run the RenderDoc-side script (rd_inside.py) through qrenderdoc and wait for it.

Normal Python 3.7+ only, no third-party packages. qrenderdoc embeds its own Python and the
`renderdoc` module, so all replay work happens inside it; this launcher only passes arguments,
waits, and prints the log.

Safety: it only ever terminates the qrenderdoc process it started itself (by PID). It never
kills other RenderDoc windows the user may have open.

Modes
  survey   passes, render-target groups and shader families (pick the event range of ONE frame from it)
  frame    whole-frame replay data (frame.json); then make_replay_shaders.py and make_camera_bindings.py run
           automatically. This is the main path (exact lighting, fog, clouds, post-processing; real Unity scene).
  extract  older, simpler path: objects with world transforms + per-object replay materials (scene.json)

Examples
  python run_in_renderdoc.py survey  frame.rdc out_dir
  python run_in_renderdoc.py frame   frame.rdc out_dir --events 79-6189
  python run_in_renderdoc.py extract frame.rdc out_dir --events 1200-3400 --max-draws 2000
"""
import argparse, json, os, shutil, subprocess, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))


def find_qrenderdoc(explicit):
    cands = [explicit] if explicit else []
    cands += [os.environ.get("QRENDERDOC", "")]
    if os.name == "nt":
        for root in (os.environ.get("ProgramFiles", r"C:\Program Files"), os.environ.get("ProgramFiles(x86)", "")):
            if root:
                cands.append(os.path.join(root, "RenderDoc", "qrenderdoc.exe"))
    cands += [shutil.which("qrenderdoc") or "", "/usr/bin/qrenderdoc", "/Applications/RenderDoc.app/Contents/MacOS/RenderDoc"]
    for c in cands:
        if c and os.path.isfile(c):
            return c
    sys.exit("qrenderdoc not found. Install RenderDoc or pass --qrenderdoc <path>.")


def parse_events(s):
    if not s:
        return {}
    a, _, b = s.partition("-")
    return {"event_first": int(a), "event_last": int(b) if b else int(a)}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["survey", "extract", "frame"],
                    help="survey: list passes/shaders; extract: rebuild objects as a Unity scene; frame: record the whole frame for exact replay")
    ap.add_argument("capture", help=".rdc capture file")
    ap.add_argument("out", help="output directory")
    ap.add_argument("--qrenderdoc", help="path to qrenderdoc executable")
    ap.add_argument("--events", help="event range to extract, e.g. 1200-3400 (default: whole frame)")
    ap.add_argument("--targets", help="comma separated colour target ids (e.g. ResourceId::64082) to keep")
    ap.add_argument("--include-no-depth", action="store_true", help="also extract draws without a depth target (UI, post, sky)")
    ap.add_argument("--max-draws", type=int, default=100000)
    ap.add_argument("--max-instances", type=int, default=256, help="instances expanded per instanced draw")
    ap.add_argument("--transform-mode", choices=["auto", "baked"], default="auto",
                    help="auto: recover per-object world matrices, bake only what does not fit; baked: bake every draw to world space")
    ap.add_argument("--fit-tolerance", type=float, default=2e-3, help="relative RMS allowed for a draw to count as rigid")
    ap.add_argument("--reference-event", type=int, help="event of a draw whose object-to-world is identity, used if the camera VP cannot be found automatically")
    ap.add_argument("--keep-duplicates", action="store_true", help="keep objects drawn more than once with the same mesh and world matrix")
    ap.add_argument("--keep-far", action="store_true", help="keep baked draws on the far plane or far beyond the scene (sky domes, distant cards)")
    ap.add_argument("--no-validate", action="store_true", help="frame mode: do not export per-pass ground-truth images")
    ap.add_argument("--no-textures", action="store_true")
    ap.add_argument("--no-shaders", action="store_true", help="skip shader replay data (decompiled shaders, raw constant buffers)")
    ap.add_argument("--spirv-cross", help="path to spirv-cross (default: the one shipped with RenderDoc)")
    ap.add_argument("--max-texture-size", type=int, default=4096)
    ap.add_argument("--timeout", type=float, default=3600, help="seconds before the launched qrenderdoc is terminated")
    a = ap.parse_args()

    capture = os.path.abspath(a.capture)
    if not os.path.isfile(capture):
        sys.exit("capture not found: " + capture)
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    args = {"mode": a.mode, "capture": capture, "out": out, "max_draws": a.max_draws, "max_instances": a.max_instances,
            "transform_mode": a.transform_mode, "fit_tolerance": a.fit_tolerance, "require_depth": not a.include_no_depth,
            "textures": not a.no_textures, "max_texture_size": a.max_texture_size, "keep_duplicates": a.keep_duplicates, "keep_far": a.keep_far, "shaders": not a.no_shaders,
            "validate": not a.no_validate, "script_dir": HERE,
            "targets": [t.strip() for t in a.targets.split(",")] if a.targets else []}
    args.update(parse_events(a.events))
    if a.reference_event:
        args["reference_event"] = a.reference_event
    fd, args_path = tempfile.mkstemp(prefix="rd2u_", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(args, f)

    exe = find_qrenderdoc(a.qrenderdoc)
    env = dict(os.environ, RD2U_ARGS=args_path)
    log_path = os.path.join(out, "rd2u.log")
    if os.path.exists(log_path):
        os.remove(log_path)
    print("qrenderdoc:", exe)
    proc = subprocess.Popen([exe, "--python", os.path.join(HERE, "rd_inside.py")], env=env)
    start, printed = time.time(), 0
    try:
        while proc.poll() is None:
            if time.time() - start > a.timeout:
                proc.kill()  # only the process we started
                sys.exit("timeout after %.0fs; killed our qrenderdoc pid %d" % (a.timeout, proc.pid))
            time.sleep(1.0)
            if os.path.exists(log_path):
                with open(log_path, encoding="utf-8", errors="replace") as f:
                    lines = f.readlines()
                for line in lines[printed:]:
                    print("  " + line.rstrip())
                printed = len(lines)
    finally:
        os.remove(args_path)
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8", errors="replace") as f:
            lines = f.readlines()
        for line in lines[printed:]:
            print("  " + line.rstrip())
        if any(l.startswith("ERROR") for l in lines):
            sys.exit(1)
    else:
        sys.exit("no log written; the script did not start inside qrenderdoc (check RenderDoc version / python support)")
    produced = {"survey": "survey.json", "extract": "scene.json", "frame": "frame.json"}[a.mode]
    if not os.path.exists(os.path.join(out, produced)):
        sys.exit(produced + " was not produced")
    if a.mode in ("extract", "frame") and not a.no_shaders and os.path.exists(os.path.join(out, "shaders", "families.json")):
        cmd = [sys.executable, os.path.join(HERE, "make_replay_shaders.py"), out]
        if a.spirv_cross:
            cmd += ["--spirv-cross", a.spirv_cross]
        subprocess.run(cmd)
    if a.mode == "frame":
        # camera-dependent cbuffer values, for the free-camera replay
        subprocess.run([sys.executable, os.path.join(HERE, "make_camera_bindings.py"), out])
    print("done in %.1fs -> %s" % (time.time() - start, os.path.join(out, produced)))


if __name__ == "__main__":
    main()
