# Agregarr templates

The same badges as the Jellyfin plugin, built for Agregarr (Plex). They're in `out/`, all prefixed `Badges_`. Rebuild them with `python3 build.py`.

| Group | Templates | Shows when |
|---|---|---|
| Resolution | 4K, BluRay, 1080p Full HD, 720p HD, SD | BluRay: 1080p + `Bluray-1080p` / `Remux-1080p` / `BR-DISK` in the file name. Full HD: 1080p + `WEBDL-1080p` / `WEBRip-1080p` / `HDTV-1080p` |
| HDR | DoVi Top Left, HDR10 Top Left | Your original rules |
| IMAX | IMAX (no HDR / after HDR10 / after Dolby Vision) | `Imax` in the path; one copy per position, so there is no gap |
| Audio logos | Dolby Atmos, DTS HD MA, DTS X | Your original rules (TrueHD 8ch, DCA 6ch, DCA 8ch) |
| Audio labels | DOLBY TrueHD 5.1, DOLBY Digital+ 7.1/5.1/2.0, DOLBY Digital 5.1/2.0, AAC 5.1/Stereo, FLAC 7.1/5.1/Stereo | `audioCodec` + `audioChannels`. No TrueHD 7.1 or DTS labels, because the logos above cover those |
| Editions | Director's Cut, Extended, Unrated, Theatrical, Ultimate, Special, Final Cut, Remastered, Criterion, Anniversary, Collector's | Radarr `{edition-...}` tag in the file name |
| Ratings | IMDb Rating, Rotten Tomatoes Fresh / Rotten | `imdbRating` > 0; `rtCriticsScore` ≥ 60 or 1–59 |

Layout:

- The top row holds resolution, Dolby Vision or HDR, then IMAX.
- Edition sits under the top row.
- Audio is bottom-left.
- Ratings are bottom-right.

## Install / update (Unraid)

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash
```

The script does three things:

- copies the logos into Agregarr's icons folder
- updates any `Badges_` template that already exists, in place, so library assignments are kept
- creates the rest through Agregarr's API, using the API key from `settings.json`

To turn the badges on in every library, add `--libraries`. That previews the change; add `--apply` as well to save it:

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash -s -- --libraries --apply
```

This applies to movie and TV libraries. It turns on all `Badges_` templates and turns off every other overlay. Turned-off overlays stay in the list unticked, so any of them can be switched back on.

The install is safe to re-run after every change. Rebuild the templates with `python3 build.py`.

Why not the import button:

- Agregarr's importer rewrites a template's icon reference to an internal ID that its renderer can't load, so imported logos come out blank. These templates point at `/api/v1/posters/icons/user/Badges_<name>.svg`, which Agregarr serves straight from its icons folder.
- Agregarr allows only one variable element per template, so the fixed labels (editions, audio) use plain text elements.

## Handing a library over to Kometa

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash -s -- --handoff "Movies 4K/HDR" "TV 4K/HDR"
```

For each named library this:

1. Enables only a template that matches nothing.
2. Runs the library once. Agregarr resets every poster it overlaid to its saved original, but only when nothing matches.
3. Waits for that run to finish.
4. Leaves the library with nothing enabled, so Agregarr skips it from then on. Its templates stay in the list, unticked.

## Stop Agregarr and restore original posters

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash -s -- --revert
```

This:

1. Cancels any running overlay sync and waits until nothing is running.
2. In every library that still has overlays enabled, turns them off.
3. Runs Agregarr's own **reset library posters** job, which puts each item's saved original poster back.

Libraries already handed to Kometa have nothing enabled, so they're skipped. Add library names (`--revert "Movies" "TV"`) to limit it to those.

## Remove Agregarr's collections

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash -s -- --collections
```

That previews what would be removed. Add `--apply` to delete them, and library names to limit it to those libraries.

- It stops a running collection sync first.
- It uses Agregarr's own delete, which removes the Plex collections Agregarr created, their labels and any placeholder items.
- Collections that existed in Plex before Agregarr are only unlinked; they stay in Plex.
