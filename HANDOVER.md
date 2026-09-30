# TraumaRL: handover

## RVIP progress

- **Stage 1 (Get + build): done.**
- **Stage 2 (Explore + stairs + no `--More--`): done.** Next: **stage 3**.
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
