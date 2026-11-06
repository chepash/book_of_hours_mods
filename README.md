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
- **BookFinder** — F (or Ctrl+F) opens the Finder: books (mystery, difficulty, read state,
  language), items (principles, tags), people already met (visitors and assistants) and
  desks/workstations (principles, special slots, idle or busy), and Cures: infected books by
  contamination with what the cure needs and which of the player's skills work against it.
  Inks: inks the player can brew now, as click-through steps (desk, skill, soul).
  Souls: pick a soul to see where it can be evolved now and with which skill.
  Items: a chip lets a double click also put the card into a free carried-things slot when no
  open window takes it (config `Items.DoubleClickToHand`).
  Memories: principle levels narrow the list (all of them); with "Any" ticked one is enough.
  Memories › Weather: the weathers (each a memory) and the coming days from the season's deck.
  Cookbook: kitchen recipes the player has cooked (recorded by the mod), with the kitchen, a
  soul and the ingredients on hand or missing, and earlier steps (Cake Batter before a cake).
  Filters: food or drinks, course of a formal meal (first, main, side, pudding), principles.
  Middle click a dish to get it into a free slot of your carried things (cheat).
  People › Invite: how to write a guest's invitation (desk, Writing Case, ink, skill, soul) and the
  invitation it gives; middle click the invitation to get it at once (cheat).
  Books show their subject and its level ("On Ragged Crossroads [10]"); the search matches subjects too.
  While a constellation window on the Tree screen asks for a text ("Ragged Crossroads [8]"), the Books tab
  shows a "For constellation" row: a click lists the books that fit.
  Only things the player can reach are listed. Clicking a result flies the camera there.
- **QuietMenu** — hides the main menu's promo banner, News and "Bugs and Issues?" buttons.
- **SmoothZoom** — the mouse wheel zooms by a percentage of the camera height instead of a
  fixed distance. Config: `BepInEx\config\shirofaii.boh.smoothzoom.cfg` (`StepPercent`).
  Also swaps the zoom keys: E zooms in, Q zooms out.
- **SpeedKeys** — keys 1–4 set the game speed (Normal, Fast, VeryFast, VeryVeryFast)
  instead of jumping between zoom levels.
- **OrderPreview** — discovered recipes in a desk's Craftable list show their result (what an
  order delivers, what a craft makes); click the icon (or Alt-hover it) for the item's details. Situation windows show the result of the craft
  about to start or in progress the same way.
- **AspectPin** — middle-click an aspect icon to keep its items highlighted (one aspect at a
  time); the pin sits in a small panel under the clock. Works on the Finder's aspect icons too.
- **RoomHints** — locked rooms show what it takes to open them, without clicking.
- **CopyBreaks** — text copied by clicking a description is wrapped in line breaks (one before, one after), so
  copies pasted one after another stay separate paragraphs. A finished book's window gets a "Book text" button:
  title, description, "I'm Reading..." and "I've Read..." in one copy.
  A salon's window gets "Conversations" (red border, so it stands out from the game's own buttons): the whole conversation so far in one copy.
- **TreeDim** — while the Tree of Wisdoms is open, skills already committed to a Wisdom are
  darkened in the skills tray, so the ones still to place stand out.
- **LessonFit** — while a Lesson card is open in the detail window, skills it can't upgrade
  (no shared principle, or already level 9) are darkened in the skills tray.
- **SlotFit** — while a desk or any other window is open, souls, skills and memories that fit
  none of its slots are darkened in their trays.
- **Attention** — a violet button right of "Go to the next completed": how many reading desks are
  waiting for a card right now (a mastery reading's 10-second Opportunities: a Soul, a Comfort, a
  Memory...); a click flies to the next one and opens it. The game pauses when one appears
  (config `PauseWhenAsked`; `DesksOnly` limits it to desks and Consider). Salons too: a
  conversation waiting for a soul card counts and pauses the game (config `Salons`).
- **NextClear** — windows never open over the "Go to the next completed" button: one that would
  is moved down below it, so the button can be clicked again and again.
- **AutoTalk** — a Talk button right of the speed controls toggles automatic Affair talks. A
  visitor who came for an Affair gets talked to about it and given a fitting book from the
  library: right mystery, at least their interest, not consulted yet, in a language they read,
  read books first, then the lowest level. The arrival and the talk are collected on their own.
  Config `Skip` lists `affair:visitor` pairs that are talked to without a book.
  A smaller button next to it starts Current Affairs now (what a season change does: one visitor
  for the current Affair, or a new Affair).
- **DevTools** — Ctrl+Shift+D dumps the game's UI hierarchy into `BepInEx\dumps`.
