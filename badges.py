#!/usr/bin/env python3
"""Burn Agregarr overlay templates into Jellyfin movie posters.

Drop Agregarr template exports (*_overlay_template.zip) into ./templates.
Each template's elements (tiles + SVG icons) and conditions are applied as-is.
Originals are backed up once to ./data/originals and every render starts from
the backup, so re-running never stacks overlays.

Usage:
  JELLYFIN_URL=http://host:8096 JELLYFIN_API_KEY=xxx python3 badges.py [--dry-run | --restore]
  python3 badges.py --demo in.jpg out.jpg --apply "4K,DoVi Top Left,IMAX"
"""
import argparse, base64, io, json, os, sys, urllib.parse, urllib.request, zipfile
from pathlib import Path
from PIL import Image, ImageDraw
import cairosvg

HERE = Path(__file__).resolve().parent
URL = os.environ.get("JELLYFIN_URL", "http://localhost:8096").rstrip("/")
KEY = os.environ.get("JELLYFIN_API_KEY", "")
TEMPLATES = Path(os.environ.get("BADGE_TEMPLATES", HERE / "templates"))
DATA = Path(os.environ.get("BADGE_DATA", HERE / "data"))

# Agregarr/Plex codec names -> Jellyfin codec names
CODEC_ALIASES = {"dca": "dts", "eac3": "eac3", "ac3": "ac3", "truehd": "truehd"}


# ---------- templates ----------
def load_templates():
    out = []
    for z in sorted(TEMPLATES.glob("*.zip")):
        with zipfile.ZipFile(z) as zf:
            t = json.loads(zf.read("template.json"))
            icons = {Path(n).name: zf.read(n) for n in zf.namelist() if n.endswith(".svg")}
        t["_icons"] = icons
        out.append(t)
    if not out:
        sys.exit(f"No templates found in {TEMPLATES}")
    return out


def norm(v):
    v = str(v).lower()
    return CODEC_ALIASES.get(v, v)


def check(rule, facts):
    field, op, want = rule["field"], rule["operator"], rule["value"]
    if field not in facts:
        print(f"  ! unsupported condition field '{field}' - treated as no match")
        return False
    have = facts[field]
    wants = [norm(w) for w in (want if isinstance(want, list) else [want])]
    if op == "eq":
        return norm(have) == wants[0]
    if op in ("ne", "neq"):
        return norm(have) != wants[0]
    if op == "in":
        return norm(have) in wants
    if op in ("notIn", "nin"):
        return norm(have) not in wants
    if op == "contains":
        return wants[0] in norm(have)
    if op in ("notContains", "ncontains"):
        return wants[0] not in norm(have)
    if op in ("gt", "gte", "lt", "lte"):
        a, b = float(have or 0), float(wants[0])
        return {"gt": a > b, "gte": a >= b, "lt": a < b, "lte": a <= b}[op]
    print(f"  ! unsupported operator '{op}' - treated as no match")
    return False


def matches(t, facts):
    sections = (t.get("applicationCondition") or {}).get("sections") or []
    # rules within a section are AND, sections are OR
    return any(all(check(r, facts) for r in s.get("rules", [])) for s in sections)


# ---------- Jellyfin ----------
def api(path, method="GET", body=None, ctype=None, raw=False):
    req = urllib.request.Request(URL + path, data=body, method=method)
    req.add_header("Authorization", f'MediaBrowser Token="{KEY}"')
    if ctype:
        req.add_header("Content-Type", ctype)
    with urllib.request.urlopen(req, timeout=60) as r:
        data = r.read()
    return data if raw else (json.loads(data) if data else None)


def movies():
    q = urllib.parse.urlencode({
        "Recursive": "true", "IncludeItemTypes": "Movie",
        "Fields": "MediaStreams,Path", "ImageTypes": "Primary",
    })
    return api(f"/Items?{q}")["Items"]


def facts_for(item):
    streams = item.get("MediaStreams") or []
    video = next((s for s in streams if s.get("Type") == "Video"), {})
    audios = [s for s in streams if s.get("Type") == "Audio"]
    audio = next((s for s in audios if s.get("IsDefault")), audios[0] if audios else {})
    w = video.get("Width") or 0
    res = "4k" if w >= 3200 else "1080" if w >= 1800 else "720" if w >= 1200 else "sd"
    rng = (video.get("VideoRangeType") or "").upper()
    return {
        "resolution": res,
        "dolbyVision": "DOVI" in rng,
        "hdr": (video.get("VideoRange") or "").upper() == "HDR",
        "hdr10Plus": "HDR10PLUS" in rng,
        "videoCodec": video.get("Codec") or "",
        "audioCodec": audio.get("Codec") or "",
        "audioChannels": audio.get("Channels") or 0,
        "filePath": item.get("Path") or "",
        "title": item.get("Name") or "",
        "year": item.get("ProductionYear") or 0,
    }


# ---------- drawing ----------
def hex_rgba(h, opacity_pct):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (round(255 * opacity_pct / 100),)


def svg_image(svg, w, h):
    png = cairosvg.svg2png(bytestring=svg, output_width=max(w, h) * 2)
    im = Image.open(io.BytesIO(png)).convert("RGBA")
    im.thumbnail((w, h), Image.LANCZOS)  # fit inside the box, keep aspect
    return im


def draw_template(canvas, t):
    W, H = canvas.size
    td = t["templateData"]
    sx, sy = W / td.get("width", 1000), H / td.get("height", 1500)
    for el in sorted(td["elements"], key=lambda e: e.get("layerOrder", 0)):
        p = el.get("properties", {})
        x, y = el["x"] * sx, el["y"] * sy
        w, h = max(1, round(el["width"] * sx)), max(1, round(el["height"] * sy))
        if el["type"] == "tile":
            layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
            ImageDraw.Draw(layer).rounded_rectangle(
                [x, y, x + w, y + h], radius=p.get("borderRadiusTopLeft", 0) * sx,
                fill=hex_rgba(p.get("fillColor", "#000000"), p.get("fillOpacity", 100)),
                outline=hex_rgba(p.get("borderColor", "#FFFFFF"), 100) if p.get("borderWidth") else None,
                width=round(p.get("borderWidth", 0) * sx))
            canvas.alpha_composite(layer)
        elif el["type"] == "svg":
            svg = t["_icons"].get(Path(p.get("iconPath", "")).name)
            if not svg:
                print(f"  ! icon missing in template '{t['name']}'")
                continue
            im = svg_image(svg, w, h)
            if p.get("grayscale"):
                im = Image.merge("RGBA", (*[im.convert("L")] * 3, im.getchannel("A")))
            if p.get("opacity", 100) < 100:
                im.putalpha(im.getchannel("A").point(lambda a: a * p["opacity"] // 100))
            ox, oy = round(x + (w - im.width) / 2), round(y + (h - im.height) / 2)
            layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
            layer.paste(im, (ox, oy), im)  # paste handles negative offsets (clipped)
            canvas.alpha_composite(layer)
        else:
            print(f"  ! element type '{el['type']}' not supported, skipped")


def render(img, templates):
    canvas = img.convert("RGBA")
    for t in templates:
        draw_template(canvas, t)
    buf = io.BytesIO()
    canvas.convert("RGB").save(buf, "JPEG", quality=92)
    return buf.getvalue()


# ---------- main ----------
def upload(iid, jpeg):
    api(f"/Items/{iid}/Images/Primary", "POST", base64.b64encode(jpeg), "image/jpeg")


def run(dry, restore):
    if not KEY:
        sys.exit("Set JELLYFIN_API_KEY (Dashboard > API Keys)")
    templates = [] if restore else load_templates()
    (DATA / "originals").mkdir(parents=True, exist_ok=True)
    state_f = DATA / "state.json"
    state = json.loads(state_f.read_text()) if state_f.exists() else {}

    for item in movies():
        iid, name = item["Id"], item["Name"]
        orig = DATA / "originals" / f"{iid}.jpg"
        if restore:
            if orig.exists():
                if not dry:
                    upload(iid, orig.read_bytes())
                    state.pop(iid, None)
                print(f"restored  {name}")
            continue
        hits = [t for t in templates if matches(t, facts_for(item))]
        names = [t["name"] for t in hits]
        if state.get(iid) == names:
            continue
        print(f"{'would apply' if dry else 'applying '}  {name}: {names or '-'}")
        if dry:
            continue
        try:
            if not orig.exists():
                raw = api(f"/Items/{iid}/Images/Primary", raw=True)
                buf = io.BytesIO()
                Image.open(io.BytesIO(raw)).convert("RGB").save(buf, "JPEG", quality=95)
                orig.write_bytes(buf.getvalue())
            upload(iid, render(Image.open(orig), hits) if hits else orig.read_bytes())
        except Exception as e:
            print(f"  ! failed: {e}")
            continue
        state[iid] = names
        state_f.write_text(json.dumps(state))
    state_f.write_text(json.dumps(state))


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--restore", action="store_true")
    p.add_argument("--demo", nargs=2, metavar=("IN", "OUT"))
    p.add_argument("--apply", default="", help="demo: comma-separated template names")
    a = p.parse_args()
    if a.demo:
        want = [n.strip() for n in a.apply.split(",") if n.strip()]
        ts = [t for t in load_templates() if not want or t["name"] in want]
        Path(a.demo[1]).write_bytes(render(Image.open(a.demo[0]), ts))
    else:
        run(a.dry_run, a.restore)
