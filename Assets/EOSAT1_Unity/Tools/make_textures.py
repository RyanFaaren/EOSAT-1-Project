#!/usr/bin/env python3
"""
make_textures.py - procedural textures for the space scene around EOS SAT-1.

    python3 make_textures.py --out ../Assets/EOSAT1/Textures

Writes (all equirectangular maps put longitude -180 at the left edge,
latitude +90 at the top, which is how NASA's Blue Marble is laid out too):
  earth_day.png     4096x2048 RGBA  colour + clouds; alpha = open water (sun glint)
  earth_night.png   2048x1024 RGB   city lights
  moon.png          1024x512  RGB   maria and bright craters (selenographic)
  ../Data/bright_stars.txt          named stars to ~mag 3 + random fainter filler

The spacecraft's own textures (solar panels, gold foil) come from
make_spacecraft_textures.py.

Geography is hand-entered (geo_data.py): recognisable continents and seas,
not survey-grade coastlines. Replace earth_day.png with NASA Blue Marble
(public domain, visibleearth.nasa.gov) for real detail; keep the same layout.
"""
import argparse
import math
import os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import geo_data as G

RS = Image.Resampling


def poly_px(poly, w, h, offset=0.0):
    return [((lon + offset + 180.0) / 360.0 * w, (90.0 - lat) / 180.0 * h) for lon, lat in poly]


def raster(polys, w, h, ss=2):
    img = Image.new("L", (w * ss, h * ss), 0)
    d = ImageDraw.Draw(img)
    for p in polys:
        if len(p) < 3:
            continue
        for off in (-360.0, 0.0, 360.0):
            d.polygon(poly_px(p, w * ss, h * ss, off), fill=255)
    if ss > 1:
        img = img.resize((w, h), RS.BOX)
    return np.asarray(img, dtype=np.float32) / 255.0


def fbm(w, h, base=6, octaves=7, persistence=0.55, seed=1):
    """Fractal noise that wraps seamlessly in longitude."""
    rng = np.random.default_rng(seed)
    total = np.zeros((h, w), np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        gw = base * 2 ** o
        gh = max(3, gw // 2)
        g = rng.standard_normal((gh, gw)).astype(np.float32)
        pad = 3
        gp = np.concatenate([g[:, -pad:], g, g[:, :pad]], axis=1)
        up_w = int(round(w * (gw + 2 * pad) / gw))
        arr = np.asarray(Image.fromarray(gp, mode="F").resize((up_w, h), RS.BICUBIC))
        x0 = int(round(w * pad / gw))
        total += amp * arr[:, x0:x0 + w]
        norm += amp
        amp *= persistence
    total /= norm
    return (total - total.mean()) / (total.std() + 1e-9)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def lat_grid(w, h):
    lat = 90.0 - (np.arange(h, dtype=np.float32) + 0.5) / h * 180.0
    return np.repeat(lat[:, None], w, axis=1)


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def blur(a, sigma):
    return ndimage.gaussian_filter(a, sigma, mode=("nearest", "wrap"))


# ------------------------------------------------------------------ Earth
def earth_day(w=4096, h=2048):
    land_polys = [G.NORTH_AMERICA, G.SOUTH_AMERICA, G.AFRICA, G.EURASIA, G.AUSTRALIA, G.ANTARCTICA]
    land_polys += list(G.ISLANDS.values())
    land = raster(land_polys, w, h)
    water = raster(list(G.WATER.values()), w, h)
    land = np.clip(land - water, 0, 1)
    lat = lat_grid(w, h)
    alat = np.abs(lat)

    # ---- ocean: deep blue, lighter over the shelves near the coast
    ocean_bin = land < 0.5
    dist = ndimage.distance_transform_edt(ocean_bin).astype(np.float32)
    n_ocean = fbm(w, h, base=5, octaves=6, seed=11)
    shelf = np.exp(-dist / (5.0 + 4.0 * np.clip(n_ocean + 1.0, 0, 2))) * 0.55
    deep = np.array([12, 30, 66], np.float32)
    mid = np.array([18, 52, 95], np.float32)
    shallow = np.array([34, 96, 128], np.float32)
    ocean = lerp(lerp(np.broadcast_to(deep, (h, w, 3)), np.broadcast_to(mid, (h, w, 3)),
                      np.clip(0.35 + 0.18 * n_ocean, 0, 1)),
                 np.broadcast_to(shallow, (h, w, 3)), shelf * 0.8)
    polar_sea = smoothstep(62, 78, alat) * 0.55
    ocean = lerp(ocean, np.broadcast_to(np.array([150, 170, 190], np.float32), (h, w, 3)),
                 polar_sea * (np.clip(fbm(w, h, base=8, octaves=5, seed=12), -1, 2) * 0.3 + 0.7))

    # ---- land: latitude biomes + deserts, forests, plateaus, ice
    tropic = np.array([42, 92, 38], np.float32)
    temperate = np.array([82, 106, 52], np.float32)
    boreal = np.array([44, 74, 46], np.float32)
    tundra = np.array([118, 114, 94], np.float32)
    col = lerp(np.broadcast_to(tropic, (h, w, 3)), np.broadcast_to(temperate, (h, w, 3)), smoothstep(18, 32, alat))
    col = lerp(col, np.broadcast_to(boreal, (h, w, 3)), smoothstep(48, 56, alat))
    col = lerp(col, np.broadcast_to(tundra, (h, w, 3)), smoothstep(62, 70, alat))
    desert = blur(raster(G.DESERTS, w, h, 1), 18)
    forest = blur(raster(G.FORESTS, w, h, 1), 14)
    plateau = blur(raster(G.PLATEAUS, w, h, 1), 10)
    n_land = fbm(w, h, base=8, octaves=7, seed=21)
    desert_col = lerp(np.broadcast_to(np.array([206, 176, 122], np.float32), (h, w, 3)),
                      np.broadcast_to(np.array([168, 120, 78], np.float32), (h, w, 3)),
                      np.clip(0.5 + 0.35 * n_land, 0, 1))
    col = lerp(col, desert_col, np.clip(desert * (0.85 + 0.25 * n_land), 0, 1))
    col = lerp(col, np.broadcast_to(np.array([26, 66, 30], np.float32), (h, w, 3)), forest * 0.8)
    col = lerp(col, np.broadcast_to(np.array([128, 112, 92], np.float32), (h, w, 3)), plateau * 0.8)
    n_fine = fbm(w, h, base=64, octaves=4, persistence=0.6, seed=22)
    col *= (1.0 + 0.10 * n_land + 0.07 * n_fine)[..., None]
    grey = col.mean(axis=-1, keepdims=True)
    col = grey + (col - grey) * 0.85                 # slightly muted, like the real thing from orbit
    ice = raster([G.ISLANDS["Greenland"], G.ANTARCTICA, G.ISLANDS["Svalbard"], G.ISLANDS["SevernayaZemlya"],
                  G.ISLANDS["FranzJosef"], G.ISLANDS["NovayaZemlya"]], w, h)
    ice = np.clip(np.maximum(ice * smoothstep(55, 70, alat + 4 * n_land), smoothstep(74, 82, alat)), 0, 1)
    # ice sheets end in a ragged rim of bare rock along the coast
    rim = blur((ndimage.binary_erosion(ice > 0.5, iterations=2) == 0).astype(np.float32) * (ice > 0.5), 1.5)
    ice = np.clip(ice - 0.55 * rim * np.clip(0.6 + 0.6 * n_land, 0, 1), 0, 1)
    ice = blur(ice, 1.2)
    ice_col = lerp(np.broadcast_to(np.array([206, 216, 228], np.float32), (h, w, 3)),
                   np.broadcast_to(np.array([242, 246, 250], np.float32), (h, w, 3)),
                   np.clip(0.65 + 0.3 * n_land, 0, 1))
    col = lerp(col, ice_col, ice)
    snow = smoothstep(0.7, 0.95, plateau) * np.clip(n_land * 0.7 + 0.1, 0, 1) * 0.45
    col = lerp(col, np.broadcast_to(np.array([214, 218, 224], np.float32), (h, w, 3)), snow)

    surf = lerp(ocean, col, land)

    # ---- clouds: domain-warped fractal noise (swirls and streaks), banded like
    # the real circulation: ITCZ near 6 N, clear subtropical highs, stormy 50-60 deg.
    base_n = fbm(w, h, base=14, octaves=8, persistence=0.62, seed=31)
    wx = fbm(w, h, base=4, octaves=5, persistence=0.5, seed=33) * 55.0
    wy = fbm(w, h, base=4, octaves=5, persistence=0.5, seed=34) * 28.0
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    polar_calm = smoothstep(88, 70, alat)          # no warping right at the poles (avoids streaks)
    c = ndimage.map_coordinates(base_n, [np.clip(yy + wy * polar_calm, 0, h - 1), np.mod(xx + wx * polar_calm, w)],
                                order=1, mode="wrap")
    fine = fbm(w, h, base=96, octaves=3, persistence=0.5, seed=35)
    thr = (1.05 - 0.55 * np.exp(-((lat - 6) / 6) ** 2)
           + 0.40 * np.exp(-((alat - 24) / 9) ** 2)
           - 0.55 * np.exp(-((alat - 56) / 10) ** 2))
    cloud = smoothstep(thr - 0.25, thr + 1.5, c + 0.3 * fine) ** 1.35 * 0.86
    cloud *= 1.0 - 0.5 * smoothstep(0.5, 1.0, desert)
    cloud_col = np.array([244, 246, 250], np.float32)
    rgb = lerp(surf, np.broadcast_to(cloud_col, (h, w, 3)), cloud)
    glint = np.clip((1.0 - land) * (1.0 - cloud) * (1.0 - polar_sea), 0, 1)
    rgba = np.dstack([np.clip(rgb, 0, 255), glint * 255]).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA"), land


def earth_night(land, w=2048, h=1024, seed=5):
    rng = np.random.default_rng(seed)
    land_small = np.asarray(Image.fromarray((land * 255).astype(np.uint8)).resize((w, h), RS.BOX),
                            np.float32) / 255.0
    lights = np.zeros((h, w), np.float32)
    density = np.zeros((h, w), np.float32)

    def stamp(arr, x, y, sigma, peak):
        r = int(math.ceil(sigma * 3)) + 1
        xs = np.arange(-r, r + 1)
        k = np.exp(-(xs[None, :] ** 2 + xs[:, None] ** 2) / (2 * sigma * sigma)) * peak
        xi, yi = int(round(x)), int(round(y))
        for dx_off in (-w, 0, w):
            x0, x1 = xi - r + dx_off, xi + r + 1 + dx_off
            y0, y1 = yi - r, yi + r + 1
            cx0, cx1, cy0, cy1 = max(0, x0), min(w, x1), max(0, y0), min(h, y1)
            if cx0 < cx1 and cy0 < cy1:
                arr[cy0:cy1, cx0:cx1] += k[cy0 - y0:cy1 - y0, cx0 - x0:cx1 - x0]

    for lat, lon, pop in G.CITIES:
        x = (lon + 180.0) / 360.0 * w
        y = (90.0 - lat) / 180.0 * h
        stamp(lights, x, y, 0.55 + 0.45 * math.sqrt(pop), 0.55 + 0.45 * min(1.0, pop / 8.0))
        stamp(density, x, y, 6.0 + 2.0 * math.sqrt(pop), min(1.0, 0.25 + pop / 12.0))
        for _ in range(int(4 + pop * 3)):
            ang = rng.uniform(0, 2 * math.pi)
            rr = abs(rng.normal(0, 2.0 + 1.5 * math.sqrt(pop)))
            stamp(lights, x + rr * math.cos(ang), y + rr * math.sin(ang), 0.6, rng.uniform(0.08, 0.3))
    density = np.clip(density, 0, 1)
    # small towns where people live
    xs = rng.uniform(0, w, 400000)
    ys = rng.uniform(0, h, 400000)
    p = density[ys.astype(int), xs.astype(int)] * land_small[ys.astype(int), xs.astype(int)]
    keep = (p > 0.02) & (rng.random(xs.size) < p * 0.8)
    np.add.at(lights, (ys[keep].astype(int), xs[keep].astype(int)), rng.uniform(0.05, 0.3, keep.sum()))
    lights = ndimage.gaussian_filter(lights, 0.6, mode=("nearest", "wrap"))
    lights += 0.06 * blur(density, 2.0)
    lights *= np.clip(land_small * 1.4, 0, 1)
    lights = np.clip(lights, 0, 1) ** 0.8
    col = np.array([255, 196, 120], np.float32)
    rgb = (lights[..., None] * col).clip(0, 255).astype(np.uint8)
    return Image.fromarray(rgb, "RGB")


# ------------------------------------------------------------------- Moon
MARIA = [  # (selenographic lat, lon, angular radius deg, darkness)
    (25, -55, 15, 0.55), (10, -60, 14, 0.55), (-2, -48, 10, 0.5), (32.8, -15.6, 17, 0.6), (28.0, 17.5, 10, 0.6),
    (8.5, 31.4, 12, 0.6), (17.0, 59.1, 8, 0.6), (-7.8, 51.3, 9, 0.5), (-15.2, 35.5, 5.5, 0.5), (-21.3, -16.6, 10, 0.5),
    (-24.4, -38.6, 6, 0.55), (57, -30, 5, 0.45), (56, -10, 5, 0.45), (56, 10, 5, 0.45), (55, 30, 4, 0.4),
    (13.3, 3.6, 3.5, 0.5), (7.5, -30.9, 7, 0.5), (-10.0, -23.1, 5, 0.5), (1.3, 87.0, 5, 0.45), (13.3, 86.1, 4, 0.4),
    (-38.9, 93.0, 6, 0.35), (-19.4, -92.8, 4, 0.4), (27.3, 147.9, 4, 0.45), (-33.7, 163.5, 3, 0.3),
]
BRIGHT_CRATERS = [(-43.3, -11.4, 1.3, 0.35, 14), (9.6, -20.1, 1.3, 0.25, 8), (8.1, -38.0, 0.6, 0.2, 5),
                  (23.7, -47.4, 0.6, 0.35, 3)]


def moon(w=1024, h=512, seed=9):
    rng = np.random.default_rng(seed)
    lat = np.radians(lat_grid(w, h))
    lon = np.radians(((np.arange(w) + 0.5) / w * 360.0 - 180.0))[None, :].repeat(h, 0)
    xyz = np.stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)], -1)

    def ang_to(la, lo):
        la, lo = math.radians(la), math.radians(lo)
        c = np.array([math.cos(la) * math.cos(lo), math.cos(la) * math.sin(lo), math.sin(la)], np.float32)
        return np.degrees(np.arccos(np.clip(xyz @ c, -1, 1)))
    n = fbm(w, h, base=8, octaves=7, seed=41)
    alb = 0.66 + 0.06 * n
    for la, lo, r, dark in MARIA:
        d = ang_to(la, lo) + 2.0 * n
        alb -= dark * 0.45 * smoothstep(r, r * 0.6, d)
    alb = np.clip(alb, 0.25, 1)
    # small craters
    for _ in range(900):
        la, lo = math.degrees(math.asin(rng.uniform(-1, 1))), rng.uniform(-180, 180)
        r = rng.pareto(2.2) * 0.6 + 0.4
        d = ang_to(la, lo)
        alb += 0.06 * (smoothstep(r * 1.25, r, d) - smoothstep(r, r * 0.4, d) * 1.2)
    for la, lo, r, bright, rays in BRIGHT_CRATERS:
        d = ang_to(la, lo)
        alb += bright * smoothstep(r * 2, r * 0.3, d)
        for k in range(rays):
            a = rng.uniform(0, 2 * math.pi)
            for s in np.linspace(r, r * 12, 24):
                la2 = la + s * math.cos(a)
                lo2 = lo + s * math.sin(a) / max(0.2, math.cos(math.radians(la)))
                alb += 0.05 * smoothstep(0.9, 0.0, ang_to(la2, lo2)) * (1 - s / (r * 12))
    alb = np.clip(alb, 0, 1)
    rgb = np.stack([alb * 236, alb * 232, alb * 224], -1).clip(0, 255).astype(np.uint8)
    return Image.fromarray(rgb, "RGB")


# ------------------------------------------------------------------ stars
BRIGHT_STARS = [  # name, RA hours, Dec deg, V, B-V   (J2000)
    ("Sirius", 6.7525, -16.716, -1.46, 0.00), ("Canopus", 6.3992, -52.696, -0.74, 0.15),
    ("Rigil Kentaurus", 14.6601, -60.834, -0.27, 0.71), ("Arcturus", 14.2610, 19.182, -0.05, 1.23),
    ("Vega", 18.6156, 38.784, 0.03, 0.00), ("Capella", 5.2782, 45.998, 0.08, 0.80), ("Rigel", 5.2423, -8.202, 0.13, -0.03),
    ("Procyon", 7.6550, 5.225, 0.34, 0.42), ("Achernar", 1.6286, -57.237, 0.46, -0.16),
    ("Betelgeuse", 5.9195, 7.407, 0.50, 1.85), ("Hadar", 14.0637, -60.373, 0.61, -0.23), ("Altair", 19.8464, 8.868, 0.76, 0.22),
    ("Acrux", 12.4433, -63.099, 0.76, -0.24), ("Aldebaran", 4.5987, 16.509, 0.86, 1.54), ("Antares", 16.4901, -26.432, 0.96, 1.83),
    ("Spica", 13.4199, -11.161, 0.97, -0.23), ("Pollux", 7.7553, 28.026, 1.14, 1.00), ("Fomalhaut", 22.9608, -29.622, 1.16, 0.09),
    ("Deneb", 20.6905, 45.280, 1.25, 0.09), ("Mimosa", 12.7954, -59.689, 1.25, -0.23), ("Regulus", 10.1395, 11.967, 1.40, -0.11),
    ("Adhara", 6.9771, -28.972, 1.50, -0.21), ("Castor", 7.5767, 31.888, 1.58, 0.03), ("Shaula", 17.5601, -37.104, 1.62, -0.22),
    ("Gacrux", 12.5194, -57.113, 1.64, 1.60), ("Bellatrix", 5.4189, 6.350, 1.64, -0.22), ("Elnath", 5.4382, 28.608, 1.65, -0.13),
    ("Miaplacidus", 9.2200, -69.717, 1.68, 0.07), ("Alnilam", 5.6036, -1.202, 1.69, -0.18), ("Regor", 8.1589, -47.337, 1.83, -0.22),
    ("Alnair", 22.1372, -46.961, 1.74, -0.13), ("Alnitak", 5.6793, -1.943, 1.77, -0.21), ("Alioth", 12.9005, 55.960, 1.77, -0.02),
    ("Dubhe", 11.0621, 61.751, 1.79, 1.07), ("Mirfak", 3.4054, 49.861, 1.79, 0.48), ("Wezen", 7.1399, -26.393, 1.84, 0.68),
    ("Kaus Australis", 18.4029, -34.385, 1.85, -0.03), ("Avior", 8.3752, -59.510, 1.86, 1.28), ("Alkaid", 13.7923, 49.313, 1.86, -0.19),
    ("Sargas", 17.6220, -42.998, 1.86, 0.40), ("Menkalinan", 5.9921, 44.948, 1.90, 0.03), ("Atria", 16.8111, -69.028, 1.91, 1.44),
    ("Alhena", 6.6285, 16.399, 1.92, 0.00), ("Peacock", 20.4275, -56.735, 1.94, -0.20), ("Alsephina", 8.7451, -54.708, 1.96, 0.04),
    ("Mirzam", 6.3783, -17.956, 1.98, -0.23), ("Alphard", 9.4598, -8.659, 1.98, 1.44), ("Polaris", 2.5303, 89.264, 1.98, 0.60),
    ("Hamal", 2.1196, 23.462, 2.00, 1.15), ("Algieba", 10.3329, 19.841, 2.01, 1.13), ("Diphda", 0.7265, -17.987, 2.04, 1.02),
    ("Nunki", 18.9211, -26.297, 2.05, -0.13), ("Menkent", 14.1114, -36.370, 2.06, 1.01), ("Mirach", 1.1622, 35.621, 2.05, 1.58),
    ("Alpheratz", 0.1398, 29.091, 2.06, -0.11), ("Rasalhague", 17.5822, 12.560, 2.07, 0.15), ("Kochab", 14.8451, 74.156, 2.08, 1.47),
    ("Saiph", 5.7959, -9.670, 2.09, -0.17), ("Denebola", 11.8177, 14.572, 2.14, 0.09), ("Algol", 3.1361, 40.956, 2.12, -0.05),
    ("Tiaki", 22.7111, -46.885, 2.10, 1.60), ("Muhlifain", 12.6919, -48.960, 2.17, -0.01), ("Aspidiske", 9.2848, -59.275, 2.21, 0.18),
    ("Suhail", 9.1333, -43.433, 2.21, 1.66), ("Alphecca", 15.5781, 26.715, 2.23, -0.02), ("Mintaka", 5.5334, -0.299, 2.23, -0.22),
    ("Sadr", 20.3705, 40.257, 2.23, 0.67), ("Eltanin", 17.9434, 51.489, 2.24, 1.52), ("Schedar", 0.6751, 56.537, 2.24, 1.17),
    ("Naos", 8.0597, -40.003, 2.25, -0.27), ("Almach", 2.0650, 42.330, 2.26, 1.37), ("Caph", 0.1530, 59.150, 2.28, 0.34),
    ("Izar", 14.7498, 27.074, 2.37, 0.97), ("Dschubba", 16.0056, -22.622, 2.29, -0.12), ("Larawag", 16.8361, -34.293, 2.29, 1.15),
    ("Merak", 11.0307, 56.383, 2.34, -0.02), ("Ankaa", 0.4381, -42.306, 2.39, 1.09), ("Girtab", 17.7081, -39.030, 2.39, -0.17),
    ("Enif", 21.7364, 9.875, 2.38, 1.52), ("Scheat", 23.0629, 28.083, 2.42, 1.67), ("Sabik", 17.1730, -15.725, 2.43, 0.06),
    ("Phecda", 11.8972, 53.695, 2.44, 0.00), ("Aludra", 7.4016, -29.303, 2.45, -0.08), ("Markeb", 9.3686, -55.011, 2.47, -0.18),
    ("Navi", 0.9451, 60.717, 2.47, -0.15), ("Markab", 23.0794, 15.205, 2.49, -0.04), ("Aljanah", 20.7702, 33.970, 2.48, 1.03),
    ("Acrab", 16.0906, -19.806, 2.56, -0.07), ("Zosma", 11.2351, 20.524, 2.56, 0.12), ("Arneb", 5.5455, -17.822, 2.58, 0.21),
    ("Gienah", 12.2634, -17.542, 2.59, -0.11), ("Ascella", 19.0435, -29.880, 2.60, 0.08), ("Zubeneschamali", 15.2834, -9.383, 2.61, -0.11),
    ("Unukalhai", 15.7378, 6.426, 2.63, 1.17), ("Sheratan", 1.9107, 20.808, 2.64, 0.13), ("Phact", 5.6608, -34.074, 2.64, -0.12),
    ("Kraz", 12.5731, -23.397, 2.65, 0.89), ("Muphrid", 13.9114, 18.398, 2.68, 0.58), ("Mizar", 13.3988, 54.925, 2.04, 0.02),
    ("Ruchbah", 1.4303, 60.235, 2.68, 0.13), ("Segin", 1.9066, 63.670, 3.37, -0.15), ("Alderamin", 21.3096, 62.586, 2.45, 0.22),
    ("Rasalgethi", 17.2441, 14.390, 3.10, 1.44), ("Kornephoros", 16.5037, 21.490, 2.77, 0.94), ("Albireo", 19.5120, 27.960, 3.05, 1.13),
    ("Tarazed", 19.7710, 10.613, 2.72, 1.52), ("Vindemiatrix", 13.0363, 10.959, 2.83, 0.94), ("Porrima", 12.6943, -1.449, 2.74, 0.36),
    ("Menkar", 3.0380, 4.090, 2.54, 1.64), ("Alcyone", 3.7914, 24.105, 2.87, -0.09), ("Atlas", 3.8194, 24.053, 3.62, -0.08),
    ("Electra", 3.7479, 24.113, 3.70, -0.11), ("Maia", 3.7636, 24.368, 3.87, -0.07), ("Merope", 3.7721, 23.948, 4.18, -0.06),
    ("Taygeta", 3.7535, 24.467, 4.30, -0.11), ("Hatysa", 5.5903, -5.910, 2.77, -0.24), ("Meissa", 5.5856, 9.934, 3.33, -0.16),
    ("Imai", 12.2524, -58.749, 2.79, -0.23), ("Ginan", 12.3561, -60.401, 3.59, 1.42), ("Alniyat", 16.3531, -25.593, 2.89, 0.13),
    ("Paikauhale", 16.5980, -28.216, 2.82, -0.25), ("Xamidimura", 16.8645, -38.048, 3.00, -0.20), ("Lesath", 17.5127, -37.296, 2.69, -0.22),
    ("Fang", 15.9809, -26.114, 2.89, -0.19), ("Kaus Media", 18.3499, -29.828, 2.70, 1.38), ("Kaus Borealis", 18.4662, -25.422, 2.81, 1.02),
    ("Phi Sgr", 18.7609, -26.991, 3.17, -0.11), ("Tau Sgr", 19.1157, -27.671, 3.32, 1.19), ("Alnasl", 18.0968, -30.424, 2.99, 1.00),
    ("Megrez", 12.2571, 57.033, 3.31, 0.08), ("Pherkad", 15.3455, 71.834, 3.05, 0.05), ("Sheliak", 18.8347, 33.363, 3.52, 0.00),
    ("Sulafat", 18.9824, 32.690, 3.25, -0.05), ("Fawaris", 19.7496, 45.131, 2.87, -0.03), ("Alshain", 19.9219, 6.407, 3.71, 0.86),
    ("Tianguan", 5.6274, 21.143, 3.00, -0.19), ("Algorab", 12.4977, -16.515, 2.94, -0.05), ("Epsilon Cen", 13.6648, -53.466, 2.30, -0.22),
    ("Eta Cen", 14.5918, -42.158, 2.31, -0.19), ("Zeta Cen", 13.9257, -47.288, 2.55, -0.22), ("Delta Cen", 12.1394, -50.722, 2.52, -0.12),
    ("Alpha Lup", 14.6988, -47.388, 2.30, -0.20), ("Beta Ara", 17.4217, -55.530, 2.85, 1.46), ("Alpha Ara", 17.5307, -49.876, 2.95, -0.17),
    ("Theta Car", 10.7159, -64.394, 2.76, -0.22), ("Upsilon Car", 9.7849, -65.072, 2.97, 0.27), ("Pi Pup", 7.2857, -37.097, 2.70, 1.62),
    ("Tureis", 8.1258, -24.304, 2.81, 0.43), ("Mu Vel", 10.7795, -49.420, 2.69, 0.90), ("Beta TrA", 15.9191, -63.430, 2.85, 0.29),
    ("Gamma TrA", 15.3152, -68.680, 2.89, -0.01), ("Beta Hyi", 0.4290, -77.254, 2.80, 0.62), ("Alpha Hyi", 1.9796, -61.570, 2.86, 0.28),
    ("Cursa", 5.1309, -5.086, 2.78, 0.13), ("Zaurak", 3.9672, -13.509, 2.95, 1.59), ("Algenib", 0.2206, 15.184, 2.83, -0.23),
    ("Sadalsuud", 21.5260, -5.571, 2.87, 0.83), ("Sadalmelik", 22.0964, -0.320, 2.94, 0.97), ("Deneb Algedi", 21.7840, -16.127, 2.81, 0.29),
    ("Zeta Oph", 16.6193, -10.567, 2.54, 0.02), ("Yed Prior", 16.2391, -3.694, 2.73, 1.58), ("Zeta Her", 16.6881, 31.603, 2.81, 0.65),
    ("Rastaban", 17.5072, 52.301, 2.79, 0.98), ("Thuban", 14.0731, 64.376, 3.65, -0.05), ("Aldhibah", 16.3999, 61.514, 2.73, 0.91),
    ("Cor Caroli", 12.9338, 38.318, 2.89, -0.12), ("Nihal", 5.4709, -20.759, 2.84, 0.82), ("Zubenelgenubi", 14.8480, -16.042, 2.75, 0.15),
    ("Alpha Mus", 12.6198, -69.136, 2.69, -0.20), ("Alpha Tuc", 22.3083, -60.260, 2.87, 1.39), ("Alpha Ind", 20.6261, -47.291, 3.11, 1.00),
    ("Polaris Australis", 21.1465, -88.956, 5.42, 0.41),
]


def stars_text(seed=17, n_fill=3600):
    rng = np.random.default_rng(seed)
    lines = ["# EOSAT-1 star field: ra_deg dec_deg vmag b_minus_v [name]",
             "# Named stars: real J2000 positions (to ~mag 3). Unnamed: random filler, denser along the Milky Way."]
    for name, ra_h, dec, v, bv in BRIGHT_STARS:
        lines.append("%.4f %.4f %.2f %.2f %s" % (ra_h * 15.0, dec, v, bv, name.replace(" ", "_")))
    # galactic pole and centre in J2000
    ngp = np.array([math.cos(math.radians(27.128)) * math.cos(math.radians(192.859)),
                    math.cos(math.radians(27.128)) * math.sin(math.radians(192.859)), math.sin(math.radians(27.128))])
    k = 0
    while k < n_fill:
        v = rng.normal(size=3)
        v /= np.linalg.norm(v)
        b = math.degrees(math.asin(np.clip(v @ ngp, -1, 1)))
        if rng.random() > 0.35 + 0.65 * math.exp(-abs(b) / 18.0):
            continue
        mag = 6.3 - 2.4 * rng.random() ** 2.2
        bv = float(np.clip(rng.normal(0.55, 0.45), -0.3, 1.9))
        ra = math.degrees(math.atan2(v[1], v[0])) % 360
        dec = math.degrees(math.asin(v[2]))
        lines.append("%.4f %.4f %.2f %.2f" % (ra, dec, mag, bv))
        k += 1
    return "\n".join(lines) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="../Assets/EOSAT1/Textures")
    ap.add_argument("--data", default="../Assets/EOSAT1/Data")
    ap.add_argument("--preview", default=None, help="also write preview images to this folder")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    os.makedirs(a.data, exist_ok=True)
    day, land = earth_day()
    day.save(os.path.join(a.out, "earth_day.png"), optimize=True)
    earth_night(land).save(os.path.join(a.out, "earth_night.png"), optimize=True)
    moon().save(os.path.join(a.out, "moon.png"), optimize=True)
    with open(os.path.join(a.data, "bright_stars.txt"), "w", newline="\n") as fh:
        fh.write(stars_text())
    print("textures written to", os.path.abspath(a.out))


if __name__ == "__main__":
    main()
