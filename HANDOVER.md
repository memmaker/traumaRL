# TraumaRL: handover

## RVIP progress

- **Stage 1 (Get + build): done.** Next: **stage 2** (explore + stairs + no `--More--`).
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

- No trimming: ILLink 10 crashes (IL1012, KeyNotFound in CompilerGeneratedState)
  on QuickGraph's net4 iterators; dist is 25 MB. Try excluding QuickGraph from
  ILLink properly or rebuilding QuickGraph from source.
- Shared page code (`rvip-wm.js`, `rvip-app.js`, `rvip-sound.js`) was not
  reachable (ruzzoli.de 403 from the cloud): stage 5 needs it.
- No saves/IndexedDB yet; start-up movies (`qe_start`, `helpkeys`) path and quit
  (Shift+Q) untested in the browser; debug keys with `debug=false` unchecked.
- Texts are drawn at cell positions with the TTF on the canvas (stage 5: panes).
