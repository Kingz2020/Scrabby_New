"""Draws the tutorial hand - the pointing finger that taps things for you.

No image library and no artist: the hand is four rounded shapes described as
signed distances (a capsule for the finger, one for the thumb, a rounded box
for the fist, a bump for the knuckles), unioned together, then rendered at
three times the final size and averaged down for clean edges.

Signed distance means every pixel knows how far it is from the shape's edge,
which gives the outline and the antialiasing for free: inside is white, the
last few pixels before the edge are the dark outline, and the edge itself
fades over one pixel.

    python make_hand.py ../../../../Resources/Tutorial/hand.png
"""

import math
import os
import struct
import sys
import zlib

W, H = 256, 320
SS = 3                      # supersampling: drawn at 3x, averaged down

FILL = (255, 255, 255)
LINE = (26, 36, 48)
OUTLINE = 7.0               # how thick the dark edge is, in final pixels
SHADOW = (8, 20, 34)


def capsule(px, py, ax, ay, bx, by, r):
    """Distance from a point to a line with round ends - a finger."""
    pax, pay = px - ax, py - ay
    bax, bay = bx - ax, by - ay

    along = (pax * bax + pay * bay) / float(bax * bax + bay * bay)
    along = max(0.0, min(1.0, along))

    dx = pax - bax * along
    dy = pay - bay * along

    return math.sqrt(dx * dx + dy * dy) - r


def rounded_box(px, py, cx, cy, hw, hh, r):
    dx = abs(px - cx) - (hw - r)
    dy = abs(py - cy) - (hh - r)

    outside = math.sqrt(max(dx, 0.0) ** 2 + max(dy, 0.0) ** 2)

    return outside + min(max(dx, dy), 0.0) - r


def hand(px, py):
    """The whole hand, as one distance: nearest of its parts.

    The index finger sits to the LEFT of the fist, not on top of it, and the
    folded fingers show as knuckles to its right. A finger centred over the
    fist with nothing beside it does not read as pointing - it reads as a
    gesture nobody wants in a children's word game, which is exactly what the
    first attempt drew.
    """
    # The pointing finger, up the left side.
    d = capsule(px, py, 102, 48, 102, 158, 27)

    # The fist, sitting to its right.
    d = min(d, rounded_box(px, py, 148, 226, 62, 60, 36))

    # Three folded fingers, as knuckles along the top of the fist.
    d = min(d, capsule(px, py, 150, 176, 150, 196, 26))
    d = min(d, capsule(px, py, 186, 186, 186, 202, 24))
    d = min(d, capsule(px, py, 212, 200, 212, 214, 21))

    # The thumb, across the front of the fist.
    d = min(d, capsule(px, py, 96, 232, 78, 198, 23))

    return d


def shade(d):
    """Colour and coverage for one distance sample."""
    if d > 0.5:
        return None

    # Antialias across the last half pixel.
    alpha = 1.0 if d < -0.5 else 0.5 - d

    colour = LINE if d > -OUTLINE else FILL

    return colour, alpha


def render():
    width, height = W * SS, H * SS
    scale = float(SS)

    # Accumulate at full resolution, then average blocks of SS x SS.
    big = [[(0, 0, 0, 0.0)] * width for _ in range(height)]

    for y in range(height):
        py = (y + 0.5) / scale

        for x in range(width):
            px = (x + 0.5) / scale

            # The shadow is the same hand, moved down and fattened, which is
            # a cheap and convincing blur at this size.
            ds = hand(px - 3.0, py - 9.0) - 2.0
            pixel = (0, 0, 0, 0.0)

            if ds < 6.0:
                fade = 1.0 if ds < 0.0 else 1.0 - ds / 6.0
                pixel = (SHADOW[0], SHADOW[1], SHADOW[2], 0.30 * fade)

            got = shade(hand(px, py))

            if got is not None:
                colour, alpha = got
                # The hand over its own shadow.
                out_a = alpha + pixel[3] * (1.0 - alpha)
                if out_a > 0:
                    r = (colour[0] * alpha + pixel[0] * pixel[3] * (1.0 - alpha)) / out_a
                    g = (colour[1] * alpha + pixel[1] * pixel[3] * (1.0 - alpha)) / out_a
                    b = (colour[2] * alpha + pixel[2] * pixel[3] * (1.0 - alpha)) / out_a
                    pixel = (r, g, b, out_a)

            big[y][x] = pixel

    rows = []

    for y in range(H):
        row = bytearray()
        row.append(0)                                   # PNG filter: none

        for x in range(W):
            r = g = b = a = 0.0

            for sy in range(SS):
                for sx in range(SS):
                    pr, pg, pb, pa = big[y * SS + sy][x * SS + sx]
                    r += pr * pa
                    g += pg * pa
                    b += pb * pa
                    a += pa

            count = float(SS * SS)
            a /= count

            if a > 0:
                r = r / count / a
                g = g / count / a
                b = b / count / a

            row += bytes((int(max(0, min(255, r))), int(max(0, min(255, g))),
                          int(max(0, min(255, b))), int(max(0, min(255, a * 255)))))

        rows.append(bytes(row))

    return b"".join(rows)


def write_png(path, raw):
    def chunk(kind, data):
        body = kind + data
        return (struct.pack(">I", len(data)) + body +
                struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)   # 8-bit RGBA

    blob = (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", header) +
            chunk(b"IDAT", zlib.compress(raw, 9)) +
            chunk(b"IEND", b""))

    folder = os.path.dirname(os.path.abspath(path))

    if folder and not os.path.isdir(folder):
        os.makedirs(folder)

    with open(path, "wb") as out:
        out.write(blob)

    print("wrote %s (%d x %d, %.1f KB)" % (path, W, H, len(blob) / 1024.0))


if __name__ == "__main__":
    where = sys.argv[1] if len(sys.argv) > 1 else "hand.png"
    write_png(where, render())
