"""
Cuts the stall sounds from a steady milking-pump recording (made for Erdie's "Milk Pump Step02", freesound.org/s/49877).

    ffmpeg -i 49877__erdie__milk-pump-step02.flac -ac 1 -ar 44100 src.wav
    python3 cut_pump.py src.wav VelvetDairy/Assets/sounds

pump_start.wav  the 2.6 s before the loop, spun up from 55% speed, ending where the loop begins
pump_loop.wav   six whole pump cycles (~7 s) with a 40 ms crossfade so it repeats without a seam
pump_stop.wav   2.4 s after the loop, slowing to 35% and fading out
"""
import os
import sys
import wave

import numpy as np
from scipy import signal

SR = 44100
src, out = sys.argv[1], sys.argv[2]
with wave.open(src) as w:
    x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768
x = x - np.mean(x)

h = int(SR * 0.005)
e = np.sqrt(signal.convolve(x ** 2, np.ones(h) / h, "same"))
win = e[int(3 * SR):int(4.2 * SR)]
t0 = int(3 * SR) + int(np.argmin(signal.convolve(win, np.ones(2000) / 2000, "same")))
P, cycles = 1.16, 6
guess = t0 + int(cycles * P * SR)
L = int(0.08 * SR)
ref = x[t0:t0 + L]
best, t1 = -1, guess
for k in range(guess - int(0.06 * SR), guess + int(0.06 * SR)):
    c = np.dot(ref, x[k:k + L]) / (np.linalg.norm(ref) * np.linalg.norm(x[k:k + L]) + 1e-9)
    if c > best:
        best, t1 = c, k

F = int(0.04 * SR)
loop = x[t0:t1].copy()
wf = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, F))
loop[:F] = x[t1:t1 + F] * (1 - wf) + x[t0:t0 + F] * wf


def varispeed(seg, s0, s1):
    n = len(seg)
    pos = np.cumsum(np.linspace(s0, s1, n))
    pos = pos[pos < n - 1]
    return np.interp(pos, np.arange(n), seg)


def fade(n, a, r):
    f = np.ones(n)
    ai, ri = int(a * SR), int(r * SR)
    if ai:
        f[:ai] = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, ai))
    if ri:
        f[-ri:] = 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, ri))
    return f


start = varispeed(x[max(0, t0 - int(2.6 * SR)):t0], 0.55, 1.0)
start *= fade(len(start), 0.35, 0)
stop = varispeed(x[t1:t1 + int(2.4 * SR)], 1.0, 0.35)
stop *= fade(len(stop), 0.0, 1.1)

g = 0.85 / max(np.max(np.abs(a)) for a in (loop, start, stop))
for name, a in (("pump_loop.wav", loop), ("pump_start.wav", start), ("pump_stop.wav", stop)):
    with wave.open(os.path.join(out, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(a * g, -1, 1) * 32767).astype("<i2").tobytes())
print("done")
