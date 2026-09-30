/*
 * TraumaRL in the browser (RVIP stage 5 page). The game (C#, .NET browser-wasm)
 * runs in worker.js and hands over its finished cell buffer (per cell a layer
 * count, then sprite id, fg RGB, bg RGB or -1 per layer) plus a JSON (shim
 * WebRenderer.Panes): text runs, the map rectangle, status lines, prompt, new
 * message lines, inventory rows, the Enter/i menu and whether a whole-screen view
 * (movie, history, end screen) is up. Everything shown is decided there; this page
 * lays the windows out (../rvip-wm.js), draws sprites from TraumaSprites.png
 * (white -> fg, magenta -> bg, nearest-neighbour), forwards keys and keeps its
 * settings in its own IndexedDB database ('/traumarl/files'; no localStorage).
 * One save slot (traumarl.sav, the game's whole state written by C# RvipSave): the
 * page asks for it when the tab is hidden and on leaving (only at the command
 * prompt, only if time moved), keeps it in the same database and hands it to the
 * worker on load (resume). The game deletes it at death or victory. A save takes
 * ~5 s in the wasm interpreter, so there is no timed autosave; leaving with an
 * unsaved run shows the browser's "Leave site?" and saves meanwhile (Stay = kept).
 */
const SLOT = 48, NSLOT = 64, SPR = 16;   /* sheet sprite size: TraumaSprites.png as shipped, never pre-scaled */
/* map cell size: whole multiples of the 16 px sprite only (RVIP 5.8), A−/A+ on the Map title bar.
   The WM keeps the map's size like every window's (state.fs.map, 8..11); here it is a step:
   8 -> 16 px, 9 -> 32, 10 -> 48, 11 -> 64. */
const MAPSTEP0 = 8, MAPSTEPS = 4;
const mapCell = () => SPR * (Math.max(0, Math.min(MAPSTEPS - 1, (wm && wm.zoomed('map') || MAPSTEP0) - MAPSTEP0)) + 1);
const DB = RvipApp.dir + '/files';
const $ = id => document.getElementById(id);
const hex = c => '#' + (c & 0xffffff).toString(16).padStart(6, '0');
let worker, ring, db, scr = null, info = { texts: [] }, dirty = false, sheet, sheetData, ended = false;
let onStored = null, wm = null, L = { wm: null, face: '', sound: false }, saveT = 0;
const cache = new Map(), icons = new Map();

const SAV = 'traumarl.sav';
const app = RvipApp({
	name: 'traumarl',
	save: async () => (await getFile(SAV)) ? SAV : null,
	read: name => getFile(name),
	clear: () => delFile(SAV),
	put: (file, data) => putFile(SAV, data),
	flush: done => {   /* Export: save the current state first (the game answers with a store, ~5 s) */
		if (!info.unsaved || !info.atCmd) { done(); return; }
		const t = setTimeout(() => { onStored = null; done(); }, 20000);
		onStored = () => { clearTimeout(t); onStored = null; done(); };
		app.status('Saving the run…'); requestSave();
	},
	noSave: 'No saved run yet: the game saves when the tab is hidden or closed.',
	helpText: 'Press ? in the game for its key help; Enter opens the command menu.'
});

/* ---------- cross-origin isolation (SharedArrayBuffer) ---------- */
async function isolate() {
	if (window.crossOriginIsolated) return true;
	if (!('serviceWorker' in navigator)) return false;
	const u = new URL(location.href);
	if (u.searchParams.has('coi')) return false;   /* reloaded once already: give up */
	await navigator.serviceWorker.register('coi-sw.js');
	await navigator.serviceWorker.ready;
	u.searchParams.set('coi', '1');
	location.replace(u);
	return new Promise(() => {});
}

/* ---------- IndexedDB: page settings (web-layout.json) ---------- */
function openDB() {
	return new Promise((ok, bad) => {
		const r = indexedDB.open(DB, 1);
		r.onupgradeneeded = () => r.result.createObjectStore('files');
		r.onsuccess = () => ok(r.result);
		r.onerror = () => bad(r.error);
	});
}
function tx(mode, f) {
	return new Promise((ok, bad) => {
		const t = db.transaction('files', mode), out = f(t.objectStore('files'));
		t.oncomplete = () => ok(out && out.result);
		t.onerror = () => bad(t.error);
	});
}
const putFile = (name, data) => tx('readwrite', s => s.put(data, name));
const getFile = name => tx('readonly', s => s.get(name));
const delFile = name => tx('readwrite', s => s.delete(name));
/* the game saves only at its command prompt and only if time moved (C# RvipSave.Request) */
function requestSave() { if (ring && app.running && !ended) sendKey('RvipSave', '', ''); }
function saveLayout(now) {
	clearTimeout(saveT);
	const w = () => putFile('web-layout.json', new TextEncoder().encode(JSON.stringify(L))).catch(() => {});
	if (now) w(); else saveT = setTimeout(w, 300);
}

/* ---------- keys ---------- */
function sendKey(code, key, mods) {
	const str = code + '\t' + key + '\t' + mods;
	const w = Atomics.load(ring, 0);
	if (w - Atomics.load(ring, 1) >= NSLOT) return;
	const s = 2 + (w % NSLOT) * SLOT, n = Math.min(str.length, SLOT - 1);
	ring[s] = n;
	for (let i = 0; i < n; i++) ring[s + 1 + i] = str.charCodeAt(i);
	Atomics.store(ring, 0, w + 1);
	Atomics.notify(ring, 0);
}
function onKey(e) {
	if (!ring || !app.running || e.metaKey) return;
	if (e.target && /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName)) return;
	if (/^(Shift|Control|Alt|Meta)/.test(e.code) || e.key === 'Dead' || /^F(5|11|12)$/.test(e.key)) return;
	e.preventDefault();
	sendKey(e.code || '', e.key || '', (e.shiftKey ? 's' : '') + (e.ctrlKey ? 'c' : '') + (e.altKey ? 'a' : ''));
}

/* ---------- sprites ---------- */
function sprite(id, fg, bg) {
	const k = id + ':' + fg + ':' + bg;
	let c = cache.get(k);
	if (c) return c;
	c = document.createElement('canvas'); c.width = c.height = SPR;
	const g = c.getContext('2d'), sx = (id % 16) * SPR, sy = Math.floor(id / 16) * SPR;
	const img = g.createImageData(SPR, SPR), d = img.data;
	for (let y = 0; y < SPR; y++) for (let x = 0; x < SPR; x++) {
		const si = ((sy + y) * sheet.width + sx + x) * 4, di = (y * SPR + x) * 4;
		let r = sheetData[si], gg = sheetData[si + 1], b = sheetData[si + 2], a = sheetData[si + 3];
		if (r === 255 && gg === 0 && b === 255) {
			if (bg < 0) a = 0; else { r = bg >> 16; gg = (bg >> 8) & 255; b = bg & 255; a = 255; }
		} else if (r === 255 && gg === 255 && b === 255) { r = fg >> 16; gg = (fg >> 8) & 255; b = fg & 255; }
		d[di] = r; d[di + 1] = gg; d[di + 2] = b; d[di + 3] = a;
	}
	g.putImageData(img, 0, 0);
	cache.set(k, c);
	return c;
}
/* a sprite as an inline image for the text windows (sized 1em by CSS, so it follows A−/A+) */
function icon(id, fg) {
	const k = id + ':' + fg;
	let u = icons.get(k);
	if (!u) { u = sprite(id, fg, -1).toDataURL(); icons.set(k, u); }
	const im = document.createElement('img'); im.src = u; im.alt = '';
	return im;
}

/* ---------- map (the only canvas) ---------- */
function draw() {
	dirty = false;
	if (!scr || !sheetData || !wm) return;
	const cv = $('map').querySelector('canvas'), C = info.cols, S = 1 + info.layers * 3, cell = mapCell();
	/* whole-screen views (movies, history, end screen) and one-window mode: the game's full screen; else its map viewport */
	const [x0, y0, w, h] = info.full || wm.mode() === 'single' ? [0, 0, info.cols, info.rows] : info.map;
	if (cv.width !== w * cell || cv.height !== h * cell) { cv.width = w * cell; cv.height = h * cell; }
	cv.style.width = cv.width + 'px'; cv.style.height = cv.height + 'px';
	const g = cv.getContext('2d');
	g.imageSmoothingEnabled = false;
	g.fillStyle = '#000'; g.fillRect(0, 0, cv.width, cv.height);
	for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
		const o = (x0 + x + (y0 + y) * C) * S, n = scr[o];
		for (let l = 0; l < n; l++) { const p = o + 1 + l * 3; g.drawImage(sprite(scr[p], scr[p + 1], scr[p + 2]), x * cell, y * cell, cell, cell); }
	}
	g.font = Math.round(cell * 18 / SPR) + 'px alexis, monospace'; g.textBaseline = 'top';
	for (const [x, y, rgb, s] of info.texts) {
		if (y < y0 || y >= y0 + h || x < x0 || x >= x0 + w) continue;
		g.fillStyle = hex(rgb); g.fillText(s, (x - x0) * cell, (y - y0) * cell - cell / SPR);
	}
	/* the game centres its own viewport on the hero: the page centres that viewport in the window
	   (scrolls it when the window is smaller, clamped at the edges) */
	RvipWM.center(cv, cv.width / 2, cv.height / 2, cv.width, cv.height);
}
function redraw() { if (!dirty) { dirty = true; requestAnimationFrame(draw); } }


/* ---------- text windows: lines and colours from the game ---------- */
function status(rows) {
	const k = JSON.stringify(rows), pre = $('stat');
	if (pre._k === k) return;
	pre._k = k; pre.replaceChildren();
	rows.forEach((segs, i) => {
		if (i) pre.append('\n');
		for (const [v, rgb] of segs) {
			if (typeof v === 'number') pre.append(icon(v, rgb));
			else { const s = document.createElement('span'); s.textContent = v; if (rgb) s.style.color = hex(rgb); pre.append(s); }
		}
	});
}
function inventory(rows) {
	const k = JSON.stringify(rows), el = $('inv');
	if (el._k === k) return;
	el._k = k; el.replaceChildren();
	for (const [label, key, rgb, head] of rows) {
		const d = document.createElement('div');
		if (head) { d.className = 'wm-vh'; d.textContent = label; }
		else { d.textContent = key + ') ' + label; d.style.color = hex(rgb); }
		el.append(d);
	}
}
function menu(m) {
	const box = $('menu');
	if (!m) { box.hidden = true; return; }
	box.replaceChildren();
	box.style.fontSize = RvipWM.fontSize('msg') + 'px';
	const t = document.createElement('div'); t.className = 'title'; t.textContent = m.title; box.append(t);
	m.rows.forEach(([acc, label, key, rgb, head], i) => {
		const d = document.createElement('div');
		if (head) { d.className = 'head'; d.textContent = label; d.style.color = hex(rgb); }
		else {
			d.className = 'row' + (i === m.cur ? ' cur' : '');
			const a = document.createElement('span'); a.textContent = acc + ') ';
			const l = document.createElement('span'); l.textContent = label; l.style.color = hex(rgb);
			const k = document.createElement('span'); k.className = 'key'; k.textContent = key;
			d.append(a, l, k);
			d.onclick = () => sendKey('Key' + acc.toUpperCase(), acc, '');
		}
		box.append(d);
	});
	box.hidden = false;
	RvipWM.popup(box, { center: true });
}
function update(i) {
	RvipWM.prompt.text(i.full ? '' : i.prompt || '');
	RvipWM.prompt.wait(i.atCmd);
	if (i.log) i.log.forEach(l => RvipWM.log($('log'), l));
	if (i.status) status(i.status);
	if (i.inv) inventory(i.inv);
	menu(i.menu);
}

/* ---------- windows ---------- */
function fonts() {
	const f = L.face ? '"' + L.face + '", ui-monospace, Menlo, Consolas, monospace' : '';
	['statb', 'msgb', 'inv', 'menu'].forEach(id => { $(id).style.fontFamily = f; });
}
function loadFace(n) {
	if (!n) { fonts(); return; }
	const ff = new FontFace(n, 'url(../fonts/' + n + '.woff)');
	ff.load().then(() => { document.fonts.add(ff); fonts(); }).catch(() => app.status('Could not load the font ' + n + '.', true));
}
async function makeWM() {
	try {
		const d = await getFile('web-layout.json');
		if (d) { const s = JSON.parse(new TextDecoder().decode(d)); L = { wm: s.wm || null, face: s.face || '', sound: !!s.sound, name: s.name }; }
	} catch (_) { }
	loadFace(L.face);
	$('chk-sound').checked = L.sound; /* off by default */
	wm = RvipWM({
		area: $('game'), menu: $('btn-layout'),
		wins: [{ id: 'map', title: 'Map' }, { id: 'status', title: 'Status' }, { id: 'msg', title: 'Messages' }, { id: 'inv', title: 'Inventory' }],
		multi: { d: 'h', r: 0.7, a: { d: 'v', r: 0.78, a: 'map', b: 'msg' }, b: { d: 'v', r: 0.62, a: 'status', b: 'inv' } },
		single: 'map',
		state: L.wm,
		save: st => { L.wm = st; saveLayout(); },
		layout: () => { redraw(); if (info.menu) menu(info.menu); },
		zoom: { map: () => redraw() },
		size: { map: () => MAPSTEP0 },
		fontMax: { map: MAPSTEP0 + MAPSTEPS - 1 },
		onReset: () => { L.wm = wm.state(); saveLayout(); redraw(); }
	});
	wm.apply();
}
function bar() {
	RvipWM.dropdown($('btn-file'), $('menu-file'));
	RvipWM.dropdown($('btn-audio'), $('menu-audio'));
	$('chk-sound').onchange = function () { L.sound = this.checked; saveLayout(); this.blur(); };
	const sel = $('sel-font');
	RvipWM.fonts.then(() => { RvipWM.fontOptions(sel); sel.value = L.face || ''; }).catch(() => { });
	sel.onchange = function () { L.face = this.value; saveLayout(); loadFace(this.value); this.blur(); };
	sel.addEventListener('keydown', e => e.stopPropagation());
	$('btn-restart').onclick = () => location.reload();
	document.querySelectorAll('#bar button').forEach(b => b.addEventListener('mousedown', e => e.preventDefault()));
}

/* ---------- test hooks (web/test.mjs): top sprite as ASCII per cell, text runs, menu ---------- */
function text() {
	if (!scr) return '';
	const C = info.cols, R = info.rows, S = 1 + info.layers * 3, rows = [];
	for (let y = 0; y < R; y++) {
		let s = '';
		for (let x = 0; x < C; x++) { const o = (x + y * C) * S, n = scr[o], id = n ? scr[o + 1 + (n - 1) * 3] : 32; s += id > 32 && id < 127 ? String.fromCharCode(id) : n ? '#' : ' '; }
		rows.push(s.trimEnd());
	}
	let s = rows.join('\n') + '\n' + info.texts.map(t => t[3]).join('\n');
	if (info.menu) s += '\n[menu ' + info.menu.title + ']\n' + info.menu.rows.map((r, i) => (i === info.menu.cur ? '>' : ' ') + (r[4] ? r[1] : r[0] + ') ' + r[1] + ' ' + r[2])).join('\n');
	return s;
}
window.trauma = { running: false, text, key: sendKey, get cell() { return mapCell(); }, get info() { return info; }, get cells() { return scr; }, get wm() { return wm; } };

function gameOver() {
	if (ended) return;
	ended = true; app.running = false;
	setTimeout(() => { $('overlay').hidden = false; }, 300);
}
async function main() {
	bar();
	if (!await isolate()) { app.status('This browser cannot isolate the page (SharedArrayBuffer): the game cannot run.', true); return; }
	db = await openDB();
	await makeWM();
	sheet = new Image(); sheet.src = 'TraumaSprites.png';
	await sheet.decode();
	const c = document.createElement('canvas'); c.width = sheet.width; c.height = sheet.height;
	const g = c.getContext('2d'); g.drawImage(sheet, 0, 0);
	sheetData = g.getImageData(0, 0, sheet.width, sheet.height).data;
	try { const f = new FontFace('alexis', 'url(alexisv3.ttf)'); await f.load(); document.fonts.add(f); } catch (e) {}
	ring = new Int32Array(new SharedArrayBuffer(4 * (2 + SLOT * NSLOT)));
	worker = new Worker('worker.js', { type: 'module' });
	worker.onerror = e => app.crashed(e);
	worker.onmessage = e => {
		const m = e.data;
		if (m.t === 'screen') {
			scr = m.cells;
			try { info = JSON.parse(m.info); } catch (err) { console.error('info', err); return; }
			if (!app.running && !ended) { app.running = window.trauma.running = true; app.status(''); $('game').hidden = false; wm.apply(); }
			update(info); redraw();
		}
		else if (m.t === 'started') app.status(sav ? 'Loading the saved run…' : 'Generating the station…');
		else if (m.t === 'crash') { app.crashed(new Error(m.msg.split('\n')[0])); console.error(m.msg); }
		else if (m.t === 'store') putFile(m.name, m.data).then(() => { app.status(''); if (onStored) onStored(); }).catch(err => app.status('Saving to browser storage (IndexedDB) failed: ' + err, true));
		else if (m.t === 'beacon') { if (window.RvipWM && RvipWM.report) RvipWM.report(m.q); else fetch('/roguelikes/beacon?' + m.q, { keepalive: true, mode: 'no-cors' }).catch(function () {}); }
		else if (m.t === 'sound') { if (L.sound) RVIPSound.play([m.name], 0.5); }
		else if (m.t === 'delete') delFile(m.name).catch(() => {});
		else if (m.t === 'quit' || m.t === 'exit') gameOver();
	};
	const files = {}, sav = await getFile(SAV).catch(() => null);
	if (sav) files[SAV] = new Uint8Array(sav);
	/* RVIP 9: the game never asks a name (always "Dave"): ask once for the graveyard, kept in web-layout.json; Cancel = no name */
	if (typeof L.name !== 'string') { let n = ''; try { n = window.prompt('Your name for the graveyard and leaderboard (Cancel: none):', ''); } catch (e) {}  /* prompt() throws in embedded browsers: no name, game still starts */ L.name = (n || '').trim().slice(0, 30); saveLayout(true); }
	const args = new URLSearchParams(location.search).has('rviplocks') ? ['rviplocks'] : [];
	if (L.name) args.push('name=' + L.name);
	worker.postMessage({ t: 'init', ring: ring.buffer, files, args });
	window.addEventListener('keydown', onKey);
	document.addEventListener('visibilitychange', () => { if (document.hidden) requestSave(); });
	window.addEventListener('beforeunload', e => {
		if (!app.running || ended || !info.unsaved) return;
		requestSave(); app.status('Saving the run…');
		e.preventDefault(); e.returnValue = '';
	});
	window.addEventListener('pagehide', () => { requestSave(); saveLayout(true); });
}
main();
