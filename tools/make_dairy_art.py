"""
Generates Eorzea Milkers' art in the boutique creamery style: warm cream, dusty rose, powder blue, sage.
Matte surfaces, fine lines, a classic serif. Nothing glossy or cute.

    python3 make_dairy_art.py <output Assets folder>

Fonts are in tools/fonts (Young Serif and Outfit, SIL Open Font License).
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets"
SS = 2
SERIF = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts", "YoungSerif-Regular.ttf")

# Palette: (light, deep)
ROSE = ((222, 170, 176), (168, 98, 112))
BLUE = ((178, 198, 218), (98, 128, 162))
SAGE = ((184, 204, 180), (106, 138, 110))
HONEY = ((228, 204, 156), (176, 138, 78))
MAUVE = ((198, 178, 196), (126, 98, 124))
STONE = ((214, 208, 200), (138, 130, 124))
CREAM = (250, 245, 236)
PAPER = (244, 237, 225)
INK = (92, 58, 66)


def font(px):
    return ImageFont.truetype(SERIF, int(px))


def vgrad(w, h, top, bot):
    t = np.linspace(0, 1, h)[:, None, None]
    return np.array(top, float) * (1 - t) + np.array(bot, float) * t + np.zeros((h, w, 3))


def noise(w, h, amp, seed, cells=None):
    rng = np.random.default_rng(seed)
    if cells:
        g = rng.standard_normal((cells, cells))
        gi = Image.fromarray(((g - g.min()) / (np.ptp(g) + 1e-9) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        return (np.asarray(gi, float) / 255 - 0.5) * 2 * amp
    return rng.standard_normal((h, w)) * amp


def to_img(arr, alpha=None):
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    if alpha is not None:
        img.putalpha(alpha)
    return img


def rmask(w, h, r, box):
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=r, fill=255)
    return m


def solid(mask, color, alpha=1.0):
    img = Image.new("RGBA", mask.size, tuple(color) + (0,))
    img.putalpha(mask.point(lambda v: int(v * alpha)))
    return img


def grad_fill(mask, top, bot):
    w, h = mask.size
    bbox = mask.getbbox() or (0, 0, w, h)
    y0, y1 = bbox[1], max(bbox[3], bbox[1] + 1)
    t = np.clip((np.arange(h) - y0) / (y1 - y0), 0, 1)[:, None, None]
    arr = np.array(top, float) * (1 - t) + np.array(bot, float) * t + np.zeros((h, w, 3))
    return to_img(arr, mask)


def clip(a, b):
    return Image.fromarray(np.minimum(np.asarray(a), np.asarray(b)).astype(np.uint8))


def shadow(layer, dy, blur, opacity, color=(90, 60, 60)):
    a = layer.getchannel("A").filter(ImageFilter.GaussianBlur(blur))
    sh = Image.new("RGBA", layer.size, tuple(color) + (0,))
    sh.putalpha(a.point(lambda v: int(v * opacity)))
    out = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    out.alpha_composite(sh, (0, int(dy)))
    return out


def text_layer(text, px, color, tracking=0.0):
    f = font(px)
    widths = [f.getlength(ch) for ch in text]
    total = sum(widths) + tracking * px * (len(text) - 1)
    b = f.getbbox(text)
    w, h = int(total) + 20, b[3] - b[1] + 20
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    x = 10.0
    for ch, cw in zip(text, widths):
        d.text((x, 10 - b[1]), ch, font=f, fill=255)
        x += cw + tracking * px
    return solid(m, color)


def place(dst, src, center):
    dst.alpha_composite(src, (int(center[0] - src.width / 2), int(center[1] - src.height / 2)))


def line_mask(size, draw_fn):
    m = Image.new("L", size, 0)
    draw_fn(ImageDraw.Draw(m))
    return m


# ---------------------------------------------------------------- background

def background(w=1600, h=1000):
    """Creamery wallpaper: warm cream with faint rose pinstripes and a soft linen grain."""
    s = SS
    W_, H_ = w * s, h * s
    arr = vgrad(W_, H_, (248, 241, 230), (238, 228, 214))
    arr += noise(W_, H_, 6, 1, 40)[..., None]
    img = to_img(arr)
    stripes = line_mask((W_, H_), lambda d: [d.line([(x, 0), (x, H_)], fill=255, width=s)
                                            for x in range(0, W_, 46 * s)])
    img.alpha_composite(solid(stripes, ROSE[1], 0.10))
    grain = np.clip(noise(W_, H_, 1, 2) * 18 + 128, 0, 255).astype(np.uint8)
    g = Image.fromarray(grain).filter(ImageFilter.BoxBlur(0.5))
    gl = Image.new("RGBA", (W_, H_), (120, 90, 80, 0))
    gl.putalpha(g.point(lambda v: max(0, v - 128) // 3))
    img.alpha_composite(gl)
    # Wainscot: a darker rose panel along the bottom with a moulding line
    y0 = int(H_ * 0.78)
    wain = Image.new("L", (W_, H_), 0)
    ImageDraw.Draw(wain).rectangle([0, y0, W_, H_], fill=255)
    img.alpha_composite(grad_fill(wain, (230, 204, 202), (214, 184, 184)))
    mould = line_mask((W_, H_), lambda d: (d.line([(0, y0), (W_, y0)], fill=255, width=3 * s),
                                           d.line([(0, y0 + 9 * s), (W_, y0 + 9 * s)], fill=255, width=s)))
    img.alpha_composite(solid(mould, ROSE[1], 0.6))
    # Vignette
    yy, xx = np.mgrid[0:H_, 0:W_]
    v = np.clip(np.sqrt(((xx / W_) - 0.5) ** 2 + ((yy / H_) - 0.45) ** 2) * 1.5, 0, 1) ** 2
    vig = Image.fromarray((v * 70).astype(np.uint8))
    img.alpha_composite(solid(vig, (120, 80, 80)))
    return img.resize((w, h), Image.LANCZOS).convert("RGB")


# ---------------------------------------------------------------- cards and buttons

def card(w=512, h=256, ring=ROSE[1]):
    """Matte cream card with a fine double rule. 9-slice border: 48 px."""
    s = SS
    W_, H_ = w * s, h * s
    pad = 10 * s
    box = [pad, pad, W_ - pad, H_ - pad - 3 * s]
    img = Image.new("RGBA", (W_, H_), (0, 0, 0, 0))
    m = rmask(W_, H_, 22 * s, box)
    body = to_img(vgrad(W_, H_, (253, 250, 244), (246, 240, 230)) + noise(W_, H_, 2, 7, 30)[..., None], m)
    img.alpha_composite(shadow(body, 3 * s, 7 * s, 0.22))
    img.alpha_composite(body)
    edge = line_mask((W_, H_), lambda d: d.rounded_rectangle(box, radius=22 * s, outline=255, width=2 * s))
    img.alpha_composite(solid(edge, ring, 0.55))
    i = 14 * s
    inner = [box[0] + i, box[1] + i, box[2] - i, box[3] - i]
    rule = line_mask((W_, H_), lambda d: d.rounded_rectangle(inner, radius=12 * s, outline=255, width=s))
    img.alpha_composite(solid(rule, ring, 0.45))
    # Small corner diamonds
    dots = Image.new("L", (W_, H_), 0)
    dd = ImageDraw.Draw(dots)
    for cx, cy in ((inner[0], inner[1]), (inner[2], inner[1]), (inner[0], inner[3]), (inner[2], inner[3])):
        r = 4 * s
        dd.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=255)
    img.alpha_composite(solid(dots, ring, 0.7))
    return img.resize((w, h), Image.LANCZOS)


def button(light, deep, w=256, h=96):
    """Matte pill with a fine darker outline and a faint inner bevel. 9-slice border: 40 px."""
    s = SS
    W_, H_ = w * s, h * s
    box = [3 * s, 3 * s, W_ - 3 * s, H_ - 7 * s]
    rad = int((box[3] - box[1]) * 0.5)
    img = Image.new("RGBA", (W_, H_), (0, 0, 0, 0))
    m = rmask(W_, H_, rad, box)
    mid = tuple(int(a * 0.72 + b * 0.28) for a, b in zip(light, deep))
    body = grad_fill(m, light, mid)
    img.alpha_composite(shadow(body, 3 * s, 3 * s, 0.25, deep))
    img.alpha_composite(body)
    bevel = line_mask((W_, H_), lambda d: d.rounded_rectangle([box[0] + 3 * s, box[1] + 3 * s, box[2] - 3 * s, box[3] - 3 * s],
                                                            radius=rad - 3 * s, outline=255, width=s))
    hi = clip(bevel, line_mask((W_, H_), lambda d: d.rectangle([0, 0, W_, H_ * 0.45], fill=255)))
    img.alpha_composite(solid(hi, (255, 255, 255), 0.45))
    edge = line_mask((W_, H_), lambda d: d.rounded_rectangle(box, radius=rad, outline=255, width=2 * s))
    img.alpha_composite(solid(edge, deep, 0.8))
    return img.resize((w, h), Image.LANCZOS)


def seal(size=200, ring=ROSE):
    """Round seal for the grade letter: cream disc, double rule, rose band."""
    s = size * SS
    c = s / 2
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    outer = line_mask((s, s), lambda d: d.ellipse([s * 0.04, s * 0.04, s * 0.96, s * 0.96], fill=255))
    disc = grad_fill(outer, ring[0], ring[1])
    img.alpha_composite(shadow(disc, 3 * SS, 5 * SS, 0.25))
    img.alpha_composite(disc)
    inner = line_mask((s, s), lambda d: d.ellipse([s * 0.14, s * 0.14, s * 0.86, s * 0.86], fill=255))
    img.alpha_composite(grad_fill(inner, (253, 250, 244), (244, 236, 224)))
    rule = line_mask((s, s), lambda d: d.ellipse([s * 0.19, s * 0.19, s * 0.81, s * 0.81], outline=255, width=SS))
    img.alpha_composite(solid(rule, ring[1], 0.6))
    ticks = Image.new("L", (s, s), 0)
    td = ImageDraw.Draw(ticks)
    for k in range(36):
        a = 2 * math.pi * k / 36
        r0, r1 = s * 0.405, s * 0.445
        td.line([(c + math.cos(a) * r0, c + math.sin(a) * r0), (c + math.cos(a) * r1, c + math.sin(a) * r1)], fill=255, width=SS)
    img.alpha_composite(solid(ticks, (255, 255, 255), 0.55))
    return img.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------- milk bottle

BW, BH = 256, 512


# Five bottle types, all on the same 256x512 canvas so the plugin can swap them freely.
# Each has: glass (or metal) shape, the area milk can fill, a cap or stopper, and a label.
BOTTLES = {
    1: dict(name="MILK BOTTLE", kind="quart", trim=ROSE),
    2: dict(name="CREAM JAR", kind="halfgal", trim=BLUE),
    3: dict(name="DAIRY JUG", kind="gallon", trim=SAGE),
    4: dict(name="BIG JUG", kind="bigjug", trim=HONEY),
    5: dict(name="MILK CHURN", kind="churn", trim=MAUVE),
}


def shape_mask(kind, inset=0):
    """Outline of the vessel (inset > 0 gives the inside of the glass)."""
    s = SS
    W_, H_ = BW * s, BH * s
    m = Image.new("L", (W_, H_), 0)
    d = ImageDraw.Draw(m)
    i = inset * s

    def R(x0, y0, x1, y1, r):
        d.rounded_rectangle([W_ * x0 + i, H_ * y0 + i, W_ * x1 - i, H_ * y1 - i], radius=max(1, W_ * r - i), fill=255)

    def Poly(pts):
        cx = sum(x for x, _ in pts) / len(pts)
        d.polygon([(W_ * x + (i if x < cx else -i), H_ * y) for x, y in pts], fill=255)

    if kind == "quart":
        R(0.08, 0.35, 0.92, 0.975, 0.13)
        Poly([(0.32, 0.18), (0.68, 0.18), (0.92, 0.35 + 0.06), (0.08, 0.35 + 0.06)])
        d.rectangle([W_ * 0.32 + i, H_ * 0.085 + i, W_ * 0.68 - i, H_ * 0.21], fill=255)
    elif kind == "halfgal":
        # Squat wide-mouth jar
        R(0.04, 0.34, 0.96, 0.975, 0.34)
        Poly([(0.27, 0.25), (0.73, 0.25), (0.96, 0.50), (0.04, 0.50)])
        d.rectangle([W_ * 0.27 + i, H_ * 0.17 + i, W_ * 0.73 - i, H_ * 0.28], fill=255)
    elif kind == "gallon":
        R(0.03, 0.30, 0.78, 0.975, 0.12)
        Poly([(0.29, 0.185), (0.52, 0.185), (0.78, 0.30 + 0.06), (0.03, 0.30 + 0.06)])
        d.rectangle([W_ * 0.29 + i, H_ * 0.085 + i, W_ * 0.52 - i, H_ * 0.2], fill=255)
    elif kind == "bigjug":
        # Big milk jug: wide body, small cap off to the left, hollow carry handle moulded into the top right
        if inset == 0:
            R(0.03, 0.34, 0.97, 0.975, 0.10)
            Poly([(0.19, 0.20), (0.37, 0.20), (0.97, 0.44), (0.03, 0.44)])
            d.rectangle([W_ * 0.19, H_ * 0.10, W_ * 0.37, H_ * 0.24], fill=255)
            d.rounded_rectangle([W_ * 0.44, H_ * 0.14, W_ * 0.95, H_ * 0.40], radius=W_ * 0.09, fill=255)
            d.rounded_rectangle([W_ * 0.57, H_ * 0.195, W_ * 0.85, H_ * 0.315], radius=W_ * 0.05, fill=0)   # the hole you grip
        else:
            m = shape_mask(kind, 0)
            for _ in range(max(1, inset * s // 4)):
                m = m.filter(ImageFilter.MinFilter(9))
            return m
    elif kind == "pitcher":
        # Glass milk pitcher: open rim with a pouring spout, round belly, flat foot
        top, bot = 0.135, 0.975
        pts_l, pts_r = [], []
        for k in range(41):
            t = k / 40
            y = top + (bot - top) * t
            # Narrow at the rim, widening into a full belly near the bottom
            bulge = math.sin(math.pi / 2 * min(1.0, t * 1.25)) * 0.13
            foot = max(0.0, (t - 0.92) / 0.08) ** 2 * 0.04
            pts_l.append((W_ * (0.20 - bulge + foot) + i, H_ * y))
            pts_r.append((W_ * (0.72 + bulge - foot) - i, H_ * y))
        d.polygon([(x, y + (i if y < H_ * (top + 0.01) else 0)) for x, y in pts_l + pts_r[::-1]], fill=255)
        if inset == 0:
            d.polygon([(W_ * 0.07, H_ * 0.112), (W_ * 0.24, H_ * 0.13), (W_ * 0.21, H_ * 0.20), (W_ * 0.15, H_ * 0.17)], fill=255)
    elif kind == "dispenser":
        # Wide glass jar that stands on a wooden base; the lid and base are drawn on the front layer
        R(0.06, 0.19, 0.94, 0.905, 0.07)
    elif kind == "churn":
        if inset == 0:
            R(0.12, 0.34, 0.88, 0.975, 0.07)
            Poly([(0.31, 0.21), (0.69, 0.21), (0.88, 0.36), (0.12, 0.36)])
            d.rectangle([W_ * 0.31, H_ * 0.11, W_ * 0.69, H_ * 0.23], fill=255)
            d.rounded_rectangle([W_ * 0.27, H_ * 0.05, W_ * 0.73, H_ * 0.125], radius=W_ * 0.03, fill=255)
            d.ellipse([W_ * 0.44, H_ * 0.018, W_ * 0.56, H_ * 0.06], fill=255)
        else:
            # Milk only shows through the sight window
            R(0.40, 0.44, 0.60, 0.905, 0.08)
    return m.filter(ImageFilter.GaussianBlur(s * 2.5)).point(lambda v: 255 if v > 128 else 0)


def minus(a, b):
    return Image.fromarray(np.clip(np.asarray(a, int) - np.asarray(b, int), 0, 255).astype(np.uint8))


def bottle_label(front, box, trim, size_name, oval=False):
    s = SS
    W_, H_ = front.size
    x0, y0, x1, y1 = [int(v) for v in box]
    lab = Image.new("L", (W_, H_), 0)
    if oval:
        ImageDraw.Draw(lab).ellipse([x0, y0, x1, y1], fill=255)
    else:
        ImageDraw.Draw(lab).rounded_rectangle([x0, y0, x1, y1], radius=int(W_ * 0.02), fill=255)
    labimg = to_img(vgrad(W_, H_, (250, 244, 234), (238, 228, 214)) + noise(W_, H_, 2, 9, 20)[..., None], lab)
    front.alpha_composite(shadow(labimg, 2 * s, 3 * s, 0.25))
    front.alpha_composite(labimg)
    pad = 8 * s
    if oval:
        rule = line_mask((W_, H_), lambda d: d.ellipse([x0 + pad, y0 + pad, x1 - pad, y1 - pad], outline=255, width=s))
    else:
        rule = line_mask((W_, H_), lambda d: d.rectangle([x0 + pad, y0 + pad, x1 - pad, y1 - pad], outline=255, width=s))
    front.alpha_composite(solid(rule, trim[1], 0.75))
    cx = (x0 + x1) / 2
    h = y1 - y0
    w = x1 - x0
    scale = min(1.0, w / (W_ * 0.68))
    place(front, text_layer("EORZEA", H_ * 0.030 * scale, trim[1], 0.25), (cx, y0 + h * 0.30))
    front.alpha_composite(solid(line_mask((W_, H_), lambda d: d.line([(cx - w * 0.24, y0 + h * 0.47), (cx + w * 0.24, y0 + h * 0.47)], fill=255, width=s)), trim[1], 0.7))
    place(front, text_layer("Milkers", H_ * 0.046 * scale, INK), (cx, y0 + h * 0.64))
    place(front, text_layer(size_name, H_ * 0.017 * scale, trim[1], 0.3), (cx, y0 + h * 0.86))


def glass_front(front, outer, inner, kind):
    s = SS
    W_, H_ = front.size
    front.alpha_composite(solid(minus(outer, inner), (250, 248, 246), 0.65))
    edge = minus(outer, outer.filter(ImageFilter.MinFilter(5)))
    front.alpha_composite(solid(edge, (120, 100, 100), 0.45))
    bb = outer.getbbox()
    lx = bb[0] / W_ + 0.08
    hl = line_mask((W_, H_), lambda d: d.rounded_rectangle([W_ * lx, H_ * 0.42, W_ * (lx + 0.06), H_ * 0.9], radius=W_ * 0.03, fill=150))
    front.alpha_composite(solid(clip(hl.filter(ImageFilter.GaussianBlur(s * 4)), outer), (255, 255, 255)))


def bottle_layers(tier):
    spec = BOTTLES[tier]
    kind, trim, name = spec["kind"], spec["trim"], spec["name"]
    s = SS
    W_, H_ = BW * s, BH * s
    outer = shape_mask(kind, 0)
    inner = shape_mask(kind, 8 if kind != "churn" else 2)
    front = Image.new("RGBA", (W_, H_), (0, 0, 0, 0))

    back = solid(inner, (196, 168, 168) if kind != "churn" else (70, 60, 62), 0.62 if kind != "churn" else 0.9)
    milk_arr = vgrad(W_, H_, (255, 253, 248), (240, 234, 224))
    x = np.linspace(0, 1, W_)[None, :, None]
    milk_arr -= np.abs(x - 0.40) * 22
    milk = to_img(milk_arr, inner)

    if kind == "churn":
        # Brushed metal cylinder with a sight window cut into it
        xs = np.linspace(0, 1, W_)[None, :]
        shade = 0.72 + 0.34 * np.cos((xs - 0.42) * math.pi * 1.4)
        metal = np.array([206, 202, 198], float)[None, None, :] * shade[..., None] + np.zeros((H_, W_, 3))
        metal += noise(W_, H_, 1, 4)[..., None] * 0  # keep it clean
        streak = np.asarray(Image.fromarray((np.random.default_rng(3).random((1, W_)) * 255).astype(np.uint8)).resize((W_, H_)), float) / 255
        metal += (streak[..., None] - 0.5) * 10
        window = shape_mask("churn", 0)
        win = shape_mask("churn", 1)  # the window shape
        body = to_img(metal, minus(window, win))
        front.alpha_composite(shadow(body, 2 * s, 3 * s, 0.25))
        front.alpha_composite(body)
        # Bands and rims
        bands = line_mask((W_, H_), lambda d: [d.rectangle([W_ * 0.12, H_ * y, W_ * 0.88, H_ * (y + 0.018)], fill=255) for y in (0.36, 0.93)])
        front.alpha_composite(solid(clip(bands, window), (150, 146, 144), 0.85))
        lid = line_mask((W_, H_), lambda d: d.rounded_rectangle([W_ * 0.27, H_ * 0.05, W_ * 0.73, H_ * 0.125], radius=W_ * 0.03, outline=255, width=s * 2))
        front.alpha_composite(solid(lid, (140, 134, 132), 0.8))
        # Side handles
        hd = line_mask((W_, H_), lambda d: (d.arc([W_ * 0.02, H_ * 0.23, W_ * 0.22, H_ * 0.37], 90, 270, fill=255, width=s * 5),
                                            d.arc([W_ * 0.78, H_ * 0.23, W_ * 0.98, H_ * 0.37], 270, 90, fill=255, width=s * 5)))
        front.alpha_composite(solid(hd, (150, 146, 144)))
        # Window rim and glass
        wr = line_mask((W_, H_), lambda d: d.rounded_rectangle([W_ * 0.395, H_ * 0.435, W_ * 0.605, H_ * 0.91], radius=W_ * 0.08, outline=255, width=s * 3))
        front.alpha_composite(solid(wr, trim[1], 0.9))
        gl = line_mask((W_, H_), lambda d: d.rounded_rectangle([W_ * 0.43, H_ * 0.47, W_ * 0.46, H_ * 0.87], radius=W_ * 0.015, fill=130))
        front.alpha_composite(solid(gl.filter(ImageFilter.GaussianBlur(s * 2)), (255, 255, 255)))
        ticks = line_mask((W_, H_), lambda d: [d.line([(W_ * 0.61, H_ * y), (W_ * 0.65, H_ * y)], fill=255, width=s) for y in np.linspace(0.46, 0.89, 6)])
        front.alpha_composite(solid(ticks, trim[1], 0.8))
        # Name plate on the neck
        plate = rmask(W_, H_, int(W_ * 0.02), [int(W_ * 0.34), int(H_ * 0.145), int(W_ * 0.66), int(H_ * 0.205)])
        front.alpha_composite(grad_fill(plate, trim[0], trim[1]))
        place(front, text_layer("EORZEA", H_ * 0.022, (255, 255, 255), 0.25), (W_ * 0.5, H_ * 0.176))
        place(front, text_layer(name, H_ * 0.018, (110, 104, 102), 0.3), (W_ * 0.5, H_ * 0.955))
    else:
        glass_front(front, outer, inner, kind)
        if kind == "gallon":
            # Glass loop handle
            ring = line_mask((W_, H_), lambda d: d.rounded_rectangle([W_ * 0.62, H_ * 0.27, W_ * 0.97, H_ * 0.62], radius=W_ * 0.14, outline=255, width=s * 9))
            ring = minus(ring, shape_mask(kind, 0))
            front.alpha_composite(solid(ring, (250, 248, 246), 0.7))
            front.alpha_composite(solid(minus(ring, ring.filter(ImageFilter.MinFilter(5))), (120, 100, 100), 0.45))

        # Caps and stoppers
        if kind == "bigjug":
            # Grip ridges near the base
            ridges = line_mask((W_, H_), lambda d: [d.line([(W_ * 0.06, H_ * y), (W_ * 0.94, H_ * y)], fill=255, width=s * 2) for y in (0.81, 0.845, 0.88, 0.915)])
            ridges = clip(ridges, shape_mask(kind, 0))
            front.alpha_composite(solid(ridges, (255, 255, 255), 0.55))
            front.alpha_composite(solid(ridges.filter(ImageFilter.GaussianBlur(s)), (120, 100, 100), 0.18))
        if kind == "pitcher":
            # Big glass handle on the right
            ring = line_mask((W_, H_), lambda d: d.rounded_rectangle([W_ * 0.62, H_ * 0.17, W_ * 0.975, H_ * 0.60], radius=W_ * 0.15, outline=255, width=s * 11))
            ring = minus(ring, shape_mask(kind, 0))
            front.alpha_composite(solid(ring, (250, 248, 246), 0.72))
            front.alpha_composite(solid(minus(ring, ring.filter(ImageFilter.MinFilter(5))), (120, 100, 100), 0.45))
            # Open rim seen slightly from above, and a honey ribbon tied under it
            rim = line_mask((W_, H_), lambda d: d.ellipse([W_ * 0.20, H_ * 0.12, W_ * 0.72, H_ * 0.152], outline=255, width=s * 3))
            front.alpha_composite(solid(rim, (250, 248, 246), 0.9))
            front.alpha_composite(solid(line_mask((W_, H_), lambda d: d.ellipse([W_ * 0.20, H_ * 0.12, W_ * 0.72, H_ * 0.152], outline=255, width=s)), (120, 100, 100), 0.5))
            ribbon = line_mask((W_, H_), lambda d: d.line([(W_ * 0.10, H_ * 0.215), (W_ * 0.82, H_ * 0.215)], fill=255, width=s * 4))
            ribbon = clip(ribbon, shape_mask(kind, 0))
            front.alpha_composite(solid(ribbon, HONEY[1], 0.9))
            bow = line_mask((W_, H_), lambda d: (d.polygon([(W_ * 0.46, H_ * 0.215), (W_ * 0.38, H_ * 0.20), (W_ * 0.38, H_ * 0.23)], fill=255),
                                                 d.polygon([(W_ * 0.46, H_ * 0.215), (W_ * 0.54, H_ * 0.20), (W_ * 0.54, H_ * 0.23)], fill=255),
                                                 d.line([(W_ * 0.46, H_ * 0.215), (W_ * 0.43, H_ * 0.26)], fill=255, width=s * 3),
                                                 d.line([(W_ * 0.46, H_ * 0.215), (W_ * 0.50, H_ * 0.26)], fill=255, width=s * 3)))
            front.alpha_composite(solid(bow, HONEY[1]))
        elif kind == "dispenser":
            # Wooden stand
            base = rmask(W_, H_, int(W_ * 0.02), [int(W_ * 0.03), int(H_ * 0.895), int(W_ * 0.97), int(H_ * 0.985)])
            baseimg = to_img(vgrad(W_, H_, (214, 178, 128), (150, 112, 72)) + noise(W_, H_, 6, 21, 40)[..., None], base)
            front.alpha_composite(shadow(baseimg, 2 * s, 3 * s, 0.3))
            front.alpha_composite(baseimg)
            grain = line_mask((W_, H_), lambda d: [d.line([(W_ * 0.05, H_ * y), (W_ * 0.95, H_ * y)], fill=255, width=s) for y in (0.92, 0.95)])
            front.alpha_composite(solid(grain, (120, 86, 56), 0.35))
            # Glass rim and wooden lid with a knob
            rim = rmask(W_, H_, int(W_ * 0.02), [int(W_ * 0.05), int(H_ * 0.175), int(W_ * 0.95), int(H_ * 0.205)])
            front.alpha_composite(solid(rim, (250, 248, 246), 0.85))
            front.alpha_composite(solid(minus(rim, rim.filter(ImageFilter.MinFilter(5))), (120, 100, 100), 0.45))
            lid = Image.new("L", (W_, H_), 0)
            ld = ImageDraw.Draw(lid)
            ld.pieslice([W_ * 0.08, H_ * 0.09, W_ * 0.92, H_ * 0.26], 180, 360, fill=255)
            ld.rectangle([W_ * 0.07, H_ * 0.165, W_ * 0.93, H_ * 0.18], fill=255)
            lidimg = to_img(vgrad(W_, H_, (226, 196, 150), (168, 128, 82)) + noise(W_, H_, 5, 22, 40)[..., None], lid)
            front.alpha_composite(shadow(lidimg, 2 * s, 3 * s, 0.3))
            front.alpha_composite(lidimg)
            knob = line_mask((W_, H_), lambda d: d.ellipse([W_ * 0.44, H_ * 0.06, W_ * 0.56, H_ * 0.105], fill=255))
            front.alpha_composite(grad_fill(knob, (214, 178, 128), (140, 104, 66)))
            # Brass spigot near the bottom
            tap = Image.new("L", (W_, H_), 0)
            td = ImageDraw.Draw(tap)
            td.ellipse([W_ * 0.42, H_ * 0.80, W_ * 0.58, H_ * 0.86], fill=255)
            td.rounded_rectangle([W_ * 0.47, H_ * 0.84, W_ * 0.53, H_ * 0.885], radius=W_ * 0.02, fill=255)
            td.rounded_rectangle([W_ * 0.40, H_ * 0.775, W_ * 0.60, H_ * 0.80], radius=W_ * 0.012, fill=255)
            front.alpha_composite(shadow(grad_fill(tap, HONEY[0], HONEY[1]), 2 * s, 2 * s, 0.35))
            front.alpha_composite(grad_fill(tap, (240, 214, 150), (150, 110, 50)))
        else:
            cx = {"quart": 0.5, "halfgal": 0.5, "gallon": 0.405, "bigjug": 0.28}[kind]
            half = {"quart": 0.21, "halfgal": 0.27, "gallon": 0.15, "bigjug": 0.115}[kind]
            top = {"quart": 0.035, "halfgal": 0.125, "gallon": 0.035, "bigjug": 0.055}[kind]
            cap = rmask(W_, H_, int(W_ * 0.025), [int(W_ * (cx - half)), int(H_ * top), int(W_ * (cx + half)), int(H_ * (top + 0.075))])
            capimg = grad_fill(cap, trim[0], trim[1])
            front.alpha_composite(shadow(capimg, 2 * s, 3 * s, 0.3))
            front.alpha_composite(capimg)
            band = line_mask((W_, H_), lambda d: d.line([(W_ * (cx - half), H_ * (top + 0.06)), (W_ * (cx + half), H_ * (top + 0.06))], fill=255, width=s * 2))
            front.alpha_composite(solid(band, (255, 255, 255), 0.5))

        # Labels
        boxes = {
            "quart": ([W_ * 0.16, H_ * 0.50, W_ * 0.84, H_ * 0.76], False),
            "halfgal": ([W_ * 0.12, H_ * 0.55, W_ * 0.88, H_ * 0.79], False),
            "gallon": ([W_ * 0.09, H_ * 0.50, W_ * 0.72, H_ * 0.75], False),
            "dispenser": ([W_ * 0.16, H_ * 0.40, W_ * 0.84, H_ * 0.64], False),
            "pitcher": ([W_ * 0.14, H_ * 0.52, W_ * 0.78, H_ * 0.77], True),
            "bigjug": ([W_ * 0.12, H_ * 0.50, W_ * 0.88, H_ * 0.75], False),
        }
        box, oval = boxes[kind]
        bottle_label(front, box, trim, name, oval)

    # Where milk can be, for the plugin
    bb = inner.getbbox()
    info = dict(top=round(bb[1] / H_, 3), bottom=round(bb[3] / H_, 3))
    fin = lambda im: im.resize((BW, BH), Image.LANCZOS)
    return fin(back), fin(milk), fin(front), info


# ---------------------------------------------------------------- icons (flat, fine line, muted)

def medallion(draw_symbol, ring, size=160):
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    m = line_mask((s, s), lambda d: d.ellipse([s * 0.04, s * 0.04, s * 0.96, s * 0.96], fill=255))
    disc = grad_fill(m, (252, 248, 240), (240, 232, 220))
    img.alpha_composite(shadow(disc, 2 * SS, 4 * SS, 0.22))
    img.alpha_composite(disc)
    r1 = line_mask((s, s), lambda d: d.ellipse([s * 0.04, s * 0.04, s * 0.96, s * 0.96], outline=255, width=int(s * 0.025)))
    r2 = line_mask((s, s), lambda d: d.ellipse([s * 0.1, s * 0.1, s * 0.9, s * 0.9], outline=255, width=SS))
    img.alpha_composite(solid(r1, ring[1], 0.85))
    img.alpha_composite(solid(r2, ring[1], 0.45))
    sym = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw_symbol(sym, s)
    img.alpha_composite(sym)
    return img.resize((size, size), Image.LANCZOS)


def P(s, pts):
    return [(x * s, y * s) for x, y in pts]


def B(s, box):
    return [v * s for v in box]


def flat(img, s, fn, color, alpha=1.0):
    img.alpha_composite(solid(line_mask((s, s), fn), color, alpha))


def stroke(img, s, fn, color):
    img.alpha_composite(solid(line_mask((s, s), fn), color, 0.95))


def sym_hay(img, s):
    flat(img, s, lambda d: d.rounded_rectangle(B(s, (0.28, 0.36, 0.72, 0.72)), radius=s * 0.04, fill=255), HONEY[0])
    for k in range(4):
        x = 0.35 + k * 0.1
        stroke(img, s, lambda d, x=x: d.line(P(s, [(x, 0.39), (x + 0.01, 0.69)]), fill=255, width=SS * 2), HONEY[1])
    flat(img, s, lambda d: d.rectangle(B(s, (0.28, 0.52, 0.72, 0.56)), fill=255), ROSE[1])


def sym_water(img, s):
    def f(d):
        cx, cy, r = 0.5 * s, 0.58 * s, 0.17 * s
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
        d.polygon([(cx - r * 0.97, cy - r * 0.25), (cx, 0.24 * s), (cx + r * 0.97, cy - r * 0.25)], fill=255)
    flat(img, s, f, BLUE[0])
    stroke(img, s, lambda d: d.arc(B(s, (0.39, 0.47, 0.61, 0.69)), 110, 170, fill=255, width=SS * 3), (255, 255, 255))


def sym_grain(img, s):
    flat(img, s, lambda d: d.polygon(P(s, [(0.32, 0.36), (0.68, 0.36), (0.74, 0.78), (0.26, 0.78)]), fill=255), (226, 208, 182))
    flat(img, s, lambda d: d.rectangle(B(s, (0.3, 0.42, 0.7, 0.46)), fill=255), BLUE[1])
    stroke(img, s, lambda d: d.line(P(s, [(0.5, 0.52), (0.5, 0.7)]), fill=255, width=SS * 2), HONEY[1])
    for k, y in enumerate((0.55, 0.61, 0.67)):
        stroke(img, s, lambda d, y=y: (d.ellipse(B(s, (0.42, y - 0.03, 0.5, y + 0.01)), fill=255),
                                       d.ellipse(B(s, (0.5, y - 0.03, 0.58, y + 0.01)), fill=255)), HONEY[1])


def sym_clover(img, s):
    def f(d):
        cx, cy = 0.5 * s, 0.46 * s
        for a in (90, 210, 330):
            ang = math.radians(a)
            px, py = cx + math.cos(ang) * s * 0.11, cy - math.sin(ang) * s * 0.11
            r = s * 0.11
            d.ellipse([px - r, py - r, px + r, py + r], fill=255)
        d.line([(cx, cy), (cx + s * 0.05, s * 0.77)], fill=255, width=int(s * 0.03))
    flat(img, s, f, SAGE[1])


def sym_salt(img, s):
    flat(img, s, lambda d: d.polygon(P(s, [(0.5, 0.26), (0.76, 0.38), (0.5, 0.5), (0.24, 0.38)]), fill=255), (240, 236, 232))
    flat(img, s, lambda d: d.polygon(P(s, [(0.24, 0.38), (0.5, 0.5), (0.5, 0.77), (0.24, 0.65)]), fill=255), STONE[0])
    flat(img, s, lambda d: d.polygon(P(s, [(0.5, 0.5), (0.76, 0.38), (0.76, 0.65), (0.5, 0.77)]), fill=255), STONE[1])


def sym_star(img, s):
    pts = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        r = 0.29 if i % 2 == 0 else 0.13
        pts.append((0.5 + math.cos(a) * r, 0.53 - math.sin(a) * r))
    flat(img, s, lambda d: d.polygon(P(s, pts), fill=255), HONEY[1])


def sym_bucket(img, s):
    flat(img, s, lambda d: d.polygon(P(s, [(0.27, 0.41), (0.73, 0.41), (0.67, 0.79), (0.33, 0.79)]), fill=255), STONE[1])
    flat(img, s, lambda d: d.ellipse(B(s, (0.27, 0.36, 0.73, 0.46)), fill=255), (252, 250, 246))
    stroke(img, s, lambda d: d.arc(B(s, (0.28, 0.18, 0.72, 0.62)), 195, 345, fill=255, width=SS * 3), STONE[1])
    for y in (0.55, 0.68):
        stroke(img, s, lambda d, y=y: d.line(P(s, [(0.3, y), (0.7, y)]), fill=255, width=SS), (255, 255, 255))


def sym_bell(img, s):
    def f(d):
        d.pieslice(B(s, (0.3, 0.26, 0.7, 0.66)), 180, 360, fill=255)
        d.polygon(P(s, [(0.3, 0.46), (0.7, 0.46), (0.77, 0.7), (0.23, 0.7)]), fill=255)
    flat(img, s, f, HONEY[1])
    flat(img, s, lambda d: d.ellipse(B(s, (0.46, 0.68, 0.54, 0.76)), fill=255), HONEY[1])
    flat(img, s, lambda d: d.rectangle(B(s, (0.36, 0.18, 0.64, 0.24)), fill=255), ROSE[1])


def sym_cushion(img, s):
    flat(img, s, lambda d: d.rounded_rectangle(B(s, (0.2, 0.38, 0.8, 0.68)), radius=s * 0.1, fill=255), ROSE[0])
    stroke(img, s, lambda d: d.rounded_rectangle(B(s, (0.25, 0.43, 0.75, 0.63)), radius=s * 0.07, outline=255, width=SS), ROSE[1])
    flat(img, s, lambda d: d.ellipse(B(s, (0.48, 0.51, 0.52, 0.55)), fill=255), ROSE[1])


def sym_gear(img, s):
    def f(d):
        cx, cy = 0.5 * s, 0.52 * s
        for i in range(8):
            a = i * math.pi / 4
            x, y = cx + math.cos(a) * s * 0.21, cy + math.sin(a) * s * 0.21
            d.ellipse([x - s * 0.06, y - s * 0.06, x + s * 0.06, y + s * 0.06], fill=255)
        d.ellipse([cx - s * 0.21, cy - s * 0.21, cx + s * 0.21, cy + s * 0.21], fill=255)
    flat(img, s, f, BLUE[1])
    flat(img, s, lambda d: d.ellipse(B(s, (0.42, 0.44, 0.58, 0.6)), fill=255), (250, 246, 238))


def sym_coin(img, s):
    flat(img, s, lambda d: d.ellipse(B(s, (0.26, 0.26, 0.74, 0.74)), fill=255), HONEY[0])
    stroke(img, s, lambda d: d.ellipse(B(s, (0.31, 0.31, 0.69, 0.69)), outline=255, width=SS * 2), HONEY[1])
    flat(img, s, lambda d: d.polygon(drop_points(s, 0.5, 0.5, 0.2), fill=255), HONEY[1])


def sym_bottle(img, s):
    flat(img, s, lambda d: (d.rounded_rectangle(B(s, (0.36, 0.38, 0.64, 0.8)), radius=s * 0.05, fill=255),
                            d.rectangle(B(s, (0.43, 0.24, 0.57, 0.4)), fill=255)), (252, 250, 246))
    stroke(img, s, lambda d: (d.rounded_rectangle(B(s, (0.36, 0.38, 0.64, 0.8)), radius=s * 0.05, outline=255, width=SS * 2),
                              d.rectangle(B(s, (0.43, 0.24, 0.57, 0.4)), outline=255, width=SS * 2)), STONE[1])
    flat(img, s, lambda d: d.rectangle(B(s, (0.42, 0.19, 0.58, 0.25)), fill=255), ROSE[1])
    flat(img, s, lambda d: d.rectangle(B(s, (0.37, 0.54, 0.63, 0.64)), fill=255), BLUE[0])


def sym_ribbon(img, s):
    flat(img, s, lambda d: d.polygon(P(s, [(0.37, 0.52), (0.48, 0.52), (0.43, 0.84), (0.35, 0.77)]), fill=255), BLUE[1])
    flat(img, s, lambda d: d.polygon(P(s, [(0.52, 0.52), (0.63, 0.52), (0.65, 0.77), (0.57, 0.84)]), fill=255), BLUE[1])
    flat(img, s, lambda d: d.ellipse(B(s, (0.3, 0.22, 0.7, 0.62)), fill=255), BLUE[0])
    stroke(img, s, lambda d: d.ellipse(B(s, (0.36, 0.28, 0.64, 0.56)), outline=255, width=SS * 2), (255, 255, 255))


def sym_milkdrop(img, s):
    sym_water.__wrapped__ if False else None
    def f(d):
        cx, cy, r = 0.5 * s, 0.58 * s, 0.17 * s
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
        d.polygon([(cx - r * 0.97, cy - r * 0.25), (cx, 0.24 * s), (cx + r * 0.97, cy - r * 0.25)], fill=255)
    flat(img, s, f, (252, 250, 246))
    stroke(img, s, lambda d: d.ellipse(B(s, (0.33, 0.41, 0.67, 0.75)), outline=255, width=SS * 2), STONE[1])


def sym_biscuit(img, s):
    flat(img, s, lambda d: d.rounded_rectangle(B(s, (0.25, 0.32, 0.75, 0.72)), radius=s * 0.08, fill=255), HONEY[0])
    stroke(img, s, lambda d: d.rounded_rectangle(B(s, (0.29, 0.36, 0.71, 0.68)), radius=s * 0.06, outline=255, width=SS * 2), HONEY[1])
    for x, y in ((0.38, 0.45), (0.5, 0.45), (0.62, 0.45), (0.38, 0.58), (0.5, 0.58), (0.62, 0.58)):
        flat(img, s, lambda d, x=x, y=y: d.ellipse(B(s, (x - 0.018, y - 0.018, x + 0.018, y + 0.018)), fill=255), HONEY[1])
    flat(img, s, lambda d: d.ellipse(B(s, (0.42, 0.22, 0.58, 0.34)), fill=255), (252, 250, 246))


def sym_alfalfa(img, s):
    stroke(img, s, lambda d: d.line(P(s, [(0.5, 0.80), (0.5, 0.26)]), fill=255, width=SS * 3), SAGE[1])
    for k, y in enumerate((0.30, 0.40, 0.50, 0.60, 0.70)):
        for side in (-1, 1):
            x0 = 0.5 + side * 0.02
            x1 = 0.5 + side * (0.17 - k * 0.012)
            flat(img, s, lambda d, x0=x0, x1=x1, y=y: d.ellipse(B(s, (min(x0, x1), y - 0.035, max(x0, x1), y + 0.035)), fill=255), HONEY[1] if k % 2 == 0 else HONEY[0])


def sym_starclover(img, s):
    sym_clover(img, s)
    pts = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        r = 0.12 if i % 2 == 0 else 0.055
        pts.append((0.66 + math.cos(a) * r, 0.30 - math.sin(a) * r))
    flat(img, s, lambda d: d.polygon(P(s, pts), fill=255), HONEY[1])


ICONS = {
    "biscuit": (sym_biscuit, HONEY), "alfalfa": (sym_alfalfa, HONEY), "starclover": (sym_starclover, SAGE),
    "hay": (sym_hay, HONEY), "water": (sym_water, BLUE), "grain": (sym_grain, HONEY),
    "clover": (sym_clover, SAGE), "salt": (sym_salt, STONE), "premium": (sym_star, HONEY),
    "bucket": (sym_bucket, STONE), "bell": (sym_bell, HONEY), "cushion": (sym_cushion, ROSE),
    "gear": (sym_gear, BLUE), "coin": (sym_coin, HONEY), "bottle": (sym_bottle, BLUE),
    "ribbon": (sym_ribbon, BLUE), "milk": (sym_milkdrop, STONE),
}


# ---------------------------------------------------------------- portraits

def drop_points(s, cx, cy, h):
    """A milk drop: a point on top, round at the bottom, h tall (all as fractions of s), centred on (cx, cy)."""
    r = h * 0.36
    bx, by = cx, cy + h / 2 - r          # centre of the round bottom
    top = (cx * s, (cy - h / 2) * s)
    pts = [top]
    for k in range(41):
        a = math.radians(-35 + 250 * k / 40)   # sweep around the bottom from right to left
        pts.append(((bx + math.cos(a) * r) * s, (by + math.sin(a) * r) * s))
    return pts


def portrait_drop(img, s, color):
    """The 'you' portrait's mark: a milk drop with a soft shine."""
    m = line_mask((s, s), lambda d: d.polygon(drop_points(s, 0.5, 0.5, 0.46), fill=255))
    img.alpha_composite(solid(m, color))
    shine = line_mask((s, s), lambda d: d.ellipse([s * 0.42, s * 0.5, s * 0.48, s * 0.62], fill=255))
    img.alpha_composite(solid(shine.filter(ImageFilter.GaussianBlur(s * 0.006)), (255, 255, 255), 0.55))


def portrait(ring, letter, size=256, patches=False):
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    m = line_mask((s, s), lambda d: d.ellipse([s * 0.02, s * 0.02, s * 0.98, s * 0.98], fill=255))
    img.alpha_composite(grad_fill(m, ring[0], ring[1]))
    inner = line_mask((s, s), lambda d: d.ellipse([s * 0.07, s * 0.07, s * 0.93, s * 0.93], fill=255))
    face = grad_fill(inner, (252, 248, 240), (238, 230, 218))
    if patches:
        pm = Image.new("L", (s, s), 0)
        pd = ImageDraw.Draw(pm)
        rng = np.random.default_rng(11)
        for cx, cy, r in ((0.24, 0.28, 0.17), (0.78, 0.74, 0.2), (0.74, 0.2, 0.08)):
            pts = []
            for k in range(16):
                a = 2 * math.pi * k / 16
                rr = r * (0.8 + rng.random() * 0.35)
                pts.append(((cx + math.cos(a) * rr) * s, (cy + math.sin(a) * rr) * s))
            pd.polygon(pts, fill=255)
        pm = clip(pm.filter(ImageFilter.GaussianBlur(s * 0.01)).point(lambda v: 255 if v > 128 else 0), inner)
        face.alpha_composite(solid(pm, (176, 150, 142), 0.55))
    img.alpha_composite(face)
    rule = line_mask((s, s), lambda d: d.ellipse([s * 0.12, s * 0.12, s * 0.88, s * 0.88], outline=255, width=SS * 2))
    img.alpha_composite(solid(rule, ring[1], 0.5))
    if letter == "drop":
        portrait_drop(img, s, ring[1])
    elif letter:
        place(img, text_layer(letter, s * 0.42, ring[1]), (s / 2, s / 2 + s * 0.01))
    return img.resize((size, size), Image.LANCZOS)


def main():
    ui = os.path.join(OUT, "ui")
    icons = os.path.join(OUT, "icons")
    avs = os.path.join(OUT, "avatars")
    for p in (ui, icons, avs):
        os.makedirs(p, exist_ok=True)

    background().save(os.path.join(OUT, "bg.png"), optimize=True)
    for tier in BOTTLES:
        back, milk, front, info = bottle_layers(tier)
        back.save(os.path.join(ui, f"bottle{tier}_back.png"), optimize=True)
        milk.save(os.path.join(ui, f"bottle{tier}_milk.png"), optimize=True)
        front.save(os.path.join(ui, f"bottle{tier}_front.png"), optimize=True)
        print(f"bottle {tier}: milk from {info['top']} to {info['bottom']}")
    card().save(os.path.join(ui, "card.png"), optimize=True)
    card(ring=BLUE[1]).save(os.path.join(ui, "card_blue.png"), optimize=True)
    for name, (light, deep) in {"rose": ROSE, "blue": BLUE, "sage": SAGE, "honey": HONEY, "mauve": MAUVE,
                                "cream": ((250, 246, 238), (196, 184, 172))}.items():
        button(light, deep).save(os.path.join(ui, f"button_{name}.png"), optimize=True)
    seal().save(os.path.join(ui, "seal.png"), optimize=True)
    for name, (fn, ring) in ICONS.items():
        medallion(fn, ring).save(os.path.join(icons, f"{name}.png"), optimize=True)
    portrait(ROSE, "drop", patches=True).save(os.path.join(avs, "you.png"), optimize=True)
    portrait(BLUE, "F").save(os.path.join(avs, "farmhand.png"), optimize=True)
    portrait(SAGE, "T").save(os.path.join(avs, "test.png"), optimize=True)
    print("done")


if __name__ == "__main__":
    main()
