# Poster Overlays for Jellyfin

Jellyfin 12.4 plugin that burns quality badges (4K, Blu-ray, Dolby Vision, HDR10, IMAX, Dolby Atmos, DTS-HD MA, DTS:X) into movie posters, so they show in **every** client: TV apps, mobile, web and Infuse.

Badges come from [Agregarr](https://agregarr.org) overlay template exports, with the same positions, logos and rules as in Agregarr.

## Install

1. Jellyfin → Dashboard → Plugins → Repositories → **+**
   URL: `https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/manifest.json`
2. Catalog → **Poster Overlays** → Install, then restart Jellyfin.
3. Dashboard → Plugins → Poster Overlays: pick templates. Optionally set a test-mode title filter.
4. Dashboard → Scheduled Tasks → **Apply poster overlays** → Run. It also runs daily at 3am.

Updates show up in Jellyfin's plugin page like any other plugin.

## How it works

- The original poster is backed up once. Every render starts from the backup, so badges never stack.
- Only new or changed movies get redrawn. Changes include a new file version, a changed template, or a poster replaced by a metadata refresh, which becomes the new original.
- **Restore original posters** (Scheduled Tasks) puts every backup back. Disable the daily apply trigger first, or the badges come back on the next run.
- Backups, state and custom templates live in `<jellyfin config>/plugins/configurations/PosterOverlays/`.

## Templates

Built-in templates are in `Jellyfin.Plugin.PosterOverlays/Templates/`. To add one without a new release, export it from Agregarr and drop the `.zip` into `PosterOverlays/templates/`. A custom template replaces a built-in one with the same name.

Supported condition fields: `resolution` (`4k`, `1080`, `720`, `sd`), `dolbyVision`, `hdr`, `hdr10Plus`, `videoCodec`, `audioCodec` (Plex `dca` = `dts`), `audioChannels`, `filePath`, `title`, `year`. Supported operators: `eq`, `ne`, `in`, `notIn`, `contains`, `notContains`, `gt`, `gte`, `lt`, `lte`.

## Release

Bump `VERSION` (e.g. `1.0.1.0`) and push to `main`. GitHub Actions tests and builds the plugin, publishes the release and updates `manifest.json`.
