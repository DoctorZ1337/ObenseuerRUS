"""Чтение ресурсов Obenseuer (только чтение). Нужны UnityPy и TypeTreeGeneratorAPI (см. tools/requirements.txt)."""
import os
import struct

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

UNITY_VERSION = "6000.0.77f1"


def game_dir(arg=None):
    candidates = [arg, os.environ.get("OBENSEUER_GAME_DIR"), os.environ.get("OBENSEUER_DIR"),
                  r"C:\Program Files (x86)\Steam\steamapps\common\Obenseuer"]
    for c in candidates:
        if c and os.path.isfile(os.path.join(c, "Obenseuer_Data", "Managed", "Assembly-CSharp.dll")):
            return c
    raise SystemExit("Папка игры не найдена: передайте --game или задайте OBENSEUER_GAME_DIR")


class Reader:
    def __init__(self, game):
        self.data = os.path.join(game, "Obenseuer_Data")
        self.bundles = os.path.join(self.data, "StreamingAssets", "aa", "StandaloneWindows64")
        self._gen = None
        self._scripts = {}

    def gen(self):
        if self._gen is None:
            self._gen = TypeTreeGenerator(UNITY_VERSION)
            self._gen.load_local_dll_folder(os.path.join(self.data, "Managed"))
        return self._gen

    def load(self, *names):
        paths = [n if os.path.isabs(n) else os.path.join(self.data, n) for n in names]
        env = UnityPy.load(*paths, os.path.join(self.data, "globalgamemanagers.assets"))
        env.typetree_generator = self.gen()
        return env

    def script_class(self, obj):
        """Имя класса MonoBehaviour по ссылке m_Script (читается из сырых байтов, без дерева типов)."""
        raw = obj.get_raw_data()
        fid, pid = struct.unpack_from("<iq", raw, 16)
        af = obj.assets_file
        key = (af.name, fid, pid)
        if key not in self._scripts:
            if fid == 0:
                target = af
            else:
                ext = os.path.basename(af.externals[fid - 1].path)
                target = next(f for k, f in af.environment.files.items() if os.path.basename(k) == ext)
            s = target.objects[pid].parse_as_object()
            self._scripts[key] = s.m_ClassName
        return self._scripts[key]

    def find_monobehaviours(self, env, class_name):
        for obj in env.objects:
            if obj.type.name != "MonoBehaviour":
                continue
            try:
                if self.script_class(obj) == class_name:
                    yield obj
            except Exception:
                continue

    def string_tables(self, locales=("english(en)",)):
        """Таблицы Unity Localization: {таблица: [{"key", "id", "<код>": текст}]}."""
        shared = {}
        env = UnityPy.load(os.path.join(self.bundles, "localization-assets-shared_assets_all.bundle"))
        for obj in env.objects:
            if obj.type.name == "MonoBehaviour":
                t = obj.read_typetree()
                shared[t["m_TableCollectionName"]] = [(e["m_Id"], e["m_Key"]) for e in t["m_Entries"]]
        out = {name: [{"key": key, "id": eid} for eid, key in entries] for name, entries in shared.items()}
        index = {(name, row["id"]): row for name, rows in out.items() for row in rows}
        for loc in locales:
            env = UnityPy.load(os.path.join(self.bundles, "localization-string-tables-%s_assets_all.bundle" % loc))
            for obj in env.objects:
                if obj.type.name != "MonoBehaviour":
                    continue
                t = obj.read_typetree()
                name, code = t["m_Name"].rsplit("_", 1)
                for e in t["m_TableData"]:
                    row = index.get((name, e["m_Id"]))
                    if row is not None:  # записи без ключа в общей таблице игра по ключу не найдёт
                        row[code] = e["m_Localized"]
        return out
