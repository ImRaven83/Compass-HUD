# Compass-HUD

Adds a compass at the top of the screen during a raid, with customization options

## Compatibility

| Compass HUD | SPT | Loader |
|---|---|---|
| 2.0.0 (`CompassHUD-2.0.0.cs`) | 5.0 (EFT 1.0, IL2CPP) | BepInEx 6 IL2CPP |
| 1.1.3 (git history) | 4.1.x | BepInEx 5 (Mono) |

SPT 5.0 moved to the IL2CPP build of EFT, so mods built for 4.x will not load on 5.0 and 2.0.0 will not load on 4.x.

## Building for SPT 5.0

Launch the game once with BepInEx so it generates `BepInEx/interop`, then:

```
dotnet build -c Release -p:GameDir="C:/path/to/SPT"
```

Copy `bin/Release/CompassHUD.dll` into `BepInEx/plugins`.
