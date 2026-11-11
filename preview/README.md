# Plugin preview

A web mock-up of the plugin's windows (HUD, main window, Herd Care, Kitchen, What's new, milking request), drawn with the
plugin's own images, colors and sizes, to check how changes look before they go to testing.

- Live page: https://claude.ai/artifact/VdzBrC7CZa2hiQcKTBEhZb
- `python3 preview/build.py` refreshes `data.js` (version, What's new notes, theme colors) from the plugin's source.
- `assets` points at `VelvetDairy/Assets`, so the page always uses the current art.
- Fonts are stand-ins for the game's (Jupiter, TrumpGothic, Axis), so text widths are close but not exact.

When a change touches a window, update the matching part of `index.html` too, run `build.py`, and republish.
