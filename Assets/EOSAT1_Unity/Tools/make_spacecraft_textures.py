#!/usr/bin/env python3
"""
make_spacecraft_textures.py - textures for the EOS SAT-1 model, drawn to match the
reference render ("wings closed" picture) and the exploded view.

    python3 make_spacecraft_textures.py --out ../Assets/EOSAT1/Textures

Writes
  panel_a.png, panel_a_mask.png   the six-row panel (3 strings of 2 rows of cut-corner cells,
                                  white bypass diodes along each string's centre line)
  panel_b.png, panel_b_mask.png   the three-band panel (3 strings of 4 x 16 cells; the bronze
                                  substrate shows as small diamonds between cell corners)
  mli_gold.png, mli_gold_normal.png   crinkled gold multi-layer insulation, tiles seamlessly

Panel textures cover one whole panel: u runs along the panel's long edge (across the
face of the body), v along the spacecraft's camera axis (top of the image = camera end).
Masks are linear: R = metallic, A = smoothness (Unity's Metallic/Smoothness layout).
"""
import argparse
import math
import os

import numpy as np
from PIL import Image, ImageDraw

RS = Image.Resampling

# panel size in mm (keep in step with eosat1_model.py)
PANEL_W, PANEL_H = 830.0, 790.0
BORDER, BAR = 22.0, 18.0
TEX = 1024
SS = 3                      # supersampling for clean edges

BRONZE = (172, 141, 88)
BRONZE_DARK = (120, 96, 58)
CELL = (12, 15, 24)
DIODE = (226, 222, 210)
HOLE = (40, 32, 22)


class Canvas:
    """Draw in panel millimetres; image row 0 is the camera end (v = 1)."""

    def __init__(self, w_mm, h_mm, px):
        self.w_mm, self.h_mm = w_mm, h_mm
        self.W = self.H = px * SS
        self.sx, self.sy = self.W / w_mm, self.H / h_mm
        self.rgb = Image.new("RGB", (self.W, self.H), BRONZE)
        self.mask = Image.new("RGBA", (self.W, self.H), (220, 0, 0, 140))   # bronze: metal 0.86, smooth 0.55
        self.d = ImageDraw.Draw(self.rgb)
        self.m = ImageDraw.Draw(self.mask)

    def pt(self, x, y):
        return (x * self.sx, y * self.sy)

    def poly(self, pts, fill, mask):
        p = [self.pt(x, y) for x, y in pts]
        self.d.polygon(p, fill=fill)
        self.m.polygon(p, fill=mask)

    def circle(self, x, y, r, fill, mask=None):
        self.d.ellipse([self.pt(x - r, y - r), self.pt(x + r, y + r)], fill=fill)
        if mask is not None:
            self.m.ellipse([self.pt(x - r, y - r), self.pt(x + r, y + r)], fill=mask)

    def finish(self, px):
        rgb = self.rgb.resize((px, px), RS.LANCZOS)
        mask = self.mask.resize((px, px), RS.LANCZOS)
        a = np.asarray(rgb).astype(np.float32)
        # faint brushed grain on the metal, a soft sheen across the cells
        rng = np.random.default_rng(7)
        grain = rng.normal(0, 1, (px, px)).astype(np.float32)
        grain = np.asarray(Image.fromarray(grain, "F").resize((px, px // 16), RS.BILINEAR).resize((px, px), RS.BICUBIC))
        metal = np.asarray(mask)[..., 0:1].astype(np.float32) / 255.0
        a += grain[..., None] * 5.0 * metal
        yy, xx = np.mgrid[0:px, 0:px] / px
        a += (1 - metal) * (6.0 * np.exp(-((xx - 0.35) ** 2 + (yy - 0.3) ** 2) / 0.18))[..., None] * np.array([0.6, 0.8, 1.0])
        return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB"), mask


CELL_MASK = (0, 0, 0, 225)      # cells: not metal, glassy cover
DIODE_MASK = (0, 0, 0, 90)
HOLE_MASK = (0, 0, 0, 40)


def frame_and_bars(c):
    """Bronze frame, two bars between the three strings, screw holes; returns the band rectangles."""
    W, H = c.w_mm, c.h_mm
    usable = H - 2 * BORDER - 2 * BAR
    band_h = usable / 3.0
    bands = []
    y = BORDER
    for i in range(3):
        bands.append((BORDER, y, W - BORDER, y + band_h))
        y += band_h + BAR
    # screw holes along the border and the bars
    step = 46.0
    n = int((W - 2 * 12) // step)
    for k in range(n + 1):
        x = 12 + k * (W - 24) / n
        for yy in (11, H - 11):
            c.circle(x, yy, 2.4, HOLE, HOLE_MASK)
    n = int((H - 2 * 12) // step)
    for k in range(n + 1):
        yy = 12 + k * (H - 24) / n
        for x in (11, W - 11):
            c.circle(x, yy, 2.4, HOLE, HOLE_MASK)
    for i in range(2):
        yb = bands[i][3] + BAR / 2
        for k in range(0, 9):
            x = BORDER + 30 + k * (W - 2 * BORDER - 60) / 8
            c.circle(x, yb, 3.0, HOLE, HOLE_MASK)
    return bands


def panel_a():
    """Six rows: each string has two rows of 17 cells, cut corners and diodes on the centre line."""
    c = Canvas(PANEL_W, PANEL_H, TEX)
    bands = frame_and_bars(c)
    cols = 18
    for (x0, y0, x1, y1) in bands:
        mid = (y0 + y1) / 2
        gap_mid, gap_cell, margin = 7.0, 2.6, 5.0
        pitch = (x1 - x0 - 2 * margin) / cols
        cw = pitch - gap_cell
        for row in (0, 1):
            ry0 = y0 + margin if row == 0 else mid + gap_mid / 2
            ry1 = mid - gap_mid / 2 if row == 0 else y1 - margin
            for k in range(cols):
                cx0 = x0 + margin + k * pitch + gap_cell / 2
                cx1 = cx0 + cw
                cut = min(cw, ry1 - ry0) * 0.28
                if row == 0:     # cut the lower-left corner, towards the string's centre line
                    pts = [(cx0, ry0), (cx1, ry0), (cx1, ry1), (cx0 + cut, ry1), (cx0, ry1 - cut)]
                    tri = [(cx0 + 0.6, ry1 - cut + 1.2), (cx0 + cut - 1.2, ry1 - 0.4), (cx0 + 0.6, ry1 - 0.4)]
                else:            # cut the upper-left corner
                    pts = [(cx0, ry0 + cut), (cx0 + cut, ry0), (cx1, ry0), (cx1, ry1), (cx0, ry1)]
                    tri = [(cx0 + 0.6, ry0 + 0.4), (cx0 + cut - 1.2, ry0 + 0.4), (cx0 + 0.6, ry0 + cut - 1.2)]
                c.poly(pts, CELL, CELL_MASK)
                c.poly(tri, DIODE, DIODE_MASK)
                # thin silver grid fingers across each cell
                for f in range(1, 6):
                    yy = ry0 + (ry1 - ry0) * f / 6
                    c.d.line([c.pt(cx0 + 1.5, yy), c.pt(cx1 - 1.5, yy)], fill=(19, 22, 31), width=max(1, SS // 3))
    return c.finish(TEX)


def panel_b():
    """Three strings of 4 x 16 cells; all four corners cut so the bronze shows as diamonds."""
    c = Canvas(PANEL_W, PANEL_H, TEX)
    bands = frame_and_bars(c)
    cols, rows = 14, 3
    for (x0, y0, x1, y1) in bands:
        margin, gap = 4.0, 2.4
        px = (x1 - x0 - 2 * margin) / cols
        py = (y1 - y0 - 2 * margin) / rows
        for r in range(rows):
            for k in range(cols):
                a0 = x0 + margin + k * px + gap / 2
                a1 = a0 + px - gap
                b0 = y0 + margin + r * py + gap / 2
                b1 = b0 + py - gap
                cut = min(px, py) * 0.17
                pts = [(a0 + cut, b0), (a1 - cut, b0), (a1, b0 + cut), (a1, b1 - cut), (a1 - cut, b1), (a0 + cut, b1),
                       (a0, b1 - cut), (a0, b0 + cut)]
                c.poly(pts, CELL, CELL_MASK)
                for f in range(1, 5):
                    yy = b0 + (b1 - b0) * f / 5
                    c.d.line([c.pt(a0 + 2, yy), c.pt(a1 - 2, yy)], fill=(18, 21, 30), width=max(1, SS // 3))
    return c.finish(TEX)


# ------------------------------------------------------------------ gold foil
def periodic_noise(n, beta, aniso=(1.0, 1.0), seed=0):
    """Seamlessly tiling noise with a 1/f^beta spectrum (optionally stretched)."""
    rng = np.random.default_rng(seed)
    white = rng.normal(size=(n, n))
    F = np.fft.fft2(white)
    fy = np.fft.fftfreq(n)[:, None] * aniso[1]
    fx = np.fft.fftfreq(n)[None, :] * aniso[0]
    f = np.sqrt(fx ** 2 + fy ** 2)
    f[0, 0] = 1.0
    out = np.real(np.fft.ifft2(F / f ** beta))
    out -= out.mean()
    return out / (out.std() + 1e-9)


def mli_gold(n=512):
    # creases: sharp lines along the zero contours of smooth noise (long, curvy, like
    # heat-shrunk foil), three layers at different scales and stretches, plus soft billows
    h = np.zeros((n, n))
    for seed, beta, aniso, sharp, wgt in ((1, 2.3, (1.0, 2.4), 3.2, 1.0), (2, 2.2, (2.1, 1.0), 3.8, 0.65),
                                          (8, 2.4, (1.0, 1.0), 4.5, 0.35)):
        z = periodic_noise(n, beta, aniso, seed=seed)
        h -= wgt * np.exp(-(z * sharp) ** 2)          # a valley along each zero crossing
    h += 0.45 * periodic_noise(n, 2.6, seed=4)         # broad billows
    h += 0.012 * periodic_noise(n, 1.0, seed=6)        # faint fine texture
    h = (h - h.min()) / (h.max() - h.min())
    # normal map from the height field (wrapping gradients so it tiles)
    k = 9.0
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * k
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * k
    nrm = np.dstack([-dx, dy, np.ones_like(h)])     # +y up in tangent space (image rows go down)
    nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
    normal = ((nrm * 0.5 + 0.5) * 255).astype(np.uint8)
    # colour: warm gold, darker in the creases, with some olive/brown tint variation
    shade = np.clip(0.55 + 1.1 * (nrm[..., 0] * 0.55 + nrm[..., 1] * 0.45), 0, 1)
    crease = np.clip((h - 0.25) / 0.5, 0, 1)
    tint = periodic_noise(n, 1.8, seed=5)
    light = np.array([228, 202, 118], np.float32)
    dark = np.array([112, 84, 34], np.float32)
    col = dark + (light - dark) * (0.15 + 0.55 * shade + 0.30 * crease)[..., None]
    col *= (1.0 + 0.06 * tint)[..., None]
    col[..., 2] *= (1.0 - 0.10 * np.clip(tint, 0, 2))
    return Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB"), Image.fromarray(normal, "RGB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "EOSAT1", "Textures"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rgb, mask = panel_a()
    rgb.save(os.path.join(a.out, "panel_a.png"), optimize=True)
    mask.save(os.path.join(a.out, "panel_a_mask.png"), optimize=True)
    rgb, mask = panel_b()
    rgb.save(os.path.join(a.out, "panel_b.png"), optimize=True)
    mask.save(os.path.join(a.out, "panel_b_mask.png"), optimize=True)
    col, nrm = mli_gold()
    col.save(os.path.join(a.out, "mli_gold.png"), optimize=True)
    nrm.save(os.path.join(a.out, "mli_gold_normal.png"), optimize=True)
    print("spacecraft textures written to", os.path.abspath(a.out))


if __name__ == "__main__":
    main()
