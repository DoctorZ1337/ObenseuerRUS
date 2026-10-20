"""
Долгий проход (30–60 минут) по всем сценам и ресурсам игры: собирает строковые поля MonoBehaviour
из сборок игры (TextMeshPro m_text, title, description и т.п.). Игра только читается.

  python -I tools/extract/scan_scenes.py [--game ПАПКА] [--out build/scene_strings.json] [файлы...]

Результат использует extract.py (--only ui) вместе с правилами ui_rules.py.
"""
import argparse
import json
import os
import re
import struct
import sys
import time
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import UnityPy  # noqa: E402
from unity_read import Reader, game_dir  # noqa: E402

GAME_ASM = {"Assembly-CSharp", "Assembly-CSharp-firstpass", "Unity.TextMeshPro", "UnityEngine.UI"}
SKIP_CLASSES = {"Image", "Outline", "Button", "ContentSizeFitter", "Mask", "GraphicRaycaster", "LayoutElement",
                "GridLayoutGroup", "VerticalLayoutGroup", "HorizontalLayoutGroup", "ProBuilderMesh", "HxVolumetricLight",
                "LightController", "FootStepsSurface", "OverrideTransform", "BoneRenderer", "AnimationRigHelper",
                "CanvasScaler", "RectMask2D", "Shadow", "ScrollRect", "Scrollbar", "Slider", "Toggle", "RawImage", "Node",
                "DialogueDatabase", "LocalizationSettings", "TMP_FontAsset", "TMP_SpriteAsset", "TMP_Settings",
                "TMP_StyleSheet", "logic_objects_visibility", "TextButtonColorEffectTMP", "ComponentHider", "Relay"}
SKIP_KEYS = {"m_GameObject", "m_Script", "m_Name", "m_EditorClassIdentifier"}
SURROGATE = re.compile("[\ud800-\udfff]")


def monoscripts(env):
    names = {}
    for fname, f in env.files.items():
        for s in [f] + list(getattr(f, "files", {}).values()):
            objs = getattr(s, "objects", None)
            if not objs:
                continue
            for pid, o in objs.items():
                if o.type.name == "MonoScript":
                    m = o.parse_as_object()
                    names[(os.path.basename(getattr(s, "name", fname)).lower(), pid)] = (m.m_ClassName, m.m_AssemblyName.replace(".dll", ""))
    return names


def walk(v, path, acc):
    if isinstance(v, str):
        if re.search(r"[A-Za-z]{2,}", v) and not SURROGATE.search(v):
            acc.append((path, v))
    elif isinstance(v, dict):
        for k, x in v.items():
            if k not in SKIP_KEYS:
                walk(x, path + "." + k if path else k, acc)
    elif isinstance(v, list):
        for x in v[:2000]:
            walk(x, path + "[]", acc)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--game")
    ap.add_argument("--out", default=os.path.join(ROOT, "build", "scene_strings.json"))
    ap.add_argument("--worker", help="внутренний режим: обработать один файл и записать часть результата")
    ap.add_argument("only", nargs="*")
    args = ap.parse_args()
    reader = Reader(game_dir(args.game))
    data, bundles = reader.data, reader.bundles
    if args.worker:
        scan_files(reader, [args.worker], args.out)
        return
    # UnityPy не отпускает память между файлами, поэтому каждый файл игры читается отдельным процессом.
    import subprocess
    files = ["resources.assets"] + sorted(f for f in os.listdir(data) if f.startswith("level") or (f.startswith("sharedassets") and f.endswith(".assets")))
    files += [os.path.join(bundles, f) for f in os.listdir(bundles) if f.startswith("defaultlocalgroup")]
    if args.only:
        files = [f for f in files if os.path.basename(f) in args.only]
    parts = os.path.join(os.path.dirname(args.out), "scene_parts")
    os.makedirs(parts, exist_ok=True)
    t0 = time.time()
    merged = defaultdict(lambda: defaultdict(set))
    for f in files:
        part = os.path.join(parts, os.path.basename(f) + ".json")
        if not os.path.exists(part):
            cmd = [sys.executable, "-I", os.path.abspath(__file__), "--worker", f, "--out", part] + (["--game", args.game] if args.game else [])
            subprocess.run(cmd, check=True)
        with open(part, encoding="utf-8") as fh:
            for val, places in json.load(fh)["strings"].items():
                for k, fs in places.items():
                    merged[val][k].update(fs)
        print("%s: уникальных строк всего %d, %.0f c" % (os.path.basename(f), len(merged), time.time() - t0), flush=True)
    res = {v: {k: sorted(fs)[:5] for k, fs in d.items()} for v, d in merged.items()}
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump({"strings": res}, fh, ensure_ascii=False)
    print("готово:", len(res))


def scan_files(reader, files, out):
    data, bundles = reader.data, reader.bundles
    base = UnityPy.load(os.path.join(data, "globalgamemanagers.assets"),
                        os.path.join(bundles, [f for f in os.listdir(bundles) if "_monoscripts_" in f][0]))
    shared = monoscripts(base)
    found = defaultdict(lambda: defaultdict(set))
    t0 = time.time()
    for f in files:
        env = UnityPy.load(f if os.path.isabs(f) else os.path.join(data, f))
        env.typetree_generator = reader.gen()
        n = 0
        for fname, ff in list(env.files.items()):
            for s in [ff] + list(getattr(ff, "files", {}).values()):
                objs = getattr(s, "objects", None)
                if not objs:
                    continue
                local = {}
                for pid, o in objs.items():
                    if o.type.name == "MonoScript":
                        m = o.parse_as_object()
                        local[pid] = (m.m_ClassName, m.m_AssemblyName.replace(".dll", ""))
                for pid, o in objs.items():
                    if o.type.name != "MonoBehaviour":
                        continue
                    try:
                        fid, spid = struct.unpack_from("<iq", o.get_raw_data(), 16)
                        if fid == 0:
                            cn, asm = local.get(spid, ("?", "?"))
                        else:
                            cn, asm = shared.get((os.path.basename(s.externals[fid - 1].path).lower(), spid), ("?", "?"))
                    except Exception:
                        continue
                    if cn in SKIP_CLASSES or asm not in GAME_ASM:
                        continue
                    try:
                        tree = o.read_typetree()
                    except Exception:
                        continue
                    acc = []
                    walk(tree, "", acc)
                    for path, val in acc:
                        found[val][cn + ":" + path].add(os.path.basename(f))
                        n += 1
        print("%s: %d строк, уникальных %d, %.0f c" % (os.path.basename(f), n, len(found), time.time() - t0), flush=True)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    res = {v: {k: sorted(fs) for k, fs in d.items()} for v, d in found.items()}
    with open(out, "w", encoding="utf-8") as fh:
        json.dump({"strings": res}, fh, ensure_ascii=False)


if __name__ == "__main__":
    main()
