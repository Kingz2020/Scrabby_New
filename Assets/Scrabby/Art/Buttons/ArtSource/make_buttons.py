"""Draws the glossy button faces: white shaded in greys, and one blue.

Built to look thick, the way a chunky game button does, with the light coming
from above:

    outline   a dark line round the whole shape, cutting it out of the sky
    base      the button's thickness below the face - deep, in two bands of
              shade, like the two sloped sides underneath a key
    chamfer   the sloped edge of the face, lit according to which way it
              faces: the upward slopes in two steps of light, the downward
              slopes in two steps of shade
    face      the flat top, near-white shading to grey, darker along its
              bottom, with a gloss across the upper half

Deliberately overdone: at the size a button is shown on a phone, subtle
shading reads as flat.

Each is a 9-slice sprite: the rounded ends and the top and bottom bands are
kept as drawn and only a narrow strip in the middle stretches.

    python make_buttons.py ../../../../Resources/Buttons

No image library: every layer is a signed distance, rendered at 3x and
averaged down.
"""

import math
import os
import struct
import sys
import zlib

W, H = 256, 150
SS = 3

DEPTH = 28.0        # how far the base shows below the face
FACE = H - DEPTH    # height of the face
OUTLINE = 3.0       # dark line round everything
CHAMFER = 13.0      # width of the sloped edge
LIP = 0.26          # share of the flat face that is the darker bottom band

STYLES = {
    "white": dict(
        outline=(34, 38, 46),
        base_upper=(112, 120, 130), base_lower=(58, 64, 74),
        light1=(255, 255, 255), light2=(226, 230, 236),
        shade1=(160, 168, 178), shade2=(104, 112, 122),
        top=(246, 247, 249), bot=(200, 207, 216), lip=(172, 180, 190),
        gloss=0.9),

    "blue": dict(
        outline=(10, 32, 70),
        base_upper=(22, 74, 140), base_lower=(10, 42, 92),
        light1=(206, 236, 255), light2=(132, 196, 250),
        shade1=(38, 104, 190), shade2=(20, 66, 140),
        top=(118, 192, 255), bot=(48, 132, 222), lip=(30, 98, 186),
        gloss=0.5),
}


def pill(px, py, top, height, inset):
    """Signed distance to a pill of the given height, starting at top, and
    the vertical part of the direction outward from its middle - which is
    what says whether a point on its edge faces up or down."""
    r = height / 2.0 - inset
    cx0 = height / 2.0
    cx1 = W - height / 2.0
    cy = top + height / 2.0

    x = min(max(px, cx0), cx1)
    dx, dy = px - x, py - cy
    dist = math.hypot(dx, dy)

    ny = dy / dist if dist > 1e-6 else 0.0

    return dist - r, ny


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def cover(d):
    return max(0.0, min(1.0, 0.5 - d))


def step(edge, value, soft=0.06):
    """0 below edge, 1 above, with a narrow blend so the facets are distinct
    without their boundaries stair-stepping."""
    return max(0.0, min(1.0, (value - edge) / soft + 0.5))


def smooth(t):
    """Eased 0..1, so a blend starts and finishes gently instead of at an
    edge the eye picks out."""
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def facet(style, ny):
    """The chamfer's colour for a slope facing ny (-1 straight up, +1 down):
    the two lights above and the two shades below, blended continuously from
    one to the next rather than switched - the switch was what showed as
    steps."""
    stops = [(-1.00, style["light1"]),
             (-0.30, style["light2"]),
             ( 0.25, style["shade1"]),
             ( 1.00, style["shade2"])]

    for i in range(len(stops) - 1):
        a_at, a_col = stops[i]
        b_at, b_col = stops[i + 1]

        if ny <= b_at:
            return mix(a_col, b_col, smooth((ny - a_at) / (b_at - a_at)))

    return stops[-1][1]


def shade(px, py, style):
    face_d, face_ny = pill(px, py, 0.0, FACE, 0.0)
    base_d, _ = pill(px, py, DEPTH, FACE, 0.0)

    whole = min(face_d, base_d)          # the silhouette: face and base together

    if whole > 0.5:
        return (0, 0, 0, 0.0)

    alpha = cover(whole)
    colour = style["outline"]

    # ---- the base: thickness, in two bands of shade ----
    inside = min(face_d, base_d) + OUTLINE
    if inside < 0.5:
        # How far below the face this point is, as a share of the depth.
        below = max(0.0, face_d) / DEPTH
        base = mix(style["base_upper"], style["base_lower"], smooth(below))
        colour = mix(colour, base, cover(inside))

    # ---- the face ----
    fd, ny = pill(px, py, 0.0, FACE, OUTLINE)

    if fd < 0.5:
        # the chamfer
        colour = mix(colour, facet(style, ny), cover(fd))

        # the flat top, inside the chamfer
        flat_d, _ = pill(px, py, 0.0, FACE, OUTLINE + CHAMFER)

        if flat_d < 3.0:
            inset = OUTLINE + CHAMFER
            fv = max(0.0, min(1.0, (py - inset) / (FACE - 2 * inset)))

            flat = mix(style["top"], style["bot"], fv)

            if fv > 1.0 - LIP:
                flat = mix(flat, style["lip"], (fv - (1.0 - LIP)) / LIP)

            g_d = gloss(px, py, inset)
            if g_d < 3.0:
                fade = 1.0 - max(0.0, min(1.0, fv / 0.5))
                flat = mix(flat, (255, 255, 255),
                           style["gloss"] * fade * smooth(-g_d / 4.0 + 0.5))

            # Blended in over a few pixels rather than one, so the sloped
            # edge rolls onto the flat top instead of meeting it at a line.
            colour = mix(colour, flat, smooth(-flat_d / 5.0 + 0.5))

    return (colour[0], colour[1], colour[2], alpha)


def gloss(px, py, inset):
    top = inset + 3.0
    height = (FACE - 2 * inset) * 0.46
    r = height / 2.0

    side = inset + 10.0
    cx0 = side + r
    cx1 = W - side - r
    cy = top + r

    x = min(max(px, cx0), cx1)

    return math.hypot(px - x, py - cy) - r


def render(style):
    rows = []

    for y in range(H):
        row = bytearray([0])

        for x in range(W):
            r = g = b = a = 0.0

            for sy in range(SS):
                for sx in range(SS):
                    cr, cg, cb, ca = shade(x + (sx + 0.5) / SS, y + (sy + 0.5) / SS, style)
                    r += cr * ca
                    g += cg * ca
                    b += cb * ca
                    a += ca

            n = float(SS * SS)
            a /= n

            if a > 0:
                r, g, b = r / n / a, g / n / a, b / n / a

            row += bytes((int(min(255, r)), int(min(255, g)), int(min(255, b)),
                          int(min(255, a * 255))))

        rows.append(bytes(row))

    return b"".join(rows)


def write_png(path, raw):
    def chunk(kind, data):
        body = kind + data
        return (struct.pack(">I", len(data)) + body +
                struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF))

    blob = (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(raw, 9)) +
            chunk(b"IEND", b""))

    with open(path, "wb") as out:
        out.write(blob)

    print("wrote", path)


# ------------------------------------------------------------------ icons --
#
# The board's two small buttons had their icons painted onto white plates, so
# the icons could not be lifted off and put on the new face. These are drawn
# fresh, in the same dark ink as the lettering, on nothing.

ICON = 128
INK = (44, 52, 64)
STROKE = 11.0


def capsule(px, py, ax, ay, bx, by, r):
    pax, pay = px - ax, py - ay
    bax, bay = bx - ax, by - ay
    t = max(0.0, min(1.0, (pax * bax + pay * bay) / float(bax * bax + bay * bay)))
    return math.hypot(pax - bax * t, pay - bay * t) - r


def triangle(px, py, a, b, c):
    """Signed distance to a triangle - negative inside."""
    def edge(p, q):
        ex, ey = q[0] - p[0], q[1] - p[1]
        vx, vy = px - p[0], py - p[1]
        t = max(0.0, min(1.0, (vx * ex + vy * ey) / (ex * ex + ey * ey)))
        dx, dy = vx - ex * t, vy - ey * t
        return dx * dx + dy * dy, ex * vy - ey * vx

    d0, s0 = edge(a, b)
    d1, s1 = edge(b, c)
    d2, s2 = edge(c, a)

    d = math.sqrt(min(d0, d1, d2))
    inside = (s0 > 0 and s1 > 0 and s2 > 0) or (s0 < 0 and s1 < 0 and s2 < 0)

    return -d if inside else d


def chevrons(px, py):
    """Two chevrons pointing down: bring the tiles back to the rack."""
    r = STROKE / 2.0
    d = 1e9

    for top in (34.0, 62.0):
        d = min(d, capsule(px, py, 38.0, top, 64.0, top + 26.0, r))
        d = min(d, capsule(px, py, 90.0, top, 64.0, top + 26.0, r))

    return d


def arc(px, py, cx, cy, radius, start, end):
    """A ring segment from angle start to end, going clockwise on screen
    (angles in degrees, 0 to the right, 90 straight up)."""
    dx, dy = px - cx, cy - py                # y flipped, so 90 is up
    angle = math.degrees(math.atan2(dy, dx)) % 360.0

    # Clockwise from start to end means decreasing angle.
    span = (start - end) % 360.0
    along = (start - angle) % 360.0

    if along <= span:
        return abs(math.hypot(dx, dy) - radius) - STROKE / 2.0

    # Past the ends: round caps.
    def at(deg):
        return (cx + radius * math.cos(math.radians(deg)),
                cy - radius * math.sin(math.radians(deg)))

    sx, sy = at(start)
    ex, ey = at(end)

    return min(math.hypot(px - sx, py - sy), math.hypot(px - ex, py - ey)) - STROKE / 2.0


def arrowhead(cx, cy, radius, deg):
    """A triangle at the clockwise end of an arc, pointing the way it turns."""
    t = math.radians(deg)
    ox, oy = cx + radius * math.cos(t), cy - radius * math.sin(t)

    # clockwise tangent on screen
    tx, ty = math.sin(t), math.cos(t)
    nx, ny = math.cos(t), -math.sin(t)

    size = 17.0
    tip = (ox + tx * size, oy + ty * size)
    left = (ox + nx * size * 0.95, oy + ny * size * 0.95)
    right = (ox - nx * size * 0.95, oy - ny * size * 0.95)

    return tip, left, right


def turning(px, py):
    """Two arrows chasing each other round a circle: shuffle."""
    cx, cy, radius = 64.0, 64.0, 36.0

    d = arc(px, py, cx, cy, radius, 165.0, 30.0)       # over the top
    d = min(d, arc(px, py, cx, cy, radius, 345.0, 210.0))  # under the bottom

    for deg in (30.0, 210.0):
        tip, left, right = arrowhead(cx, cy, radius, deg)
        d = min(d, triangle(px, py, tip, left, right) - 1.5)

    return d


def render_icon(shape):
    rows = []

    for y in range(ICON):
        row = bytearray([0])

        for x in range(ICON):
            a = 0.0

            for sy in range(SS):
                for sx in range(SS):
                    a += cover(shape(x + (sx + 0.5) / SS, y + (sy + 0.5) / SS))

            a /= float(SS * SS)
            row += bytes((INK[0], INK[1], INK[2], int(min(255, a * 255))))

        rows.append(bytes(row))

    return b"".join(rows)


def write_icon(path, raw):
    def chunk(kind, data):
        body = kind + data
        return (struct.pack(">I", len(data)) + body +
                struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF))

    blob = (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", ICON, ICON, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(raw, 9)) +
            chunk(b"IEND", b""))

    with open(path, "wb") as out:
        out.write(blob)

    print("wrote", path)


if __name__ == "__main__":
    folder = sys.argv[1] if len(sys.argv) > 1 else "."

    if not os.path.isdir(folder):
        os.makedirs(folder)

    for name, style in STYLES.items():
        write_png(os.path.join(folder, "Button_" + name + ".png"), render(style))

    write_icon(os.path.join(folder, "Icon_return.png"), render_icon(chevrons))
    write_icon(os.path.join(folder, "Icon_shuffle.png"), render_icon(turning))
