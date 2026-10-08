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
dotnet build src\AltPeek\AltPeek.csproj
```

The build copies the dll into `BepInEx\scripts`. ScriptEngine watches that folder and
reloads the plugins ~3 s after a change (the game window must be focused, Unity doesn't
tick in the background). F6 forces a reload. Logs: the BepInEx console window and
`BepInEx\LogOutput.log`.

## Mods

- **AltPeek** — hold Alt to show a second details window by the cursor for whatever is
  under it (card, slot, book, aspect icon). The top-right window keeps showing what you
  clicked last. Config: `BepInEx\config\shirofaii.boh.altpeek.cfg`.
