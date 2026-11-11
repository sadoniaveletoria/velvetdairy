"""Plugin icon for Eorzea Milkers (512x512): rose tile with soft cow spots, cream seal, milk bottle."""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_dairy_art import ROSE, BLUE, CREAM, line_mask, grad_fill, solid, shadow

S = 1024  # drawn at 2x, saved at 512
UI = sys.argv[1] if len(sys.argv) > 1 else "VelvetDairy/Assets/ui"
OUT = sys.argv[2] if len(sys.argv) > 2 else "VelvetDairy/images/icon.png"

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# Rounded tile
tile = line_mask((S, S), lambda d: d.rounded_rectangle([16, 16, S - 16, S - 16], radius=200, fill=255))
img.alpha_composite(grad_fill(tile, (232, 186, 192), (176, 104, 120)))

# Soft cow spots, clipped to the tile
spots = Image.new("L", (S, S), 0)
d = ImageDraw.Draw(spots)
rng = np.random.default_rng(7)
for cx, cy, r in [(170, 190, 120), (860, 150, 95), (900, 780, 140), (120, 860, 110), (600, 70, 60), (60, 520, 70)]:
    pts = []
    for k in range(9):
        a = 2 * math.pi * k / 9
        rr = r * (0.85 + 0.25 * rng.random())
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    d.polygon(pts, fill=255)
spots = spots.filter(ImageFilter.GaussianBlur(40)).point(lambda v: 255 if v > 110 else 0).filter(ImageFilter.GaussianBlur(3))
spots = Image.fromarray(np.minimum(np.array(spots), np.array(tile)).astype(np.uint8))
img.alpha_composite(solid(spots, CREAM, 0.22))

# Inner frame line
frame = line_mask((S, S), lambda d: d.rounded_rectangle([56, 56, S - 56, S - 56], radius=165, outline=255, width=5))
img.alpha_composite(solid(frame, (255, 246, 240), 0.55))

# Cream seal
c = S / 2
disc = line_mask((S, S), lambda d: d.ellipse([c - 360, c - 360, c + 360, c + 360], fill=255))
img.alpha_composite(shadow(grad_fill(disc, CREAM, CREAM), 10, 22, 0.30))
img.alpha_composite(grad_fill(disc, (196, 214, 230), (132, 160, 190)))
rule = line_mask((S, S), lambda d: d.ellipse([c - 330, c - 330, c + 330, c + 330], outline=255, width=6))
img.alpha_composite(solid(rule, (250, 245, 236), 0.75))

# Milk bottle, 80% full
back = Image.open(os.path.join(UI, "bottle1_back.png")).convert("RGBA")
milk = Image.open(os.path.join(UI, "bottle1_milk.png")).convert("RGBA")
front = Image.open(os.path.join(UI, "bottle1_front.png")).convert("RGBA")
bw, bh = back.size
top, bottom = 0.20 * bh, 0.96 * bh
cut = int(bottom - 0.8 * (bottom - top))
m = np.array(milk)
m[:cut, :, 3] = 0
milk = Image.fromarray(m)
bottle = Image.new("RGBA", (bw, bh), (0, 0, 0, 0))
for layer in (back, milk, front):
    bottle.alpha_composite(layer)
scale = 620 / bh
bottle = bottle.resize((int(bw * scale), int(bh * scale)), Image.LANCZOS)
pos = (int(c - bottle.width / 2), int(c - bottle.height / 2 + 6))
sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sh.alpha_composite(bottle, pos)
img.alpha_composite(shadow(sh, 8, 14, 0.25))
img.alpha_composite(sh)

# Little heart sparkle
hx, hy, hr = c + 225, c - 205, 40
heart = line_mask((S, S), lambda d: (
    d.ellipse([hx - hr, hy - hr * 0.6, hx, hy + hr * 0.4], fill=255),
    d.ellipse([hx, hy - hr * 0.6, hx + hr, hy + hr * 0.4], fill=255),
    d.polygon([(hx - hr * 0.97, hy), (hx + hr * 0.97, hy), (hx, hy + hr * 1.15)], fill=255)))
img.alpha_composite(grad_fill(heart, ROSE[0], ROSE[1]))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.resize((512, 512), Image.LANCZOS).save(OUT, optimize=True)
print("saved", OUT)
