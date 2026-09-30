/*
 * TraumaRL in the browser (RVIP stage 1 page). The game (C#, .NET browser-wasm)
 * runs in worker.js and hands over its finished 60x45 cell buffer: per cell a
 * layer count, then (sprite id, fg RGB, bg RGB or -1) per layer, all decided by
 * the game; plus a JSON of text runs. This page draws sprites from
 * TraumaSprites.png (white -> fg, magenta -> bg/transparent, as the SDL build
 * did) and forwards keys. No storage yet (stage 1).
 */
const SLOT = 48, NSLOT = 64, SPR = 16;
const $ = id => document.getElementById(id);
let worker, ring, scr = null, info = { texts: [] }, dirty = false, sheet, sheetData;
const cache = new Map();
window.trauma = { running: false, text, get info() { return info; }, get cells() { return scr; } };

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
document.addEventListener('keydown', e => {
	if (!ring || e.metaKey) return;
	if (/^(Shift|Control|Alt|Meta)/.test(e.code) || e.key === 'Dead') return;
	e.preventDefault();
	sendKey(e.code || '', e.key || '', (e.shiftKey ? 's' : '') + (e.ctrlKey ? 'c' : '') + (e.altKey ? 'a' : ''));
});

/* ---------- drawing ---------- */
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
function draw() {
	dirty = false;
	if (!scr || !sheetData) return;
	const cv = $('screen'), g = cv.getContext('2d'), C = info.cols, R = info.rows, S = 1 + info.layers * 3;
	if (cv.width !== C * SPR) { cv.width = C * SPR; cv.height = R * SPR; }
	g.imageSmoothingEnabled = false;
	g.fillStyle = '#000'; g.fillRect(0, 0, cv.width, cv.height);
	for (let y = 0; y < R; y++) for (let x = 0; x < C; x++) {
		const o = (x + y * C) * S, n = scr[o];
		for (let l = 0; l < n; l++) { const p = o + 1 + l * 3; g.drawImage(sprite(scr[p], scr[p + 1], scr[p + 2]), x * SPR, y * SPR); }
	}
	g.font = '18px alexis, monospace'; g.textBaseline = 'top';
	for (const [x, y, rgb, s] of info.texts) { g.fillStyle = '#' + rgb.toString(16).padStart(6, '0'); g.fillText(s, x * SPR, y * SPR - 1); }
}
/* text shadow for tests: top sprite as ASCII (ids < 127 are glyphs) plus the text runs */
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

/* ---------- menu pop-up: the game sends title, rows [accel, label, key, rgb, header] and cursor ---------- */
const hex = c => '#' + c.toString(16).padStart(6, '0');
function menu(m) {
	const box = $('menu');
	if (!m) { box.hidden = true; return; }
	box.replaceChildren();
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
}

async function main() {
	if (!await isolate()) { $('status').textContent = 'This browser cannot isolate the page (SharedArrayBuffer): the game cannot run.'; return; }
	sheet = new Image(); sheet.src = 'TraumaSprites.png';
	await sheet.decode();
	const c = document.createElement('canvas'); c.width = sheet.width; c.height = sheet.height;
	const g = c.getContext('2d'); g.drawImage(sheet, 0, 0);
	sheetData = g.getImageData(0, 0, sheet.width, sheet.height).data;
	try { const f = new FontFace('alexis', 'url(alexisv3.ttf)'); await f.load(); document.fonts.add(f); } catch (e) {}
	ring = new Int32Array(new SharedArrayBuffer(4 * (2 + SLOT * NSLOT)));
	worker = new Worker('worker.js', { type: 'module' });
	worker.onmessage = e => {
		const m = e.data;
		if (m.t === 'screen') { if (!window.trauma.running) $('status').textContent = ''; window.trauma.running = true; scr = m.cells; info = JSON.parse(m.info); menu(info.menu); if (!dirty) { dirty = true; requestAnimationFrame(draw); } }
		else if (m.t === 'started') { $('status').textContent = 'Generating the station…'; }
		else if (m.t === 'wait') { window.trauma.running = true; }
		else if (m.t === 'crash') { $('status').textContent = 'The game crashed: ' + m.msg; console.error(m.msg); }
		else if (m.t === 'quit' || m.t === 'exit') { $('status').textContent = 'The game has ended. Reload to play again.'; }
	};
	worker.postMessage({ t: 'init', ring: ring.buffer, args: new URLSearchParams(location.search).has('rviplocks') ? ['rviplocks'] : [] });
}
main();
