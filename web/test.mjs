/*
 * Headless browser test (Playwright + Chromium) against web/dist served by
 * plain `python3 -m http.server` (so the COI service worker path is used).
 *   node web/test.mjs [--keep] [--shot name] key key ...
 * Keys are Playwright key names ("a", "Enter", "Shift+Period", "Numpad6") or
 * "wait:ms", "text:..." (types each char), "reload". Prints the game screen
 * (read through window.trauma.text()) after the keys; screenshot into
 * web/shots/<name>.png. Without --keep the page's IndexedDB is fresh.
 */
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
let shot = null, profile = null, query = '';
while (args[0] && args[0].startsWith('--')) {
	const a = args.shift();
	if (a === '--shot') shot = args.shift();
	if (a === '--keep') profile = join(here, '..', '.pw-profile');
	if (a === '--seed') query = '?seed=' + args.shift();
}
const port = 8000 + Math.floor(Math.random() * 1000);
const srv = spawn('python3', ['-m', 'http.server', String(port), '-d', join(here, 'dist')], { stdio: 'ignore' });
await new Promise(r => setTimeout(r, 800));
const opts = { executablePath: process.env.CHROME || '/opt/pw-browsers/chromium', headless: true };
let browser, ctx;
if (profile) ctx = await chromium.launchPersistentContext(profile, { ...opts, viewport: { width: 1280, height: 800 } });
else { browser = await chromium.launch(opts); ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } }); }
const page = await ctx.newPage();
const errors = [];
page.on('pageerror', e => errors.push(String(e)));
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
page.on('dialog', d => d.accept());
page.on('response', r => { if (r.status() >= 400) errors.push('HTTP ' + r.status() + ' ' + r.url()); });
async function boot() {
	await page.goto(`http://localhost:${port}/${query}`);
	await page.waitForFunction(() => window.trauma && window.trauma.running, null, { timeout: 180000 });
	await page.waitForTimeout(300);
}
const settle = () => page.waitForTimeout(250);
/* random keys (crash hunt in the browser) */
async function random(n, seed) {
	let x = seed >>> 0 || 1;
	const rnd = m => { x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; x >>>= 0; return x % m; };
	const pool = 'abcdefghijklmnoprstuvwxyzABCDEFGHIJKLMNOPRSTUVWXYZ0123456789<>.,;:?/!@#$%^&*()-=+[]'.split('')
		.concat(['Enter', 'Escape', 'Space', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Numpad1', 'Numpad3', 'Numpad7', 'Numpad9', 'Numpad5']);
	for (let i = 0; i < n; i++) {
		const t = await page.evaluate(() => window.trauma.text());
		
		const k = pool[rnd(pool.length)];
		await page.keyboard.press(k);
		await page.waitForTimeout(15);
	}
}
try {
	await boot();
	for (const k of args) {
		if (k.startsWith('wait:')) await page.waitForTimeout(+k.slice(5));
		else if (k === 'reload') await boot();
		else if (k.startsWith('click:')) { await page.click(k.slice(6)); await settle(); }
		else if (k.startsWith('eval:')) console.log('eval:', JSON.stringify(await page.evaluate(k.slice(5))));
		else if (k.startsWith('random:')) await random(+k.split(':')[1], +(k.split(':')[2] || 1));
		else if (k.startsWith('text:')) { for (const c of k.slice(5)) { await page.keyboard.type(c); await settle(); } }
		else { await page.keyboard.press(k); await settle(); }
	}
	await page.waitForTimeout(400);
	console.log(await page.evaluate(() => window.trauma.text()));
	console.log('info:', JSON.stringify(await page.evaluate(() => window.trauma.info)));
	if (shot) { mkdirSync(join(here, 'shots'), { recursive: true }); await page.screenshot({ path: join(here, 'shots', shot + '.png') }); }
	if (errors.length) { console.log('ERRORS:\n' + errors.join('\n')); process.exitCode = 1; }
} catch (e) {
	console.log('FAILED:', e.message);
	console.log(await page.evaluate(() => document.getElementById('status').textContent).catch(() => ''));
	console.log('ERRORS:\n' + errors.join('\n'));
	process.exitCode = 1;
} finally {
	await ctx.close(); if (browser) await browser.close();
	srv.kill();
}
