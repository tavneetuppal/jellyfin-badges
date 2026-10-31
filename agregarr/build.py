"""Builds the Agregarr template pack in ./out from the Agregarr exports in ./src and the logos in ../Jellyfin.Plugin.PosterOverlays.

Every icon is normalized (viewBox from 0 0, width/height equal to the element's size) and written to out/icons with a
readable Badges_ name; see write() for why icons are installed by copying rather than through the importer.

    python3 build.py
"""
import copy, datetime, json, re, shutil, zipfile
from pathlib import Path

HERE = Path(__file__).parent
SRC, OUT = HERE / "src", HERE / "out"
LOGOS = HERE.parent / "Jellyfin.Plugin.PosterOverlays" / "Badges"
PLUGIN_TEMPLATES = HERE.parent / "Jellyfin.Plugin.PosterOverlays" / "Templates"
NOW = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")

MARGIN, GAP, TILE_H, ROW_TOP, ROW_BOTTOM = 11.302785050976325, 9, 53, 6.403666924993082, 1430
TILE_PROPS = {"fillColor": "#000000", "fillOpacity": 35, "borderColor": "#FFFFFF", "borderWidth": 0, "lockCorners": True, "borderRadiusTopLeft": 15}


def normalize(svg: bytes, width: float, height: float) -> bytes:
    s = svg.decode("utf-8")
    root = re.search(r"<svg\b[^>]*>", s, re.S).group(0)
    inner = s[s.index(root) + len(root): s.rindex("</svg>")]
    vb = re.search(r'viewBox="([^"]+)"', root)
    if vb:
        x0, y0, w, h = [float(v) for v in vb.group(1).replace(",", " ").split()]
    else:
        x0 = y0 = 0.0
        w = float(re.search(r'\bwidth="([\d.]+)', root).group(1))
        h = float(re.search(r'\bheight="([\d.]+)', root).group(1))
    ns = " ".join(re.findall(r'xmlns:\w+="[^"]+"', root))
    body = inner if (x0, y0) == (0.0, 0.0) else f'<g transform="translate({-x0:g} {-y0:g})">{inner}</g>'
    # width/height = the element's size: Agregarr sizes imported icons from the SVG itself, so the two must agree.
    return (f'<svg xmlns="http://www.w3.org/2000/svg" {ns} version="1.1" width="{width:g}" height="{height:g}" '
            f'viewBox="0 0 {w:g} {h:g}" preserveAspectRatio="xMidYMid meet">{body}</svg>').encode()


def read(path):
    with zipfile.ZipFile(path) as z:
        t = json.loads(z.read(next(n for n in z.namelist() if n.endswith("template.json"))))
        icons = {Path(n).name: z.read(n) for n in z.namelist() if n.endswith(".svg")}
    return t, icons


def write(fname, t, icons):
    """Writes an importable zip (prefixed Badges_) plus its icon as out/icons/Badges_<name>.svg.

    Icons are NOT put in the zip: Agregarr's importer replaces iconPath with the new icon's metadata id, which its
    renderer can't load. Instead the zip points at /api/v1/posters/icons/user/Badges_<name>.svg and the icon files are
    copied into Agregarr's icons folder, which serves files by name and adds them to the icon list on its own.
    """
    t = copy.deepcopy(t)
    t["name"] = "Badges_" + t["name"]
    fname = "Badges_" + fname
    for e in t["templateData"]["elements"]:
        if e["type"] == "svg":
            old = Path(e["properties"]["iconPath"]).name
            new = fname.replace("_overlay_template.zip", ".svg")
            e["properties"]["iconPath"] = f"/api/v1/posters/icons/user/{new}"
            (OUT / "icons" / new).write_bytes(normalize(icons[old], round(e["width"], 2), round(e["height"], 2)))
    t["exportedAt"], t["version"] = NOW, "1.0"
    with zipfile.ZipFile(OUT / fname, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("template.json", json.dumps(t, indent=2))
    return fname


def template(name, desc, kind, elements, sections):
    return {"name": name, "description": desc, "type": kind,
            "templateData": {"width": 1000, "height": 1500, "elements": elements},
            "applicationCondition": {"sections": sections}}


def tile(x, y, w, h=TILE_H, eid="tile"):
    return {"id": eid, "layerOrder": 0, "type": "tile", "x": x, "y": y, "width": w, "height": h, "properties": dict(TILE_PROPS), "rotation": 0}


def svg_el(x, y, w, h, icon, layer=1):
    return {"id": f"icon{layer}", "layerOrder": layer, "type": "svg", "x": x, "y": y, "width": w, "height": h,
            "properties": {"iconType": "custom-icon", "iconPath": f"/api/v1/posters/icons/user/{icon}", "opacity": 100, "grayscale": False}, "rotation": 0}


def text_el(x, y, w, h, segments, size, weight="bold", layer=2, color="#FFFFFF", eid=None):
    return {"id": eid or f"text{layer}", "layerOrder": layer, "type": "variable", "x": x, "y": y, "width": w, "height": h,
            "properties": {"segments": segments, "fontSize": size, "fontFamily": "Inter", "fontWeight": weight,
                           "fontStyle": "normal", "color": color, "textAlign": "center", "opacity": 100}}


def static_el(x, y, w, h, text, size, weight="bold", layer=2, color="#FFFFFF", eid=None):
    """Plain text. Agregarr allows only one (unconditional) variable element per template, so fixed labels use this."""
    return {"id": eid or f"text{layer}", "layerOrder": layer, "type": "text", "x": x, "y": y, "width": w, "height": h,
            "properties": {"text": text, "fontSize": size, "fontFamily": "Inter", "fontWeight": weight,
                           "fontStyle": "normal", "color": color, "textAlign": "center", "opacity": 100}}


def rule(field, op, value):
    return {"field": field, "operator": op, "value": value}


# Text boxes are sized with DejaVu Sans, which runs wider than Agregarr's Inter, so Inter text always fits
# (it's centered in its box, so the small difference stays symmetric).
def text_w(s, size, bold):
    from PIL import ImageFont
    font = ImageFont.truetype(str(LOGOS / ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf")), size)
    return font.getlength(s)


def main():
    shutil.rmtree(OUT, ignore_errors=True)
    (OUT / "icons").mkdir(parents=True)
    built = []
    src = {re.sub(r"^[0-9a-f]{8}-", "", p.name): p for p in SRC.glob("*.zip")}

    # 1. Your original Agregarr templates, unchanged rules and positions (fresh, normalized icons).
    for f in ["4K_overlay_template.zip", "DoVi_Top_Left_overlay_template.zip", "HDR10_Top_Left_overlay_template.zip",
              "Dolby_Atmos_overlay_template.zip", "DTS_HD_MA_overlay_template.zip", "DTS_X_overlay_template.zip"]:
        t, icons = read(src[f])
        built.append(write(f, t, icons))

    r1080 = [rule("resolution", "eq", "1080")]

    # 2. Resolution: Blu-ray only for Blu-ray sources (Radarr quality names), Full HD for other 1080p, HD, SD.
    #    All resolution tiles are 304 wide so Dolby Vision / HDR10 always start at x=324.
    t, icons = read(src["BluRay_overlay_template.zip"])
    t["description"] = "1080p from a Blu-ray source (Radarr: Bluray-1080p, Remux-1080p, BR-DISK)"
    t["applicationCondition"] = {"sections": [{"rules": r1080 + [rule("filePath", "contains", v)]} for v in ["Bluray-1080p", "Remux-1080p", "BR-DISK"]]}
    built.append(write("BluRay_overlay_template.zip", t, icons))

    for fname, name, desc, sections in [
        ("1080p_Full_HD_overlay_template.zip", "1080p Full HD", "1080p WEB-DL / WEBRip / HDTV (Radarr quality names)",
         [{"rules": r1080 + [rule("filePath", "contains", v)]} for v in ["WEBDL-1080p", "WEBRip-1080p", "HDTV-1080p"]]),
        ("720p_HD_overlay_template.zip", "720p HD", "720p files", [{"rules": [rule("resolution", "eq", "720")]}]),
        ("SD_overlay_template.zip", "SD", "Anything below 720p", [{"rules": [rule("resolution", "in", ["480", "576", "sd"])]}]),
    ]:
        pt, icons = read(PLUGIN_TEMPLATES / fname)
        _, icon = pt["templateData"]["elements"]
        iw, ih = icon["width"], icon["height"]
        name_svg = Path(icon["properties"]["iconPath"]).name
        els = [tile(MARGIN, ROW_TOP, 304), svg_el(MARGIN + (304 - iw) / 2, ROW_TOP + (TILE_H - ih) / 2, iw, ih, name_svg)]
        built.append(write(fname, template(name, desc, "technical", els, sections), icons))

    # 3. IMAX: one copy per position so it sits right after whatever is before it.
    t, icons = read(src["IMAX_overlay_template.zip"])
    imax_x = next(e["x"] for e in t["templateData"]["elements"] if e["type"] == "tile")
    imax = [rule("filePath", "contains", "Imax")]
    for name, x, rules in [
        ("IMAX (no HDR)", 324, imax + [rule("dolbyVision", "eq", False), rule("hdr", "eq", False)]),
        ("IMAX (after HDR10)", 322.4367956852765 + 230 + GAP, imax + [rule("hdr", "eq", True), rule("dolbyVision", "eq", False)]),
        ("IMAX (after Dolby Vision)", 637, imax + [rule("dolbyVision", "eq", True)]),
    ]:
        v = copy.deepcopy(t)
        for e in v["templateData"]["elements"]:
            e["x"] += x - imax_x
        v["name"], v["description"] = name, "IMAX in the file path, placed after the badges before it"
        v["applicationCondition"] = {"sections": [{"rules": rules}]}
        built.append(write(re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_") + "_overlay_template.zip", v, icons))

    # 4. Ratings, bottom-right: IMDb then Rotten Tomatoes (fresh at 60%+, rotten 1-59%).
    w = 160
    rt_x = 1000 - MARGIN - w
    imdb_x = rt_x - GAP - w
    lw = round(30 * 575 / 289.83, 1)
    built.append(write("IMDb_Rating_overlay_template.zip", template(
        "IMDb Rating", "IMDb rating, bottom-right next to Rotten Tomatoes", "rating",
        [tile(imdb_x, ROW_BOTTOM, w), svg_el(imdb_x + 14, ROW_BOTTOM + (TILE_H - 30) / 2, lw, 30, "imdb.svg"),
         text_el(imdb_x + 14 + lw + 6, ROW_BOTTOM, w - (14 + lw + 6) - 8, TILE_H, [{"type": "variable", "field": "imdbRating"}], 30)],
        [{"rules": [rule("imdbRating", "gt", 0)]}]), {"imdb.svg": (LOGOS / "imdb.svg").read_bytes()}))
    rt_text = [{"type": "variable", "field": "rtCriticsScore"}, {"type": "text", "value": "%"}]
    for fname, name, logo, aspect, rules in [
        ("RT_Fresh_overlay_template.zip", "Rotten Tomatoes Fresh", "rt_fresh.svg", 138.75 / 141.25, [rule("rtCriticsScore", "gte", 60)]),
        ("RT_Rotten_overlay_template.zip", "Rotten Tomatoes Rotten", "rt_rotten.svg", 145 / 140, [rule("rtCriticsScore", "lte", 59), rule("rtCriticsScore", "gt", 0)]),
    ]:
        iw = round(36 * aspect, 1)
        built.append(write(fname, template(
            name, "Rotten Tomatoes critics score, bottom-right", "rating",
            [tile(rt_x, ROW_BOTTOM, w), svg_el(rt_x + 14, ROW_BOTTOM + (TILE_H - 36) / 2, iw, 36, logo),
             text_el(rt_x + 14 + iw + 6, ROW_BOTTOM, w - (14 + iw + 6) - 8, TILE_H, rt_text, 30)],
            [{"rules": rules}]), {logo: (LOGOS / logo).read_bytes()}))

    # 5. Editions, under the resolution badge: Radarr {edition-...} tag in the file name.
    for token, label in [
        ("{edition-Director", "DIRECTOR'S CUT"), ("{edition-Extended", "EXTENDED EDITION"), ("{edition-Unrated", "UNRATED"),
        ("{edition-Theatrical", "THEATRICAL CUT"), ("{edition-Ultimate", "ULTIMATE EDITION"), ("{edition-Special", "SPECIAL EDITION"),
        ("{edition-Final Cut", "FINAL CUT"), ("{edition-Remaster", "REMASTERED"), ("{edition-Criterion", "CRITERION"),
        ("{edition-Anniversary", "ANNIVERSARY EDITION"), ("{edition-Collector", "COLLECTOR'S EDITION"),
    ]:
        tw = text_w(label, 24, True) + 44
        y = ROW_TOP + TILE_H + GAP
        name = "Edition " + label.title().replace("'S", "'s")
        built.append(write(re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_") + "_overlay_template.zip", template(
            name, f"File name contains {token}}}", "generic",
            [tile(MARGIN, y, tw, 44), static_el(MARGIN, y, tw, 44, label, 24, layer=1)],
            [{"rules": [rule("filePath", "contains", token)]}]), {}))

    # 6. Audio labels, bottom-left, from audioCodec + audioChannels (Plex codec names). TrueHD 7.1 and all DTS are
    #    left out: your Atmos (TrueHD 8ch), DTS-HD MA (DCA 6ch) and DTS:X (DCA 8ch) templates already cover those.
    for codec, ch, brand, detail in [
        ("truehd", 6, "DOLBY", "TrueHD 5.1"),
        ("eac3", 8, "DOLBY", "Digital+ 7.1"), ("eac3", 6, "DOLBY", "Digital+ 5.1"), ("eac3", 2, "DOLBY", "Digital+ 2.0"),
        ("ac3", 6, "DOLBY", "Digital 5.1"), ("ac3", 2, "DOLBY", "Digital 2.0"),
        ("aac", 6, "AAC", "5.1"), ("aac", 2, "AAC", "Stereo"),
        ("flac", 8, "FLAC", "7.1"), ("flac", 6, "FLAC", "5.1"), ("flac", 2, "FLAC", "Stereo"),
    ]:
        bw, dw, sep = text_w(brand, 26, True), text_w(detail, 26, False), 16
        start = MARGIN + (304 - (bw + sep + dw)) / 2
        els = [tile(MARGIN, ROW_BOTTOM, 304),
               static_el(start, ROW_BOTTOM, bw, TILE_H, brand, 26, layer=1, eid="brand"),
               tile(start + bw + sep / 2 - 1, ROW_BOTTOM + 14, 2, TILE_H - 28, eid="separator"),
               static_el(start + bw + sep, ROW_BOTTOM, dw, TILE_H, detail, 26, weight="normal", layer=3, eid="detail")]
        els[2]["layerOrder"] = 2
        els[2]["properties"].update(fillColor="#FFFFFF", fillOpacity=55, borderRadiusTopLeft=0)
        name = f"Audio {brand} {detail}"
        built.append(write(re.sub(r"[^A-Za-z0-9]+", "_", name.replace("+", " Plus")).strip("_") + "_overlay_template.zip", template(
            name, f"audioCodec {codec}, {ch} channels", "technical", els,
            [{"rules": [rule("audioCodec", "eq", codec), rule("audioChannels", "eq", ch)]}]), {}))

    print(len(built), "templates")
    for b in built:
        print(" ", b)


if __name__ == "__main__":
    main()
