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

Each icon has its width and height set to its template element size, with a `0 0` viewBox. Agregarr draws imported icons at the SVG's own size, so the two have to match.
