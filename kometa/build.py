"""Builds the Kometa version of the badge set: PNG badges in out/images and the overlay file out/overlays.yml.

Same look and rules as the Jellyfin plugin and the Agregarr pack:
  top row (queue, no gaps):   resolution -> Dolby Vision / HDR10 -> IMAX
  under the top row:          edition
  bottom-left (one badge):    best audio (Atmos / DTS:X / DTS-HD MA logos, else a codec label)
  bottom-right (queue):       IMDb, Rotten Tomatoes (fresh/rotten)

Kometa draws on a 1000x1500 poster, so every PNG is made at its final pixel size (rendered 4x and scaled down).

    python3 build.py
"""
import io, json, shutil, zipfile
from pathlib import Path

import cairosvg
import yaml
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).parent
OUT = HERE / "out"
IMG = OUT / "images"
AGREGARR = HERE.parent / "agregarr" / "out"          # templates already carry positions + normalized logos
LOGOS = HERE.parent / "Jellyfin.Plugin.PosterOverlays" / "Badges"
BOLD, REGULAR = HERE / "fonts" / "Inter-Bold.ttf", HERE / "fonts" / "Inter-Regular.ttf"
SS = 4                                                  # supersampling
TILE = (0, 0, 0, 89)                                    # black at 35%, same as the other packs
GAP, TILE_H, TOP_W = 9, 53, 304                        # design units (same as the Agregarr / Jellyfin designs)
SCALE = 1.2                                             # badge size on the poster; 1.2 is the largest where
                                                        # 4K + Dolby Vision + IMAX still fit across (982 of 1000 px)
MARGIN = 11


def px(v: float) -> int:
    """Design units -> poster pixels."""
    return round(v * SCALE)
IMAGE_PATH = "config/badges/images"                     # where the images live inside the Kometa container

# Regexes, case-insensitive. Source names follow Radarr quality names (Bluray-1080p, Remux-1080p, WEBDL-1080p ...).
BLURAY = r"\b(blu-?ray|remux|br-?disk|bdrip|brrip)\b"


def svg_png(svg: bytes, w: int, h: int) -> Image.Image:
    im = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg, output_width=w * SS, output_height=h * SS))).convert("RGBA")
    return im


def fit(im: Image.Image, w: int, h: int) -> Image.Image:
    im = im.copy()
    im.thumbnail((w, h), Image.LANCZOS)
    return im


def tile(w: int, h: int) -> Image.Image:
    im = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle([0, 0, w * SS - 1, h * SS - 1], radius=15 * SS, fill=TILE)
    return im


def save(im: Image.Image, name: str) -> str:
    # drawn at SS x design size; scaled straight to the final poster size
    im = im.resize((round(im.width * SCALE / SS), round(im.height * SCALE / SS)), Image.LANCZOS)
    im.save(IMG / f"{name}.png", optimize=True)
    return f"{IMAGE_PATH}/{name}.png"


def from_agregarr(template: str, width: int = None) -> Image.Image:
    """Tile + logo from an Agregarr template, cropped to the tile (logos may have transparent overhang)."""
    t = json.loads(zipfile.ZipFile(AGREGARR / f"Badges_{template}_overlay_template.zip").read("template.json"))
    els = t["templateData"]["elements"]
    tl = next(e for e in els if e["type"] == "tile")
    ic = next(e for e in els if e["type"] == "svg")
    w, h = width or round(tl["width"]), round(tl["height"])
    svg = (AGREGARR / "icons" / Path(ic["properties"]["iconPath"]).name).read_bytes()
    logo = svg_png(svg, round(ic["width"]), round(ic["height"]))
    canvas = tile(w, h)
    # keep the designed position relative to the tile, re-centred horizontally when the tile is widened
    dx = (ic["x"] - tl["x"]) if width is None else (w - ic["width"]) / 2
    dy = ic["y"] - tl["y"]
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(logo, (round(dx * SS), round(dy * SS)), logo)
    return Image.alpha_composite(canvas, layer)


def label(brand: str, detail: str, w: int = TOP_W, h: int = TILE_H, size: int = 26) -> Image.Image:
    """"DOLBY | TrueHD 7.1" style label, bold brand + regular detail with a thin separator."""
    canvas = tile(w, h)
    d = ImageDraw.Draw(canvas)
    fb, fr = ImageFont.truetype(str(BOLD), size * SS), ImageFont.truetype(str(REGULAR), size * SS)
    bw, dw, sep = d.textlength(brand, font=fb), d.textlength(detail, font=fr), 16 * SS
    x = (w * SS - (bw + sep + dw)) / 2
    mid = h * SS / 2
    d.text((x, mid), brand, font=fb, fill="white", anchor="lm")
    d.line([(x + bw + sep / 2, 14 * SS), (x + bw + sep / 2, (h - 14) * SS)], fill=(255, 255, 255, 140), width=2 * SS)
    d.text((x + bw + sep, mid), detail, font=fr, fill="white", anchor="lm")
    return canvas


def edition(text: str) -> Image.Image:
    f = ImageFont.truetype(str(BOLD), 24 * SS)
    w = round(ImageDraw.Draw(Image.new("RGBA", (1, 1))).textlength(text, font=f) / SS) + 44
    canvas = tile(w, 44)
    ImageDraw.Draw(canvas).text((w * SS / 2, 22 * SS), text, font=f, fill="white", anchor="mm")
    return canvas


def logo_only(svg_name: str, h: int) -> Image.Image:
    svg = (LOGOS / svg_name).read_bytes()
    full = svg_png(svg, h * 4, h)                       # render wide enough, then trim to the logo's own aspect
    bbox = full.getbbox()
    return full.crop(bbox) if bbox else full


def main():
    shutil.rmtree(OUT, ignore_errors=True)
    IMG.mkdir(parents=True)
    overlays, top = {}, {"queue": "badges_top"}

    def image_overlay(key, file, builder, filters=None, **pos):
        o = {"overlay": {"name": key, "file": file, **pos}, **builder}
        if filters:
            o["filters"] = filters
        overlays[key] = o

    all_items = {"plex_all": True}

    # Top row: resolution (weight 300) -> DV / HDR10 (200) -> IMAX (100). Queue slots are 304 + gap apart, so
    # everything that can be followed by another badge is 304 wide; IMAX is always last and keeps its own width.
    for key, tpl, flt in [
        ("Badges 4K", "4K", {"resolution.regex": r"(?i)^(4k|2160)"}),
        ("Badges BluRay", "BluRay", {"resolution.regex": r"(?i)^(1080|2k)", "filepath.regex": f"(?i){BLURAY}"}),
        ("Badges 1080p Full HD", "1080p_Full_HD", {"resolution.regex": r"(?i)^(1080|2k)", "filepath.regex": f"(?i)^(?!.*{BLURAY})"}),
        ("Badges 720p HD", "720p_HD", {"resolution.regex": r"(?i)^720"}),
        ("Badges SD", "SD", {"resolution.regex": r"(?i)^(480|576|sd)"}),
    ]:
        image_overlay(key, save(from_agregarr(tpl, TOP_W), key.replace(" ", "_")), all_items, flt, weight=300, **top)
    image_overlay("Badges Dolby Vision", save(from_agregarr("DoVi_Top_Left", TOP_W), "Badges_Dolby_Vision"),
                  all_items, {"has_dolby_vision": True}, weight=200, **top)
    image_overlay("Badges HDR10", save(from_agregarr("HDR10_Top_Left", TOP_W), "Badges_HDR10"),
                  {"plex_search": {"all": {"hdr": True}}}, {"has_dolby_vision": False}, weight=200, **top)
    image_overlay("Badges IMAX", save(from_agregarr("IMAX_no_HDR"), "Badges_IMAX"),
                  all_items, {"filepath.regex": r"(?i)\bimax\b"}, weight=100, **top)

    # Edition: under the top row. Known editions get an upper-case label; anything else shows Plex's edition text.
    ed_pos = {"group": "badges_edition", "horizontal_align": "left", "vertical_align": "top",
              "horizontal_offset": MARGIN, "vertical_offset": 6 + px(TILE_H) + GAP}
    editions = [("DIRECTOR'S CUT", r"director'?s"), ("EXTENDED EDITION", r"extended"), ("UNRATED", r"unrated"),
                ("THEATRICAL CUT", r"theatrical"), ("ULTIMATE EDITION", r"ultimate"), ("SPECIAL EDITION", r"special"),
                ("FINAL CUT", r"final[ ._-]?cut"), ("REMASTERED", r"remaster"), ("CRITERION", r"criterion"),
                ("ANNIVERSARY EDITION", r"anniversary"), ("COLLECTOR'S EDITION", r"collector'?s")]
    for i, (text, rx) in enumerate(editions):
        key = "Badges Edition " + text.title().replace("'S", "'s")
        file = save(edition(text), key.replace(" ", "_").replace("'", ""))
        # Plex's edition field (Plex reads {edition-...} from the file name) or the tag itself, as a fallback.
        # Two overlays in one group act as "either": the group draws at most one edition per item.
        image_overlay(key, file, all_items, {"edition.regex": f"(?i){rx}"}, weight=200 - 2 * i, **ed_pos)
        image_overlay(key + " (file name)", file, all_items, {"filepath.regex": f"(?i)\\{{edition-[^}}]*{rx}"}, weight=199 - 2 * i, **ed_pos)
    overlays["Badges Edition (other)"] = {
        "overlay": {"name": "text(<<edition>>)", "font": "fonts/Inter-Bold.ttf", "font_size": px(24), "font_color": "#FFFFFF",
                    "back_color": "#00000059", "back_radius": px(15), "back_padding": px(10), "weight": 1, **ed_pos},
        "plex_all": True, "filters": {"has_edition": True, "edition.regex": r"(?i)^(?!.*\bimax\b)"}}  # IMAX has its own badge

    # Audio: one badge bottom-left, best match wins (group). Logos first, then codec labels from Plex's stream info.
    au_pos = {"group": "badges_audio", "horizontal_align": "left", "vertical_align": "bottom", "horizontal_offset": MARGIN, "vertical_offset": 17}
    def either(key, file, branches, weight):
        """One overlay per alternative, same image, consecutive weights: the group draws it if any alternative matches."""
        for n, (suffix, flt) in enumerate(branches):
            image_overlay(f"{key} ({suffix})", file, all_items, flt, weight=weight - n, **au_pos)
    def title_or_path(rx):
        return [("track title", {"audio_track_title.regex": f"(?i){rx}"}), ("file name", {"filepath.regex": f"(?i){rx}"})]
    either("Badges Dolby Atmos", save(from_agregarr("Dolby_Atmos"), "Badges_Dolby_Atmos"),
           title_or_path(r"\batmos(\b|\d)"), 160)
    either("Badges DTS-X", save(from_agregarr("DTS_X"), "Badges_DTS_X"),
           title_or_path(r"\b(dts[-_. :]?x7?)\b(?![-_. ]?(26[456]))"), 150)
    either("Badges DTS-HD MA", save(from_agregarr("DTS_HD_MA"), "Badges_DTS_HD_MA"),
           title_or_path(r"\bdts[ ._-]?(hd[ ._-])?(ma|xll)(\b|\d)") + [("stream", {"audio_codec.is": "dca", "audio_profile.is": "ma"})], 110)
    ch = {"7.1": {"channels.gte": 8}, "5.1": {"channels.gte": 6, "channels.lt": 8}, "2.0": {"channels.lte": 2}}
    for i, (codec, brand, name, chans) in enumerate([
        ("truehd", "DOLBY", "TrueHD", ["7.1", "5.1"]), ("flac", "FLAC", "", ["7.1", "5.1", "2.0"]),
        ("eac3", "DOLBY", "Digital+", ["7.1", "5.1", "2.0"]), ("dca", "DTS", "", ["7.1", "5.1"]),
        ("ac3", "DOLBY", "Digital", ["5.1", "2.0"]), ("aac", "AAC", "", ["5.1", "2.0"]),
    ]):
        for j, c in enumerate(chans):
            detail = " ".join(p for p in [name, "Stereo" if c == "2.0" and not name else c] if p)
            key = f"Badges Audio {brand} {detail}"
            fname = key.replace(" ", "_").replace("+", "Plus").replace(".", "_")
            image_overlay(key, save(label(brand, detail), fname), all_items,
                          {"audio_codec.is": codec, **ch[c]}, weight=100 - i * 10 - j, **au_pos)

    # Ratings: bottom-right queue; Rotten Tomatoes takes the corner, IMDb sits left of it (or in the corner if RT is missing).
    rating_style = {"font": "fonts/Inter-Bold.ttf", "font_size": px(30), "font_color": "#FFFFFF", "back_color": "#00000059",
                    "back_radius": px(15), "back_width": px(160), "back_height": px(TILE_H), "addon_position": "left",
                    "addon_offset": px(10), "queue": "badges_ratings"}
    for name, svg, h in [("imdb", "imdb.svg", 30), ("rt_fresh", "rt_fresh.svg", 36), ("rt_rotten", "rt_rotten.svg", 36)]:
        im = logo_only(svg, h)
        im.resize((round(im.width * SCALE / SS), px(h)), Image.LANCZOS).save(IMG / f"Badges_{name}.png", optimize=True)
    overlays["Badges IMDb"] = {"overlay": {"name": "text(<<plex_imdb_rating>>)", "file": f"{IMAGE_PATH}/Badges_imdb.png", "weight": 10, **rating_style},
                               "plex_all": True, "value_filter": {"plex_imdb_rating.gt": 0}}
    overlays["Badges RT Fresh"] = {"overlay": {"name": "text(<<plex_tomatoes_rating%>>%)", "file": f"{IMAGE_PATH}/Badges_rt_fresh.png", "weight": 20, **rating_style},
                                   "plex_all": True, "value_filter": {"plex_tomatoes_rating.gte": 6.0}}
    overlays["Badges RT Rotten"] = {"overlay": {"name": "text(<<plex_tomatoes_rating%>>%)", "file": f"{IMAGE_PATH}/Badges_rt_rotten.png", "weight": 20, **rating_style},
                                    "plex_all": True, "value_filter": {"plex_tomatoes_rating.gt": 0, "plex_tomatoes_rating.lt": 6.0}}

    doc = {
        # Dynamic queues: position n = initial offset + n * spacing (all badges in a queue have the same width).
        "queues": {
            "badges_top": {"settings": {"overlay_limit": 3, "dynamic_position": {
                "initial_horizontal_align": "left", "initial_vertical_align": "top",
                "initial_horizontal_offset": MARGIN, "initial_vertical_offset": 6,
                "horizontal_spacing": px(TOP_W) + GAP, "vertical_spacing": 0}}},
            "badges_ratings": {"settings": {"overlay_limit": 2, "dynamic_position": {
                "initial_horizontal_align": "right", "initial_vertical_align": "bottom",
                "initial_horizontal_offset": MARGIN, "initial_vertical_offset": 17,
                "horizontal_spacing": px(160) + GAP, "vertical_spacing": 0}}},
        },
        "overlays": overlays,
    }
    header = "# Generated by kometa/build.py - edit that, not this file.\n"
    (OUT / "overlays.yml").write_text(header + yaml.safe_dump(doc, sort_keys=False, allow_unicode=True, width=200))
    print(f"{len(overlays)} overlays, {len(list(IMG.glob('*.png')))} images")


if __name__ == "__main__":
    main()
