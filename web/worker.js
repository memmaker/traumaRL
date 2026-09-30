/*
 * TraumaRL: the .NET runtime (web/wasm, browser-wasm) runs in this module
 * worker, so the game loop can block. Keys arrive through a SharedArrayBuffer
 * ring written by trauma.js; waitKey() sleeps in Atomics.wait until one is
 * there (or the timeout ends: the game's event loop ticks between keys).
 * JS decides nothing here: it only moves bytes.
 */
import { dotnet } from './_framework/dotnet.js';

const SLOT = 48, NSLOT = 64;          /* ring: [0] written, [1] read, then NSLOT slots of SLOT ints */
let ring, nap, files = {};

function takeKey() {
	const r = Atomics.load(ring, 1), s = 2 + (r % NSLOT) * SLOT, n = ring[s];
	let str = '';
	for (let i = 0; i < n; i++) str += String.fromCharCode(ring[s + 1 + i]);
	Atomics.store(ring, 1, r + 1);
	return str;
}
const imports = {
	present(view, info) {
		const cells = view.slice();
		postMessage({ t: 'screen', cells, info }, [cells.buffer]);
	},
	waitKey(ms) {
		const w = Atomics.load(ring, 0);
		if (w !== Atomics.load(ring, 1)) return takeKey();
		if (ms < 0) postMessage({ t: 'wait' });
		Atomics.wait(ring, 0, w, ms < 0 ? Infinity : ms);
		return Atomics.load(ring, 0) !== Atomics.load(ring, 1) ? takeKey() : '';
	},
	sleep(ms) { Atomics.wait(nap, 0, 0, ms); },
	quit() { postMessage({ t: 'quit' }); },
	/* the save file: the page keeps it in IndexedDB */
	storeFile(name, view) { postMessage({ t: 'store', name, data: view.slice() }); },
	sound(name) { postMessage({ t: 'sound', name }); },
	deleteFile(name) { postMessage({ t: 'delete', name }); },
	initialFile(name) { return files[name] || null; },
};

onmessage = async (e) => {
	if (e.data.t !== 'init') return;
	ring = new Int32Array(e.data.ring);
	files = e.data.files || {};
	nap = new Int32Array(new SharedArrayBuffer(4));
	try {
		const rt = await dotnet.withConfig({ disableIntegrityCheck: true }).create();
		rt.setModuleImports('trauma', imports);
		postMessage({ t: 'started' });
		await rt.runMain(rt.getConfig().mainAssemblyName, e.data.args || []);
		postMessage({ t: 'exit' });
	} catch (err) {
		postMessage({ t: 'crash', msg: String(err && (err.stack || err.message) || err) });
	}
};
