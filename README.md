# Compass HUD

Adds a compass tape to the screen during a raid, with optional extraction, transit and quest markers.

## Compatibility

| Compass HUD | SPT | Loader |
|---|---|---|
| 2.0.x | 5.0 (EFT 1.0, IL2CPP) | BepInEx 6 IL2CPP (ships with SPT) |
| 1.1.3 | 4.1.x | BepInEx 5 (Mono) |

SPT 5.0 moved to the IL2CPP build of EFT, so 1.x does not load on 5.0 and 2.0.x does not load on 4.x.

## Install

1. Extract the release zip into your SPT folder (the one containing `EscapeFromTarkov.exe`).
2. You should end up with `BepInEx/plugins/CompassHUD/CompassHUD.dll`.
3. Start the game through the SPT launcher. The config file is created at `BepInEx/config/com.vinarators.compasshud.cfg` after the first launch.

To uninstall, delete the `BepInEx/plugins/CompassHUD` folder.

## Settings

| Section | Setting | Default | |
|---|---|---|---|
| General | Enabled | true | Toggle with the hotkey in game |
| General | Toggle Hotkey | F8 | |
| General | Show Degrees / Show Letters | true | Tape labels |
| General | Show Central Indicator | true | `[ heading ]` box in the middle |
| General | Language | 0 | 0 = follow game, 1 = English, 2 = Russian |
| Appearance | Scale / Opacity / Roundedness | 1.0 / 0.9 / 1.0 | |
| Appearance | Width | 1.0 | 0.4 to 2.5. Wider shows more of the horizon (1.0 = 80 degrees) at the same tick spacing |
| Appearance | Vertical Position | 0.0 | 0 = top of screen, 1 = bottom |
| Appearance | Labels Above Tape | false | Use with a bottom position so the tape sits flush to the screen edge |
| UI Markers | Show Extractions / Show Transits / Show Quests | false | |
| UI Markers | Marker Scale | 1.21 | |
| UI Markers | Independent Marker Size | false | Disable distance-based marker scaling |

Settings can also be changed in game with a BepInEx configuration manager plugin.

## Marker icons

Extraction and quest markers use `Standard_27.png` and `icon_quest.png`. The mod searches `BepInEx/plugins/CompassHUD`, then the `SPT_Data` folders, then the game folder. If they aren't found, markers are drawn as small coloured squares; you can drop the two PNGs next to `CompassHUD.dll` to fix that.

## Troubleshooting

Check `BepInEx/LogOutput.log` for lines starting with `Compass HUD`:

- `marker scan` lists how many exfils, transits, quest triggers (by type) and quest items were found each raid.
- `could not find Standard_27.png ...` lists every folder searched for icons.
- `disabled itself after an unexpected error` means a game update changed something; please report it with the log.

## Building

Launch the game once so BepInEx generates `BepInEx/interop`, then:

```
dotnet build -c Release -p:GameDir="C:/path/to/SPT"              # bin/Release/CompassHUD.dll
dotnet build -c Release -p:GameDir="C:/path/to/SPT" -t:Package   # bin/Release/CompassHUD-2.0.2-SPT5.zip
```

Requires the .NET 10 SDK.
