#!/usr/bin/env python3
"""Synthesizes the TraumaRL web sound effects (TraumaRL has none of its own) into
<out>/sound/*.wav: short, quiet, made here, no third-party assets.
Names are the ones the game sends (RvipInput.Sound): hit miss hurt kill fire
pickup elevator death win.   python3 web/make-sounds.py web/dist"""
import math, os, random, struct, sys, wave

RATE = 22050
out = os.path.join(sys.argv[1] if len(sys.argv) > 1 else '.', 'sound')
os.makedirs(out, exist_ok=True)
rnd = random.Random(4)


def env(i, n, a=0.01, r=0.6):
    t = i / n
    return min(1.0, t / a) * (1 - t) ** (1 / max(r, 0.05))


def tone(freqs, dur, kind='sine', vol=0.35, noise=0.0):
    n = int(RATE * dur)
    out, ph = [], 0.0
    for i in range(n):
        f = freqs[min(len(freqs) - 1, int(i / n * len(freqs)))] if isinstance(freqs, list) else freqs(i / n)
        ph += 2 * math.pi * f / RATE
        s = math.sin(ph) if kind == 'sine' else (1 if math.sin(ph) > 0 else -1) * 0.5
        s = s * (1 - noise) + (rnd.random() * 2 - 1) * noise
        out.append(s * env(i, n) * vol)
    return out


def write(name, samples):
    with wave.open(os.path.join(out, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, s)) * 32000)) for s in samples))


write('hit', tone(lambda t: 220 - 120 * t, 0.12, noise=0.6))
write('miss', tone(lambda t: 900 - 500 * t, 0.1, vol=0.12, noise=0.8))
write('hurt', tone(lambda t: 140 - 60 * t, 0.2, kind='square', vol=0.3, noise=0.3))
write('kill', tone(lambda t: 300 - 250 * t, 0.3, kind='square', vol=0.25, noise=0.4))
write('fire', tone(lambda t: 1400 - 1200 * t, 0.15, kind='square', vol=0.2, noise=0.2))
write('pickup', tone([660, 990], 0.14))
write('elevator', tone(lambda t: 260 + 260 * t, 0.7, vol=0.25))
write('death', tone(lambda t: 330 * (1 - 0.7 * t), 1.1, kind='square', vol=0.3))
write('win', tone([523, 659, 784, 1047], 0.6, vol=0.3))
