#!/usr/bin/env python3
"""
make_one_orbit.py - write one orbit of EOSAT-1 state frames as JSON Lines.

Unofficial stand-in for the course's sample file, built from first principles so
the Unity project can be tested before (or alongside) the real simulator.

    python3 make_one_orbit.py                      # writes eosat1_one_orbit.jsonl
    python3 make_one_orbit.py -o frames.jsonl --step 1 --duration 5670
    python3 make_one_orbit.py --check-only         # just run the physics checks

Each line is one JSON object, in the frame format from the course slides:
    {"type":"state","seq":1812,"sim_time":"2026-08-20T12:30:12Z",
     "orbit":{"pos_eci_km":[...]},"attitude":{"q_body_to_eci":[x,y,z,w]},
     "sun":{"dir_eci":[...],"in_eclipse":false,"sunlit_fraction":1.0},
     "deploy":{"wing_my":{"state":"DEPLOYING","fraction":0.43},
               "wing_py":{...},"antenna":{"state":"DEPLOYED"}}}
"sunlit_fraction" is an extra field; clients ignore fields they don't know.
"""
import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from eosat_physics import (DEG, SCENARIO_ALT_KM, SCENARIO_ARGLAT_DEG, SCENARIO_EPOCH, SCENARIO_INC_DEG,
                           SCENARIO_RAAN_DEG, CircularOrbit, Scenario, angle, iso_utc, neg, qrot,
                           secs_j2000, sub, sun_j2000, sunlit_fraction, unit, norm, q_nadir, qangle)


def build(step=1.0, duration=5670.0):
    epoch = secs_j2000(SCENARIO_EPOCH)
    orbit = CircularOrbit(SCENARIO_ALT_KM, SCENARIO_INC_DEG, SCENARIO_RAAN_DEG, SCENARIO_ARGLAT_DEG, epoch)
    sc = Scenario(orbit)
    lines = [json.dumps({"type": "hello", "spacecraft": "EOSAT-1", "source": "make_one_orbit.py",
                         "note": "unofficial reference data, slide-23 timeline", "frame": "J2000",
                         "rate_hz": 1.0 / step}, separators=(",", ":"))]
    frames = []
    n = int(round(duration / step))
    for k in range(n + 1):
        t = k * step
        s = epoch + t
        r, v = orbit.state(s)
        q = sc.attitude(t)
        rs = sun_j2000(s)
        frac = sunlit_fraction(r, rs)
        sdir = unit(sub(rs, r))
        frame = {
            "type": "state",
            "seq": k,
            "sim_time": iso_utc(s),
            "orbit": {"pos_eci_km": [round(c, 3) for c in r]},
            "attitude": {"q_body_to_eci": [round(c, 7) for c in q]},
            "sun": {"dir_eci": [round(c, 6) for c in sdir], "in_eclipse": frac < 0.5,
                    "sunlit_fraction": round(frac, 4)},
            "deploy": sc.deploy(t),
        }
        frames.append((t, r, v, q, sdir, frac, frame))
        lines.append(json.dumps(frame, separators=(",", ":")))
    return lines, frames, orbit, sc


def check(frames, orbit):
    ok = True

    def expect(cond, msg):
        nonlocal ok
        print(("  ok   " if cond else "  FAIL ") + msg)
        ok = ok and cond

    print("Physics checks")
    expect(abs(orbit.period - 5668.1) < 1.0, "orbital period %.1f s (slide: ~95 min / 5670 s)" % orbit.period)
    # eclipse window
    ecl = [t for (t, r, v, q, sd, fr, f) in frames if f["sun"]["in_eclipse"]]
    expect(abs(ecl[0] - 1980) <= 2 and abs(ecl[-1] - 3998) <= 3,
           "eclipse %d-%d s (slide: 1980-4000 s)" % (ecl[0], ecl[-1]))
    by_t = {int(t): (r, v, q, sd, f) for (t, r, v, q, sd, fr, f) in frames}

    def body_axis(q, axis):
        return qrot(q, axis)
    # nadir pointing after the slew: camera (+Z) at Earth, +X along flight
    worst = 0.0
    for t in range(1200, 4300, 50):
        r, v, q, sd, f = by_t[t]
        worst = max(worst, math.degrees(angle(body_axis(q, (0, 0, 1)), neg(r))))
    expect(worst < 0.01, "camera face points at Earth 1200-4300 s (worst %.4f deg)" % worst)
    r, v, q, sd, f = by_t[2500]
    expect(math.degrees(angle(body_axis(q, (1, 0, 0)), v)) < 0.01, "+X body along the velocity")
    # roll for imaging
    r, v, q, sd, f = by_t[4500]
    off = math.degrees(angle(body_axis(q, (0, 0, 1)), neg(r)))
    expect(abs(off - 25.0) < 0.05, "25 deg roll at 4500 s (camera %.2f deg off nadir)" % off)
    r, v, q, sd, f = by_t[5000]
    expect(math.degrees(angle(body_axis(q, (0, 0, 1)), neg(r))) < 0.01, "back to nadir at 5000 s")
    # tumble slows down (B-dot style) before the slew
    def rate(t):
        return math.degrees(qangle(by_t[t][2], by_t[t + 1][2]))
    expect(rate(0) > 5.0 and rate(880) < 0.5, "tumbling %.2f deg/s at 0 s, %.2f deg/s at 880 s" % (rate(0), rate(880)))
    # deployments
    expect(by_t[599][4]["deploy"]["antenna"]["state"] == "STOWED" and
           by_t[600][4]["deploy"]["antenna"]["state"] == "DEPLOYED", "antenna out at 600 s")
    w = by_t[1315][4]["deploy"]["wing_my"]
    expect(w["state"] == "DEPLOYING" and abs(w["fraction"] - 0.5) < 1e-6, "wing -Y half way at 1315 s")
    expect(by_t[1430][4]["deploy"]["wing_py"]["state"] == "DEPLOYED", "both wings out by 1430 s")
    # quaternions are unit length
    worstn = max(abs(math.sqrt(sum(c * c for c in fr[3])) - 1) for fr in frames)
    expect(worstn < 1e-9, "unit quaternions")
    return ok


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("-o", "--output", default="eosat1_one_orbit.jsonl")
    ap.add_argument("--step", type=float, default=1.0, help="seconds between frames (default 1)")
    ap.add_argument("--duration", type=float, default=5670.0, help="seconds (default 5670, one orbit)")
    ap.add_argument("--check-only", action="store_true")
    args = ap.parse_args()
    lines, frames, orbit, sc = build(1.0, 5670.0) if args.check_only else build(args.step, args.duration)
    ok = check(frames, orbit) if args.check_only or args.step == 1.0 else True
    if not args.check_only:
        with open(args.output, "w", newline="\n") as fh:
            fh.write("\n".join(lines) + "\n")
        print("wrote %d frames to %s" % (len(lines) - 1, args.output))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
