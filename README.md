# TraumaRL in the browser

Web port of **TraumaRL** (flend / Tom Ford, 7DRL 2014), upstream
[flend/roguelike, branch `traumarl` @ `d429380`](https://github.com/flend/roguelike/tree/d4293808).
Our changes: https://github.com/memmaker/traumaRL/compare/d429380...main
Play: https://ruzzoli.de/roguelikes/traumarl/

- Build: `sh web/build.sh` → `web/dist` (.NET 10 SDK, browser-wasm; see `web/toolchain.sh`).
- Deploy: `web/deploy.sh` (from a pushed, clean checkout).
- Port notes and status: `HANDOVER.md`.

Original README below.

---

This repository contains the sources for my roguelike games:

DDRogue
PrincessRL
TraumaRL

The source code is covered by the GPL-v3 (see LICENSE)

The graphics (where applicable) are proprietary and may not be used in other projects without my permission.

The different games are in different branches:

ddrogue

princessrl

flatlinerl

traumarl


All these branches build with the latest VC# Express 2013.

These are code-jam games so don't expect the code to be very pretty :)

-flend
