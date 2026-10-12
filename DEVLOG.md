# Development log

Project history: what was done, why it was done that way, and which pitfalls we hit.
New entries go on top. Entry format: date, what was done, why, decisions and rejected
options, pitfalls.

---

## 2026-10-11 — Finder: kinds of readables; book contamination

The Books tab lists everything with the `readable` aspect, and that's not only books. The
kind comes from aspects (`WorldIndex.ReadableKind`): `film` (projector reel),
`record.phonograph` (record), `scroll`, `tablet`, `correspondence` (letter), `journal`;
anything else is a book (`codex`). The Type filter offers only kinds the player has; for
non-books the kind is written in the row's subtitle. Records and films work like books:
the same `mystery.*`, `mastery.*` and `reading.*`.

**Book contamination.** `contamination.*`, 8 kinds (`elements/contamination_aspects.json`):
actinic, bloodlines, chionic, curse.fifth.eye, keeperskin, sthenic.taint, winkwell,
witchworms. On the Books tab the Contamination block (Clean, Any, plus the kinds the player
has) appears only while some book is contaminated; when it hides, its filter is cleared. A
contaminated book shows the contamination's icon in its row. "Malady" (`malady`) is a soul
card illness, not a book one; it shows as a tag on the Items tab.

**How contamination really spreads** (researched for the player; the aspect descriptions
say "spread to nearby objects", but distance and shelves don't matter at all):
- A book gets contaminated when catalogued (`catalogue.book.<period>` links to
  `book.contamination.enact`, chance by period: Dawn 20%, Solar 10%, Baronial and Curia 5%,
  Nocturnal 2%; only while `status.rhonwen.open`). The kind follows the book's mystery.
- Soul cards carry xtriggers per contamination: using a contaminated book together with a
  susceptible soul gives the soul `ability.exposed.<kind>` (plus a malady).
- The `codex` aspect has xtriggers on `ability.exposed.*`: an exposed soul used with another
  book contaminates it with 50% chance. Curing the malady (`malady.curing`) removes the
  exposure.

## 2026-10-11 — OrderPreview: what an order will deliver

**How ordering works** (`recipes/correspondence_ordering.json`). On a desk: an order form
(Papers: `form.order.trn` for T.R.N. Ltd or `form.order.ch` for C&H) plus ink (With).
Soul/Skill/Memory aren't needed. The recipe `write.order.<firm>.<item>` mutates the form:
`orderplaced.<item>` +1, `cost.pence` + price, `post.ready`. Neither form nor ink is used up,
several orders on one form add up, "Begin Again" (`write.order.*.reset`) clears it. "(7/-)"
means 7 shillings, 0 pence (= 84 pence). The form goes to the post office (`trn.order.send`,
needs `pence >= cost.pence`); the parcel arrives about 3 days later.

**The mod.** The order recipe doesn't name the item, so the chain is rebuilt from data:
mutation `orderplaced.X` → that aspect's `provisioning` xtrigger mutates `contains.X`
("Contains something fragile") → its `unwrapping` xtrigger spawns the item (skipping `^`,
the parcel itself). A postfix on `AmbittableDisplayOpen.Populate` adds the item's icon to the
Craftable list row. The icon is an `IDetailable`: a click opens the item's details in the
game window (`ShowDetailable`), AltPeek shows them on Alt. The icon handles the click, so the
recipe under it isn't selected.

**Extended to crafting.** Discovered craft recipes name their result in `Recipe.Effects`
(e.g. `{"eigengrau": 1}`). We show elements with a positive numeric count that aren't
aspects, up to three; expression values like `-cost.pence` are skipped. Undiscovered recipes
are drawn by another class, `AmbittableDisplayLocked`, so they stay untouched and unspoiled.
Deck draws (`DeckEffects`) aren't shown: the result is random.

**Pitfall:** `AmbittablesDisplay.TryShowAmbittables` rebuilds rows only when the set of
recipes changes; otherwise it keeps the old rows and doesn't call `Populate`. Rows built
before the mod loaded (and a hot reload removes the mod's own icons) were left without
icons. Fixed with a postfix on `TryShowAmbittables` that fills in missing icons, plus a
one-time pass over existing lists when the plugin loads.

## 2026-10-11 — Performance: slowdown late in a long session

**Symptom.** After ~5 hours of playing while developing, the game started to stutter. No
errors repeating every frame in the logs.

**Main cause: hot reloads.** ScriptEngine reloaded the mods 36 times in one session. Mono
doesn't unload assemblies: every reload leaves old copies of all DLLs in memory together
with JIT code and Harmony detours. The process grew to 4.8 GB of private memory and GC
pauses got longer. It only costs during development: after a long series of rebuilds,
**restart the game**.

**Fixed in the mods** (work that ran all the time):
- QuietMenu called `FindObjectOfType<BHMenuScreenController>()` every second, during play
  too. Now it searches for the menu only for 10 seconds after a scene loads, then caches the
  reference or goes quiet until the next scene.
- RoomHints walked all spheres and tokens every second and rebuilt rooms without a hint.
  Now the room set is rescanned every 30 seconds, flags are checked every 2 seconds, and
  rooms without a hint are remembered.
- PeopleIndex (every 5 seconds, every token in the world) called `GetAspects`, a new
  dictionary per token. Now a cheap check on the static `Element.Aspects` comes first.
  Prototype aspects (`visitor` from `_visitor`) are merged into it on load
  (`Element.InheritFrom` → `CombineAspects`), so visitors aren't missed.

**Rule:** nothing heavy every frame or every second across the whole world.
`HornedAxe.GetSpheres()` copies the set plus each sphere's token list.
`FindObjectOfType` and `Resources.FindObjectsOfTypeAll` only once in a while.

## 2026-10-11 — Finder: what rereading a book gives; Items tags

**Rereading.** A book's xtriggers have `reading.<mystery>` → spawn of exactly one id
(`mem.impulse`, `mem.foresight`, music, parables, numina: 35 options across all books), and
`mastering.<mystery>` → a lesson. The mastering recipe also carries `reading.<mystery>`, so
after mastering the player has already seen that memory. It's shown only for mastered books
(no spoilers). Data: `Element.XTriggers["reading.<mystery>"][0].Id`.

Filtering by the memory's name didn't help the player: they search by composition ("I need
2 Moth and 1 Heart"). So the filter is counters per principle (left click +1, right click
-1, `ClickHandler`): a book passes if its memory (`Element.Aspects`) has at least every
required level. Later the Books tab was judged too crowded, and this moved to **its own
Memories tab**: counters on top, memory rows (icon, principles, "From N books"), and under
each, indented, the books that give it (click flies the camera to the book). Books again just
marks mastered books (with a check mark).

**Pitfall:** setting `TMP_Text.outlineWidth` on text created inside a window that hasn't
been shown yet throws NullReferenceException (the font material isn't set up), and the
window isn't created. For digit contrast we use a dark `box-rnd-16px` badge instead.

**Items tags.** A cap of the 60 most common tags hid rare ones (Ink); removed. Bookkeeping
aspects aren't tags: `zedge` etc. ("[Fatigued]"), `acted.*`, `a.x*`, `relevance.*`,
`wisdom.*`, `w.*`. Tags are grouped by initial letter (letter in the margin, each group on a
new line); selected tags come first, marked "•". Mouse wheel in lists: own scrolling
`WheelStepper`, exactly one row per notch; ScrollRect's step depends on the raw wheel delta
and was far too large. A transparent background under the tags, otherwise the wheel wasn't
caught in the gaps. Thin scrollbars styled like the room window.

**Items principles** are counters too ("at least": Moth 2 passes items with 6 Moth). Item
aspects are per card (`GetAspectsDividedByQuantity`), not multiplied by the stack size, the
same as the game's own details window: two Shapt cards still give Forge 1 per use.

## 2026-10-11 — SpeedKeys

Keys 1–4 set the game speed instead of zoom levels: 1 Normal, 2 Fast, 3 VeryFast,
4 VeryVeryFast (`GameSpeed`; Paused = 0, on Space). The wheel zooms well enough.
The game's bindings stay: prefixes on `UIController.Input_ZoomClose/Mid/QuiteFar/Farthest`
(handlers of the `kbzoom1..4` actions) return `false` and send `SpeedControlEvent`, like the
N/M keys. Rebinding "zoom 1–4" in the game's settings moves the speed keys too. Pause is the
same `Paused` speed at priority 2, so 1–4 lift it. Same guard as the game:
`HotkeysDisabled()` (typing, forced pause).

## 2026-10-11 — RoomHints; Finder: Desks tab

### RoomHints (new mod)
What it takes to open a locked room is drawn on the room itself, no click needed. Same data
as `TerrainDetailWindow`: `TerrainFeature.GetInfoRecipe().PreSlots[0]` —
`Essential` / `Required` / `Forbidden`. Only rooms the room window would offer are marked:
`ConnectedTerrain` with `IsShrouded && !IsSealed`. The panel is a child of the room token on
the world canvas, so it moves and scales with the map. `CanvasGroup.blocksRaycasts = false`:
clicks on the room still work. Look (chosen by the player): a column to the right of the
unlock icon, no group marks, `assistance` omitted (needed everywhere), forbidden aspects
tinted red. Size follows the room height and the number of rows.
Shared look-and-feel helpers (`GameStyle`) moved to `src/Shared` (namespace `BohShared`)
and are compiled into every mod via `Directory.Build.props`.

### Finder → Desks, later Places
Desks are `Situation`s whose verb has `Verb.Category == Workstation`
(`verbs/workstations_*.json`); a desk's principles are `Verb.Hints`, its slots `Verb.Slots`.
The first three slots (Soul / Skill / Memory, essential `ability` / `skill` / `memory`) are
on every desk; the rest are desk-specific (e.g. "Papers: Readable / Blank") and are what we
show. List: `HornedAxe.GetRegisteredSituations()`; state: `StateIdentifier`
(Unstarted/Inchoate = idle, Complete = done). Icon: `Situation.GetDetailSprite()`. Click:
`ShowDetailable(situation)`; `Situation` is an `IDetailable` too.

The `Workstation` category isn't only desks: village buildings, walks and gathering spots,
beds, hearths and the garden are in it too. The tab was renamed Places with our own groups by
verb id (`DeskIndex.Classify`): `village.*` → Village; `world.*` and `lighthouse*` ("Behind
the Scenes", DLC) → Outdoors; in the house: Desks if some slot accepts `readable` (reading
desks), `library.bed.*` → Beds (guest beds), the rest → House. The "C&H" the player
mentioned isn't a verb: it's the second order company.

## 2026-10-11 — Finder: People tab (visitors + assistants)

The Visitors tab became People: visitors and village assistants in one list, with an
All / Visitors / Assistants filter. Merged because both are searched by aspects for a task,
and an extra tab wasn't needed. The principle filter checks `interest.<principle>` on
visitors and the principles themselves on assistants.

**Assistants in the game.** A card `assistance.<who>` (aspect `assistance`, unique), e.g.
`assistance.rector`: Lantern 2, Knock 1. It appears in hand when hired and vanishes after
helping. No mark needed: the game records every unique element that ever existed in
`Character.UniqueElementsManifested` (`Watchman.Get<Stable>().Protag()`), which is exactly
the list of hired ones. Departed assistants are shown from the Compendium element; a click
opens `AureateNotifier.ShowAspectDetails(element)` since there's no card any more. An
assistant card offstage doesn't count as hired.

## 2026-10-11 — Finder: Visitors tab; QuietMenu; SmoothZoom

### Finder → Visitors
**Goal.** A list of visitors the player has already met, with their interests, languages and
tastes, without spoiling those who haven't come yet.

**In the game.** All visitor cards exist from the start and wait in `~/visitors.offstage`,
so "exists" doesn't mean "met". There's no explicit met flag: visitors aren't in
`Character.UniqueElementsManifested`, and all mutations are empty until you talk to them. A
visitor has the `visitor` aspect (a departing one, `embarking_<id>`, has
`visitor.embarking`); interests are `interest.<principle>: N`, understood languages
`u.<language>`, tastes `prefs.<id>` with a description, topics `convo.*`. Visits add
mutations (`interest.*`, `v.*`, `grateful`).

**Solution.** The mod marks visitors itself: as soon as one is on stage (a reachable sphere,
not `offstage`, room open), the card gets the illumination `shirofaii.boh.seen = 1`.
Illuminations are a card's string notes; the game saves them, so the mark survives restarts
and a new game starts with an empty list. Cards with mutations count as met too, which
covers visits from before the mod. The plugin scans every 5 seconds even with the window
closed: a visit can end before the player opens the Finder. Rejected: keeping the list in a
separate file (it would have to be tied to the save).

**Is writing to the save safe?** Checked in the game code: on load
(`ElementStackCreationCommand`) all illumination pairs are copied without validation; the
game only ever reads its own keys (`EDITABLE_ILLUM_DESC`, `xext|*`). An unknown key is
ignored, and if the mod is removed it just stays in the save unused.

### QuietMenu (new mod)
Hides the main menu's ads and external links. The banner is `ProemHolder` (a button calling
`ShowNews`; inside, `ProemSlot` with `NavigateToDestinationUrl`), the News button `NewsBtn`
(`ShowNews`), "Bugs and Issues?" `Button_Bugs` (`OpenUrl`). Buttons are found by the name of
the persistent method in `onClick`. `VersionText` also calls `ShowNews` but is kept.
**Pitfalls.** The menu buttons aren't under `BHMenuScreenController`; search the whole scene
(`S3MenuUmber`). Patching the menu's `Start` doesn't work: ScriptEngine adds the plugin a
frame later, so the scene is checked periodically. Once per scene the log gets the list of
buttons with their methods, which shows what else could be hidden.

### SmoothZoom (new mod)
**Problem.** The wheel moves the camera height by a fixed amount: `scroll/y` (120 per notch
on Windows) is clamped to 5 by the `mousezoom` action's processors (`Invert,Clamp(-5,5)`) and
multiplied by the `ZoomStepDistance` setting (80). That's 400 units over a −120…−2400 range:
close up one notch quadruples the view, the whole range takes ~6 notches.
**Solution.** A prefix on `CamOperator.OnZoomEvent`, wheel only: the height is multiplied by
a constant factor (`StepPercent`, 40% — the player's choice), based on
`smoothTargetPosition.z` so quick notches accumulate. The rest (`GetCursorWorldPosition`,
the smooth glide) is the game's. Keys, absolute levels and touch are untouched. Ctrl as a
"fast" and later a "fine" step was tried; the player needed just one step.
Priority `Low` plus a `__runOriginal` check: BookFinder blocks zoom over its window first.
**Later:** the wheel over a desk's Craftable list zoomed the camera instead of scrolling.
The game's guard (`LocalNexus.DisablePlayerZoom`) is only called from prefab events, not
from code, and not on every list. Now SmoothZoom raycasts the UI before zooming: if the top
element is inside a `ScrollRect` that has something to scroll, there's no zoom.
Diagnostics showed the Craftable list (`AmbittablesPanel/ScrollMask/ScrollRect`) has
`scrollSensitivity = 0` in the game: the developers switched the wheel off there, leaving
only the scrollbar. SmoothZoom scrolls such lists itself, 60 px per notch. Except lists with
their own `IScrollHandler` besides ScrollRect (Finder's WheelStepper), or they'd scroll
twice.
**Pitfall:** `Debug.Log` from a plugin goes only to Player.log, not LogOutput.log.

---

## 2026-10-08 — DevTools, Finder (books and items)

### DevTools (new mod)
Ctrl+Shift+D dumps the hierarchy of the details window, the room window and all canvases
into `BepInEx\dumps`: sprites, fonts, colours, layout. That's how we copy the game's look
instead of guessing. Key findings: font `EBGaramond08-Regular SDF`, colours `#F8EED9` (text)
and `#A38851` (gold), sprites `slots_bg_top`, `slot_bg_grey`, `slots_bg_outline`,
`slots_bg`, `information_goldborder`, `window_x_large 1`, input field `editableillum_bg`.

### Finder (BookFinder mod, Ctrl+F)
A window in the game's style, built in code from its sprites. Tabs now: Books / Memories /
Items / People / Places, a shared search field and result list. A click flies the camera to
the thing (`CamOperator.PointAtTableLevelAtQuiteCloseHeight`), opens its details in the
corner, and a thin gold frame pulses around it (`TokenMarker`).

**In the game.**
- A book has the `readable` aspect; mystery and difficulty are `mystery.<principle>: N`;
  read means `mastery.<principle>` is present (the reading recipe adds it by mutation);
  language is `w.<language>` (label "Written in Vak", trimmed to "Vak" in the UI).
- All spheres: `HornedAxe.GetSpheres()`, each with `Tokens`.
- The game's hotkeys switch off while a `TMP_InputField` has focus
  (`UIController.IsEditingText`).
- Ctrl+F is free: `F` isn't bound, Ctrl is only a click modifier.

**Pitfall: spoilers.** At first every book in the game was listed. Causes, one by one:
1. Locked rooms are already filled; only the room itself is flagged (`TerrainFeature`
   `IsShrouded`/`IsSealed`), so each item's room is found through its container chain.
2. The deck of undiscovered books `~/d.books.nocturnal_draw` (category `Dormant`). Hence a
   whitelist of categories: `World`, `Threshold`, `SituationStorage`, `Output`.
3. `~/visitors.offstage` and `~/incidents.offstage` have category `World`; cut by `offstage`
   in the path.
Debug report `BepInEx\dumps\finder.txt`: every item, SHOW/SKIP with the reason, sphere path,
visibility, hierarchy path, plus the list of tags.

**Other pitfalls.**
- Books in hand and in Belongings are on a screen-space canvas: flying the camera to them
  sends it into the sky. The camera moves only for the world canvas (`CanvasWorld`,
  WorldSpace).
- `AttentionPls` on small "Thing" items (`PhysicalManifestation`) is a thin half-second
  outline, easy to miss. Hence our own marker; the first version was too bright, now it's a
  thin frame.
- Secret compartments: `thingslotseeded` inside `SecretCompartment*/OpenedCompartment`
  (`AbstractSecretCompartment` is an ancestor of the slot). The game doesn't record whether
  the player found one (`isOpen` is purely visual), so such items are shown marked "secret
  compartment". Checking the slot's siblings marked the whole room; only ancestors count.
- A filter chip filled with gold looked "off"; an active chip now has a bright frame.
- Items doesn't list readables: they have their own tab.
- Post orders sit in `~/purchases.*` (e.g. `purchases.europe`) under the invisible
  `OffstageSphereHolder` of the screen canvas: they aren't in the world yet. First shown as
  "In the post", but that spoils what's coming; now hidden.

---

## 2026-10-08 — AltPeek working in the game

**Problem.** The mod built and was copied to `BepInEx\scripts`, ScriptEngine logged
`Loading shirofaii.boh.altpeek`, but the plugin's `Awake` never ran: no log line, no error.
Hot reload after a rebuild didn't work either.

**Cause.** ScriptEngine adds the plugin component not immediately but via a coroutine on the
next frame (`DelayAction`), and runs F6 and auto-reload from its `Update`. By default BepInEx
starts from the static constructor of `UnityEngine.Application`, and in this game that's
triggered by the Input System's initialisation, before the engine ticks MonoBehaviours. So
the BepInEx object's coroutines and `Update` never ran.

**Fix.** In `BepInEx\config\BepInEx.cfg`, section `[Preloader.Entrypoint]`:
`Type = Camera` instead of `Application`. Added to `tools/setup.ps1`.

**Another pitfall (2026-10-07).** ScriptEngine reads DLLs with Cecil and `ReadSymbols = true`
and fails with `SymbolsNotFoundException` when there's no pdb next to them. Fix:
`<DebugType>embedded</DebugType>` in `Directory.Build.props`: symbols inside the DLL, one
file to deploy.

---

## 2026-10-07 — Project start, environment, first mod AltPeek

### Goal
Mods for Book of Hours that change the UI and behaviour. The game's official mod support is
JSON content only (elements, recipes, texts) and can't change UI behaviour. Hot-reloading
mods without restarting the game was wanted too.

### Stack
- The game is **Unity 2022.3.62, Mono backend** (`bh_Data/Managed/*.dll`, no IL2CPP), so the
  classic **BepInEx 5 + Harmony** fits.
- **ScriptEngine** (from BepInEx.Debug) gives hot reload: plugins live in
  `BepInEx\scripts` and reload ~3 s after the DLL changes; F6 by hand. Settings
  `LoadOnStart = true` and `EnableFileSystemWatcher = true`.
- The game code is decompiled with `ilspycmd` into `decompiled/` (git-ignored). Version
  **8.2.0.7535**, since newer ones need .NET newer than 8. It targets net6, so it runs with
  `DOTNET_ROLL_FORWARD=Major`.
- Build: `net472` + the `Microsoft.NETFramework.ReferenceAssemblies` package. References to
  game and BepInEx assemblies with `Private=false`. After `Build` the DLL is copied to
  `BepInEx\scripts`. Everything shared is in `Directory.Build.props`, so a new mod is an
  empty csproj in `src/<Name>/`.

### AltPeek
**Goal.** Clicking an item or slot opens its details top right, and clicking another item
replaces them. The player wants two descriptions at once: the pinned one from the click and
another on hover.

**How it works.** While Alt is held, a second details window appears at the cursor for
whatever is under it. Release Alt and it disappears; the corner window doesn't change.

**In the game** (see `decompiled/`):
- The corner window is `SecretHistories.UI.AureateMasterDetailWindow`, a single instance,
  reachable through the private field `masterDetailWindow` of `AureateNotifier`, which is
  `Watchman.Get<AureateNotifier>()`.
- All clicks end up in `AureateNotifier.ShowDetailable / ShowCardElementDetails /
  ShowSlotDetails / ShowAspectDetails`, which call the window's
  `ShowDetailable(IDetailable)` or `PreviewAspect(Element)`. Each call overwrites the window.
- `IDetailable` is implemented by `ElementStack` (cards), `Sphere` (slots), `Situation`,
  `TerrainFeature`, `AbstractPermanentPayload`, `NoteIllumination` and more. Cards on the
  table have a `Token` on their GameObject; the payload is `token.Payload`.
- Aspect icons are `ElementFrame`; the aspect is in the protected field `_aspect`.
- The game reads the keyboard through the new Input System (`Keyboard.current`); the old
  `Input.GetKey` isn't used.

**Decisions.**
- The window is **cloned** with `Instantiate` rather than rebuilt: layout, style and filling
  logic come along, and nothing needs syncing when the game updates. The window's children
  don't register anything in `Watchman` (checked), so the clone doesn't interfere.
- The clone sits in its own full-screen layer with `CanvasGroup.blocksRaycasts = false`.
  The window creates aspect icons itself, and without this they would catch clicks.
- The clone is created ahead of time, not on Alt: its `Start()` hides the window and only
  runs on the next frame.
- Target under the cursor: `EventSystem.RaycastAll`, **topmost hit only** (what a click would
  hit), then up the parents to an `ElementFrame`, a `Token` or any
  `MonoBehaviour : IDetailable`.
- When hiding, the layer is deactivated first, then `HideAll()`; otherwise
  `CanvasGroupFader` starts a fade coroutine that gets cut off halfway.
- The clone hides the book renaming field (`EditableIllumination`): meaningless in a window
  you can't click.
- The window opens towards the larger part of the screen and stays inside it. The cursor
  offset is the `CursorOffset` config entry.
- Some detailables carry aspects missing from the compendium (e.g. `NULL_ELEMENT_ID`) and
  the game's window code throws on them; such targets are skipped quietly.
