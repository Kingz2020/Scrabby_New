"""The same notes, struck rather than beeped.

A piano note is not a sine: it is a stack of partials that are not quite
whole multiples of the fundamental (a real string is stiff, so the higher
partials run sharp), each dying at its own rate, with the top of the stack
going first - which is why a piano gets darker as it rings. Two strings per
note, tuned a whisker apart, give the slow beating that makes it sound like
an instrument rather than an oscillator. A hammer thump goes underneath.

Then a room: eight comb filters and two allpasses, the Schroeder arrangement,
with a little damping so the tail loses its top end like a real room does.
And a delay - one repeat, quiet, because these play over a game and not on
their own.
"""

import math
import os
import random
import struct
import sys
import wave

RATE = 44100

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
SHIFT = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
QUIET = 1.0e-4          # -80 dB: below this a partial is not worth computing


# --------------------------------------------------------------- the string --

def piano(buf, freq, amp, start=0.0, decay=2.4, partials=16, tilt=0.55,
          bright=1.35, attack=0.024, thump=0.035):
    """One note. decay is the fundamental's rate; partials above it go faster.

    attack is how long the note takes to arrive. A hard strike is a couple of
    milliseconds and cracks; a gentle one is twenty or thirty and blooms. Two
    other things go with a gentle touch on a real piano, and both are here:
    the upper partials arrive a little after the fundamental rather than all
    at once, and there is less of them to begin with.
    """
    begin = int(start * RATE)
    stiffness = 0.0004                      # inharmonicity, mid-piano

    for n in range(1, partials + 1):
        centre = freq * n * math.sqrt(1.0 + stiffness * n * n)

        if centre > 15000.0:
            break

        # A softer strike is a darker one: the top of the stack never gets
        # the energy it would from a hard hit.
        level = amp * bright / (n ** 1.55)
        rate = decay * (1.0 + tilt * (n - 1))

        # The higher the partial, the later it blooms - up to three times the
        # fundamental's rise, which is what takes the edge off the onset.
        rise = attack * min(3.0, 1.0 + 0.32 * (n - 1))

        # Two strings, a whisker apart: the beating between them is the piano.
        for offset, share in ((1.0, 0.56), (1.0003, 0.44)):
            f = centre * offset
            a = level * share
            last = int(math.log(QUIET / max(a, QUIET)) / -rate * RATE) if a > QUIET else 0
            end = min(len(buf), begin + max(0, last) + int(rise * RATE))
            step = 2.0 * math.pi * f / RATE

            for i in range(begin, end):
                t = (i - begin) / float(RATE)
                envelope = math.exp(-rate * t)

                # Raised cosine in, so there is no corner for the ear to hear.
                if t < rise:
                    envelope *= 0.5 - 0.5 * math.cos(math.pi * t / rise)

                buf[i] += a * envelope * math.sin(step * (i - begin))

    # The hammer. Quiet, and with its top end taken off - the fizz in a noise
    # burst is most of what reads as harsh.
    length = int(0.045 * RATE)
    knock = [0.0] * length
    smooth = 0.0
    keep = math.exp(-2.0 * math.pi * 1400.0 / RATE)

    for i in range(length):
        t = i / float(RATE)
        raw = amp * thump * math.exp(-70.0 * t) * (random.random() * 2.0 - 1.0)
        smooth = (1.0 - keep) * raw + keep * smooth

        if t < attack:
            smooth *= 0.5 - 0.5 * math.cos(math.pi * t / attack)

        knock[i] = smooth

    for i in range(length):
        if begin + i < len(buf):
            buf[begin + i] += knock[i]


# ----------------------------------------------------------------- the room --

COMBS = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617]
ALLPASS = [556, 441]


def reverb(buf, wet=0.22, room=0.84, damping=0.34):
    out = [0.0] * len(buf)

    for size in COMBS:
        line = [0.0] * size
        store = 0.0
        pos = 0

        for i in range(len(buf)):
            got = line[pos]
            # Damping: the tail loses its top end, as a room does.
            store = got * (1.0 - damping) + store * damping
            line[pos] = buf[i] + store * room
            pos = (pos + 1) % size
            out[i] += got

    for size in ALLPASS:
        line = [0.0] * size
        pos = 0

        for i in range(len(buf)):
            got = line[pos]
            line[pos] = out[i] + got * 0.5
            out[i] = got - out[i]
            pos = (pos + 1) % size

    scale = wet / len(COMBS)

    for i in range(len(buf)):
        buf[i] = buf[i] * (1.0 - wet * 0.5) + out[i] * scale


def delay(buf, seconds=0.165, feedback=0.28, wet=0.18):
    size = int(seconds * RATE)
    line = [0.0] * size
    pos = 0

    for i in range(len(buf)):
        got = line[pos]
        line[pos] = buf[i] + got * feedback
        pos = (pos + 1) % size
        buf[i] += got * wet


# ---------------------------------------------------------------- the notes --

def buffer(seconds):
    return [0.0] * int(RATE * seconds)


def note(buf, freq, amp, **kw):
    """One note of the set, at whatever octave this run is in.

    Dropping an octave costs more than pitch on a phone: the speaker cannot
    move air at the fundamental at all, and what reaches the ear is the
    partials above it, from which the ear rebuilds the pitch. So a lower set
    keeps more partials and keeps them alive longer, or it arrives thin.
    """
    if SHIFT < 1.0:
        kw["bright"] = kw.get("bright", 1.35) * 1.18
        kw["tilt"] = kw.get("tilt", 0.55) * 0.78
        kw["partials"] = kw.get("partials", 16) + 5

    return piano(buf, freq * SHIFT, amp, **kw)


def write(name, buf):
    peak = max(abs(s) for s in buf) or 1.0
    scale = 0.89 / peak

    frames = b"".join(
        struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767))
        for s in buf)

    with wave.open(os.path.join(OUT, name), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(frames)

    print("%-22s %6.0f ms" % (name, 1000.0 * len(buf) / RATE))


# Notes, and why these notes: a phone speaker has almost nothing below about
# 400 Hz, so the low end of the old set was being thrown away. Everything here
# sits where a phone can actually reproduce it.
D4, E4, F4, Gb4 = 293.66, 329.63, 349.23, 369.99
A4, B4, C5, D5 = 440.00, 493.88, 523.25, 587.33
E5, G5, A5, B5 = 659.25, 783.99, 880.00, 987.77
C6, E6 = 1046.50, 1318.51

random.seed(23)

# 1. a tile going down: one note, damped almost at once - a key played with
#    the damper still on it. Short, because it is the sound of the game.
tile = buffer(0.55)
note(tile, D5, 0.62, decay=19.0, partials=8, tilt=0.8, bright=1.0,
     attack=0.012, thump=0.05)
reverb(tile, wet=0.17, room=0.78, damping=0.42)
delay(tile, seconds=0.105, feedback=0.16, wet=0.10)
write("tile_place.wav", tile)

# 2. the word is good: a rising fifth, quiet - forty times a game.
good = buffer(1.15)
note(good, E5, 0.50, start=0.0, decay=3.1)
note(good, B5, 0.46, start=0.085, decay=3.3)
reverb(good, wet=0.24, room=0.85, damping=0.32)
delay(good, seconds=0.155, feedback=0.26, wet=0.17)
write("word_accepted.wav", good)

# 3. the word is not: a minor second, both notes at once. Dissonant, low in
#    the hand, over quickly - a wrong word is not a telling-off.
bad = buffer(0.95)
note(bad, D4, 0.55, decay=5.2, partials=12, bright=1.0)
note(bad, Gb4 * 0.9438, 0.50, decay=5.4, partials=12, bright=1.0)   # E flat 4
reverb(bad, wet=0.18, room=0.80, damping=0.45)
delay(bad, seconds=0.13, feedback=0.18, wet=0.11)
write("word_rejected.wav", bad)

# 4. the round is yours: C E G C up, the last one left to ring.
won = buffer(2.0)
for step, pitch in enumerate((C5, E5, G5, C6)):
    note(won, pitch, 0.46, start=step * 0.088,
         decay=2.6 if step < 3 else 1.5)
reverb(won, wet=0.27, room=0.86, damping=0.30)
delay(won, seconds=0.175, feedback=0.30, wet=0.20)
write("round_won.wav", won)

# 5. and the round is not: the same shape coming down, darker, and it does
#    not resolve - B, G, E over an A, which leans rather than lands.
lost = buffer(1.9)
for step, pitch in enumerate((B4, G5 * 0.7943, E4 * 2.0)):            # B4 G4 E5
    note(lost, pitch, 0.44, start=step * 0.095, decay=2.9, bright=1.05)
note(lost, A4, 0.26, start=0.30, decay=2.2, bright=0.95)
reverb(lost, wet=0.25, room=0.85, damping=0.40)
delay(lost, seconds=0.185, feedback=0.24, wet=0.16)
write("round_lost.wav", lost)

# 6. all six letters: six notes up, the top two together, and the room left
#    open. Twenty points a handful of times a game deserves the flourish.
bonus = buffer(2.9)
for step, pitch in enumerate((C5, D5, E5, G5, A5, C6)):
    note(bonus, pitch, 0.42, start=step * 0.072, decay=2.4)
note(bonus, E6, 0.34, start=6 * 0.072, decay=1.4)
reverb(bonus, wet=0.32, room=0.88, damping=0.26)
delay(bonus, seconds=0.21, feedback=0.34, wet=0.24)
write("all_six_bonus.wav", bonus)
