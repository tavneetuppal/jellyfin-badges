# Agregarr templates

The same badge logic as the Jellyfin plugin, for Agregarr (Plex). Import each zip in Agregarr's overlay template editor.

| Template | Shows when |
|---|---|
| BluRay | 1080p and the file name has `Bluray-1080p`, `Remux-1080p` or `BR-DISK` |
| 1080p Full HD | 1080p and the file name has `WEBDL-1080p`, `WEBRip-1080p` or `HDTV-1080p` |
| 720p HD | 720p |
| SD | 480p / 576p / SD |
| IMAX (no HDR / after HDR10 / after Dolby Vision) | `Imax` in the path; one copy per position so there is no gap |

The rules depend on Radarr's default quality names in file names.

Agregarr places badges at fixed positions, so these templates use a few tricks:

- The resolution tiles are all 304 wide, so Dolby Vision and HDR10 always start at the same spot.
- IMAX comes in three copies, one per position.

Keep the 4K, DoVi, HDR10 and audio templates you already have. Disable the old single IMAX template.
