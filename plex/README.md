# Plex poster cleanup

Puts Plex's own poster back on movies, shows and seasons whose selected poster is still an uploaded image, such as an old Agregarr overlay. Items Kometa manages (label `Overlay`) are skipped. The Plex URL and token are read from Kometa's `config.yml`.

```bash
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/plex/fix-posters.sh | bash                 # preview
curl -sL https://raw.githubusercontent.com/tavneetuppal/jellyfin-badges/main/plex/fix-posters.sh | bash -s -- --apply   # switch them
```

Add library names after `--apply` to limit it, e.g. `--apply "Movies" "TV"`.

Note: custom posters you uploaded yourself are also uploads. Check the preview list before applying.
