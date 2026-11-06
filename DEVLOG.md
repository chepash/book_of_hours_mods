# Development log

Project history: what was done, why it was done that way, and which pitfalls we hit.
New entries go on top. Entry format: date, what was done, why, decisions and rejected
options, pitfalls.

---

## 2026-11-07 — AutoTalk: automatic Affair talks (new mod)

A toggle button (Talk verb icon) right of the speed controls; state kept in the BepInEx config
(`Enabled`). While on, a visitor who came for an Affair is talked to about it, given a book
and the talk is collected.

How the game does it:
- `incident.setup` pulls the current Incident and links to `visitor.for.<key>` in
  `~/arrivalverbs`. That recipe greedily takes a visitor with a matching interest from
  `~/visitors.offstage` and mutates `v.<key>` on them.
- Talk slot "talk" takes the visitor; the `_visitor` prototype opens slot "subject" for the
  Incident. `talk.visitor.intro.<v>.<key>` (craftable) links to
  `talk.visitor.show.slot.specific` (60 s, slot "v" Book).
- At the end `talk.visitor.incident.success.<v>.<key>.mystery.<p>` needs `mystery.p >= interest.p`.
  It marks `acted.<v>.<key>.<p>` on the Incident and `tx.<v>` on the book. The alts
  `intercept.tx.<v>` and `cantread.<lang>` come first and fail the talk.

How the mod does it:
- **Arrivals.** Every 0.5 s it looks at registered situations whose verb or recipe is
  `visitor.for.<key>`. The visitor sits in their slot during the 5 s warmup, so the
  visitor → Affair link is read there.
- **Choosing a pair.** Every 5 s, while the Talk verb is idle and empty, one pass over the
  World spheres collects free visitors and live Incidents (in `hand.misc`, not `finished`).
  A pair needs:
  - `v.<key>` on the visitor;
  - no `acted.<v>.*` on the Incident;
  - an existing success recipe;
  - pairs on the `Skip` list (default `opera.wings:olympe,opera.wings:arun`, which protects
    Morgen's Exploits) are talked to without a book;
  - not tried this session.
  
  Unknown arrivals (toggle-on, reload) fall back to a visitor with exactly one candidate Affair.
- **Choosing a book.** World spheres under `~/library`, room not shrouded or sealed, no
  christmas slot. The book needs a mystery the success recipe takes, `>= interest`, no `tx.<v>`,
  a language the visitor reads (`w.X` → `u.X`) and no contamination. Read books come first,
  then the lowest enough level. If no book fits, the pair is left to the player.
- **The talk.** A state machine on the Talk situation:
  1. visitor → "talk", Incident → "subject";
  2. `UpdateRecipePrediction` must predict `talk.visitor.intro.<v>.<key>`, then `TryStart`;
     otherwise `DumpUnstartedBusiness`;
  3. wait for the "v" slot under `show.slot.specific`, then put the book in;
  4. when Complete, `Conclude()`.

Fixes after the first try in the game:
- **Arrivals need a Collect.** A finished `visitor.for.<key>` waits in state Complete with the
  visitor and the Incident in its output. The mod now `Conclude()`s it, and only then can the
  visitor be talked to.
- **Skip pairs get a talk without a book.** The player wants them talked to, just not given a
  book. The empty slot ends as `talk.visitor.reading.failure`, and no `acted` mark is set.
- **Finished Affair talks are collected even if the mod didn't start them.** This covers talks
  lost on a reload and talks the player started by hand. The check is: Talk is Complete and an
  Incident is in its output. Collecting returns the Incident to `hand.misc`. Checked live: it
  collected Arun's Opera talk.
- **Pitfall: `CurrentRecipeId` doesn't stay `talk.visitor.show.slot.specific` while that recipe
  runs.** It switches at once to the predicted alt (`talk.visitor.incident.failure.<v>.<key>`
  with the slot empty). The book step waited for the slot recipe id and never put the book in.
  It now waits for the "v" threshold while the situation is Ongoing.
- **Running talks are taken over.** After a reload, or if the player started the talk, an
  Ongoing talk with an empty "v" slot, a visitor and an Incident in its spheres is picked up at
  the book step. Checked live: it gave Connie her book on 'Apollo and Marsyas'.
- **Current Affairs on demand.** A smaller button right of the talk toggle does what a season
  change does: it spawns `incident.setup` (verb `incident.base`) in `~/arrivalverbs` through
  `SituationCreationCommand.WithRecipeAboutToActivate` + `TokenCreationCommand`.
  - The season recipes (`year.season.*`, every 2160 s = 6 days, and the `.return` ones after
    Numa) link to `incident.setup` with `additional` and `topath ~/arrivalverbs`.
  - After a 60 s warmup, `incident.setup` sends one visitor for the current Affair, draws a new
    Affair, or retires an Affair with no visitors left.
  - The button is refused and dimmed while any `incident.base` situation is running or holds
    output.
  - Checked live: it sent Dagmar for 'intrusion', and the mod then talked to her, gave her a book
    and collected the talk.
  - `Skip` also got `intrusion:zachary` and `revolution:azita` (council members with
    mission-breaking steps there).
- **Pitfall: books came back to the hand, not to their shelf.** A token returns on Collect to
  its `HomeLocation`, which `Token.StartDrag` sets through
  `RequestHomeLocationFromCurrentSphere()`; `GreedyAngel` does the same. A plain
  `TryAcceptToken` leaves it unset, and the token goes to the default sphere (the hand). The mod
  now calls `RequestHomeLocationFromCurrentSphere()` before every move (visitor, Incident, book).
- **The talk window is closed after the Collect** (`Situation.Close()` if `IsOpen`), at the
  player's request.
- **`opera.apollo:morgen` was added to the default `Skip`.** Morgen's steps in that
  constellation give Stone Remembered, against the Institute mission. An existing config keeps
  its old value, so the user's cfg was edited by hand.
- **The wait for the Book slot is in game time, so it has no real-time deadline.** The intro's
  1 s warmup doesn't run while the game is paused, and a 30 s timeout gave up too early.

Rejected: talking to any visitor with an unacted `v.<key>`. Corso, for example, keeps
`v.opera.wings` without acting, and would be talked to whenever he comes for something else.

Not checked live yet: the whole flow, the button position and look, and whether Conclude sends
the book back to its shelf.

## 2026-11-07 — SpeedKeys: speed lock (middle click on the speed button)

The game drops back to normal speed at every daybreak; the player wanted to keep x2–x6. A
middle click on the middle speed button locks the speed (the buttons turn purple); another
middle click unlocks.

How the game does it: `day.daybreak` (`celestial_recipes_time.json`) has the fx
`"meta.setspeed": 1`. `LocalNexus.BroadcastFx` turns a `meta`/`setspeed` command into
`SpeedControlEvent` (priority 2, `GameSpeed` 1 = Normal, no SFX). Its listeners include
`Heart.RespondToSpeedControlCommand` (the real speed) and
`SimplerSpeedControlUI.RespondToSpeedControlCommand` (which button shows).

The mod: a prefix on `BroadcastFx` marks "sending setspeed" (a finalizer clears it). Prefixes on
both listeners swap `args.GameSpeed` for the speed that is running (or the speed before a
pause). The args object is shared, so whichever listener runs first swaps it and both agree.
The player's own speed changes are untouched; the lock keeps whatever speed is running.
The buttons (`pausedButton`..`veryVeryFastButton`, private fields of `SimplerSpeedControlUI`,
read through `AccessTools`) get a small `IPointerClickHandler` that only reacts to the middle
button. The `Button` ignores non-left clicks, so both handlers live on the same object. The
UI is looked up with `FindObjectOfType` once a second only until found (it comes with the game
scene). `OnDestroy` removes the handlers and restores the colours.

Rejected: changing the fx parameter (it is also stored in `Xamanek` as the current fx);
patching only `Heart` (the button would show Normal if the UI listener ran first).

## 2026-11-07 — Finder Invite: middle click on a portrait posts the invitation (cheat)

The player wanted to skip writing and posting letters: a middle click on a guest's portrait in
People › Invite now does what posting the invitation does.

How the game does it (`village_interactions.json`): `postoffice.invitation.send` at the Post
Office links, as an *additional* recipe with `topath ~/arrivalverbs`, to
`postoffice.invitation.send.<visitor>` ("Zachary Incoming?", verb `visitor.<visitor>`, 60 s). Its
greedy slot pulls the visitor from `~/visitors.offstage`; when it ends, the visitor arrives.
`Situation.AdditionalRecipeSpawnToken` builds a `SituationCreationCommand(verb)
.WithRecipeAboutToActivate(recipe)`; `TryActivateRecipeCommand` doesn't check reqs unless asked,
so the letter itself isn't needed.

The mod creates that situation directly in `~/arrivalverbs` (`TokenCreationCommand`, like
`CollectionTrigger`), so the arrival keeps the game's timing and the usual "Incoming?" token.
It refuses if a `visitor.<id>` situation already runs. A letter already written for the guest
is retired, as posting would use it up. Rows: every guest still to invite, plus "Invitation
written: post it". Echidna (summoned on the Moor, no letter) has no such click.

Not handled: the Numa season (the Post Office refuses letters then; the cheat doesn't), and
guests with a special-item letter (Hokobald: Orpiment Exultant) get summoned anyway.

## 2026-11-06 — DayNotice: Daybreak and Dusk banners close by themselves

The Daybreak / Dusk banner in the middle of the screen stayed until clicked; the player wanted it
gone after about a second. New mod `src/DayNotice`.

How the game does it: `day.daybreak` and `day.dusk` in `celestial_recipes_time.json` are
`"notable": true`. `RecipeCompletionEffectCommand.RespectNotables` (private) calls
`AureateNotifier.ShowNotificationWindow(label, desc, isEnduring: false)`, which builds an
`AureateNotificationWindow` via the private `BuildNotificationWindow` and gives it a coroutine timer
of 10 s + 2 × setting `NotificationTime` (the player's is the default, 10 s). The coroutine counts
down only while `Heart.Metapaused` is false. Why it didn't close in practice is unclear: no
"Coroutine couldn't be started" in Player.log; `Hide()` is a no-op while `IsBusy()` (animating),
so a timer that fires during an animation leaves the window up for good — a plausible cause.

The mod: a prefix on `RespectNotables` marks "announcing a notable recipe" (only `day.*` by
default, config `OnlyDaybreakAndDusk`), a postfix on `BuildNotificationWindow` remembers the window
built meanwhile, and `Update` calls `Hide()` after `Seconds` (default 1, real time, so it works
while paused), retrying every 0.25 s until the window is gone. Nothing is created in the scene;
`OnDestroy` unpatches and drops the list.

Rejected: shortening every notification (also hides "can't put this card here" and others the
player may want to read); changing the `NotificationTime` setting (global, and the timer it feeds is the one that didn't
close the banner anyway).

## 2026-11-06 — Finder Invite: summoning Echidna

Echidna is a visitor without a letter, so the Invite list (built from `write.summon.*` recipes
and noted addresses) never showed her. New `SummonIndex.cs` plus a "Summon (no letter)" row
under the guests, with its recipe in the lower area like a letter's.

- How the game does it (`gathering_1_exceptional.json`): `gather.echidna.moor` at `world.moor`
  ("A Walk on the Moor") needs a soul, a skill that carries the mutation
  `technique.summon.echidna` (learnt by Considering `x.summon.echidna` with Coil & Chasm,
  Pentiments, Serpents & Venoms or Wolf Stories), Serpent-Milk in the recipe's preslot and
  Scale 7 in total. Serpent-Milk has Scale 3 itself. The Moor's soul and skill slots take only
  Heart / Rose / Scale cards, so the soul is checked with `SlotCheck`.
- Two arrival forms: `echidna` (leaves after 360 s) or, once an n-state with
  `echidna.inspired` exists (she has had her revenge in The Second Skin), `echidna.persistent`
  (stays; a Secretary Persistent candidate). She is `unsociable`: no ordinary salon.
- Spoilers: the row appears only once a skill has the technique.
- Status: "Here now" (a person whose id starts with `echidna`), "On the way"
  (`echidna.enroute*` token), Moor busy or missing. The row stays clickable when she is here.
- Missing Serpent-Milk is a dimmed step that names the craft (Serpents & Venoms +
  Perinculate, Keeper Scale 15); middle click gives one, like the Cookbook.
- Steps: Place (Moor) › soul › skill › Serpent-Milk (its slot opens once soul and skill are in).

## 2026-11-05 — Finder People: Sweet Bones deck only on "Assistants"

The Sweet Bones deck panel (hiring helpers, reordering the deck) was shown above every People
list, which was noise when looking for visitors. `AddSweetBonesDeck()` now also requires
`_peopleFilter.Kind == PersonKind.Assistant`. The Who chips call `Refresh()`, which calls
`AddSweetBonesDeck()`, so the panel appears and disappears with the filter. It stays hidden in
Invite mode as before.

## 2026-11-05 — Finder People: "Numa" filter instead of "Here now"

The "Here now" chip was not useful (people in the house are already listed first), so it
became **Numa**. That chip shows only the visitors who come on their own in Numa.

How the game marks them:
- the element has aspect `numatic` (it inherits `_visitor.numa`), e.g. Aunt Mopsy, Julian Coseley, Lord Franklin Bancroft;
- they have no `address.*` and no `write.summon.*` recipe, so they can't be invited;
- `visitor.for.numa.*` in `visitors_correspondence_auctions.json` sends one from
  `~/visitors.offstage` while a matching Numa incident (`incident.numa.*`) is held.

`PersonEntry.Numa` reads `numatic` from the person's own element (`ElementOf(PersonId)`),
not from the card, because a departing card is an `embarking_*` element without it.

## 2026-11-02 — Finder Books: no shop order forms

The order forms (Cater & Hero, T.R.N. Ltd) showed up in the Books list: they are `readable`
(the `_order` prototype in `correspondence_elements.json` gives `readable` + `form.order`), but
have no mystery, nothing to master, and are filled in with ink, not read. `WorldIndex.ReadBook`
now skips anything with `form.order`, so the forms count as plain items and show on the Items tab.

## 2026-10-28 — Finder Books: busy books last

A book a desk is working with (being read, or used by a running recipe) showed up first in the
Books list like any other, though the player can't take it. Such books now sort after all the free
ones, their row is faded and the subtitle starts with a violet "Busy". "Busy" reuses `BusyHost`
(the host situation is running: not Unstarted, Inchoate or Complete), so a book merely lying in an
idle desk, or in a finished one waiting to be collected, still counts as free.

## 2026-10-28 — Finder Memories: "Any" for the principle filter

The principle chips on the Memories tab only narrowed the list: a memory had to meet every chosen
level. An "Any" checkbox at the end of the chip row switches that to "one of them is enough", so
the player can look for, say, a Heart or a Grail memory at once. One helper (`MemoryLevelsMatch`)
now does the check for every source mode (books, objects, well/fire/sea, crafts, weather), which
used to repeat the same `All(...)` lambda four times. The sort by the chosen principles' sum is
unchanged.

## 2026-10-28 — CopyBreaks: "Book text" next to Collect

The player looks at Collect when a reading ends, and the "Book text" button at the far left of
the footer went unnoticed. It now sits just left of Collect (AureateWindow.startButton, found
through AccessTools; same anchor, pivot and height, 12 px gap), the way "Conversations" sits by
the salon's Start. Its look follows Collect: the game's rounded `boh button` sprite (sliced,
border 9-10 px) tinted red or gold as the border, with a second copy inset 2.5 px and tinted
dark as the plate. That sprite is light gold, so CopyButtonFx got per-button plate tints
(`SetPlate`); the salon button keeps the white `slots_bg` tints.

## 2026-10-28 — Finder Items tab: no souls, skills or lessons

The Items tab listed every reachable card except readables, so souls (`ability`, maladies
included), skills (`skill`) and lessons (`lesson`) showed up among the things. They are now
filtered out of that tab only (`NotItems`); the shared item index still has them, because the
Souls tab, the skill checks and the "moved cards" refresh read it.

## 2026-10-27 — Finder Cookbook: course filter

A formal meal (Lunch, Supper) needs one dish of each course on the table: the salon recipes
require `course.first`, `course.main`, `course.side` and `course.pudding` (aspects summed over the
table, so one card can cover two courses: Pickled Mushrooms are first and side). The Cookbook now
has a course row (Any / First / Main / Side / Pudding, one at a time) and shows each dish's
courses in its subtitle. These four are all the course aspects the game uses.

Pitfall: the Cookbook's `Seed` list only fills the cooked-recipes file on the very first run. A
dish cooked before the mod (or outside the last two logs) is added by appending its recipe id to
`BepInEx\config\shirofaii.boh.bookfinder.cooked.txt`, then reloading the mod.

---

## 2026-10-27 — DevTools: nx-request and remove-request (undoing a constellation choice)

The player returned a constellation offering by mistake. The game has no undo, and the only
older save predated several other steps, so the state was patched live. Two new file requests in
`BepInEx/cache`, next to save-/spawn-request:

- `nx-request`, "from to" per line: a constellation's story card (`n.claw.patient.quelled.finally`)
  sits in the Nx node's private `_internalSphere`. It is retired and a new card provisioned
  (`ProvisionElementToken`) rather than `ElementStack.ChangeTo`: ChangeTo raises no sphere-contents
  event, so the node would keep its old title and choices; a new token triggers
  `OnTokensChangedForSphere` → `UpdateSummary` and the choices refresh.
- `remove-request`, element ids: one card each, from the exterior spheres. The step's rewards were
  found in `~/limbo`, where the window's outputs wait before going to the table.

The offering came back through the existing `spawn-request`.

## 2026-10-27 — Invite: the invitation as a result; Books hand placement

People › Invite: the recipe header shows, at its right, the card the recipe writes,
`letter.summon.<visitor>` (the effect of `write.summon.<visitor>`), in place of the principle badge
(the subtitle already says "Grail 7 in total at the desk"). Click shows its details, middle click
gives it into a free carried-things slot (`GiveToPortage`, the Cookbook's cheat). Shown also when
the letter can't be written now, so the cheat works without a free desk. A separate "Result" strip
under the recipe was tried first and dropped: the header is where the player looks.

The Books tab got the Items tab's "Hand placement" checkbox (own config `Books.DoubleClickToHand`),
in the Status row: a separate row would push the results down. A constellation window isn't a
situation, so `SendToOpenSituation` can't fill it; with the checkbox the book lands in the carried
things, one drag from the window.

## 2026-10-27 — Finder: books by subject, constellation requests

Constellations on the Tree screen (further stories: Nx nodes, `elements/DLC_HOL_ns.json`, choices in
`recipes/DLC_HOL_nrs.json`, UTF-16 with trailing commas) ask for "a text on Ragged Crossroads [8]".
That is not a skill level: the step recipe (`nxh.*`) requires the node's story card, the visitor,
`r.<subject>` and `soph` >= N. Every book carries a subject aspect (`r.raggedcrossroads`, label
"Subject: ...") and a hidden sophistication `soph`, usually equal to its mystery level; manuscripts
get `soph` from the writing skill (doubled by an ink of the skill's principle). Cards put into the
window come back after the step (inputs flush to the final sphere, then `TryEvictOutputs`).

- `BookInfo.Subject` / `Soph`; book rows show "On <subject> [soph]"; the search box matches the
  subject name too.
- `NxRequests.ForOpenWindow`: the open Nx node (`NxNodesSphere`, `IsOpen`), its story card (the
  token in the private `_internalSphere`) and the visitor picked in the window (private
  `_selectedRecipe`, its required key without a dot). The `nx` recipes requiring that card and that
  visitor give the requests: `r.*` + `soph` or a particular `t.*` book. A "For constellation" row of
  chips on the Books tab filters to them (replacing Status, like a guest; Mystery/Difficulty cleared).
- Only the open window is read, so nothing past the player's current step shows. A full subject
  grid (~70 subjects) was rejected: the search box covers browsing.

## 2026-10-27 — Attention: salons waiting for a soul

A salon asks for souls the same way a mastery reading offers Opportunities: each conversation step
is a routing recipe `slnch.needs.ability.<soul>` (DLC_HOL_3b_salons_routing.json) with one
"Soul" slot (essential ability + `.met` / `.cho`...) and a 10-second warmup; an empty slot means
the talk is waiting, and missing it lets the conversation fizzle. Attention already detects "an
empty Threshold sphere active in the Ongoing state", so salons (`VerbId` salon.*) are simply let
through the filter: they count on the violet button and pause the game. Config `Salons` (on). The
salon's guest slots are verb slots, not active while it runs, so they don't count.

## 2026-10-27 — Invite view: room for the guest list

In People › Invite the list of guests got little room: the People principles filter (useless for
invitations) sat above it, and the recipe area below kept the Cures tab's fixed 330 px though a
recipe (header, desk strip, cards strip, total line) needs about 200. Now Invite hides the
principles block (`_peoplePrinciples`) and sets the recipe area to 205 px; the scrolling list is
flexible and takes the rest. Cures sets 330 back, People without Invite shows the filter again.

## 2026-10-27 — Cookbook: brews, Food / Drinks, principle filter

Coffee, cocoa and tea are not `cook.*` recipes: brewing is one generic recipe,
`brew.kitchen` / `brew.fireplace` (crafting_1_simplemanipulations.json: any `brewable` card + a
soul), and the card's own `dist` xtrigger decides the result (packet.coffee.dawnlion ->
pot.coffee.dawnlion). So the Cookbook now records the *brewed card*: the same Harmony prefix calls
`CookBook.RecordBrew`, which reads the situation's interior spheres just before the recipe runs
and stores `brew:<card>` (hatching eggs, also brewable, excluded). The seed gained the Dawnlion
coffee and cocoa the player remembers brewing (and Poire Belle Hélène, the likely pear dessert).

The tab now works on `CookEntry` (a recipe or a brew): one list, with "Brew: <packet>" rows whose
dish is the `dist` result. Filters like the Items tab: All / Food / Drinks (the dish's sustenance
or beverage) and "Dish with at least" principle levels (sorted by the chosen principles).

## 2026-10-27 — Cookbook: give ingredients; only a hen's egg cooks

Middle click on an ingredient icon in the Cookbook gives a card into a free carried-things slot
(`StripStep.Middle`, a `MiddleClick` on the icon; `GiveToPortage`): a copy of the shown card, or
for a missing one the card itself when the requirement is a card id (honey, dough.cake), else the
plainest *ingredient* carrying that aspect (`IngredientCard`: "egg" gives a hen's egg).

Pitfall (the player noticed: "the other eggs I boiled turned into animals"): `cook.egg` asks only
for the `egg` aspect, but gull, viper and outsized eggs are also `brewable`, and in a kitchen they
match `hatch.egg.gull.kitchen` (egg + brewable, crafting_1_simplemanipulations.json) and hatch.
Only `egg.chicken` (an ingredient, not brewable) cooks. `CookCards` now leaves brewable eggs out,
so the Cookbook never suggests them; `IngredientCard` already required `ingredient`.
The recipes also *show* what is really needed: `ShownIngredient` turns an aspect requirement that
only one actual ingredient can meet into that card ("egg" -> Hen's Egg: of the four eggs only
egg.chicken is an ingredient and not brewable), for the "Missing:" line, the icon and its tooltip.

## 2026-10-26 — Items tab: double click into the carried things

Until now a double click on a row only sent the card into an open window that takes it (the
player asked for that: nothing should jump around otherwise). A checkbox on the Items tab, "Hand
placement" (a framed box with the game's check mark sprite "slot yes" when on, the explanation on hover; first a long text
chip, which looked odd), now allows more: when no open window takes the card, it goes into a
free slot of the five carried-things slots. How the game does it: `BoHTokenSender.SendToPortage`
(what the game's own double click on a table card uses) tries the open situation first, then
`~/portage1` and its linked portage spheres (`GetAlternativeSphereFor`), and makes the token
travel there. The chip is a config entry (`Items.DoubleClickToHand`, off by default) so it
survives reloads; other tabs keep the old behaviour.

## 2026-10-26 — Invite and Inks: the soul must fit the chosen desk

Bug (the player hit it inviting Arun Peel): the Invite recipe listed "any fresh soul with enough of
the principle" and only checked that a desk *has* a soul slot. But every desk's soul slot (like its
skill slot) takes only cards carrying one of the desk's principles, so the suggested soul was
refused at the first suggested desk. Now `InviteIndex.Choose` builds an `InviteOption` per free
desk: the weakest skill its skill slot takes that reaches the amount with the ink, and if a soul is
needed, the weakest soul *that desk's* soul slot takes (`SlotCheck.Takes`). The recipe shows the
desks; clicking one opens it and shows exactly its Writing Case › ink › skill › soul, with the
desk's total. The Inks tab had the same flaw in a milder form (each desk checked, but the souls of
all desks merged into one row): `InkPlan.SoulsByDesk` now keeps them per desk, and the Soul row
follows the desk last clicked.

## 2026-10-26 — Finder Memories: Weather mode

Each day's weather is a memory card (Rain: Grail 2 + Nectar 2, Clouds: Moon + Moth, Fog: Moon 3 +
Knock 3...), useful for raising skills. How the game does it (celestial_recipes_weather.json):
`daily.weather.<season>` deals one card a day from `deck.weather.<season>` (decks/
incidents_decks.json, resetonexhaustion), then `weather.enactor.*` plays it. The draw pile's order
is fixed until it runs out, so the coming days are known: the mode shows the current season's pile
(`DealersTable.GetDrawPile`, top first = tomorrow) as an icon strip, then every weather of the four
season decks with its principles and "Autumn 3/12"-style odds. Middle click gives the card
(GiveMemory, like the other modes). Earthquake and the Numa weather are left out (not in the
season decks: special, possibly unseen). The five mode chips were narrowed to fit.

## 2026-10-26 — Finder Cookbook tab: dishes cooked before

A ninth Finder tab (chips narrowed to 68 px, spacing 5, so nine still fit 656 px). It lists the
kitchen recipes the player has cooked, ready ones first: the dish with its principles, what is
missing, and one strip per recipe: kitchens › the plainest fresh soul the kitchen's soul slot
takes › each ingredient (the free cards on hand, or the ingredient's icon in dimmed red with a
tooltip). A missing ingredient that another kitchen recipe makes by card id (Cake Batter for a
honey cake, Dough for the batter) is shown as earlier "Step N" blocks, deepest first. Each step
header says what it makes: "Step 1: Mix Cake Batter → [icon] Cake Batter", the icon clickable for
the card's details.

Cheat: middle click on a dish (the row's art or a step header icon) provisions the card into the
first empty slot of the player's carried things: the five `PortageSphere`s at `~/portage1..5`
(the game's own SendToPortage starts from `~/portage1`). Then a full rescan, keeping the scroll,
so the new card counts as on hand.

How the game does it: all 94 kitchen recipes are `cook.*` in DLC_HOL_cooking.json, inheriting
`_cook` (actionid `library.kitchen.*`, reqs a soul, 60 s). Ingredients are card ids or aspects
(`egg`, `intoxicating`, `spicing`); `"ingredient": -2` means nothing else edible may go in; tools
(`bowls.kitchen`, `knife`) are reqs too.

Pitfall: the game keeps **no history** of executed recipes (no RecipeExecutions in BoH's
Character), and Player.log only covers the current and previous session. So the mod records it:
a Harmony prefix on `Situation.ExecuteCurrentRecipe` (after the alt override, before the situation
may be gone) adds every `cook.*` to `BepInEx\config\shirofaii.boh.bookfinder.cooked.txt`. On load
it also parses both Player logs ("recipe 'cook.x' has executed", opened with FileShare.ReadWrite
since the game holds the log). The first run seeds the dishes made for the first-year salons
(boiled egg, pickled mackerel, pumpkin soup, Blue Crown tea, dark stuffed marrow, cake batter,
honey cake). Rejected: listing every kitchen recipe (spoils dishes never made).

## 2026-10-24 — Finder craft mode as formulas instead of sentences

Per recipe the mode printed two or three sentences ("Skill + Instrument + Sky 10 in total (skill
gives 4)", "You have no Instrument. Below 10 the desk makes X instead"). Ascendant Harmony has six
such recipes that differ only by the skill, so the page was a wall of text. Now the recipes of a
memory are grouped by (ingredient, principle, amount) and each group is one row of icons:
`[skill icons] + [ingredient] + [principle] 10   status`. A skill that can't do it right now is
dimmed (only when another skill in the group can), a missing ingredient is red, and the status is
a few coloured words: "ready · Desk", "no Instrument", "Sky 7 of 10 at best", "Desk would make X".
What the skill gives and the easier fallback recipe moved to the skill icon's hover text; a skill
click sends it to the open window. Desk / Put in rows are shown once per group, for the group's
best recipe (game-checked first). The analysis moved out of the render loop into `CheckCraft`
(a `CraftCheck` per recipe); rendering is `AddCraftFormula`.

The mode was slow: every recipe runs `DeskCraftPlan` (with a game recipe prediction) for every
idle desk. Memories the player can get for free are now left out before any check: those of a
book on hand (`Book.Memory`), of an object in Consider (`MemoryObjects`), and the well / fire /
sea ones (`PlaceRoutes`). Crafting those is never worth a desk and a soul.

## 2026-10-23 — Finder Memories: every memory card; "Craft at a desk" mode

Pitfall: the Consider mode only took `mem.*` spawns, but many memory cards have other ids: Old
Moment (Black Dove Wine, Serpent-Milk), Occult Scrap (Yellowing Newspaper), Earth-Sign (Tame Viper),
Wild Surmise (Historywax Candle). Any element with the "memory" aspect (lessons excepted) counts now.
A fourth mode lists memories made by craft recipes (`craft.*` with a skill requirement and a
memory card among the effects, read from the compendium once) whose skill card the player holds:
per recipe "skill + ingredient + principle N" and steps Desk (idle library desks whose skill slot
takes the skill) › Skill › ingredient (by aspect) › Soul; "Persistent" is noted for
`_memory.persistent` cards (Curious Hunch, Hive's Lament...), which survive the dawn.
Pitfall: a skill often has an easier craft recipe too, and the game takes the first recipe whose
reqs are met in load order (crafting_2_keeper, then 3_scholar, then 4b_prentice). Edicts Martial +
Confounding Parable + Moon 15 makes Forbidden Epic, but short of Moon 15 the prentice recipe
(Edicts Martial + Moon 5) fires and makes Old Wound - the player got that. Each craft now shows a
verdict (best skill + soul + ingredient + memory + ink/tool against the amount) and "below N the
desk makes X instead" from the same skill's recipe with a subset of the reqs and a lower amount.
An ingredient can be a card id (`confounding.parable`), not only an aspect; both are matched.
Pitfall 2: the verdict summed the best soul, memory and ink/tool overall, but every desk slot takes
only the desk's own principles, so a green "reachable" could be false (Old Moment: Scale 10 at a desk
whose slots refused the strongest cards). Now `DeskCraftPlan` fills one desk's actual slots (skill,
then the ingredient, then each other slot's best card it takes; no readables, which would make it a
reading), the Desk row lists only desks that take skill and ingredient, best total first, with the
total in the tooltip, and the Soul and ingredient rows keep only cards those desks take.
Pitfall 3: `_legacy_crafting_4a_prenticeplus_ambittable_unfriendly.json` holds ~130 "preview"
recipes with `"craftable": false` (e.g. Ouranoscopy + Winter 10 -> Bittersweet Certainty) that never
run; the list showed them. Only `Recipe.Craftable` ones count now. And filling every slot with the
best card could wake another recipe of the same skill that the game tries first (an extra memory
is often some recipe's ingredient). The desk plan now adds extra cards only until the amount is
reached, and skips any card that would make an earlier craft recipe of that skill (compendium
order) meet its reqs (`ReqsMet`: aspects plus the cards' own ids, a soul assumed). The verdict's
card list is the set to put in, nothing more.
Then the plan is checked by the game itself: `Compendium.GetFirstMatchingRecipe` (what an unstarted
desk uses; recipes ordered by Priority, then load order) over the planned cards' aspects plus the
desk verb's. A plainest fresh soul is added if none came in (every craft needs one). Desks where the
game picks the target recipe go first; the verdict says "✓ Checked with the game" or which recipe
the game would pick instead. The "earlier recipes" heuristic now uses the same game order.
Recipes with no desk that can hold skill and ingredient show only their verdict, no empty rows.
The Soul and ingredient rows offered cards some desk takes, not the chosen one, so the player put
in a soul the desk refused. They are replaced by one "Put in" row: exactly the checked cards of the
desk opened from the Desk row (else the best one); opening another desk redraws it for that desk.
The game font has no ✓ glyph (it drew an empty box): plain words now.

---

## 2026-10-23 — Finder Invite: list above, recipe below

The invite list with every guest's step rows inline grew into one long scroll. Now it uses the
Cures layout: the guests in the main scroll area (click picks, double click shows the card), and
the picked guest's recipe (Desk, Case, Ink, Skill, Soul rows) in the second area (`_cureFrame`,
shown in Invite mode, hidden on People without it). The first ready guest is picked by default;
picking redraws only the lower area.

---

## 2026-10-23 — Finder Cures: "Souls" mode

A soul with a malady is its own card (mcho, mhea, mwis2...: "malady": 1 and
`malady.cure.<principle>`: N; the `z*` cards are only fatigued). It is cured in one of the player's
own beds (`library.bed.rest.*`, crafting_0_rest.json "Cure my Malady"): the maladied card in the
"Restore" slot, a drink in "With" (beverage, covenant or restorative), and the principle summed
over the bed to N; the bed's Soul, Skill and Memory slots each take only the bed's own principles.
The Cures tab got two modes, Books (as before) and Souls: per sick soul a header with the cure
principle and amount, and steps Bed (beds whose Soul/Skill/Memory slot requires that principle) ›
Sick › Drink › Skill › Soul › Memory, strongest first, tooltips with the principle each gives.
A malady soul can also carry `ability.exposed.<contamination>` (it may pass the contamination on).
Books got the same treatment: the picked book's cure is now a verdict line (can the best skill +
soul + memory, plus the book's own principle, reach the amount, or how much is short) and steps
Desk (idle `library.*` desks taking the book, with skill and soul slots) › Book › Skill (of the
cure's class) › Soul › Memory, tooltips "Winter 3 of 7". AddStepStrip takes a parent, so the
strips go into the second scroll area.
Pitfall: the desks have a fifth slot, "With" (i2: an ink or a tool, not fatigued), whose principles
count too; the first version left it out and called a reachable cure short. It is now a step row
and part of the verdict (an ink is used up: the cure recipe fatigues everything in the desk).

---

## 2026-10-23 — SmoothZoom: E zooms in, Q out

The keyboard zoom is the game's input actions `kbzoomin`/`kbzoomout` (UIController). SmoothZoom
finds the PlayerInput once it exists (polled once a second until then, then never again) and
puts binding overrides on their `<Keyboard>/q` and `<Keyboard>/e` bindings: E in, Q out. The
overrides are removed in OnDestroy. Checked live: "Zoom keys: E in, Q out" in the log.

---

## 2026-10-22 — CopyBreaks: red "Book text" on a first reading

A first reading gives a lesson; a reread only a memory. The player copies texts for the vault only
the first time, and the gold button blended into the window. Now "Book text" gets the red border
(shared with "Conversations": `CopyButtonFx.Red/RedLit`, `SetOutline`) when the finished recipe is
`study.mystery.<p>.mastering.yes`, or a card with the "lesson" aspect is among the window's cards;
otherwise gold, as before.

---

## 2026-10-22 — SlotFit: book language slots

A foreign-language book in a desk adds its own slot (tomes.json: `"slots":[{"id":"language",
"required":{"s.fucine":1}}]`), with no Essential. SlotFit matched the skills tray only against
slots whose Essential is "skill", so the language skill was darkened although it fits. Slots with
id "language", or with no Essential and only `s.*` Required keys, now count for the skills tray.

---

## 2026-10-22 — Finder Books: language icon

Book rows show the book's language icon right of the mystery badge (named `Aspect_w.<lang>`, so a
middle click pins it). A click sends the language skill card (`s.<lang>`) into the open window via
SendOrFocus, like clicking a book; Latin, Greek, Aramaic, Phrygian and Sanskrit have no skill card,
so their icon only explains that in the header.
The row's right side is now three fixed-width cells (contaminations 58 px, right-aligned; mystery
badge 62; language 28) before the mastered mark, so the icons line up in columns from row to row.

---

## 2026-10-22 — Finder Memories: "Well, fire, sea" mode

Apart from books, objects, salons and one-off village visits, memories come from places
(renounce.json, other_activities.json): `well.renounce` (any thing, lost) gives Memory: Sound,
`well.renounce.penny` Satisfaction, `fire.renounce` (a non-brewable thing) Sight, `sea.swimming`
(a soul with Heart and Scale, `world.sea.*`) Salt, plus Solace with a basket of towels
(`sea.swimming.dry`). A third Memories mode lists these routes with steps Place › Penny/Spare or
Soul › Towels. The game's leftovers (dregs, scraps, residue, dust) have no "thing" aspect and a
1-second lifetime, so they can't be renounced; "spare" means one of three or more cards of a kind
that is not a book, skill, soul, memory, journal, beast, furniture, wall art, food, drink, ink or
tool, plainest first. Places in locked rooms are left out (`WorldIndex.FindRoom`).

---

## 2026-10-22 — Finder Memories: middle click gives the memory

At the player's wish (the reread grind slowed the game down): a middle click on a memory row's
picture provisions that memory into `hand.memories` (found by path, only on the click) and rescans,
so the row moves to "held". The picture gets `MiddleClick`, which passes other clicks on to the
row's button. Its object is named "Art", not `Aspect_<id>`, so AspectPin's middle click ignores it.

---

## 2026-10-22 — DevTools: rooms-request (room background map)

Room backgrounds are sprites `room_<period>_<name>` in the addressables atlas
(`StreamingAssets/aa/.../highqualityatlas_*.bundle`), not in resources.assets, and their names don't
follow the terrain ids (`ballroomduellinghall` shows `room_curia_duelling`; gardens have one per
season). `BepInEx\cache\rooms-request` makes DevTools write `dumps\rooms.txt`: each TerrainFeature
token's id, label, unlocked (not shrouded, not sealed) and the `room_*` sprites under it. The vault's
`vault_rooms.py` uses it to put the empty room picture into Places notes of unlocked rooms.

---

## 2026-10-22 — Finder Invite: souls count, one step per row

`write.summon.<visitor>` sums the principle over the whole desk, so the desk's soul slot counts
too (Azita's Edge 7 needs a +Mettle soul on top of Ragged Crossroads and the ink). The planner
said "No skill reaches Edge 7" because it only tried ink + skill. Now a skill qualifies if
ink + skill + the best fresh soul reaches the amount; skills that are enough alone come first,
the others carry "plus a soul with <P> N" in their tooltip, and a Soul row lists the souls that
top up the strongest skill (weakest first, sparing the strong ones; desks without a soul slot are
dropped for those skills). The steps are now one row each (Desk, Case, Ink, Skill, Soul), the
options side by side, like the Memories tab.

---

## 2026-10-22 — Attention mod; DevTools spawn request

A mastery reading with too little of the principle becomes "<P>: a Semblance?" and draws three
Opportunities (`deckeffects d.challenges.opportunities: 3`; 1_consider_books
`study.mastering.opportunity.*`): each is a linked recipe with one slot (Soul, Comfort, Thing,
Wall Art, Memory, Weather) and a 10-second warmup, or a free mood (Duende, Ubi Sunt). Missing them
ends in `study.mastering.no`, whose `fatiguing` turns what lay in the desk into echoes. The player
lost a Wormwood Dream (Moon 6, Winter 6; Peel gives it for Collected Hush House Lectures) that way.
Attention polls the registered situations once a second: Ongoing, a desk or Consider (config), and
an empty Threshold sphere active in the Ongoing state (OngoingState.IsActiveInThisState) - that is
an open recipe slot. A copy of the game's go-to-completed button (its onClick replaced, its count
badge and hint removed, tinted violet) shows the count and cycles through them; a newly appearing
one pauses the game via `UIController.SpeedControlEvent` (GameSpeed.Paused). Checked live: with
several desks reading normally the list stays empty.
DevTools also takes `BepInEx\cache\spawn-request` (element ids, one per line) and provisions them
into `hand.memories`; used once to give back the lost Wormwood Dream at the player's request.

---

## 2026-10-22 — Finder: middle click on filter aspects pins; Places opens on Desks

The principle chips in the filters (Mystery; "at least" level chips in Memories and Items) now
name their icon `Aspect_<id>`, like the result rows, so AspectPin's middle click pins that aspect.
The level chips ignore the middle button (it used to count as +1). Entering the Places tab selects
the Desks kind: that is what the tab is mostly opened for.

---

## 2026-10-22 — Finder Memories: "Consider an object" mode

Besides rereading books, memories come from examining objects in the Consider verb: every
`considerable` card has a `scrutiny` xtrigger, and most spawn a `mem.*` card (Paradise Palm and the
other potted furniture give Memory: Scent). The recipe that runs decides whether the object
survives (`2b_consider_generic`): beverage, candle, device (not inert), flower, fruit, leaf, liquid,
mark, material, remains, root, sustenance are used up (each links to
`examine.considerable.consume`); beasts ("Inspect") and the rest ("Investigate") stay. Any soul
fills the "Effort" slot. The Memories tab got two modes, "Rereading books" and "Consider an
object" (a separate mode, at the player's wish: Consider is one verb, one memory at a time, so it's
the occasional route). Object mode shows the steps Consider › Object (those that stay first, the
tooltip says "stays" or "used up") › Soul (plainest first, sparing the memory's principles).

---

## 2026-10-22 — DevTools: Ctrl+Shift+S saves now

The vault's tree plan reads AUTOSAVE.json, and the game autosaves only every 20 minutes
(`Autosaver.AUTOSAVE_INTERVAL` = 1200 s). Commits on the Tree of Wisdoms leave no trace in
Player.log (learning and level-ups do: `u.skill.*`), so between autosaves they were invisible.
Ctrl+Shift+S calls the game's own `GameGateway.TryDefaultSave()` (same file as the autosave).
Tools can also ask for a save by creating `BepInEx\cache\save-request` (checked every 2 s, then
deleted).
Pitfall (game bug): `TryDefaultSave` sets `DefaultSaveInProgress`, and when
`GameInSaveableState()` is false (`Wisdoms._saveAllowed`, false while the Tree of Wisdoms is open)
it returns without clearing it, so every later save, including the autosave and the save on quit,
is refused until a restart. The game's callers check `GameInSaveableState()` first; the first
version of this hotkey didn't and got the flag stuck. Now it checks first, and DevTools clears a
stuck flag on load and after each save.

---

## 2026-10-22 — Salon transcript wiped after collecting results

The spring picnic's transcript file held only the room's description. When the player collects a
finished salon, its notes sphere goes back to one note (the verb's own label and text). That is
fewer than `maxnotes`, so the "window holds the whole conversation" rule replaced the transcript
with it. Now a notes sphere with fewer than 2 notes never touches the transcript, and when a
different salon starts in the same room (neither transcript a prefix of the other) the old one is
kept as `<id>_<yyyyMMdd-HHmmss>.txt` first. The lost picnic was rebuilt from Player.log: the game
logs each executed recipe id (`slnev.<a>.<b>`), and the texts are those recipes' startdescription.

---

## 2026-10-22 — Finder Books: "For guest"

A row of chips for visitors in the house (not their departing `embarking_` card) at the top of the
Books filters. Picking one keeps only books that guest can be given in Talk ("Give or Take"),
following the recipes: `talk.visitor.consultation.<p>` needs `mystery.<p>` >= the guest's
`interest.<p>` (the interest grows by 1 after each satisfied consultation, so it's read live from the
card's aspects); `talk.visitor.intercept.tx.<guest>` refuses a book the guest has consulted (the
book then carries the mutation `tx.<guest>`); `talk.visitor.cantread.<lang>` refuses `w.<lang>`
without `u.<lang>`. Contaminated books are left out at the player's wish (the game doesn't block
them). The guest filter replaces the read-state filter; the others still apply. Chips are rebuilt
on rescan, and a guest who left clears the pick.
Picking a guest also resets the filters that fight its rules: Status to All, no Language chip,
Contamination to Clean (when the contamination row is shown).
Pitfall: a separate caption line plus the chip row pushed the Books tab past the window (the results
frame had `minHeight` 200 and wouldn't shrink). The caption now sits inside the chip row, and the
results' `minHeight` is 120.

---

## 2026-10-22 — Finder: Sweet Bones helper deck

The player found "Seek Unusual Help" at the Sweet Bones a gamble. It isn't random per draw:
`sweetbones.employables` (Engineer, Musician, Painter, Poet x2; Fugitive, Nun x1) is a draw pile
shuffled once when created or exhausted (`resetonexhaustion`, `Dealer.Shuffle` orders by
`Random.value`), and `Dealer.Deal` takes `GetElementTokens()[0]`. So the order is known in advance.
The People tab now shows the pile as numbered cards, next to come first; dragging a card
(`DeckCardDrag`) moves it: `SweetBonesDeck.Move` rewrites the order of the element tokens inside
the private `Sphere._allTokens` and marks `_elementsCacheDirty`, so the next deal and the save follow
it. Cards show their faces: a face-down mode for helpers not yet met (deck cover as card back,
met = drawn this round or remembered in a cache file, right click to reveal) was built and then
dropped - the player had met the whole deck, and the game keeps no record for employables anyway.
The deck lives in a fixed area (`_deckArea`) under the People filters, not in the scrolling list:
scrolled with the assistants it looked out of place. A drop redraws only the deck. Hidden in
Invite mode. Below it, "Already drawn": the game has no discard pile (a dealt card just leaves the
draw pile), so `SweetBonesDeck.Drawn()` is the spec minus the pile's cards (minus unique elements
already manifested, as `Dealer.Shuffle` skips them) - in spec order, the draw order isn't kept.
A drawn card dragged up into the deck row is put back (`SweetBonesDeck.Insert`: a fresh token via
`ProvisionElementToken`, as the shuffle does, then moved to the drop place); the extra copy lasts
until the next reshuffle. Checked first: no achievement or recipe counts deals or shuffles of this
deck (achievements come only from recipes and elements, and none touch employables). A click
without dragging opens the card in the detail window (`ShowDetails` for a pile card,
`ShowAspectDetails` for a drawn one, which has no token). The Assistants list also gets one row per
kind of deck helper (`FromDeck`), as the `assistance.*` card it turns into when hired (the
`recruiting` xtrigger), because the employable card itself carries only the fee.

## 2026-10-20 — CopyBreaks: "Salon text"

The player wants to keep what guests talk about at salons (exchanges reveal facts about people).
A salon window (`SalonWindow`, not `AureateWindow`) writes each step as a note in its notes sphere:
the opening, every exchange (`slnev.<guest>.<guest>`, e.g. Timothy and Fraser on Illopoly), the
ending. The game's own copy (`SalonWindow.CopyVisibleNoteText`) takes only the page on screen.
"Salon text" copies all notes in order (title + text each, salon name first), rich-text tags off,
through `ClipboardTextCopier` like "Book text". Placed left of the Start button (private
`startButton`, its parent is the bar); shown only once the salon is over (state Complete), like
"Book text" after a reading; the transcript is recorded all along. Postfixes on the private
`DisplayButtonState` and on `SituationSphereContentsUpdated` (notes arrive as contents change).
`CopyButtonFx` now takes its caption from the label instead of a hard-coded "Book text".
Checked: it doesn't. Salon notes are additive and the salon verbs have `"maxnotes": 9`
(`Situation.ReceiveNote` retires the oldest beyond that); the window shows them as one scrolling
list - the game's own design, not a mod effect (the player first took the stacking for breakage).
So the button keeps a transcript per situation id. First version: static, by token - two
hot reloads during the first real salon wiped it and the copy lacked its first exchanges
(recovered from Player.log, where every executed salon recipe is logged). Now the transcript is a
file (`BepInEx\cache\salon-transcripts\<id>.txt`) and stateless to rebuild: while the window shows
fewer notes than the verb's MaxNotes, it shows everything since the salon began, so the transcript
becomes exactly that (which also restarts it for the next salon in the same room - the situation id
doesn't change); once full, the shown notes are aligned against the transcript's tail and only the
new ones appended.

## 2026-10-20 — Finder: People › Invite

People got an "Invite" chip: every visitor whose address is in the Writing Case (mutation
`address.<visitor>`, noted from a calling card) with what the letter needs and click-through steps,
as on the Inks tab: open a desk, then Writing Case, ink and skill go in with one click each; start
the letter; the Post Office row flies there to post it (1 penny). `InviteIndex` reads
`write.summon.<visitor>` (actionid `*.consider`; reqs address + one principle at N, Hokobald needs
an item instead - shown as "Needs a special item", unnamed). Ink: inks are liquids and the letter's
fatiguing turns them into dregs, so the cheapest that lets ink + skill reach N is picked (non-liquid
"ink" such as a typewriter first, then by total principles; Perinculate is refused by the recipe).
Skill: the weakest that suffices, at an idle `.consider` desk whose slots take the Writing Case, the
ink and the skill (`SphereSpec.CheckPayloadAllowedHere`). Visitors here, with a letter written
(`letter.summon.<visitor>` card) or on their way (a registered `visitor.<visitor>` situation, the
arrival created at the Post Office) say so instead. Only noted addresses are listed: nothing unseen.
Compact layout (also on the Inks tab): the steps became one row of clickable icons per guest
(`AddStepStrip`: "Desk [icons] › Case › Ink › Skill [icons] › Post"), names in the window header on
hover; at most 3 icons a group so the row fits 700 px. Guests already here, written to or on their
way go to a short tinted section at the end, so no second invitation gets written.
The Memories tab uses the same strip for the books under each memory: one row of book icons, a
click puts the book into the open desk or, when no window takes it, flies to it (`SendOrFocus`).
Then each memory got its recipe as three icon rows (Desk, Book, Soul; one row each so many books fit): rereading a mastered book
(`study.mystery.<m>.mastered`) needs only the book and any soul the desk takes, no principle. Desks:
idle `.consider` desks with a Soul slot and a slot that takes one of the books (films and records
are refused by ordinary desks' Papers slot); souls: free ones the Soul slot of a listed desk takes.
Memories already on their way or held show no strip. Slot helpers moved to `SlotCheck`.
Souls row: for the desk open right now (the one last opened from the row, else any open one;
`Situation.IsOpen` is set synchronously in `OpenAt`), plainest first by the soul's `rank` aspect
(Health 12, +Health 22, ...), so rereading spends un-evolved souls - after souls sharing no
principle with the memory (the player's point: a Moon memory serves Moon tasks, and so does a Moon
soul; keep those for that work). Clicking a desk redraws the
list without jumping to the top (`RefreshKeepingScroll` restores the content position a frame later).

## 2026-10-20 — Finder: Souls tab

Evolving souls lived inside Places (Kind = Souls, then soul chips, verdicts and skill rows), though
it is a task of its own. It is now a Souls tab: soul chips (radio) on top, the soul places below
with the verdict for the picked soul and, where it can be done now, the skill to use. It keeps its
own `DeskFilter` (`Kind = Soul`), so Places filters and the soul pick don't interfere; Places lost
the Souls kind chip but still lists soul places under All. Eight tabs: the window grew to 700 px,
tabs are 74 px with 15 pt labels.

## 2026-10-20 — Finder: Inks tab

Letters to visitors burn their ink (inks inherit `_liquid`, whose `fatiguing` xtrigger turns them
into `dregs`), so the player brews cheap Prentice inks instead of spending Ashartine and the like.
The Inks tab lists inks they can brew, each as clickable steps where a single click does the step:
"1 · Open a desk" rows fly there and open it (`OpenPlace`), "2 · Skill" and "3 · Soul" rows put the
card into the open window (`TokenDestinationSender.SendToOpenSituation`). Then the player starts it.
`InkIndex`: ink recipes are ambittable craft recipes (`craft.<skill>_<ink>_<principle>`, actionid
`library.*`) whose effect carries the `ink` aspect; reqs = ability + `s.<skill>` + principle N, the
Prentice ones with no material. A desk qualifies if it is idle and its Skill and Soul slot specs
accept the cards (`SphereSpec.CheckPayloadAllowedHere`); a soul qualifies if skill + soul reach N.
Spoilers follow the game's own rule: the window shows an ambittable recipe in full when it is
executable with the cards present, and as "undiscovered" otherwise unless
`Character.IsAmbittableRecipeUnlocked` (`AmbittableDisplayLocked`). So a plan is listed only when
unlocked, or doable right now with the player's own skill and a soul - what the game would reveal
once those cards are put down. Tabs are 78 px wide (font 16) to fit seven.
Brewing: a recipe running in a desk (`Situation.GetCurrentRecipe().Id` equals the ink recipe, desk
working) leaves the ready list and shows at the end under "Brewing now" (wine-red row, violet
"Brewing at Desk: Eva"; click flies there). Other recipes of the same skill drop out meanwhile, as
the skill card is busy. The tab rescans when a skill or soul card leaves the sphere it was scanned
in (the 2 s check shared with Memories, `CheckCardsMoved`).
Pitfall: the first version treated a card as busy as soon as it was inside any desk
(`HostToken != null`), so laying the skill into the desk to start a brew made that very ink vanish.
Only a card in a desk whose recipe is running counts as busy now; cards in an idle desk's slots or
in a finished desk's results are free.

## 2026-10-19 — Finder: auction lots aren't the player's

A book on offer at Oriflamme's (bid already paid) showed up among the books to read. Offers and
other arrivals are situations under `~/arrivalverbs` (`~/arrivalverbs!oriflammes.offer_<n>`); the
lot waits in their SituationStorage until the deal completes, and SituationStorage counted as
reachable (it is, for a desk's running recipe). Now SituationStorage under `~/arrivalverbs` is
skipped as "pending offer". Their slots (the coin bid) and Output (a won lot to collect) still
count.

## 2026-10-19 — Finder: which skill evolves the soul here

The Souls verdict said whether a place can evolve the picked soul, but the player still tried every
skill in the slot to find the one. Now each soul place lists, under its row, the player's skills
attuned to that soul (`a.<soul>`), as on the Cures tab: usable ones first with a green check ("Use
this: committed to Bosk, fits the Skill slot"), the rest red with the reason - committed to another
Wisdom than the ones this place evolves the soul through, or no principle shared with the Skill
slot (`Verb.Hints`). `EvolvePlanner.SkillOptions`, next to `Verdict` and using the same rules.
Double click sends the skill to the open window. Attunement is spent by one evolution, so a used
skill drops off the list by itself. Revised: shown only under places where the evolution can be done now (green verdict), and
only the usable skills; under red places they were noise.

## 2026-10-19 — Finder: memories being made

With several desks reading at once the player forgot which memory was already on its way. On the
Memories tab, a memory whose book is inside a working situation (the book sits in the desk's
SituationStorage while the recipe runs: `ItemEntry.HostToken` -> `Situation`, state not
Unstarted/Inchoate/Complete) is faded, moved to the end and says in amber "Being made: at Desk: Eva
· 0:42 left"; the book row under it says the same.
Revised the same day: a faint grey fade read as "still available" at a glance. Memories are unique
(all 21 mem.* have `"unique": true`; when a second copy manifests the game retires the first,
`HornedAxe.EnforceUniquenessIfStackIsUnique`), so a memory already held is just as wasted as one on
its way. Both now go into a separate section at the end ("Already on the way or held · another copy
would only replace it"), title struck through, wine-red row background, amber "Being made at Desk:
Eva · 0:42 left" or green "You have it · <where>". The header count says how many are left to make.
`Fade(row)` stays for busy places only. Time left was dropped from the memory notes: it only changed on a redraw.
A memory card already put into a running recipe doesn't count as held: uniqueness is enforced only
in table, hand, portage, output spheres (`EnforceUniqueStacksInThisSphere`); entering a slot or the
situation storage deregisters the card, so a new copy elsewhere doesn't retire it, and the recipe's
`fatiguing` turns it into `echo` ("This memory has become part of something else") at the end.
Pitfall: a copy taken back out of an idle desk's slot re-enters an enforcing sphere and retires the
other copy, so a memory in an idle slot still counts as held. To follow
books put into a desk while the tab is open, every 2 s the mastered books' current sphere is
compared with the one stored at scan time (`ItemEntry.Sphere`); a change triggers a rescan. Cheap:
no world walk, and a running recipe doesn't move the book, so it doesn't rescan repeatedly.

## 2026-10-19 — Finder: busy places marked

Places rows said "Working" in small grey text, easy to miss when looking for a free desk. Now a
busy row is faded (row background 0.2 and a CanvasGroup at 0.55) and its state reads in amber
"Working: <recipe label> · m:ss left" (`Situation.GetCurrentRecipe().Label`, `TimeRemaining`);
a finished one reads green "Done: collect". The list is ordered idle, done, working. `DeskEntry.State`
is now read live from the situation instead of copied at scan time, so a desk that starts or
finishes while the window is open shows it on the next redraw.

## 2026-10-18 — AspectPin works on Finder icons

Middle click pins an aspect only on the game's `ElementFrame`s; the Finder's icons are plain
Images (and were raycast-transparent), so pinning a book's mystery from its row did nothing. The
mods share no types (Shared is compiled into each), so the contract is the GameObject name: Finder
result-row icons (`AspectIcon`: mystery badges, principles of items, memories, people and places,
contamination icons) are named `Aspect_<id>` and are raycast targets; AspectPin, finding no
`ElementFrame` under the pointer, reads the id from that name and looks the element up (an unknown
id comes back as NullElement, so the id is compared). Left clicks still reach the row: pointer
events bubble to the nearest handler up the hierarchy. Filter chips keep their own clicks and
names (`Principle_`, `Level_`), so they aren't pinned.

## 2026-10-18 — Finder: Cures tab

Curing a book meant checking each skill by hand: a skill can work against the contamination's class
and still add none of the principle the cure needs. The Cures tab groups infected books by
contamination; each group header says what the cure needs, then lists the books and the player's
skills of that class, those carrying the cure's principle first (green check, "Adds Winter 3").
The game's recipes (`0_consider_decontaminations.json`, read from the compendium once in
`CureIndex`) all look alike: `remove.contamination.<x>` at `library.*` needs a soul (not fatigued),
the book with `contamination.<x>`, a skill with `effective.contamination.<class>` (Infestation,
Theoplasma, Curse, Corruption) and one principle at 7 (witchworms: infestation + Winter). Skills of
a class rarely carry that principle, so most of the 7 comes from the soul, a memory and the desk's
With slot; and since the book takes the Papers slot, only desks can do it. Contamination chips (radio: one at a time)
filter the books (rebuilt with the book chips on rescan). Tabs got narrower (92) to fit six.
Layout: two independent scroll areas. One list (books, then skills below) put the skills under
the scroll fold once there were many infected books, so the top area lists infected books and a
click picks one; the bottom area (fixed 330 px, `BuildScrollArea` shared with the main results)
shows the cure and skills for the picked book, and is redrawn alone so the book list keeps its
scroll position.
Freshness: the Finder scanned only on opening, so a book cured with the window open stayed listed.
Now every tab switch rescans, and on the Cures tab a 2 s check of just the infected books (lost
contamination, or gone) triggers a rescan; a full world scan on a timer was rejected (CLAUDE.md:
nothing heavy every second).

## 2026-10-17 — NextClear

The player clears many finished desks in a row: "Go to the next completed" button, then C
(collect all), again and again. Windows remember where they were dragged (`WindowPositioner`
keeps `draggedToPosition`), and one left over the button blocks the next click. Every window is
placed through `WindowPositioner.GetBoundCorrectedWorldPos` (in `Show` for the target, again at the
end of `DoShowAnim`); a postfix there moves the window straight down until its top is 8 px below
the button (`GlobalSituationCompletedWatcher.buttonGoTo`, found once), but its bottom not lower
than 10 px above the screen edge. The remembered position is not changed, only this showing.
Corners and screen/world conversion use the positioner's own private methods (`GetCornerPos`,
`GetScreenPosFromWorld`, `GetWorldPosFromScreen`). Applies to every window, not only ones opened
by the button: any window on the button blocks it. Rejected: moving sideways (the player asked
for vertical), patching the button's click (the window position is decided later, in `Show`).

## 2026-10-17 — SlotFit

Slots limit which souls, skills and memories they take (a desk's Soul slot: `essential ability` plus
`required` = the desk's principles, any one; a skill's own slots in Consider: Effort/Lesson/Memory
with the skill's two principles). SlotFit darkens tray cards that fit none of the open window's
slots of their kind, for any situation, not only desks. The kind is the slot spec's Essential key:
`ability` -> `~/hand.abilities`, `skill` -> `~/hand.skills`, `memory`/`lesson` -> `~/hand.memories`;
slots with Essential of another kind (or none, like Devotion's `required ability`) are ignored, and
a tray with no slot of its kind is left alone. The test is the game's own
`Sphere.GetMatchForTokenPayload` (Forbidden, Essential, then any one Required). Slots are
`Situation.GetSpheresActiveForCurrentState()` of category Threshold, so Consider's Effort slot counts
only after a skill is placed, and a running recipe has none. The open window is the registered
situation with `IsOpen`. Polled every 0.25 s (slots appear as cards are placed, no single event);
skipped when nothing is open and nothing is dark. Overlay/fade as in TreeDim, own names.

## 2026-10-17 — LessonFit

While a Lesson card is shown in the detail window (top right, after clicking it), LessonFit
darkens the skills in `~/hand.skills` that the lesson can't upgrade. Rules from
`understanding_2_upskill.json`: a skill needs at least one Lesson "from this Skill, or from another
which shares a Power aspect", so a skill fits when it shares any principle with the lesson's element
(lessons carry two principles plus `lesson`); a skill at level 9 (aspect `skill` = 9) can't go
higher and is darkened too. The detail window (`AureateMasterDetailWindow`, reached through
`AureateNotifier.masterDetailWindow`) keeps the shown item in `_currentDetailable` and clears it in
`HideAll`; clicking a card calls `ShowCardElementDetails` -> `ShowDetailable(stack)`. Polling that
field every 0.1 s was chosen over patching every Show*/Hide path. Overlay and fade are TreeDim's,
under its own names (`LessonFitOverlay`, `LessonDimFade`), so both can be active at once.

## 2026-10-17 — TreeDim

With many skills the player lost track in the Tree of Wisdoms of which ones were still to place.
TreeDim darkens committed skills (aspect `wisdom.committed`, set by the commit recipes) in the
skills tray (`~/hand.skills`) while the tree is open. The tree is the `Wisdoms` sphere: postfixes
on `Unshroud` (open) and `Shroud` (close); on load its private `_shrouded` gives the current state.
Darkening is a black 62% overlay image (no raycasts) on each committed token, not the token's
CanvasGroup alpha, which `Token` sets itself (0/1 for hide/show). While open the tray is rechecked
every 0.5 s, so a skill committed right there darkens at once; closing removes all overlays.
Overlays fade in and out over 0.35 s (`DimFade`, unscaled time: the tree pauses the game).

## 2026-10-17 — Finder opens on F

At the player's request the Finder opens on plain F as well as Ctrl+F. F is free in the game's
default bindings (the InputActionAsset JSON in `Keybindings.cs`: N, M, Space, S, C, Tab, 1-4, Q, E,
arrows, B, F1-F4, F11, Backspace, Esc) and in the player's config.ini overrides (WASD, X). Plain F is
ignored while a text field has the focus (same test as `UIController.IsEditingText`), so typing
"f" in the Finder's own search or any game field stays a letter; Ctrl+F still toggles from there.

## 2026-10-17 — Finder: double click sends an item to the open window

Double-clicking a card on the table sends it to a fitting slot of the open window; the player
wanted the same from Finder rows (books, items) without dragging. The game's card double click
is `BoHTokenSender.SendPhysicalTokenToValidDestination` → `SendToPortage`: first
`TokenDestinationSender.SendToOpenSituation` (a valid threshold of the open situation, with the
travel animation), else the portage, i.e. the hand. Finder calls only `SendToOpenSituation`, so
with no open window or no fitting slot nothing moves (the status line says so) - the player
explicitly did not want items put in hand. The row's single click (fly the camera there) now
waits 0.3 s (`SingleOrDoubleClick`), so a double click doesn't move the camera first.
Works on Books and Items rows, the books under each memory in Memories, and people who are here
(People: a visitor or assistant into a Talk or salon slot).
On Places rows a double click flies there and opens the place's window (`Situation.OpenAt` at
its token's location); the Finder stays open, so books can then be double-clicked into it.

## 2026-10-17 — CopyBreaks: "Book text" for a finished book

For lore notes the player copied a read book in three clicks (description, "I'm Reading...",
"I've Read..."). Now a desk window that shows a finished recipe with a mastered readable gets a
"Book text" button at the left of `Footer/ButtonsBar` (Collect is on the right). The texts come
from the element, not the notes: `Label`, `Desc`, `Xexts["reading.<m>.intro"]` and
`Xexts["reading.<m>"]` for the mystery in its `mastery.<m>` aspect; rich-text tags are stripped.
Copied through `ClipboardTextCopier.CopyTextToClipboard(string)`, so the game's "copied"
notification shows and the CopyBreaks line breaks apply. Hook: postfix on the private
`AureateWindow.DisplayButtonState` (OrderPreview patches it too; postfixes stack); already open
windows are filled once on load via `_payload`. Shown only when not Unstarted and not running,
so it never overlaps OrderPreview's result icons in the same spot.
Feedback is drawn by hand (`CopyButtonFx`): hover lights border and label, a press darkens the
plate (it used to shrink the button, which the player found jittery; the game's own buttons only
change colour), a click shows "Copied!" for a second. A Button ColorTint over the white background is capped at 1
and showed nothing.

## 2026-10-17 — QuietMenu: no "Coroutine couldn't be started" for the news slot

Player.log showed "Coroutine couldn't be started because the game object 'ProemSlot' is
inactive!" right after QuietMenu hid the news slot. `ProemsRemoteConfig` keeps a list of
subscribed `ProemSlot`s and refills them (`ProemSlot.Populate` → fader + navigation animation,
a coroutine) when the remote news arrives or cycles; on a disabled object Unity refuses.
Harmless, but noise: now the slot is unsubscribed (`UnsubscribeProemSlot`) before it is
disabled, and subscribed again when the plugin restores the menu. The rest of that log was
fine: "Failed Allocations" is Unity's memory report at exit, "Can't peek" is AltPeek's debug.

## 2026-10-16 — Finder: phonographs and projector count as Desks

Records and films can't be read at a desk: only Governor's Phonograph and Thirza's Phonograph
(slot "Record", `record.phonograph`) and the Projector (slot "Film", `film`) take them; three
verbs in all the game data. They were classified as House and got lost in a big house. Rejected
a kind of their own (three places); they are Desks now (places to read), and their slot already
shows in the row's extras ("Record: ...", "Film: ...").

## 2026-10-16 — RoomHints: requirements over the unlock icon

The requirements column sat to the right of the room's unlock icon. Blockaded Stairs is a tall,
narrow room with 9 requirements, and its column spilled over the neighbouring rooms. Now the
column is centred on the room, covering the icon; it doesn't block raycasts, so clicking
still opens the room window. Its size rule (fit all rows into the room's height) is unchanged.

## 2026-10-16 — Finder: "Known" languages chip

The Books tab's language row starts with "Known": one click picks every language the player
can read now (again to clear). How the game gates reading: `study.<lang>.none` recipes fire
on `w.<lang>` without the skill `s.<lang>` (e.g. `w.vak` + no `s.vak`). Latin, Greek,
Aramaic, Phrygian and Sanskrit have no skill element at all, so the Librarian reads them from
the start. Known = native + languages with no `s.<lang>` element + languages whose skill card
is among the scanned items (skills sit in `~/hand.skills`, category World, already scanned).
Pitfall: `Compendium.GetEntityById` returns a NullElement (id `NULL_ELEMENT_ID`) for an unknown
id, not null, so the first version treated Latin etc. as needing a skill; compare the id.

## 2026-10-16 — OrderPreview: result icon in situation windows

Cooking Vegetable Stew, the window only named the dish in its note text. Now a situation
window shows the result icon(s) of its current recipe in the bottom right corner of its
`Content`, on the same dark badge as the Craftable list (48 px), clickable for details. Hook:
postfix on the private `AureateWindow.DisplayButtonState(Situation)`, which runs on every
state change and slot change, so it sees each new prediction and the start. Shown only for
`Craftable && !HintOnly` recipes, while Unstarted (about to start) or running
(`TimeRemaining > 0`): crafts are what the player makes on purpose; events and their outcomes
stay hidden. The holder (`WindowProducts`) rebuilds only when the recipe id changes. On load,
already open windows are filled once (a running recipe calls nothing until its state changes),
via the window's private `_payload`. Window layout from the UI dump: Head / Content / Footer;
notes sit in the right half of Content.
Moved the same day at the player's request to the left end of `Footer/ButtonsBar` (the Start
button's bar, empty on the left), 32 px icons (40 touched the border): in Content it could cover notes or slots.

## 2026-10-15 — CopyBreaks

Clicking a description copies its label and text; the player pastes them one after another
into a translator and had to add two line breaks by hand each time. Every copy goes through
`ClipboardTextCopier.CopyTextToClipboard(string)` (the IManifestable/IDetailable/label
overloads format `label\n\ndescription` and call it), which sets `GUIUtility.systemCopyBuffer`
and then shows the "copied" notification. A postfix on that overload appends two line breaks
to the buffer (skipped if it already ends with them). First plain `\n\n`: the clipboard held
both (checked with Get-Clipboard), but the translator kept only one on paste. Now trailing
breaks are trimmed and `\r\n\r\n` (Windows breaks) appended. Some apps then kept both, but
Google Translate still dropped all trailing breaks but one; so the final form is one `\r\n`
before the text and one after (a leading break survives there), giving the same blank line
between consecutive pastes.

## 2026-10-15 — Finder: evolve verdict per soul place

The player had two Health cards and two Health-attuned skills, yet no place would take them,
and the Souls filters (Evolve via / Soul) couldn't say why. `evolve.<wisdom>.<soul>` needs, at
once: the place's `e.<wisdom>`; a skill with `wisdom.committed`, `w.<wisdom>` and `a.<soul>`;
that skill fitting the place's Skill slot, which takes only cards with one of the place's
principles (`Verb.Hints`); the soul fitting the Soul slot likewise; and two identical cards of
one tier (xhea and xhea2 don't pair). The trap was the skill slot: Inks of Containment was
attuned to Health and committed to Preservation, but has only Winter/Moon, while both
Preservation places (Barber's Chair: Heart/Grail/Moth; Hallowed Drum: Sky/Heart) reject it.
Now, with Kind = Souls and exactly one soul chip picked, each place row starts with
`EvolvePlanner.Verdict`: "Ready: <skill> + 2× <soul>" or the first unmet condition (wrong
Wisdom, no skill committed there — listing where the attuned skills are committed —, skill or
soul doesn't fit the principles, no pair), green with the game's check mark (`slot yes`) when ready, red with a cross
(`window_x`) otherwise (icons in front of the line, the text coloured with TMP rich
text `<color>`, so it stands apart from the faint gold subtitle). Computed from the already scanned item list, only
when the list is rebuilt. Rejected: a generic aspect filter on places, since it still leaves the
player to cross-check skills, wisdoms and slots by hand.
The Soul chips first listed only souls the known places evolve, so a soul with no place yet
(Ereb: Skolekosophy or Bosk) had no chip at all, which was exactly the case the player needed.
Now every evolvable soul gets a chip; picking one with no place shows a red row naming the
Wisdoms that evolve it. Souls per Wisdom now come from the recipe's attunement requirement
(`a.x...`), not its id: `evolve.birdsong.xtris` needs `a.xtri`, and the id gave a soul that
doesn't exist.
Later the same day, at the player's request: the "Evolve via" row of Wisdom chips is gone (with a
verdict per row, picking a soul says everything), and soul chips are radio buttons: clicking
one replaces the pick, clicking the picked one clears it.

## 2026-10-13 — AspectPin

Hovering an aspect icon makes the game pulse every item with that aspect, but only while the
pointer stays on the icon: `AureateElementFrame.OnPointerEnter` starts `PulseGlow`, which
calls `Meniscate.AttentionPlsOnElementsWIthAspects` and repeats until `OnPointerExit`.
AspectPin keeps it going: a middle click on any `ElementFrame` (aspect id from its `_aspect`
field) pins the aspect; the pin shows in a small draggable panel under the clock (icon, name,
cross); another middle click or the cross unpins; clicking the pin's icon shows its details.
Only one pin at a time (the player's call): several pinned aspects lit up unrelated items
everywhere and the highlight lost its point; a new pin replaces the old one.
The panel remembers where it was dragged: `OnEndDrag` writes `Panel.X/Y` to
`shirofaii.boh.aspectpin.cfg` (BepInEx saves on set).
Every 1.2 s the mod calls `HornedAxe.FindTokensWithAspectsInWorld` and `Token.AttentionPls`
directly: Meniscate's method is throttled globally (`MaxSuitabilityPulseFrequency`) and
would drop pins. The middle mouse button isn't used by the game.

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

**Layout.** Appended to the requirements display (a wrapping grid), the icon wrapped onto a
new line when a recipe had three requirements and spilled over the next header. Now it sits
in the row background (`ButtonBg`, the display's parent) with `ignoreLayout`, pinned to the
bottom right corner level with the requirements, on a dark `box-rnd-16px` badge so it
doesn't read as one more requirement; several results line up right to left. The Craftable
panel's width was left alone: it's the game's layout.

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
desks), `library.bed.*` → Beds (guest beds), the rest → House.

**Souls group (2026-10-15).** Soul evolution is tied to places: recipes
`evolve.<wisdom>.<soul>` ("Improve my Mettle", `crafting_1_evolutions.json`) run on any
`library.*` workstation but require `e.<wisdom>` ("Evolve via Illumination"), an aspect of the
verb itself (Shrine: the Watchman, Practice Equipment, Telescope, altars, instruments...),
plus a skill committed to that Wisdom and attuned to the soul (`a.x<soul>`), and two
identical soul cards (`x<soul>: 2` → `x<soul>2`). Places whose verb has any `e.*` aspect form
the Souls group (checked before Outdoors, since Deeplight Corals evolves too); the row says
"Evolves via <Wisdom>" (label of `w.<wisdom>`) and the search matches Wisdom names.
With Kind = Souls two more filter rows appear: Evolve via (Wisdoms present among the
player's soul places, icon of `e.<wisdom>`) and Soul (souls those Wisdoms can evolve). The
Wisdom → souls map is built once from the Compendium's recipe ids `evolve.<wisdom>.<soul>`.
All tiers of a soul (`xhea`, `xhea2`, `xhea3`) evolve in the same places with the same
attunement, so the chips collapse tiers to the base soul (trailing digits trimmed).
For other kinds the rows hide and their filters are cleared. The "C&H" the player
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
- Items inside a desk (a slot, a running action's `situationstoragesphere`, uncollected
  output) sit in the desk's hidden window on the screen canvas, so flying to the item went
  nowhere. Now the item's host (`sphere.GetContainer()` is a `Situation`) is recorded: the
  camera and the marker go to the desk's token, the subtitle says "in Desk: Ambrose".
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
