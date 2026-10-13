# Changelog

## 2.0.0 - SPT 5.0

SPT 5.0 runs EFT 1.0 on IL2CPP. This release is a port to BepInEx 6 IL2CPP and does not load on SPT 4.x (use 1.1.3 there).

- Ported to BepInEx 6 IL2CPP (`BasePlugin` + injected MonoBehaviour), built against the SPT 5.0 interop assemblies.
- Uses EFT's own APIs directly for exfil eligibility, localization, quest items, transit names and entry points instead of reflection.
- Quest markers come from `PlaceItemTrigger` / `QuestTrigger` and the raid's quest item list (`QuestZone` no longer exists in 5.0).
- Language detection is cached per raid instead of being evaluated every frame; label styles are reused instead of allocated per marker.
- Marker icons (`Standard_27.png`, `icon_quest.png`) can now also be placed next to the plugin DLL in `BepInEx/plugins/CompassHUD`.
- Config file and settings are unchanged from 1.1.x.

## 1.1.3 - SPT 4.1.2

- Labels Above Tape option for bottom-anchored placement, non-overlapping label rows in that layout.
- Fallback marker icon and diagnostics when icon textures or quest markers can't be found.
