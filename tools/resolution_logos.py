"""Draws the bold-block resolution logos ("1080P | FULL HD", "720P | HD", "480P | SD") as vector SVGs and puts them
into the plugin's templates, which the Agregarr pack and the Kometa images are built from.

Style matches the 4K Ultra HD badge: a white block with dark text, then heavy white text, all Inter Black.
Text is converted to outlines, so the SVGs don't depend on any font being installed.

    python3 tools/resolution_logos.py      (then: python3 agregarr/build.py && python3 kometa/build.py)
"""
import io, json, zipfile
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent.parent
FONT = TTFont(ROOT / "kometa" / "fonts" / "Inter-Black.ttf")
TEMPLATES = ROOT / "Jellyfin.Plugin.PosterOverlays" / "Templates"

# Design units (the 1000x1500 template canvas); the tile is 53 high like every top-row badge.
SIZE, BLOCK_H, BLOCK_PAD, GAP, RADIUS, TILE_H, TILE_PAD = 30, 38, 10, 12, 6, 53, 14
UPM = FONT["head"].unitsPerEm
CAP = FONT["OS/2"].sCapHeight
CMAP, GLYPHS, HMTX = FONT.getBestCmap(), FONT.getGlyphSet(), FONT["hmtx"]


def text_path(text: str, x: float, mid_y: float) -> tuple[str, float]:
    """SVG path data for text whose capitals are centred on mid_y; returns (path, width)."""
    k = SIZE / UPM
    baseline = mid_y + CAP * k / 2
    pen = SVGPathPen(GLYPHS)
    cursor = 0.0
    for ch in text:
        name = CMAP[ord(ch)]
        GLYPHS[name].draw(TransformPen(pen, (k, 0, 0, -k, x + cursor * k, baseline)))
        cursor += HMTX[name][0]
    return pen.getCommands(), cursor * k


def logo(left: str, right: str) -> tuple[bytes, float]:
    mid = BLOCK_H / 2
    _, lw = text_path(left, 0, mid)
    block_w = lw + 2 * BLOCK_PAD
    left_d, _ = text_path(left, BLOCK_PAD, mid)
    right_d, rw = text_path(right, block_w + GAP, mid)
    w = block_w + GAP + rw
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="{w:.2f}" height="{BLOCK_H}" '
           f'viewBox="0 0 {w:.2f} {BLOCK_H}">'
           f'<rect x="0" y="0" width="{block_w:.2f}" height="{BLOCK_H}" rx="{RADIUS}" fill="#FFFFFF"/>'
           f'<path d="{left_d}" fill="#141414"/>'
           f'<path d="{right_d}" fill="#FFFFFF"/></svg>')
    return svg.encode(), w


def update_template(zip_name: str, svg: bytes, w: float):
    path = TEMPLATES / zip_name
    with zipfile.ZipFile(path) as z:
        t = json.loads(z.read("template.json"))
    tile = next(e for e in t["templateData"]["elements"] if e["type"] == "tile")
    icon = next(e for e in t["templateData"]["elements"] if e["type"] == "svg")
    tile["width"] = round(w + 2 * TILE_PAD, 1)
    tile["height"] = TILE_H
    icon.update(x=tile["x"] + TILE_PAD, y=tile["y"] + (TILE_H - BLOCK_H) / 2, width=round(w, 1), height=BLOCK_H)
    icon_file = Path(icon["properties"]["iconPath"]).name
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("template.json", json.dumps(t, indent=2))
        z.writestr(f"assets/icons/{icon_file}", svg)
    print(f"{zip_name}: tile {tile['width']} x {TILE_H}, logo {w:.1f} x {BLOCK_H}")


for zip_name, left, right in [("1080p_Full_HD_overlay_template.zip", "1080P", "FULL HD"),
                              ("720p_HD_overlay_template.zip", "720P", "HD"),
                              ("SD_overlay_template.zip", "480P", "SD")]:
    update_template(zip_name, *logo(left, right))
