# Changelog

## 2.0.2 - SPT 5.0

- New Appearance > Width setting (0.4 to 2.5, default 1.0) to make the compass narrower or wider. It changes how much of the horizon is visible (1.0 = 80 degrees) while keeping tick spacing, text and markers the same size, and is capped to the screen width.

## 2.0.1 - SPT 5.0

- Quest markers now work on 5.0: a quest trigger is shown when one of your started quests has an unfinished objective in that zone (place item / beacon, visit, stay in zone), labelled with the quest name. 2.0.0 relied on zone ids having a translation, which none do in 5.0, so no quest markers appeared.
- Quest items are re-read every refresh, so items that appear after the first scan get markers.
- Scale, Marker Scale, Opacity and Roundedness have slider ranges in the configuration manager instead of a free text box.
- Icons are found directly in `SPT_Runtime/SPT_Data/images` instead of falling back to a full game-folder search.
- The marker scan log line now includes the Show* settings and the number of active quest zones.

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
