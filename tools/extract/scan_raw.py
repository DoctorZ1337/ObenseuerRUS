"""
Дополнение к scan_scenes.py: MonoBehaviour, у которых UnityPy не смог прочитать дерево типов
(scan_scenes.py их пропускает — например, RandommailSender с названиями счетов и писем).
Строки берутся из сырых данных объекта: int32 длина + UTF-8, выравнивание по 4 байтам. Игра только читается.

  python -I tools/extract/scan_raw.py [--game ПАПКА] [--out build/raw_strings.json] [--jobs 4] [файлы...]

Результат: {"classes": {класс: {строка: [файлы]}}}. Имён полей здесь нет, поэтому что из этого видит игрок,
решается по классу (см. RAW_CLASSES в extract.py).
"""
import argparse
import json
import os
import re
import struct
import subprocess
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

GAME_ASM = {"Assembly-CSharp", "Assembly-CSharp-firstpass"}
WORD = re.compile(r"[A-Za-z]{2,}")


def raw_strings(raw):
    """Все правдоподобные строки сериализованного объекта (заголовок MonoBehaviour — первые 28 байт)."""
    out, i = [], 28
    while i + 4 <= len(raw):
        n = struct.unpack_from("<i", raw, i)[0]
        if 2 <= n <= 8000 and i + 4 + n <= len(raw):
            try:
                s = raw[i + 4:i + 4 + n].decode("utf-8")
            except UnicodeDecodeError:
                s = None
            if s and WORD.search(s) and all(c.isprintable() or c in "\n\r\t" for c in s):
                out.append(s)
                i = (i + 4 + n + 3) & ~3
                continue
        i += 4
    return out


def scan_file(reader, path):
    import UnityPy
    from scan_scenes import monoscripts
    data, bundles = reader.data, reader.bundles
    base = UnityPy.load(os.path.join(data, "globalgamemanagers.assets"),
                        os.path.join(bundles, [f for f in os.listdir(bundles) if "_monoscripts_" in f][0]))
    shared = monoscripts(base)
    found = defaultdict(lambda: defaultdict(set))
    env = UnityPy.load(path if os.path.isabs(path) else os.path.join(data, path))
    env.typetree_generator = reader.gen()
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
                    raw = o.get_raw_data()
                    fid, spid = struct.unpack_from("<iq", raw, 16)
                    if fid == 0:
                        cn, asm = local.get(spid, ("?", "?"))
                    else:
                        cn, asm = shared.get((os.path.basename(s.externals[fid - 1].path).lower(), spid), ("?", "?"))
                except Exception:
                    continue
                if asm not in GAME_ASM:
                    continue
                try:
                    o.read_typetree()
                    continue
                except Exception:
                    pass
                for value in raw_strings(raw):
                    found[cn][value].add(os.path.basename(path))
    return found


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    from unity_read import Reader, game_dir
    ap = argparse.ArgumentParser()
    ap.add_argument("--game")
    ap.add_argument("--out", default=os.path.join(ROOT, "build", "raw_strings.json"))
    ap.add_argument("--jobs", type=int, default=4)
    ap.add_argument("--worker", help="внутренний режим: один файл -> часть результата")
    ap.add_argument("only", nargs="*")
    args = ap.parse_args()
    reader = Reader(game_dir(args.game))
    if args.worker:
        res = scan_file(reader, args.worker)
        with open(args.out, "w", encoding="utf-8") as fh:
            json.dump({"classes": {c: {v: sorted(f) for v, f in d.items()} for c, d in res.items()}}, fh, ensure_ascii=False)
        return
    data, bundles = reader.data, reader.bundles
    files = ["resources.assets"] + sorted(f for f in os.listdir(data) if f.startswith("level") or (f.startswith("sharedassets") and f.endswith(".assets")))
    files += [os.path.join(bundles, f) for f in os.listdir(bundles) if f.startswith("defaultlocalgroup")]
    if args.only:
        files = [f for f in files if os.path.basename(f) in args.only]
    parts = os.path.join(os.path.dirname(args.out), "raw_parts")
    os.makedirs(parts, exist_ok=True)
    # UnityPy не отпускает память между файлами, поэтому каждый файл — отдельный процесс.
    running, merged = [], defaultdict(lambda: defaultdict(set))
    pending = [f for f in files if not os.path.exists(os.path.join(parts, os.path.basename(f) + ".json"))]
    while pending or running:
        while pending and len(running) < args.jobs:
            f = pending.pop(0)
            cmd = [sys.executable, "-I", os.path.abspath(__file__), "--worker", f, "--out", os.path.join(parts, os.path.basename(f) + ".json")]
            if args.game:
                cmd += ["--game", args.game]
            running.append((f, subprocess.Popen(cmd)))
        f, p = running.pop(0)
        if p.wait() != 0:
            print("ошибка:", f, flush=True)
        else:
            print("готово:", os.path.basename(f), flush=True)
    for f in files:
        part = os.path.join(parts, os.path.basename(f) + ".json")
        if not os.path.exists(part):
            continue
        with open(part, encoding="utf-8") as fh:
            for cls, values in json.load(fh)["classes"].items():
                for v, fs in values.items():
                    merged[cls][v].update(fs)
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump({"classes": {c: {v: sorted(f)[:5] for v, f in sorted(d.items())} for c, d in sorted(merged.items())}}, fh, ensure_ascii=False, indent=0)
    print("классов:", len(merged), "строк:", sum(len(d) for d in merged.values()))


if __name__ == "__main__":
    main()
