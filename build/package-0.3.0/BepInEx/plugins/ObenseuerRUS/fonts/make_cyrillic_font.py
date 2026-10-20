"""
Собирает fonts/ObenseuerTypewriterCyrillic.ttf — кириллицу в стиле машинописного Erika Ormig из игры.

Основа — FreeMonoBold.ttf из GNU FreeFont (freefont-ttf-20120503.zip с https://ftp.gnu.org/gnu/freefont/,
SHA-256 архива 7c85baf1bf82a1a1845d1322112bc6ca982221b484e3b3925022e25b5cae89af), лицензия GPL-3.0-or-later
с исключением для шрифтов. Глифы игры не используются: из Erika Ormig взяты только числовые пропорции
(высота прописных и строчных, ширина знака), а неровность краёв генерируется.

  python -I tools/font/make_cyrillic_font.py <путь к FreeMonoBold.ttf> fonts/ObenseuerTypewriterCyrillic.ttf

Нужен пакет fonttools (tools/requirements.txt). Результат детерминирован: одинаковый вход даёт одинаковый файл.
"""
import math
import random
import sys

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

FAMILY = "Obenseuer Typewriter Cyrillic"
CODEPOINTS = [0x0401, 0x0451, 0x2116] + list(range(0x0410, 0x0450))
# Пропорции Erika Ormig относительно FreeMono Bold: высота прописных 0.602/0.583 em, строчных 0.461/0.437 em,
# очко буквы уже при той же ширине знака 0.6 em.
CAPS_Y, LOWER_Y, NARROW_X = 1.03, 1.05, 0.89
STEP = 14.0  # шаг разбиения контура, единиц на 1000 em
WOBBLE = [(120.0, 6.5), (55.0, 3.0), (29.0, 1.0)]  # (длина волны, амплитуда) «чернильной» неровности
BOLD = 4.0  # утолщение штриха с каждой стороны: сужение по X делает вертикали тоньше, чем у Erika


def sample_contours(recording):
    """Контуры как ломаные с шагом около STEP (кривые TrueType — квадратичные B-сплайны)."""
    contours, cur, start = [], [], None

    def line_to(p):
        a = cur[-1]
        n = max(1, int(math.dist(a, p) / STEP))
        cur.extend((a[0] + (p[0] - a[0]) * i / n, a[1] + (p[1] - a[1]) * i / n) for i in range(1, n + 1))

    def quad_to(c, p):
        a = cur[-1]
        n = max(2, int((math.dist(a, c) + math.dist(c, p)) / STEP))
        for i in range(1, n + 1):
            t = i / n
            cur.append(((1 - t) ** 2 * a[0] + 2 * (1 - t) * t * c[0] + t * t * p[0],
                        (1 - t) ** 2 * a[1] + 2 * (1 - t) * t * c[1] + t * t * p[1]))

    for op, args in recording:
        if op == "moveTo":
            cur, start = [args[0]], args[0]
        elif op == "lineTo":
            line_to(args[0])
        elif op == "qCurveTo":
            pts = list(args)
            if pts[-1] is None:
                raise ValueError("контур без опорных точек не поддерживается")
            for i in range(len(pts) - 1):
                c = pts[i]
                end = pts[-1] if i == len(pts) - 2 else ((pts[i][0] + pts[i + 1][0]) / 2, (pts[i][1] + pts[i + 1][1]) / 2)
                quad_to(c, end)
        elif op == "curveTo":
            raise ValueError("ожидался шрифт TrueType (квадратичные кривые)")
        elif op in ("closePath", "endPath"):
            if math.dist(cur[-1], start) > 0.5:
                line_to(start)
            if len(cur) > 3:
                contours.append(cur[:-1])
            cur = []
    return contours


def roughen(points, rng):
    """Сдвигает точки по нормали на плавный шум — как неровный оттиск литеры."""
    waves = [(lam, amp, rng.uniform(0, 2 * math.pi)) for lam, amp in WOBBLE]
    n, s, out = len(points), 0.0, []
    for i, p in enumerate(points):
        prev, nxt = points[i - 1], points[(i + 1) % n]
        if i:
            s += math.dist(points[i - 1], p)
        dx, dy = nxt[0] - prev[0], nxt[1] - prev[1]
        length = math.hypot(dx, dy) or 1.0
        # Нормаль (dy, -dx) у контуров TrueType смотрит внутрь чернил, поэтому утолщение — со знаком минус.
        d = sum(amp * math.sin(2 * math.pi * s / lam + ph) for lam, amp, ph in waves) - BOLD
        out.append((p[0] + dy / length * d, p[1] - dx / length * d))
    return out


def build(base_path, out_path):
    base = TTFont(base_path)
    cmap, glyphs = base.getBestCmap(), base.getGlyphSet()
    advance = base["hmtx"][cmap[ord("a")]][0]
    order, char_map, outlines, metrics = [".notdef", "space"], {0x20: "space"}, {}, {}
    empty = TTGlyphPen(None)
    outlines[".notdef"] = outlines["space"] = empty.glyph()
    metrics[".notdef"] = metrics["space"] = (advance, 0)
    for cp in CODEPOINTS:
        if cp not in cmap:
            raise SystemExit("в базовом шрифте нет U+%04X" % cp)
        rec = DecomposingRecordingPen(glyphs)
        glyphs[cmap[cp]].draw(rec)
        sy = CAPS_Y if chr(cp).isupper() or cp == 0x2116 else LOWER_Y
        rng = random.Random(cp)
        pen = TTGlyphPen(None)
        for contour in sample_contours(rec.value):
            pts = [(advance / 2 + (x - advance / 2) * NARROW_X, y * sy) for x, y in contour]
            pts = roughen(pts, rng)
            pen.moveTo((round(pts[0][0]), round(pts[0][1])))
            for x, y in pts[1:]:
                pen.lineTo((round(x), round(y)))
            pen.closePath()
        name = "uni%04X" % cp
        order.append(name)
        char_map[cp] = name
        outlines[name] = pen.glyph()
        metrics[name] = (advance, 0)

    hhea, os2 = base["hhea"], base["OS/2"]
    fb = FontBuilder(base["head"].unitsPerEm, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(char_map)
    fb.setupGlyf(outlines)
    fb.updateHead(fontRevision=1.0)
    glyf = fb.font["glyf"]
    for g in order:
        glyf[g].recalcBounds(glyf)
    fb.setupHorizontalMetrics({g: (a, getattr(glyf[g], "xMin", 0)) for g, (a, _) in metrics.items()})
    fb.setupHorizontalHeader(ascent=hhea.ascent, descent=hhea.descent)
    fb.setupNameTable({
        "copyright": "Copyright 2002-2012 GNU FreeFont contributors. Cyrillic derivative 2026, ObenseuerRUS.",
        "familyName": FAMILY,
        "styleName": "Regular",
        "uniqueFontIdentifier": FAMILY + " 1.0",
        "fullName": FAMILY,
        "psName": FAMILY.replace(" ", ""),
        "version": "Version 1.0",
        "licenseDescription": "GNU GPL v3 or later with the GNU FreeFont font exception. Derived from FreeMono Bold; see COPYING-FreeFont.txt and FONT-NOTICE.txt.",
        "licenseInfoURL": "https://www.gnu.org/licenses/gpl-3.0.html",
    })
    fb.setupOS2(sTypoAscender=os2.sTypoAscender, sTypoDescender=os2.sTypoDescender, sTypoLineGap=os2.sTypoLineGap,
                usWinAscent=os2.usWinAscent, usWinDescent=os2.usWinDescent, usWeightClass=700,
                sxHeight=round(os2.sxHeight * LOWER_Y), sCapHeight=round(os2.sCapHeight * CAPS_Y),
                fsType=0, achVendID="NONE")
    fb.setupPost(isFixedPitch=1)
    fb.save(out_path)

    # Самопроверка: все знаки на месте, у каждой буквы есть контуры, ширина моноширинная.
    out = TTFont(out_path)
    got = out.getBestCmap()
    assert all(cp in got for cp in CODEPOINTS), "не все знаки попали в шрифт"
    assert all(out["glyf"][got[cp]].numberOfContours > 0 for cp in CODEPOINTS), "пустой глиф"
    assert {out["hmtx"][g][0] for g in out.getGlyphOrder()} == {advance}, "ширины знаков разные"
    print("%s: %d знаков, %d байт" % (out_path, len(CODEPOINTS), len(open(out_path, "rb").read())))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    build(sys.argv[1], sys.argv[2])
