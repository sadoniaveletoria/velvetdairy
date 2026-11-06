"""
Generates Velvet Dairy's sound effects (procedural, no samples).

    python3 make_sounds.py <output Assets/sounds folder>

pump_loop.wav   milking machine: soft vacuum-pump drone, a rhythmic suction "whoosh" with a light release puff,
                and little milk-flow bubbles (loops seamlessly)
pump_start.wav  pump spinning up
pump_stop.wav   pump winding down with an air release
squirt.wav      a hand-milking squirt into a pail
"""
import os
import sys
import wave

import numpy as np
from scipy import signal

OUT = sys.argv[1] if len(sys.argv) > 1 else "sounds"
SR = 44100
rng = np.random.default_rng(11)


# ---------------------------------------------------------------- building blocks

def noise(n):
    return rng.standard_normal(n)


def pink(n):
    """Softer than white noise: falls off toward the highs."""
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1, -2.494956002, 2.017265875, -0.522189400]
    return signal.lfilter(b, a, noise(n)) * 4


def lowpass(x, hz, order=2):
    sos = signal.butter(order, hz, "low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def highpass(x, hz, order=2):
    sos = signal.butter(order, hz, "high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], "band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def sweep_band(x, centers, q=2.5):
    """Band-pass whose centre moves over time (one biquad per block)."""
    out = np.zeros_like(x)
    block = 256
    zi = np.zeros(2)
    for s in range(0, len(x), block):
        c = float(np.clip(centers[min(s, len(centers) - 1)], 60, SR * 0.45))
        w0 = 2 * np.pi * c / SR
        alpha = np.sin(w0) / (2 * q)
        b = np.array([alpha, 0, -alpha]) / (1 + alpha)
        a = np.array([1, -2 * np.cos(w0) / (1 + alpha), (1 - alpha) / (1 + alpha)])
        y, zi = signal.lfilter(b, a, x[s:s + block], zi=zi)
        out[s:s + block] = y
    return out


def smooth_env(n, attack, release, hold=0.0):
    """Raised-cosine attack and release, so nothing clicks."""
    t = np.arange(n) / SR
    e = np.ones(n)
    a = t < attack
    e[a] = 0.5 - 0.5 * np.cos(np.pi * t[a] / attack)
    r0 = attack + hold
    r = t >= r0
    e[r] = np.clip(0.5 + 0.5 * np.cos(np.pi * np.clip((t[r] - r0) / release, 0, 1)), 0, 1)
    return e


def norm(x, peak):
    m = np.max(np.abs(x)) or 1
    return x / m * peak


def bubble(dur=0.05, f0=420):
    """A small liquid 'blip': a sine whose pitch rises quickly as it fades."""
    n = int(SR * dur)
    t = np.arange(n) / SR
    f = f0 * (1 + 2.2 * t / dur)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t / (dur * 0.35)) * smooth_env(n, 0.003, dur * 0.9)


def add(x, y, at):
    at = int(at)
    if at >= len(x):
        return
    k = min(len(y), len(x) - at)
    x[at:at + k] += y[:k]


def save(name, x):
    x = np.clip(x, -1, 1)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())


def loopable(x, fade):
    """Crossfade the tail into the head so the file loops without a seam."""
    f = int(SR * fade)
    head, body, tail = x[:f], x[f:-f], x[-f:]
    w = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, f))
    blended = tail * (1 - w) + head * w
    return np.concatenate([blended, body])


# ---------------------------------------------------------------- the machine

def drone(n, wobble=True):
    """Vacuum pump: a warm, low whir with a slow wobble. No buzzy harmonics."""
    t = np.arange(n) / SR
    whir = lowpass(pink(n), 380, 3)
    whir *= 1 + (0.12 * np.sin(2 * np.pi * 0.7 * t) if wobble else 0)
    tone = np.sin(2 * np.pi * 96 * t + 0.4 * np.sin(2 * np.pi * 0.9 * t)) * 0.18
    return norm(whir, 0.5) + tone * 0.5


def suction(n, beat):
    """Each beat: a soft rising 'whoo' as the liner pulls, then a light puff as it lets go."""
    out = np.zeros(n)
    beats = int(round(n / (SR * beat)))
    for b in range(beats):
        s = int(SR * beat * b)
        # Pull: pink noise through a band that slides up, with a smooth swell
        pn = int(SR * beat * 0.55)
        centers = np.linspace(260, 820, pn)
        pull = sweep_band(pink(pn), centers, q=3.0) * smooth_env(pn, 0.16, 0.28, 0.08)
        add(out, norm(pull, 0.55), s)
        # Release: a short airy puff and a soft rubbery tock (low, rounded, not a click)
        rs = s + int(SR * beat * 0.58)
        rn = int(SR * 0.22)
        puff = bandpass(noise(rn), 900, 3200) * smooth_env(rn, 0.01, 0.17)
        add(out, norm(puff, 0.16), rs)
        tn = int(SR * 0.08)
        tt = np.arange(tn) / SR
        tock = np.sin(2 * np.pi * (180 - 600 * tt) * tt) * np.exp(-tt / 0.018)
        add(out, lowpass(tock, 900) * 0.22, rs)
        # Milk flowing: a few small bubbles during the pull
        for _ in range(int(rng.integers(2, 5))):
            at = s + int(SR * beat * rng.uniform(0.12, 0.5))
            add(out, bubble(rng.uniform(0.035, 0.07), rng.uniform(330, 620)) * rng.uniform(0.05, 0.1), at)
    return out


def pump_loop():
    beat = 1.0                               # one pulsation a second
    beats = 4
    fade = 0.25
    n = int(SR * (beat * beats + fade))
    x = highpass(drone(n) * 0.55 + suction(n, beat), 45)
    return norm(loopable(x, fade), 0.6)   # crossfade last, after filtering, so the seam stays clean


def pump_start():
    n = int(SR * 1.3)
    t = np.arange(n) / SR
    # Motor spins up: the whir brightens and swells
    centers = np.linspace(120, 420, n)
    whir = sweep_band(pink(n), centers, q=1.2) * smooth_env(n, 0.9, 0.3, 0.1)
    tone = np.sin(2 * np.pi * np.cumsum(np.linspace(40, 96, n)) / SR) * smooth_env(n, 0.6, 0.3, 0.4) * 0.12
    tn = int(SR * 0.1)
    tt = np.arange(tn) / SR
    thunk = lowpass(np.sin(2 * np.pi * 110 * tt) * np.exp(-tt / 0.03), 500) * 0.35
    x = norm(whir, 0.4) + tone
    add(x, thunk, 0)
    return norm(highpass(x, 40), 0.55)


def pump_stop():
    n = int(SR * 1.4)
    centers = np.linspace(420, 110, n)
    whir = sweep_band(pink(n), centers, q=1.2) * smooth_env(n, 0.02, 1.1, 0.1)
    # Air let out of the line
    rn = int(SR * 0.7)
    release = bandpass(pink(rn), 500, 2600) * smooth_env(rn, 0.03, 0.55, 0.05)
    x = norm(whir, 0.35)
    add(x, norm(release, 0.3), int(SR * 0.05))
    return norm(highpass(x, 40), 0.5)


def squirt():
    """A stream of milk hitting milk in a pail: a soft 'shh' with a frothy tail, not a hiss."""
    n = int(SR * 0.55)
    jet = bandpass(pink(n), 1200, 4200) * smooth_env(n, 0.012, 0.16, 0.06)
    froth = bandpass(pink(n), 350, 1300) * smooth_env(n, 0.03, 0.3, 0.05)
    x = norm(jet, 0.35) + norm(froth, 0.25)
    for _ in range(5):
        add(x, bubble(rng.uniform(0.03, 0.06), rng.uniform(450, 900)) * rng.uniform(0.04, 0.08), SR * rng.uniform(0.05, 0.3))
    return norm(highpass(x, 120), 0.5)


def main():
    os.makedirs(OUT, exist_ok=True)
    save("pump_loop.wav", pump_loop())
    save("pump_start.wav", pump_start())
    save("pump_stop.wav", pump_stop())
    save("squirt.wav", squirt())
    print("done")


if __name__ == "__main__":
    main()
