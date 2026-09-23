"""Muffled tile sounds: more lows, fewer highs.

Built on the same piano string as the game's sounds (synth_piano.py), with
three things added for a muffled touch:
  - a steep low-pass (several one-pole stages) so the top is really gone,
  - a rounder, lower knock underneath - a tile landing on cloth, not glass,
  - less room and no delay, because a tail keeps the highs hanging about.

D ("deep and soft") is the one in the game as tile_place.wav, chosen
2026-09-21. The others are kept to go back to.

    python tile_sounds.py synth_piano.py <out folder>
"""

import math
import os
import random
import sys

SRC = sys.argv[1]            # synth_piano.py
OUT = sys.argv[2]

sys.argv = [SRC, OUT, "1.0"]
code = open(SRC, encoding="utf-8").read().split("# Notes, and why")[0]
exec(code)                   # piano, reverb, delay, buffer, write, RATE

os.makedirs(OUT, exist_ok=True)


def lowpass(buf, cutoff, stages=2):
    """Each stage takes off 6 dB per octave above cutoff."""
    k = math.exp(-2.0 * math.pi * cutoff / RATE)

    for _ in range(stages):
        y = 0.0
        for i in range(len(buf)):
            y = (1.0 - k) * buf[i] + k * y
            buf[i] = y


def knock(buf, freq, amp, decay, attack=0.004):
    """A low, round body thump: a sine that drops a little in pitch as it
    dies, which is what makes a thud sound like a thing landing."""
    phase = 0.0
    length = int(min(len(buf), RATE * 8.0 / decay))

    for i in range(length):
        t = i / float(RATE)
        f = freq * (1.0 + 0.35 * math.exp(-t * 40.0))
        phase += 2.0 * math.pi * f / RATE
        env = math.exp(-decay * t)

        if t < attack:
            env *= 0.5 - 0.5 * math.cos(math.pi * t / attack)

        buf[i] += amp * env * math.sin(phase)


D3, G3, A3, D4 = 146.83, 196.00, 220.00, 293.66

random.seed(23)

# A - a little muffled: the same note an octave down, top taken off.
a = buffer(0.45)
piano(a, G3, 0.62, decay=17.0, partials=7, tilt=0.9, bright=1.1,
      attack=0.016, thump=0.03)
knock(a, 150.0, 0.18, decay=30.0)
lowpass(a, 1400.0, stages=2)
reverb(a, wet=0.10, room=0.70, damping=0.55)
write("A_little_muffled.wav", a)

# B - muffled: felt on wood. Lower cut-off, softer strike, more body.
b = buffer(0.45)
piano(b, G3, 0.55, decay=20.0, partials=5, tilt=1.0, bright=1.0,
      attack=0.022, thump=0.02)
knock(b, 130.0, 0.30, decay=26.0)
lowpass(b, 900.0, stages=3)
reverb(b, wet=0.08, room=0.65, damping=0.65)
write("B_muffled.wav", b)

# C - very muffled: mostly the thud, a hint of note.
c = buffer(0.40)
piano(c, D3, 0.45, decay=22.0, partials=4, tilt=1.1, bright=1.0,
      attack=0.028, thump=0.0)
knock(c, 115.0, 0.45, decay=24.0, attack=0.006)
lowpass(c, 650.0, stages=3)
reverb(c, wet=0.06, room=0.60, damping=0.70)
write("C_very_muffled.wav", c)

# D - deep and soft: like B, a fourth lower and a slower bloom.
d = buffer(0.45)
piano(d, D3, 0.60, decay=18.0, partials=6, tilt=0.95, bright=1.05,
      attack=0.030, thump=0.02)
knock(d, 110.0, 0.28, decay=22.0, attack=0.008)
lowpass(d, 800.0, stages=3)
reverb(d, wet=0.08, room=0.65, damping=0.65)
write("D_deep_soft.wav", d)
