# Book of Hours mods

Code mods for Book of Hours (Unity 2022.3, Mono) on BepInEx 5 + Harmony, hot-reloaded
through ScriptEngine. Talk to the user in Russian; write docs (this file, DEVLOG.md) and
code comments in English.

**Before starting, read [DEVLOG.md](DEVLOG.md)**: history, decisions and pitfalls found.

## Where things are
- Game: `D:\games\steamapps\common\Book of Hours` (added to the workspace).
  Main game logic: `bh_Data\Managed\SecretHistories.Main.dll`.
- Decompiled game code: `decompiled/SecretHistories.Main/` (git-ignored). Search it before
  guessing how the game behaves. If it's missing, run `tools/setup.ps1`.
- Game content (recipes, elements, verbs) is JSON in
  `bh_Data\StreamingAssets\bhcontent\core`, some files UTF-16.
- Mods: `src/<ModName>/`. Shared references, target and deploy: `Directory.Build.props`.
  Shared code (game look and feel helpers): `src/Shared`, compiled into every mod.
- `tools/setup.ps1` sets up the environment (.NET SDK 8, ilspycmd, BepInEx, ScriptEngine,
  configs).

## Build and check
- `dotnet build src\<Mod>\<Mod>.csproj` (or the whole `BookOfHoursMods.sln`): the DLL is
  copied to `BepInEx\scripts`; the game reloads it ~3 s later if its window has focus, or
  on F6.
- `dotnet` may be missing from the current shell's PATH: `C:\Program Files\dotnet\dotnet.exe`.
- Logs: `BepInEx\LogOutput.log`; the full Unity log with exceptions (and anything from
  `Debug.Log`) is `%USERPROFILE%\AppData\LocalLow\Weather Factory\Book of Hours\Player.log`.
- Every plugin logs `<Name> <Version> loaded` in `Awake`; that shows whether it loaded.
- What the build can't verify needs the user in the game: say plainly what hasn't been
  checked live.

## Rules
- Every mod is a `BaseUnityPlugin` with GUID `shirofaii.boh.<modname>`. Remove everything it
  creates in the scene in `OnDestroy`: ScriptEngine destroys the plugin on reload.
- Keyboard and mouse only through the new Input System (`Keyboard.current`,
  `Mouse.current`).
- Private game members through `AccessTools` (Harmony); cache fields in static readonly.
- No spoilers: list only what the player can reach or has already seen (no decks, offstage
  spheres, locked rooms, post in transit, undiscovered recipes).
- Performance: nothing heavy every frame or every second. Walking all spheres
  (`HornedAxe.GetSpheres()`), `FindObjectOfType`, `Resources.FindObjectsOfTypeAll`: once or
  rarely, with a cache. Cheap checks (`Element.Aspects`) before expensive ones
  (`GetAspects`).
- Hot reloads pile up memory: Mono doesn't unload old assemblies. After a long series of
  rebuilds, ask the user to restart the game.
- After notable work (a new mod or feature, an important decision, a pitfall found)
  **add an entry at the top of DEVLOG.md**: what, why, how the game does it, what was
  rejected.
