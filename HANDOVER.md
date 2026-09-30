# TraumaRL: handover

## RVIP progress

- **Stage 1 (Get + build): done.**
- **Stage 5 (Web page and windows): done in the cloud, final check pending locally.** Next: **stage 6**.
  - Page: `web/index.html` + `web/trauma.js` on `../rvip-wm.js` / `../rvip-app.js` (Forays
    template). Windows: Map (the only canvas), Status, Messages, Inventory; one-window mode =
    the game's whole 60×35 screen on the map canvas. Menus (Enter, `i`) = `RvipWM.popup`
    (centred in the Map body, text size = Messages' size). Prompt line = `RvipWM.prompt`.
  - Panes come from C#: `WebRenderer.Panes()` (shim) adds to the present JSON `full`
    (a whole-screen view is up = `DrawFrame(clear:true)` since the last `Clear()`: movies,
    history/clue/log lists, end screens → drawn whole on the map canvas), `map` (viewport rect,
    `Screen.RvipMapRect`), `status` (rows of `["text",rgb]` / `[spriteId,rgb]` inside
    `Screen.RvipStatsRect`, trailing empty rows dropped; sprites become 1em inline images of
    the recoloured sheet sprite), `prompt` (text runs in `Screen.RvipMsgRect`), `log` (new
    `messageHistory` lines, counter `MessageQueue.RvipHistoryAdded`), `inv`
    (`RogueBase.RvipInventoryJson()`, same rows as the `i` list), `atCmd` (`RvipAtCmd`).
  - Map: the game's own camera (37×27 viewport centred on the hero); the page centres that
    canvas in the Map body (`RvipWM.center`), scrolls when smaller, never scales with the window.
    Cell = 16/32/48/64 px only (whole multiples of the sheet's 16 px): the WM keeps the map's
    A−/A+ value as a step 8..11 (`size: {map: () => 8}`, `fontMax.map = 11`), cell = 16 × (step − 7).
    Stage-4 top-right A−/A+ bar removed.
  - Settings: IndexedDB database `/traumarl/files` (`RvipApp.dir + '/files'`), key
    `web-layout.json` = `{wm, face}`; no localStorage. Written on change (300 ms debounce) and on `pagehide`.
  - **Saves: one slot, resume on reload** (`RogueBasin/RogueBasin/RvipSave.cs`). Upstream's XmlSerializer
    `SaveGame()` stays dead. RvipSave writes `{Game.Dungeon, Game.MessageQueue, Game.Random}` whole with
    BinaryFormatter (NuGet `System.Runtime.Serialization.Formatters` + `EnableUnsafeBinaryFormatterSerialization`
    in `web/sources.props`; `TraumaWeb.csproj` drops the runtime pack's throwing stub — `rm -rf web/wasm/obj`
    if `_framework/System.Runtime.Serialization.Formatters*.wasm` is ~55 KB instead of ~125 KB). All game/shim
    classes got `[System.Serializable]` (script-inserted, 344 declarations); surrogates only for what is left:
    delegates (fields → `DelegateRec`, rebuilt after the graph), HashSet/Dictionary/SortedDictionary and
    subclasses (QuickGraph `VertexEdgeDictionary`) constructed in place and filled after the graph (no
    `IObjectReference`: it breaks on cycles, "object with ID n was referenced in a fixup but does not exist"),
    `System.Type`, and a reflection field-copier for non-serializable framework/QuickGraph types (Random,
    `EdgeList`). `Map` packs its `MapSquare[,]` into one `long` per square (`OnSerializing`/`OnDeserialized`).
    Atomic (tmp + `File.Move`). Save only at a safe point (`RogueBase.RvipCanSave`: main loop, map command,
    no menu/auto-explore, turn done) and only if `WorldClock` moved; deleted in `Dungeon.EndOfGame`
    (death, win, quit). Load in `TraumaRunner.TemplatedMapTest` before generation.
    Web: page key `RvipSave` (handled in `RvipInput.NextKey`, only inside `Events.Run`) on `visibilitychange`
    hidden, `pagehide`, and Export (flush); `beforeunload` shows "Leave site?" only when the pane JSON says
    `unsaved` and asks for a save meanwhile. Worker `storeFile/deleteFile/initialFile` → page keeps
    `traumarl.sav` in IndexedDB `/traumarl/files` (same db as `web-layout.json`), passes it at init.
    Cost: ~5 MB, **5–8 s per save in the wasm interpreter** (0.5 s native), so no timed autosave; resume
    boot ~8 s. Export/Import/New game work on the slot.
    Native test: `NOQUIT=1 SAVE=1 TraumaNative <seed> <keys>` saves when the keys run out (prints `fp`),
    then `LOADFP=1 NOQUIT=1 EXC=1 TraumaNative <seed2> 3000` resumes (prints `LOADED fp`, same) and plays on;
    `RT=1` adds per-field round-trip sizes. Seeds 4, 11, 13 (2000 keys → save → 3000 keys): same fingerprint,
    no crash, no exceptions; seed 13 died after resume and deleted the save.
  - Tests (cloud, Playwright Chromium, real `rvip-*.js` from `/home/user/rvip/web` served at `../`):
    layout 1280×720, A+ ×2 on Map → 48 px cells (scrolled, centred), A+ on Status changes only it,
    Enter menu pop-up in the Map body, resize 1000×650 → 1440×900 → 1200×750 → 760×500 → 1280×720
    (no negative sizes, text sizes fixed), reload keeps layout and sizes, IndexedDB only
    `/traumarl/files` (+ shared `rvip-outbox`), localStorage empty, no console errors.
  - **Local agent:** check against the current `~/Games/rvip-tools/web/rvip-wm.js` (the cloud copy
    may be older), look in the pane (drag dividers, one-window mode, a movie/end screen), then
    `web/build.sh`, commit + push, `web/deploy.sh` (web name `traumarl`,
    `/var/www/ruzzoli.de/roguelikes/traumarl/`), check live with `curl` + md5 vs `web/dist`.
  - Open: saves are slow (5–8 s freeze; a faster hand-written format would fix it); no help.html yet (Help shows a fallback, stage 6); no Visible
    window (the game has no such list); the map viewport stays the game's 37×27 (bigger windows
    show black around it). Enlarging `ViewableWidth/Height` from the page was looked at and skipped
    (not cheap): the map lives inside the fixed 60×45 cell buffer (`WebRenderer.Cols/Rows`) next to
    `RvipStatsRect`, which the status pane reads, so a bigger map needs a bigger buffer (whole buffer is
    posted every present), a map region moved clear of the status area, `TileMap` rebuilt on resize and
    a separate layout for one-window mode (the game's own 60×35 screen);
    status sprites (hearts, ammo, weapon icons) are inline images of the sheet sprites, not glyphs;
    no Tiles/Font select for the map (tiles only, rule 8).
- **Stage 4 (Tiles): done.**
  - Set: the game's own `RogueBasin/TraumaRL/bin/Debug/TraumaSprites.png` (256×768,
    16×16, 16 per row), copied unchanged into `web/dist` by `build.sh`; the only set,
    never mixed. Rows 0–15 = CP437 font glyphs (ids < 256), rows 16+ = pictures
    (ids ≥ 256). README: the graphics are the author's and not for *other*
    projects; shipping them with this port of the same game is within that.
  - Tile per cell decided in C#: sprite id = `MapObject.Representation` /
    `StringEquivalent.TerrainChars` via Screen.cs tile layers → `WebRenderer.cs`
    (4 layers of id, fg, bg). Page recolours (white → fg, magenta → bg) and draws.
  - Scale: page cell size 16 px (original) by default; A−/A+ buttons (top right)
    step 8…64 px by 4, `drawImage` with `imageSmoothingEnabled=false`, canvas
    backing store = cols×cell (no CSS scaling; map bigger than window scrolls).
    TTF text runs scale with the cell. `window.trauma.zoom(d)`, `.cell`. Not
    persisted yet (stage 5: IndexedDB).
  - Coverage (native `COVER=1`, seed 1, full station): placed kinds 46/46 have a
    picture sprite (19 monsters, 6 features, 18 items, 3 lock kinds; player 256);
    terrain 43/45 pictures, `Void` (176 ░) and `NonWalkableFeature` (250 ·) are the
    sheet's own CP437 glyphs by design. All 119 constructible Monster/Item/Feature
    types: 46 pictures; the 73 glyph-only ones are legacy RogueBasin kinds (orcs,
    staircases, potions…) TraumaRL never places. → 100 % of what the game shows.
  - No text mode: the game has no ASCII alternative for its picture sprites
    (Representation *is* the sprite id), so a text/None option would need guessed
    glyphs (rule 8). Tiles button not offered.
  - Shots: `web/shots/tiles16.png`, `tiles24.png` (sprites crisp at cell size).
  - Open: zoom not persisted; text runs (status panel) are still canvas text
    (stage 5 panes).
- **Stage 3 (Enter menu + item menus): done.**
  - File `RogueBasin/RogueBasin/RvipMenu.cs` (partial `RogueBase`, in `DDRogue.csproj`
    and `web/sources.props`). Hook: `RvipMenuKey(ref args)` in `KeyboardEventHandler`
    before `ProcessKeypress`, `RvipAfterCommand()` right after it.
  - Enter (MapMovement only) opens `RvipCommandMenu()`: groups Actions (fire/throw/use
    from the equipped weapon's `Has*Action`, examine `x`, wait `.`), Items (`i`),
    Travel (`e`, `<`, `>`), Other (help `?`), Debug (locks, only when enabled).
    No movement entries. `i` opens `RvipItemMenu()` = TraumaRL's inventory: weapons
    from `ItemMapping.WeaponMapping` (via `HeavyWeaponTranslation`) and wetware
    from `WetwareMapping` the player holds; label = `Item.SingleItemDescription`,
    colour = `Item.GetColour()`, key = the mapping key.
  - Item actions run by handing the command's normal key (`KeyboardEventArgs`) to
    `ProcessKeypress` (weapon: Equip = digit, or Fire/Throw/Use = `f` when in hand;
    wetware: Switch on/off = its letter, none while disabled). Letter in the item
    list = main action; Enter/Space/5/6/→ = action sub-menu; 4/← back; Esc/0/./
    Keypad. close; 8/2/arrows move; + main action. After an item action the list
    reopens if still in MapMovement, not exploring, and no monster in view.
  - Presentation: `RogueBase.RvipMenuJson` (`{title,cur,rows:[[accel,label,key,rgb,header]]}`)
    appended as `"menu"` to the present JSON by `WebRenderer.Flush`; `RvipPublish()` sets
    `Screen.NeedsUpdate` (else no redraw happens). Page: `#menu` HTML pop-up sized to
    content (`trauma.js` `menu()`), click sends the row's accelerator; `trauma.text()`
    appends `[menu title]` + rows (`>` = cursor).
  - Debug locks: page URL `?rviplocks` → worker passes `rviplocks` to `Main` →
    `rviplocks=true` in config.txt → Shift+K (upstream debug key) and a Debug menu
    entry toggle `AllLocksOpen`, without the rest of debug mode. Off otherwise.
    Native: `OPEN=1` as before. Harness: `}` / `{` = ArrowDown/ArrowUp.
  - Tests: native seed 2 NOMON: `___i}~a` equips pistol and reopens the list;
    `___~}}~` reaches the item list; 3×3000 random keys no crash. Chromium:
    Enter ↓ ↓ Enter shows the item list (shot `web/shots/menu.png`).
  - Open: no item prompts exist in TraumaRL (only targetting), so nothing to
    cursor there; no drop/examine item actions (the game has none, no item
    descriptions); help movie `helpkeys0.amf` does not mention Enter/`i` yet;
    `?rviplocks` untested in the browser.
- **Stage 2 (Explore + stairs + no `--More--`): done.**
  - Full level generation (stage 2 remainder): `quickLevelGen = false` in
    `TraumaWorldGenerator.cs`. Upstream hang cause: with it false, the generic
    `GenerateStandardLevel` loop still ran over every level except medical
    (the special-level exclusion list was commented out), overwriting the 9
    special levels after their elevators/escape pods were recorded, so quest
    placement always threw (escape-pod room / edge missing) and
    `TraumaRunner`'s `catch`+`while(true)` retried forever. Fix: use the
    exclusion list when `!quickLevelGen`. Retries now print `RVIP gen retry:`
    to stderr. Gen: ~2 s native, ~9 s browser; page shows "Generating the
    station…" between worker start and first screen.
  - Auto-explore / `<` `>` now walk through known locks the player holds the
    key cards for (bump opens) and treat all locks as passable when
    `AllLocksOpen`. Medical elevator lock needs 10 cameras destroyed (design).
  - Harness: `OPEN=1` (with NOMON) sets `AllLocksOpen`; NOMON exit prints locks.
    Movies (lock open) wait for Enter: put `~` after explore blocks. Test:
    seeds 1,2: explore + `>>` → level 1, `<<` → back to Medical; seed 3 reaches level 1.
  - Open: map gen is not deterministic per seed (layout differs run to run);
    browser elevator travel only checked as far as `>` messages (needs cameras
    killed; no lock-open switch in the web build).
  - Keys: `e` auto-explore, `<` / `>` walk to a known elevator (TraumaRL has no
    stairs; elevators teleport on entry, so the walk stops *next to* one and the
    second press steps in; `<` prefers elevators to lower-numbered levels, `>`
    higher, else any). Help movie `bin/Debug/movies/helpkeys0.amf` updated.
  - File `RogueBasin/RogueBasin/AutoExplore.cs` (partial `RogueBase`; added to
    `DDRogue.csproj` and `web/sources.props`). Hooks in `RogueBase.cs`:
    `AutoTick()` in `ApplicationTickEventHandler` after
    `AdvanceDungeonToNextPlayerTick()` (one `PCMove` per turn, sets
    `waitingForTurnTick`; the event loop's 40 ms key wait paints each step),
    `AutoKeyIntercept()` first in `KeyboardEventHandler` (any key stops; its
    key-up is swallowed), key dispatch at top of `InputState.MapMovement`.
  - Known grid = `MapSquare.SeenByPlayer`; BFS 8-dir over walkable or closed-door
    known cells, never through `ClosedLock` or any `UseableFeature` (elevators).
    Frontier = next to unseen, until stood on; seen items are targets. Stops:
    monster in FOV ("In view: X."), new item in view ("You see: X."), any new
    message (`MessageQueue.AddedCount`, new), a step that did not move (door
    opening excepted), level change, no path ("Nothing left to explore", or
    "…behind a locked door" when known locks exist).
  - No `--More--` exists: messages never wait for a key (the `<more>` code is
    commented out upstream). Nothing to do.
  - Native harness: SCRIPT `_` = no key for one tick (lets explore run),
    `NOMON=1` clears monsters and prints seen/walkable + elevators at exit,
    `MSGS=1` prints each new message line. Known-grid test: seed 2/3,
    `~~~~~~` + 10×(`e`+300 `_`) with NOMON → walkable seen 745/745, 748/748,
    "Nothing left to explore."; without NOMON stops "In view: Maint Bot/Swarmer".
    Chromium: `e` → "In view: Maint Bot.". 3×3000 random keys natively: no crash.
- Folder `/home/user/traumarl`, branch `claude/traumarl-rvip-xj7ndq`. Base = upstream
  branch `traumarl` (the only upstream branch) @ `d429380` ("Adding libtcodnet
  references so this branch compiles"), pristine.
- Case O (C#, like Forays). VS2013 solution `RogueBasin/DDRogue.sln`: TraumaRL
  (`RogueBasin/TraumaRL`) + core `RogueBasin/RogueBasin` + `GraphMap`, QuickGraph
  3.6 net4 dll (referenced as is, works on .NET 10), libtcod-net + SdlDotNet/Tao
  (native, replaced).
- Build: `sh web/build.sh` → `web/dist` (.NET 10 SDK, `Microsoft.NET.Sdk.WebAssembly`,
  `browser-wasm`, Mono interpreter, no workload). Toolchain: `web/toolchain.sh`
  (cloud: apt `dotnet-sdk-10.0`; dot.net install script is 403 behind the proxy).
- Frontend files: `web/wasm/TraumaWeb.csproj` + `WebBackend.cs` (JSImports
  module `trauma`), `web/sources.props` (compile list = the VS csprojs' explicit
  lists, shared with the native test), `web/shim/` (`Libtcod.cs` managed FOV
  shadowcasting / A* / Bresenham / Keyboard / RootConsole stubs, `Sdl.cs`
  SdlDotNet event loop + key types + MessageBox/ImageDisplay stubs,
  `RvipInput.cs` one key queue for both input paths, `WebRenderer.cs` = class
  `MapRendererSDLDotNet` replacement → cell buffer, `RvipArray.cs`),
  `web/worker.js` (runtime in a module worker, keys via SharedArrayBuffer ring +
  `Atomics.wait` with timeout), `web/coi-sw.js`, `web/trauma.js` + `index.html`
  (stage-1 page: one canvas, no rvip-wm yet), `web/config.txt` (web config:
  `debug=false`; upstream's has `debug=true` = immortal + profiling log).
- Present format: `int[]` 60×45 cells × (count + 4 layers × (sprite id, fg RGB,
  bg RGB or -1)); JSON `{cols,rows,layers,texts:[[x,y,rgb,str]]}` for the
  strings the SDL build drew with `alexisv3.ttf`. Page recolours
  `TraumaSprites.png` (white → fg, magenta → bg / transparent) like the SDL build.
- Tests: `node web/test.mjs [--shot n] keys…` (Playwright 1.56, Chromium
  `/opt/pw-browsers`; `window.trauma.text()` text shadow, `.cells`, `.info`).
  Native headless (ASan substitute): `dotnet build -c Release web/native`, then in
  a scratch dir `dotnet web/native/bin/Release/net10.0/TraumaNative.dll <seed>
  <randomkeys>` (env `SCRIPT`, `DUMP=1`, `TRACE=1`, `EXC=1` exception histogram).
  Stage 1 test: 3 × 3000 random keys natively, 300 random keys in Chromium, no crash.

### Quirks

- Main loop is SdlDotNet-style (`Events.Run`: tick, then key-up event); modal
  screens use libtcod `Keyboard.WaitForKeyPress`. Both read `RvipInput`. The
  event loop waits ≤40 ms for a key between ticks (animations).
- The game swallows exceptions in its level-generation retry loop: any wasm-only
  exception makes startup hang silently. `WebBackend` logs
  `ArrayTypeMismatchException` with a stack to the console.
- Upstream fixes (port changes): `Map.Clone()` stores a `MemberwiseClone()`d
  `MapSquare` into `MapSquare[,]` → Mono wasm throws ArrayTypeMismatchException
  → store via `RvipArray.Set`; .NET 10 `Enumerable.Shuffle` makes
  `x.Shuffle()` ambiguous in `TraumaWorldGenerator.cs` → `RogueBasin.ShuffleExtension.Shuffle(x)`.
- `Game.Random = new Random()` is unseeded (native `seed` only drives the key feed).
- Excluded from the build: `MapRendererSDLDotNet.cs`, `MapRendererLibTCod.cs`,
  `ImageDisplay*.cs` (WinForms), AssemblyInfo files. Graphviz `.dot` files are
  still written to MEMFS at start (harmless).

### Open problems

- Upstream `TraumaWorldGenerator.quickLevelGen = true`: one level, **no
  elevators, quests or loot links**, so `<`/`>` only say "You don't know of an
  elevator on this level." Setting it false made native generation run >12 min
  without reaching the game (killed). The elevator walk is untested live.

- No trimming: ILLink 10 crashes (IL1012, KeyNotFound in CompilerGeneratedState)
  on QuickGraph's net4 iterators; dist is 25 MB. Try excluding QuickGraph from
  ILLink properly or rebuilding QuickGraph from source.
- Shared page code (`rvip-wm.js`, `rvip-app.js`, `rvip-sound.js`) was not
  reachable (ruzzoli.de 403 from the cloud): stage 5 needs it.
- No saves/IndexedDB yet; start-up movies (`qe_start`, `helpkeys`) path and quit
  (Shift+Q) untested in the browser; debug keys with `debug=false` unchecked.
- Texts are drawn at cell positions with the TTF on the canvas (stage 5: panes).
