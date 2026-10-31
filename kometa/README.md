# Kometa overlays

The same badges as the Jellyfin plugin, built for Kometa (Plex). `python3 build.py` writes `out/overlays.yml` and `out/images/`.

| Area | Badges |
|---|---|
| Top row (no gaps) | Resolution: 4K, Blu-ray (file name has `Bluray`/`Remux`/`BDRip`/`BR-DISK`), 1080P FULL HD, 720P HD, SD. Then Dolby Vision or HDR10, then IMAX (`IMAX` in the path) |
| Under the top row | Edition, from Plex's edition (`{edition-...}` in the file name). Known editions get an upper-case badge; anything else shows the edition text |
| Bottom-left (one badge) | Best audio. Logos for Atmos and DTS:X, detected from the audio track title or file name, and DTS-HD MA. Otherwise a codec label from Plex's stream info, e.g. `DOLBY \| TrueHD 7.1`, `AAC \| Stereo` |
| Bottom-right | IMDb rating, and the Rotten Tomatoes critics score (fresh ≥ 60%, rotten below). The numbers come from Plex, so no extra API key is needed. A missing rating is skipped |

## Setup on Unraid

1. **Hand the libraries over from Agregarr first.** Kometa backs up whatever poster is in Plex, so the Agregarr badges must come off first. This restores Agregarr's clean posters and turns Agregarr off for those libraries:

   ```bash
   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/agregarr/install-unraid.sh | bash -s -- --handoff "Movies 4K/HDR" "TV 4K/HDR"
   ```

2. **Install Kometa** from Community Apps (Kometa by Kometa-Team, image `kometateam/kometa`), with its config folder at `/mnt/user/appdata/kometa`.

3. **Install the badges:**

   ```bash
   curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/kometa/install-unraid.sh | bash
   ```

4. **Edit** `/mnt/user/appdata/kometa/config.yml`. Merge in `config-snippet.yml` with your Plex URL, Plex token and TMDb API key.

5. **Validate** (changes nothing):

   ```bash
   docker run --rm -v /mnt/user/appdata/kometa:/config kometateam/kometa --validate
   ```

6. **Test run, overlays only, one library:**

   ```bash
   docker run --rm -v /mnt/user/appdata/kometa:/config kometateam/kometa --run --overlays-only --run-libraries "Movies 4K/HDR"
   ```

7. Check the posters in Plex. Then run `TV 4K/HDR`, and let Kometa's schedule keep them up to date.

**Undo:** set `remove_overlays: true` on the library in `config.yml` and run once. Kometa puts back the original posters it backed up.

## Notes

- Kometa draws on a 1000×1500 poster, so the images are made at their final size.
- Top-row badges are all 304 wide, because Kometa queues place badges at fixed steps.
- Atmos and DTS:X can only be read from the audio track title or the file name, because Plex doesn't report them as separate codecs. A TrueHD Atmos track whose name doesn't mention Atmos shows `DOLBY | TrueHD 7.1`.
- The overlay file passes Kometa's own `overlay-schema.json`.

## All libraries + default collections

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/kometa/setup-libraries.sh | bash
```

This adds every Plex movie and TV library missing from `config.yml`, with the badge overlays. It also gives every library that has no `collection_files` the default `tmdb` collections (Trending, Popular, Top Rated) and `universe` collections (MCU, Star Wars, ...).

- It's safe to re-run: it only adds what's missing.
- It backs up `config.yml` first.
- To leave collections out of some libraries, set `SKIP_COLLECTIONS="Movies 4K/HDR|TV 4K/HDR"` before `bash`.
