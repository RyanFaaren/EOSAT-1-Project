#!/usr/bin/env python3
"""
eosat1_model.py - the EOS SAT-1 exterior as a parts list, written to
Assets/EOSAT1/Data/eosat1_model.json. Unity's scene builder builds the spacecraft from that
file, preview_model.py renders it for checking against the reference pictures, and the
CAD step can reuse it.

    python3 eosat1_model.py            # writes the JSON and prints a summary

Where the numbers come from
  * Envelope 1390 x 1130.3 x 821 mm, 178 kg (Wikipedia / eoPortal / EOS Data Analytics).
  * Cross-section: a diamond (rhombus). In the reference render the camera face is a
    parallelogram with equal sides whose long diagonal joins the two white blocks, and both
    lenses sit on that diagonal (fitted through the render's perspective). Long diagonal
    1390 mm, short diagonal 1130.3 mm, 821 mm along the camera axis.
  * Two DragonEye imagers, 320 mm x 930 mm each (satcatalog / eoPortal); in the render
    their lips are 320 mm across, 370 mm apart, on the long diagonal.
  * White blocks at the two tips, front and back: fine sun sensors (exploded view label).
  * Cups, magnetometer booms: positions measured on the render's camera face.
  * Two solar panels stowed on the two rear faces. How they deploy is not public: here
    they hinge at the ridge between them and fold out 39.1 deg each until they form one
    flat array facing -X. Change JOINTS below to try something else.
  * Separation ring and star cameras: from the exploded view (rear face).
  * The deployable UHF antenna is NOT on the real satellite (as far as public sources
    show); the course simulator has one, so it sits on the rear (zenith) face.

Body frame (course convention): +X flight direction, +Y solar-wing axis, +Z camera
boresight (points at Earth in nadir pointing). Millimetres.
"""
import json
import math
import os

A = 695.0           # half the long diagonal (Y): the tips
B = 565.15          # half the short diagonal (X)
HZ = 410.5          # half the depth along the camera axis
SIDE = math.hypot(A, B)
TIP_HALF = math.degrees(math.atan2(B, A))           # 39.11 deg
FOLD = TIP_HALF                                      # panel fold-out angle that makes them coplanar

V1 = (0.0, -A)      # -Y tip  (panel A / wing -Y side)
V2 = (-B, 0.0)      # rear ridge between the stowed panels
V3 = (0.0, A)       # +Y tip
V4 = (B, 0.0)       # front corner

PANEL_W, PANEL_H, PANEL_T, PANEL_GAP, PANEL_START = 830.0, 790.0, 25.0, 4.0, 12.0


def v3(x, y, z):
    return [round(x, 3), round(y, 3), round(z, 3)]


def unit(v):
    n = math.sqrt(sum(c * c for c in v))
    return [c / n for c in v]


def lathe(name, origin, axis, profile, mats, segments=48, joint=None):
    p = {"name": name, "type": "lathe", "origin": origin, "axis": unit(axis), "profile": profile,
         "materials": mats, "segments": segments}
    if joint:
        p["joint"] = joint
    return p


def box(name, center, size, material, x_axis=(1, 0, 0), z_axis=(0, 0, 1), joint=None):
    p = {"name": name, "type": "box", "center": center, "size": size, "material": material,
         "x_axis": unit(x_axis), "z_axis": unit(z_axis)}
    if joint:
        p["joint"] = joint
    return p


def cyl(name, start, end, radius, material, joint=None):
    p = {"name": name, "type": "cylinder", "start": start, "end": end, "radius": radius, "material": material}
    if joint:
        p["joint"] = joint
    return p


def sphere(name, center, radius, material):
    return {"name": name, "type": "sphere", "center": center, "radius": radius, "material": material}


def build():
    parts, joints = [], []

    # ------------------------------------------------------------------ body
    parts.append({"name": "Bus", "type": "prism", "polygon": [list(V4), list(V3), list(V2), list(V1)],
                  "z": [-HZ, HZ], "materials": {"sides": "mli_gold", "front": "mli_gold", "back": "mli_gold"},
                  "uv_mm": 420.0})

    # ------------------------------------------------------------------ camera face (+Z): two DragonEye imagers
    zf = HZ
    hood = [[196, 0], [196, 8], [182, 8], [182, 105], [168, 105], [161, 103], [160, 101], [160, 12], [0, 12]]
    hood_m = ["bronze", "bronze", "bronze", "hood_rim", "lip", "lip", "optics", "optics"]
    for tag, y in (("-Y", -205.0), ("+Y", 165.0)):
        x = -70.0
        parts.append(lathe("DragonEye " + tag + " hood", v3(x, y, zf), [0, 0, 1], hood, hood_m, 64))
        # secondary mirror on three thin vanes, set back inside the hood
        parts.append(lathe("DragonEye " + tag + " secondary", v3(x, y, zf + 48), [0, 0, 1],
                           [[56, 0], [56, 8], [0, 8]], ["secondary", "secondary"], 40))
        for k in range(3):
            a = math.radians(90 + k * 120)
            mid = 108.0
            parts.append(box("DragonEye " + tag + " vane", v3(x + mid * math.cos(a), y + mid * math.sin(a), zf + 52),
                             [104.0, 3.0, 6.0], "optics", x_axis=(math.cos(a), math.sin(a), 0)))
    # two small gold cups below the imagers (antennas or baffles; not labelled in the sources)
    cup = [[34, 0], [34, 6], [56, 64], [50, 64], [30, 16], [0, 16]]
    cup_m = ["bronze", "bronze", "hood_rim", "optics", "optics"]
    for tag, (x, y) in (("-Y", (250.0, -195.0)), ("+Y", (250.0, 45.0))):
        parts.append(lathe("Nadir cup " + tag, v3(x, y, zf), [0, 0, 1], cup, cup_m, 40))
    # magnetometer booms with white heads
    for tag, (x, y) in (("1", (280.0, 259.0)), ("2", (165.0, 436.0))):
        parts.append(lathe("Magnetometer " + tag + " collar", v3(x, y, zf), [0, 0, 1],
                           [[20, 0], [20, 14], [9, 14], [0, 14]], ["dark", "dark", "dark"], 24))
        parts.append(cyl("Magnetometer " + tag + " boom", v3(x, y, zf + 14), v3(x, y, zf + 128), 7.5, "dark"))
        parts.append(lathe("Magnetometer " + tag + " head", v3(x, y, zf + 126), [0, 0, 1],
                           [[9, 0], [21, 6], [23, 18], [21, 30], [12, 38], [0, 40]],
                           ["white", "white", "white", "white", "white"], 24))

    # ------------------------------------------------------------------ fine sun sensors at the tips (white blocks)
    for tag, sy in (("-Y", -1.0), ("+Y", 1.0)):
        for end, sz in (("front", 1.0), ("rear", -1.0)):
            c = v3(0.0, sy * (A + 12.0), sz * (HZ - 34.0))
            parts.append(box("Fine sun sensor " + tag + " " + end, c, [84.0, 62.0, 72.0], "white"))
            parts.append(cyl("Fine sun sensor " + tag + " " + end + " aperture", v3(0.0, sy * (A + 43.0), sz * (HZ - 34.0)),
                             v3(0.0, sy * (A + 45.0), sz * (HZ - 34.0)), 10.0, "optics"))

    # ------------------------------------------------------------------ rear face (-Z): separation ring, star cameras
    zb = -HZ
    parts.append(lathe("Separation ring", v3(0, 0, zb), [0, 0, -1],
                       [[232, 0], [232, 6], [215, 6], [215, 44], [204, 48], [178, 48], [178, 10], [0, 10]],
                       ["ring", "ring", "ring", "ring", "ring", "ring", "dark", "dark"], 72))
    for tag, sy in (("-Y", -1.0), ("+Y", 1.0)):
        c = (120.0, sy * 300.0, zb)                     # front half of the rear face: hidden in the reference render
        bore = unit((0.22, sy * 0.36, -0.90))
        side = unit((bore[1], -bore[0], 0.0))
        parts.append(box("Star camera " + tag, v3(c[0], c[1], c[2] - 30.0), [96.0, 96.0, 62.0], "dark",
                         x_axis=side, z_axis=bore))
        parts.append(lathe("Star camera " + tag + " baffle", v3(c[0] + bore[0] * 30, c[1] + bore[1] * 30, c[2] - 30 + bore[2] * 30),
                           bore, [[30, 0], [30, 8], [48, 92], [42, 92], [24, 10], [0, 10]],
                           ["dark", "dark", "dark", "optics", "optics"], 32))

    # ------------------------------------------------------------------ the course's deployable UHF antenna (not on the real satellite)
    ax, az = 330.0, zb
    parts.append(box("UHF antenna module", v3(ax, 0, az - 8), [100.0, 84.0, 16.0], "pcb"))
    joints.append({"name": "antenna_my", "role": "antenna_jam", "pivot": v3(ax, -30.0, az - 19.0), "axis": [1, 0, 0], "angle": 70.0})
    joints.append({"name": "antenna_py", "role": "antenna", "pivot": v3(ax, 30.0, az - 19.0), "axis": [1, 0, 0], "angle": -70.0})
    parts.append(box("UHF element -Y", v3(ax, -30.0 - 130.0, az - 19.0), [8.0, 260.0, 1.2], "tape", joint="antenna_my"))
    parts.append(box("UHF element +Y", v3(ax, 30.0 + 130.0, az - 19.0), [8.0, 260.0, 1.2], "tape", joint="antenna_py"))

    # ------------------------------------------------------------------ solar panels on the two rear faces
    for tag, (va, vb, mat, jname, sign) in (("-Y", (V2, V1, "panel_a", "wing_my", -1.0)),
                                             ("+Y", (V2, V3, "panel_b", "wing_py", 1.0))):
        d = unit((vb[0] - va[0], vb[1] - va[1], 0.0))            # from the ridge towards the tip
        # outward normal of this face (away from the centre)
        nrm = unit((d[1], -d[0], 0.0)) if sign < 0 else unit((-d[1], d[0], 0.0))
        mid = va[0] + d[0] * (PANEL_START + PANEL_W / 2), va[1] + d[1] * (PANEL_START + PANEL_W / 2)
        off = PANEL_GAP + PANEL_T / 2
        centre = v3(mid[0] + nrm[0] * off, mid[1] + nrm[1] * off, 0.0)
        pivot = v3(va[0] + d[0] * PANEL_START + nrm[0] * (PANEL_GAP + PANEL_T),
                   va[1] + d[1] * PANEL_START + nrm[1] * (PANEL_GAP + PANEL_T), 0.0)
        joints.append({"name": jname, "role": jname, "pivot": pivot, "axis": [0, 0, 1], "angle": sign * FOLD})
        parts.append({"name": "Solar panel " + tag, "type": "panel", "center": centre, "normal": nrm, "up": [0, 0, 1],
                      "width": PANEL_W, "height": PANEL_H, "thickness": PANEL_T,
                      "materials": {"front": mat, "back": "panel_back", "edge": "bronze"}, "joint": jname})
        # hinge knuckles at the ridge end, top and bottom
        for zz in (-300.0, 300.0):
            parts.append(cyl("Panel hinge " + tag, v3(pivot[0], pivot[1], zz - 30), v3(pivot[0], pivot[1], zz + 30), 9.0, "dark"))

    materials = {
        "mli_gold":   {"color": [0.86, 0.72, 0.38], "metallic": 0.55, "smoothness": 0.62,
                       "texture": "mli_gold.png", "normal": "mli_gold_normal.png", "normal_scale": 0.9},
        "panel_a":    {"color": [1, 1, 1], "metallic": 1.0, "smoothness": 1.0, "texture": "panel_a.png", "mask": "panel_a_mask.png"},
        "panel_b":    {"color": [1, 1, 1], "metallic": 1.0, "smoothness": 1.0, "texture": "panel_b.png", "mask": "panel_b_mask.png"},
        "panel_back": {"color": [0.16, 0.16, 0.17], "metallic": 0.1, "smoothness": 0.35},
        "bronze":     {"color": [0.72, 0.55, 0.33], "metallic": 0.75, "smoothness": 0.72},
        "hood_rim":   {"color": [0.05, 0.05, 0.05], "metallic": 0.3, "smoothness": 0.5},
        "lip":        {"color": [0.30, 0.40, 0.38], "metallic": 0.2, "smoothness": 0.6},
        "optics":     {"color": [0.006, 0.006, 0.008], "metallic": 0.0, "smoothness": 0.85},
        "secondary":  {"color": [0.30, 0.36, 0.34], "metallic": 0.3, "smoothness": 0.8},
        "white":      {"color": [0.90, 0.89, 0.85], "metallic": 0.0, "smoothness": 0.35},
        "dark":       {"color": [0.07, 0.07, 0.08], "metallic": 0.3, "smoothness": 0.45},
        "ring":       {"color": [0.80, 0.64, 0.36], "metallic": 0.85, "smoothness": 0.78},
        "pcb":        {"color": [0.06, 0.16, 0.10], "metallic": 0.1, "smoothness": 0.5},
        "tape":       {"color": [0.74, 0.66, 0.48], "metallic": 0.6, "smoothness": 0.6},
    }
    markers = {
        "jam_halo": {"center": v3(ax, -60.0, az - 120.0), "radius": 180.0},
        "jam_label": v3(ax, 0.0, az - 420.0),
        "boresight_origin": v3(-70.0, -20.0, HZ + 105.0),
        "boresight_length": 3000.0,
        "axes_length": 1150.0,
        "deployed_cell_normal": [-1.0, 0.0, 0.0],
    }
    return {
        "name": "EOS SAT-1",
        "units": "mm",
        "frame": "body: +X flight, +Y solar-wing axis, +Z camera boresight",
        "envelope_mm": [2 * B, 2 * A, 2 * HZ],
        "stowed_panel_normals": {"wing_my": None, "wing_py": None},
        "sources": "see Tools/eosat1_model.py",
        "materials": materials,
        "joints": joints,
        "parts": parts,
        "markers": markers,
    }


def main():
    m = build()
    for j in m["joints"]:
        if j["role"] in ("wing_my", "wing_py"):
            part = [p for p in m["parts"] if p.get("joint") == j["name"] and p["type"] == "panel"][0]
            m["stowed_panel_normals"][j["role"]] = [round(c, 6) for c in part["normal"]]
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "EOSAT1", "Data", "eosat1_model.json")
    with open(out, "w", newline="\n") as fh:
        json.dump(m, fh, indent=1)
    print("wrote %s: %d parts, %d joints" % (os.path.abspath(out), len(m["parts"]), len(m["joints"])))
    print("side %.1f mm, tip angle %.2f deg, ridge angle %.2f deg, panel fold-out %.2f deg" % (
        SIDE, 2 * TIP_HALF, 180 - 2 * TIP_HALF, FOLD))
    for j in m["joints"]:
        print("  joint %-11s pivot %s axis %s angle %+.2f" % (j["name"], j["pivot"], j["axis"], j["angle"]))


if __name__ == "__main__":
    main()
