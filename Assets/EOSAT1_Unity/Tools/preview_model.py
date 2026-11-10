#!/usr/bin/env python3
"""
preview_model.py - render Assets/EOSAT1/Data/eosat1_model.json without Unity.

A small software rasteriser (numpy + Pillow) that builds the same triangles the Unity
scene builder makes, so the model can be checked against reference pictures.

    python3 preview_model.py --out preview.png                      # 3/4 view, stowed
    python3 preview_model.py --deploy 1 --view rear --out rear.png  # panels folded out
    python3 preview_model.py --match-photo photo.png --out cmp.png  # photo's viewpoint, side by side

Views: front, rear, side, top, below, photo. Body frame: +X flight, +Y wing axis, +Z camera.
"""
import argparse
import json
import math
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL = os.path.join(HERE, "..", "Assets", "EOSAT1", "Data", "eosat1_model.json")
TEXDIR = os.path.join(HERE, "..", "Assets", "EOSAT1", "Textures")


# ------------------------------------------------------------------ small vector helpers
def nrm(v):
    v = np.asarray(v, float)
    n = np.linalg.norm(v)
    return v / n if n > 0 else v


def rot(axis, deg):
    a = nrm(axis)
    t = math.radians(deg)
    c, s = math.cos(t), math.sin(t)
    x, y, z = a
    return np.array([[c + x * x * (1 - c), x * y * (1 - c) - z * s, x * z * (1 - c) + y * s],
                     [y * x * (1 - c) + z * s, c + y * y * (1 - c), y * z * (1 - c) - x * s],
                     [z * x * (1 - c) - y * s, z * y * (1 - c) + x * s, c + z * z * (1 - c)]])


def perp_basis(a):
    a = nrm(a)
    h = np.array([1.0, 0, 0]) if abs(a[0]) < 0.9 else np.array([0, 1.0, 0])
    e1 = nrm(np.cross(a, h))
    e2 = np.cross(a, e1)
    return e1, e2


# ------------------------------------------------------------------ meshes (same recipes as ProceduralMeshes.cs)
class Mesh:
    def __init__(self):
        self.tris = []      # (p0,p1,p2, n0,n1,n2, uv0,uv1,uv2, material)

    def tri(self, p, n, uv, mat):
        self.tris.append((np.array(p, float), np.array(n, float), np.array(uv, float), mat))

    def quad(self, a, b, c, d, n, uv, mat):
        """a-b-c-d around the face; n per vertex or one for all; uv per vertex."""
        n = np.array(n, float)
        if n.ndim == 1:
            n = np.tile(n, (4, 1))
        uv = np.array(uv, float)
        self.tri([a, b, c], n[[0, 1, 2]], uv[[0, 1, 2]], mat)
        self.tri([a, c, d], n[[0, 2, 3]], uv[[0, 2, 3]], mat)


def mesh_prism(p, m):
    poly = [np.array(v, float) for v in p["polygon"]]
    z0, z1 = p["z"]
    s = p.get("uv_mm", 400.0)
    mats = p["materials"]
    k = len(poly)
    for i in range(k):
        a2, b2 = poly[i], poly[(i + 1) % k]
        e = b2 - a2
        L = np.linalg.norm(e)
        n = nrm([e[1], -e[0], 0.0])                  # polygon is counter-clockwise: outward = right of each edge
        a0, b0 = np.r_[a2, z0], np.r_[b2, z0]
        a1, b1 = np.r_[a2, z1], np.r_[b2, z1]
        m.quad(a0, b0, b1, a1, n, [(0, z0 / s), (L / s, z0 / s), (L / s, z1 / s), (0, z1 / s)], mats["sides"])
    c = np.mean(poly, axis=0)
    for i in range(k):
        a2, b2 = poly[i], poly[(i + 1) % k]
        m.tri([np.r_[c, z1], np.r_[a2, z1], np.r_[b2, z1]], [[0, 0, 1]] * 3,
              [c / s, a2 / s, b2 / s], mats["front"])
        m.tri([np.r_[c, z0], np.r_[b2, z0], np.r_[a2, z0]], [[0, 0, -1]] * 3,
              [c / s, b2 / s, a2 / s], mats["back"])


def mesh_lathe(p, m):
    o = np.array(p["origin"], float)
    ax = nrm(p["axis"])
    e1, e2 = perp_basis(ax)
    prof = [np.array(q, float) for q in p["profile"]]
    mats = p["materials"]
    seg = int(p.get("segments", 48))
    for k in range(len(prof) - 1):
        (r0, h0), (r1, h1) = prof[k], prof[k + 1]
        dn = nrm([h1 - h0, -(r1 - r0)])               # profile normal: (dh, -dr)
        for i in range(seg):
            t0, t1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
            u0 = math.cos(t0) * e1 + math.sin(t0) * e2
            u1 = math.cos(t1) * e1 + math.sin(t1) * e2
            P = [o + h0 * ax + r0 * u0, o + h0 * ax + r0 * u1, o + h1 * ax + r1 * u1, o + h1 * ax + r1 * u0]
            N = [dn[0] * u0 + dn[1] * ax, dn[0] * u1 + dn[1] * ax, dn[0] * u1 + dn[1] * ax, dn[0] * u0 + dn[1] * ax]
            if r0 < 1e-6 and r1 < 1e-6:
                continue
            m.quad(P[0], P[1], P[2], P[3], N, [(0, 0)] * 4, mats[k])


def mesh_box(p, m):
    c = np.array(p["center"], float)
    sx, sy, sz = [v / 2 for v in p["size"]]
    x = nrm(p.get("x_axis", [1, 0, 0]))
    z = nrm(p.get("z_axis", [0, 0, 1]))
    x = nrm(x - z * np.dot(x, z))
    y = np.cross(z, x)
    mat = p["material"]
    # (normal, u, v, half-size along normal, half-size along u, along v); u x v = normal
    faces = ((x, y, z, sx, sy, sz), (-x, z, y, sx, sz, sy), (y, z, x, sy, sz, sx),
             (-y, x, z, sy, sx, sz), (z, x, y, sz, sx, sy), (-z, y, x, sz, sy, sx))
    for n, u, v, d, eu, ev in faces:
        f = c + n * d
        m.quad(f - u * eu - v * ev, f + u * eu - v * ev, f + u * eu + v * ev, f - u * eu + v * ev, n,
               [(0, 0), (1, 0), (1, 1), (0, 1)], mat)


def mesh_cylinder(p, m, seg=24):
    a, b = np.array(p["start"], float), np.array(p["end"], float)
    ax = nrm(b - a)
    e1, e2 = perp_basis(ax)
    r = p["radius"]
    mat = p["material"]
    for i in range(seg):
        t0, t1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
        u0 = math.cos(t0) * e1 + math.sin(t0) * e2
        u1 = math.cos(t1) * e1 + math.sin(t1) * e2
        m.quad(a + r * u0, a + r * u1, b + r * u1, b + r * u0, [u0, u1, u1, u0], [(0, 0)] * 4, mat)
        m.tri([b, b + r * u0, b + r * u1], [ax] * 3, [(0, 0)] * 3, mat)
        m.tri([a, a + r * u1, a + r * u0], [-ax] * 3, [(0, 0)] * 3, mat)


def mesh_panel(p, m):
    c = np.array(p["center"], float)
    n = nrm(p["normal"])
    up = nrm(np.array(p["up"], float) - n * np.dot(p["up"], n))
    right = np.cross(up, n)                       # texture u: up x normal, so the picture isn't mirrored
    hw, hh, ht = p["width"] / 2, p["height"] / 2, p["thickness"] / 2
    mats = p["materials"]
    f = c + n * ht
    m.quad(f - right * hw - up * hh, f + right * hw - up * hh, f + right * hw + up * hh, f - right * hw + up * hh, n,
           [(0, 0), (1, 0), (1, 1), (0, 1)], mats["front"])
    b = c - n * ht
    m.quad(b + right * hw - up * hh, b - right * hw - up * hh, b - right * hw + up * hh, b + right * hw + up * hh, -n,
           [(0, 0), (1, 0), (1, 1), (0, 1)], mats["back"])
    for d, e, L, M in ((right, up, hw, hh), (-right, up, hw, hh), (up, right, hh, hw), (-up, right, hh, hw)):
        q = c + d * L
        m.quad(q - e * M - n * ht, q + e * M - n * ht, q + e * M + n * ht, q - e * M + n * ht, d, [(0, 0)] * 4, mats["edge"])


BUILDERS = {"prism": mesh_prism, "lathe": mesh_lathe, "box": mesh_box, "cylinder": mesh_cylinder, "panel": mesh_panel}


def build(model, deploy=0.0, antenna=0.0):
    joints = {j["name"]: j for j in model["joints"]}
    tris = []
    for p in model["parts"]:
        m = Mesh()
        if p["type"] not in BUILDERS:
            continue
        BUILDERS[p["type"]](p, m)
        jt = joints.get(p.get("joint"))
        if jt is not None:
            frac = deploy if jt["role"].startswith("wing") else antenna
            R = rot(jt["axis"], jt["angle"] * frac)
            piv = np.array(jt["pivot"], float)
            m.tris = [((pp - piv) @ R.T + piv, nn @ R.T, uv, mat) for pp, nn, uv, mat in m.tris]
        tris += m.tris
    return tris


# ------------------------------------------------------------------ materials
class Mat:
    def __init__(self, d):
        self.color = np.array(d.get("color", [0.8, 0.8, 0.8]), float)
        self.metal = float(d.get("metallic", 0.0))
        self.smooth = float(d.get("smoothness", 0.5))
        self.tex = self.mask = None
        if d.get("texture"):
            self.tex = np.asarray(Image.open(os.path.join(TEXDIR, d["texture"])).convert("RGB"), np.float32) / 255.0
        if d.get("mask"):
            self.mask = np.asarray(Image.open(os.path.join(TEXDIR, d["mask"])).convert("RGBA"), np.float32) / 255.0

    def sample(self, uv):
        if self.tex is None:
            col = np.broadcast_to(self.color, (len(uv), 3))
            return col, np.full(len(uv), self.metal), np.full(len(uv), self.smooth)
        h, w = self.tex.shape[:2]
        u = np.mod(uv[:, 0], 1.0)
        v = np.mod(uv[:, 1], 1.0)
        x = np.clip((u * w).astype(int), 0, w - 1)
        y = np.clip(((1 - v) * h).astype(int), 0, h - 1)         # image row 0 is v = 1 (Unity's convention)
        col = self.tex[y, x] * self.color
        if self.mask is not None:
            mk = self.mask[y, x]
            return col, mk[:, 0] * self.metal, mk[:, 3] * self.smooth
        return col, np.full(len(uv), self.metal), np.full(len(uv), self.smooth)


# ------------------------------------------------------------------ rasteriser
def render(tris, mats, K, R, t, size, light_dir, bg=(10, 12, 20), ambient=0.18, light=1.25, env=0.22):
    W, H = size
    img = np.zeros((H, W, 3), np.float32) + np.array(bg, np.float32) / 255.0
    zbuf = np.full((H, W), np.inf, np.float32)
    L = nrm(light_dir)
    cam_pos = -R.T @ t
    for P, N, UV, mname in tris:
        Pc = P @ R.T + t
        if np.any(Pc[:, 2] <= 1.0):
            continue
        x = K[0, 0] * Pc[:, 0] / Pc[:, 2] + K[0, 2]
        y = K[1, 1] * Pc[:, 1] / Pc[:, 2] + K[1, 2]
        area = (x[1] - x[0]) * (y[2] - y[0]) - (x[2] - x[0]) * (y[1] - y[0])
        if abs(area) < 1e-9:
            continue
        x0, x1 = max(int(math.floor(x.min())), 0), min(int(math.ceil(x.max())), W - 1)
        y0, y1 = max(int(math.floor(y.min())), 0), min(int(math.ceil(y.max())), H - 1)
        if x0 > x1 or y0 > y1:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        w0 = ((x[1] - gx) * (y[2] - gy) - (x[2] - gx) * (y[1] - gy)) / area
        w1 = ((x[2] - gx) * (y[0] - gy) - (x[0] - gx) * (y[2] - gy)) / area
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        iz = np.array([1 / Pc[0, 2], 1 / Pc[1, 2], 1 / Pc[2, 2]])
        izp = w0 * iz[0] + w1 * iz[1] + w2 * iz[2]
        z = 1 / izp
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        ok = inside & (z < sub)
        if not ok.any():
            continue
        # perspective-correct attributes
        b0, b1, b2 = (w0 * iz[0] / izp)[ok], (w1 * iz[1] / izp)[ok], (w2 * iz[2] / izp)[ok]
        n = b0[:, None] * N[0] + b1[:, None] * N[1] + b2[:, None] * N[2]
        n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
        pw = b0[:, None] * P[0] + b1[:, None] * P[1] + b2[:, None] * P[2]
        v = cam_pos - pw
        v /= np.linalg.norm(v, axis=1, keepdims=True)
        n = np.where((np.sum(n * v, 1) < 0)[:, None], -n, n)        # two-sided like the panel backs
        uv = b0[:, None] * UV[0] + b1[:, None] * UV[1] + b2[:, None] * UV[2]
        col, metal, smooth = mats[mname].sample(uv)
        ndl = np.clip(n @ L, 0, 1)
        hv = v + L
        hv /= np.linalg.norm(hv, axis=1, keepdims=True)
        spec_pow = 2.0 ** (2 + 9 * smooth)
        spec = np.clip(np.sum(n * hv, 1), 0, 1) ** spec_pow * (spec_pow + 8) / 40.0 * ndl
        f0 = 0.04 * (1 - metal)[:, None] + col * metal[:, None]
        diffuse = col * (1 - 0.85 * metal)[:, None]
        # crude environment: a dim sky above, darker below, tinted by metal colour
        envf = env * (0.5 + 0.5 * n[:, 2:3]) * f0
        c = diffuse * (ambient + light * ndl[:, None]) + f0 * spec[:, None] * light + envf
        rows, cols = np.nonzero(ok)
        img[y0 + rows, x0 + cols] = c
        sub[ok] = z[ok]
    return np.clip(img, 0, 1) ** (1 / 1.8)


def look_at(eye, target, up):
    f = nrm(np.asarray(target, float) - eye)
    r = nrm(np.cross(f, up))
    u = np.cross(r, f)
    Rm = np.stack([r, -u, f])          # camera x right, y down, z forward
    return Rm, -Rm @ eye


VIEWS = {   # eye direction (from the spacecraft), up vector
    "front": ((1.0, 0.25, 0.35), (0, 0, -1)),
    "rear": ((-1.0, -0.3, 0.35), (0, 0, -1)),
    "side": ((0.15, 1.0, 0.25), (0, 0, -1)),
    "top": ((-0.2, 0.15, -1.0), (1, 0, 0)),
    "below": ((0.35, -0.45, 1.0), (-1, 0, 0)),
    "three-quarter": ((-0.75, -0.8, 0.55), (0, 0, -1)),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default=MODEL)
    ap.add_argument("--view", default="three-quarter", choices=sorted(VIEWS))
    ap.add_argument("--deploy", type=float, default=0.0, help="solar panel fraction 0..1")
    ap.add_argument("--antenna", type=float, default=0.0, help="antenna fraction 0..1")
    ap.add_argument("--size", default="900x620")
    ap.add_argument("--distance", type=float, default=4200.0, help="mm")
    ap.add_argument("--fov", type=float, default=35.0)
    ap.add_argument("--out", default="preview.png")
    a = ap.parse_args()
    model = json.load(open(a.model))
    mats = {k: Mat(v) for k, v in model["materials"].items()}
    W, H = [int(v) for v in a.size.split("x")]
    f = W / 2 / math.tan(math.radians(a.fov / 2))
    K = np.array([[f, 0, W / 2], [0, f, H / 2], [0, 0, 1]])
    d, up = VIEWS[a.view]
    eye = nrm(d) * a.distance
    Rm, t = look_at(eye, np.zeros(3), np.array(up, float))
    tris = build(model, a.deploy, a.antenna)
    light = nrm(np.array(d) * 0.4 + np.array([-0.3, 0.6, -0.2]))
    img = render(tris, mats, K, Rm, t, (W, H), light)
    Image.fromarray((img * 255).astype(np.uint8)).save(a.out)
    print("wrote", a.out, "(%d triangles)" % len(tris))


if __name__ == "__main__":
    main()
