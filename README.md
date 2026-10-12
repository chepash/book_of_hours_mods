# Book of Hours mods

Code mods for Book of Hours, loaded with BepInEx 5 + Harmony. The game's official mod
support only covers JSON content; these change UI and behaviour.

## Setup

```
powershell -ExecutionPolicy Bypass -File tools\setup.ps1
```

Installs .NET SDK 8, `ilspycmd`, BepInEx 5 and ScriptEngine into the game folder, and
decompiles `SecretHistories.Main.dll` into `decompiled/` (git-ignored) for reference.
The game path defaults to `D:\games\steamapps\common\Book of Hours`; override with
`-GameDir` here and `/p:GameDir=...` when building.

## Dev loop

```
dotnet build BookOfHoursMods.sln
```

The build copies the dll into `BepInEx\scripts`. ScriptEngine watches that folder and
reloads the plugins ~3 s after a change (the game window must be focused, Unity doesn't
tick in the background). F6 forces a reload. Logs: the BepInEx console window and
`BepInEx\LogOutput.log`.

## Mods

- **AltPeek** — hold Alt to show a second details window by the cursor for whatever is
  under it (card, slot, book, aspect icon). The top-right window keeps showing what you
  clicked last. Config: `BepInEx\config\shirofaii.boh.altpeek.cfg`.
- **BookFinder** — Ctrl+F opens the Finder: books (mystery, difficulty, read state,
  language), items (principles, tags), people already met (visitors and assistants) and
  desks/workstations (principles, special slots, idle or busy).
  Only things the player can reach are listed. Clicking a result flies the camera there.
- **QuietMenu** — hides the main menu's promo banner, News and "Bugs and Issues?" buttons.
- **SmoothZoom** — the mouse wheel zooms by a percentage of the camera height instead of a
  fixed distance. Config: `BepInEx\config\shirofaii.boh.smoothzoom.cfg` (`StepPercent`).
- **SpeedKeys** — keys 1–4 set the game speed (Normal, Fast, VeryFast, VeryVeryFast)
  instead of jumping between zoom levels.
- **OrderPreview** — discovered recipes in a desk's Craftable list show their result (what an
  order delivers, what a craft makes); click the icon (or Alt-hover it) for the item's details.
- **RoomHints** — locked rooms show what it takes to open them, without clicking.
- **DevTools** — Ctrl+Shift+D dumps the game's UI hierarchy into `BepInEx\dumps`.
