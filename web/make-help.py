#!/usr/bin/env python3
"""Writes the in-page game guide (<out>/help.html) for the TraumaRL web build.
Self-contained (cloud: no Docs folder). Keys are parsed from the game's own
in-game help movie (movies/helpkeys0.amf, which also lists the RVIP keys
Enter and i); the intro comes from the opening movie (qe_start*.amf).
   python3 web/make-help.py web/dist"""
import glob, html, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
MOV = os.path.join(ROOT, 'RogueBasin/RogueBasin/bin/Debug/movies')
BASE = 'd429380'
esc = html.escape


def kbd(k):
    return '<kbd>' + esc(k) + '</kbd>'


def movie(name):
    fs = sorted(glob.glob(os.path.join(MOV, name + '[0-9]*.amf')), key=lambda f: int(re.findall(r'(\d+)\.amf$', f)[0]))
    return [open(f, encoding='latin-1').read().replace('\r', '') for f in fs]


keys = []
for line in movie('helpkeys')[0].split('\n')[1:]:
    m = re.match(r'\s*(.+?)\s+-\s+(.+?)\s*$', line)
    if m:
        keys.append((m.group(1).replace('(shift)', 'Shift+').replace('[dot] ', ''), m.group(2)))
    elif line.strip():   # "vi Keys": a second name for Move
        keys[-1] = (keys[-1][0] + ' / ' + line.strip(), keys[-1][1])
assert len(keys) >= 16, keys
keys = [(k, d + ' (no effect in the browser)' if k == 'Shift+F' else d) for k, d in keys]
assert ('Enter', 'Command menu') in keys and ('i', 'Inventory menu') in keys

intro = ''.join('<p>' + esc(' '.join(l.strip() for l in f.strip().split('\n'))) + '</p>'
                for f in movie('qe_start') if 'WELCOME' not in f)

KEY_HINTS = [
    ('?', "The game's own key help (the same list as below)"),
    ('e', 'Explore automatically until something comes into view'),
    ('Enter', 'Menu of all commands'),
    ('i', 'Inventory menu: pick an item for its actions'),
    ('< >', 'Walk to the nearest known elevator; step on it to change level'),
    ('f', 'Fire the equipped weapon (or use the equipped item) at a target'),
]

SAVING = '''<ul>
<li><strong>Saving is automatic.</strong> The run is stored in this browser (IndexedDB) when you switch tabs or leave the page, if a turn has passed since the last save. Reloading the page continues from there.</li>
<li>A save takes a few seconds in the browser: the game pauses briefly while it writes.</li>
<li>When you die, win or quit (<kbd>Shift+Q</kbd>), the save is deleted: one life.</li>
<li><em>File ▾ → Export save</em> downloads <code>traumarl.sav</code>; <em>Import save</em> loads one; <em>New game</em> deletes the save in this browser.</li>
<li>Private/incognito windows and "clear site data" delete the stored run. Export first if it matters.</li>
</ul>'''

TIPS = '''<ul>
<li>Damage hits your shield first; only when it is down do you lose hit points.</li>
<li>Keys <kbd>1</kbd>–<kbd>9</kbd> equip the items you carry; <kbd>a s d z c</kbd> switch your wetware (the implants in your head).</li>
<li>Read the logs you find (<kbd>Shift+L</kbd>): they tell the story and where things are. <kbd>Shift+C</kbd> lists the key cards you hold for locked doors.</li>
<li><kbd>x</kbd> examines what you can see and picks a target before you fire.</li>
<li><kbd>e</kbd> stops when something comes into view: that is your cue to fight or back off.</li>
</ul>'''

WEB = '''<ul>
<li><strong>Windows:</strong> Map, Status, Messages and Inventory are separate windows. Drag title bars to rearrange, drag the gaps to resize, hover a title for text size (A−/A+ on the map changes the tile size). <em>One window</em> shows the original screen.</li>
<li>The command menu (<kbd>Enter</kbd>) and the inventory menu (<kbd>i</kbd>) are additions of this web version; click an entry or press its key.</li>
<li>Story screens and the key help wait for a key; the message history is also in the Messages window.</li>
<li>Sound effects are off by default; switch them on in <em>Audio ▾</em>. The sounds are made for this version (the game has none of its own).</li>
<li>Browsers keep some shortcuts (<kbd>Ctrl+W</kbd>, <kbd>F5</kbd>…) for themselves.</li>
<li>The game runs in a Web Worker; the first visit installs a small service worker so it can (it reloads once).</li>
</ul>'''

CREDITS = '''<ul>
<li><strong>TraumaRL</strong> by flend (Tom Ford), written for the 7DRL Challenge 2014 on his RogueBasin/DDRogue code base; tile art by ShroomArts. Source code under the GNU GPL v3; the graphics are proprietary (see the README).</li>
</ul>'''

VERSION = (f'<ul><li>Based on <a href="https://github.com/flend/roguelike/tree/traumarl">flend/roguelike</a>, branch <code>traumarl</code> @ <code>{BASE}</code>.</li>'
           '<li>Our changes (web backend replacing SDL/libtcod, command and inventory menus, window layout, save and resume, '
           'sound effects): <a href="https://github.com/memmaker/traumaRL">memmaker/traumaRL</a>.</li>'
           '<li>Built with .NET (browser-wasm).</li></ul>')


def dl(items):
    return '<dl>' + ''.join(f'<dt>{kbd(k)}</dt><dd>{esc(d)}</dd>' for k, d in items) + '</dl>'


def section(a, t, body):
    return f'<h2 id="h-{a}">{esc(t)}</h2>{body}'


toc = [('about', 'About the game'), ('keys', 'Keyboard controls'), ('saving', 'Saving your game'), ('tips', 'Tips'),
       ('web', 'Playing in the browser'), ('credits', 'Credits'), ('version', 'About this version')]
parts = ['<p>A sci-fi roguelike: wake up in a blood-soaked trauma centre with military wetware in your head, '
         'and fight your way through the station.</p><ul class="toc">' +
         ''.join(f'<li><a href="#h-{a}">{esc(t)}</a></li>' for a, t in toc) + '</ul>',
         section('about', 'About the game', intro),
         section('keys', 'Keyboard controls', '<div class="box key"><h3>The keys to remember</h3>' + dl(KEY_HINTS) + '</div>'
                 '<h3>All keys (from the in-game help, <kbd>?</kbd>)</h3>'
                 '<div class="all">' + ''.join(f'<div>{kbd(k)}<span>{esc(d)}</span></div>' for k, d in keys) + '</div>'),
         section('saving', 'Saving your game', SAVING),
         section('tips', 'Tips', TIPS),
         section('web', 'Playing in the browser', WEB),
         section('credits', 'Credits', CREDITS),
         section('version', 'About this version', VERSION)]
out = sys.argv[1] if len(sys.argv) > 1 else '.'
open(os.path.join(out, 'help.html'), 'w').write('\n'.join(parts))
