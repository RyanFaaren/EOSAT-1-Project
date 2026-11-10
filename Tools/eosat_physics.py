"""
eosat_physics.py - reference physics for the EOSAT-1 VR project (unofficial).

Everything here is mirrored, formula for formula, by the C# core in
Assets/EOSAT1/Scripts/Core (Astro.cs, Orbit.cs, SandboxSim.cs), so the recorded
orbit that ships with the Unity project and the live sandbox inside Unity agree.

Conventions (same as the course slides):
  * ECI = J2000 / EME2000, right-handed, Z through the North Pole, km.
  * Body frame: +Z camera face (points at Earth when nadir pointing),
                +X direction of flight, +Y solar-wing axis.
  * Quaternions are [x, y, z, w] (w last) and rotate body -> ECI.
"""
import math
from datetime import datetime, timezone

MU = 398600.4418            # km^3/s^2
R_EARTH = 6371.0            # km, mean radius (rendering + shadow)
R_EARTH_EQ = 6378.137       # km, equatorial (Moon parallax, J2)
J2 = 1.08262668e-3
R_SUN = 695700.0            # km
R_MOON = 1737.4             # km
AU = 149597870.7            # km
TT_MINUS_UTC = 69.184       # s (TAI-UTC 37 s + 32.184 s)
DEG = math.pi / 180.0
J2000 = datetime(2000, 1, 1, 12, 0, 0, tzinfo=timezone.utc)


# ---------------------------------------------------------------- vectors
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def neg(a): return (-a[0], -a[1], -a[2])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def norm(a): return math.sqrt(dot(a, a))


def unit(a):
    n = norm(a)
    return (a[0] / n, a[1] / n, a[2] / n) if n > 0 else (0.0, 0.0, 0.0)


def angle(a, b):
    return math.atan2(norm(cross(a, b)), dot(a, b))


# ------------------------------------------------------------ quaternions
def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qconj(q): return (-q[0], -q[1], -q[2], q[3])


def qnormalize(q):
    n = math.sqrt(sum(c * c for c in q))
    return tuple(c / n for c in q) if n > 1e-300 else (0.0, 0.0, 0.0, 1.0)


def qrot(q, v):
    qv = (q[0], q[1], q[2])
    t = mul(cross(qv, v), 2.0)
    return add(add(v, mul(t, q[3])), cross(qv, t))


def qaxis(axis, ang):
    n = unit(axis)
    s = math.sin(ang / 2)
    return (n[0] * s, n[1] * s, n[2] * s, math.cos(ang / 2))


def qdot(a, b): return sum(x * y for x, y in zip(a, b))


def qslerp(a, b, t):
    d = qdot(a, b)
    if d < 0:
        b = tuple(-c for c in b)
        d = -d
    if d > 0.9995:
        return qnormalize(tuple(x + (y - x) * t for x, y in zip(a, b)))
    th0 = math.acos(d)
    s0 = math.sin(th0 - th0 * t) / math.sin(th0)
    s1 = math.sin(th0 * t) / math.sin(th0)
    return qnormalize(tuple(x * s0 + y * s1 for x, y in zip(a, b)))


def qangle(a, b):
    d = min(1.0, abs(qdot(qnormalize(a), qnormalize(b))))
    return 2 * math.acos(d)


def qfromcols(c0, c1, c2):
    """Rotation whose matrix columns are c0, c1, c2 (maps local axes onto them)."""
    m00, m01, m02 = c0[0], c1[0], c2[0]
    m10, m11, m12 = c0[1], c1[1], c2[1]
    m20, m21, m22 = c0[2], c1[2], c2[2]
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2
        q = ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s)
    elif m00 > m11 and m00 > m22:
        s = math.sqrt(1.0 + m00 - m11 - m22) * 2
        q = (0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s)
    elif m11 > m22:
        s = math.sqrt(1.0 + m11 - m00 - m22) * 2
        q = ((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s)
    else:
        s = math.sqrt(1.0 + m22 - m00 - m11) * 2
        q = ((m02 + m20) / s, (m12 + m21) / s, 0.25 * s, (m10 - m01) / s)
    return qnormalize(q)


def smoothstep(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


# ------------------------------------------------------------------- time
def secs_j2000(dt):
    return (dt - J2000).total_seconds()


def iso_utc(s):
    from datetime import timedelta
    t = J2000 + timedelta(seconds=s)
    return t.strftime("%Y-%m-%dT%H:%M:%S") + "Z"


def gmst_rad(s):
    """Greenwich mean sidereal time (IAU-82), UT1 ~= UTC."""
    tu = s / 86400.0 / 36525.0
    sec = 67310.54841 + (876600.0 * 3600.0 + 8640184.812866) * tu + 0.093104 * tu * tu - 6.2e-6 * tu ** 3
    sec = math.fmod(sec, 86400.0)
    if sec < 0:
        sec += 86400.0
    return sec / 86400.0 * 2 * math.pi


def centuries_tt(s):
    return (s + TT_MINUS_UTC) / (36525.0 * 86400.0)


def q_prec_j2000_to_mod(s):
    """IAU-76 precession: r_MOD = q r_J2000 q*."""
    t = centuries_tt(s)
    as2r = math.pi / (180.0 * 3600.0)
    zeta = (2306.2181 * t + 0.30188 * t * t + 0.017998 * t ** 3) * as2r
    z = (2306.2181 * t + 1.09468 * t * t + 0.018203 * t ** 3) * as2r
    theta = (2004.3109 * t - 0.42665 * t * t - 0.041833 * t ** 3) * as2r
    return qmul(qmul(qaxis((0, 0, 1), z), qaxis((0, 1, 0), -theta)), qaxis((0, 0, 1), zeta))


def q_ecef_to_j2000(s):
    return qmul(qconj(q_prec_j2000_to_mod(s)), qaxis((0, 0, 1), gmst_rad(s)))


# -------------------------------------------------------------- ephemeris
def sun_mod(s):
    n = (s + TT_MINUS_UTC) / 86400.0
    L = 280.460 + 0.9856474 * n
    g = (357.528 + 0.9856003 * n) * DEG
    lam = (L + 1.915 * math.sin(g) + 0.020 * math.sin(2 * g)) * DEG
    eps = (23.439 - 0.0000004 * n) * DEG
    r = (1.00014 - 0.01671 * math.cos(g) - 0.00014 * math.cos(2 * g)) * AU
    return (r * math.cos(lam), r * math.cos(eps) * math.sin(lam), r * math.sin(eps) * math.sin(lam))


def sun_j2000(s):
    return qrot(qconj(q_prec_j2000_to_mod(s)), sun_mod(s))


def moon_mod(s):
    T = centuries_tt(s)

    def sd(x): return math.sin(x * DEG)

    def cd(x): return math.cos(x * DEG)
    lam = (218.32 + 481267.8813 * T + 6.29 * sd(134.9 + 477198.85 * T) - 1.27 * sd(259.2 - 413335.38 * T)
           + 0.66 * sd(235.7 + 890534.23 * T) + 0.21 * sd(269.9 + 954397.70 * T)
           - 0.19 * sd(357.5 + 35999.05 * T) - 0.11 * sd(186.6 + 966404.05 * T))
    beta = (5.13 * sd(93.3 + 483202.03 * T) + 0.28 * sd(228.2 + 960400.87 * T)
            - 0.28 * sd(318.3 + 6003.18 * T) - 0.17 * sd(217.6 - 407332.20 * T))
    par = (0.9508 + 0.0518 * cd(134.9 + 477198.85 * T) + 0.0095 * cd(259.2 - 413335.38 * T)
           + 0.0078 * cd(235.7 + 890534.23 * T) + 0.0028 * cd(269.9 + 954397.70 * T))
    eps = (23.439291 - 0.0130042 * T) * DEG
    r = R_EARTH_EQ / math.sin(par * DEG)
    l, b = lam * DEG, beta * DEG
    return (r * math.cos(b) * math.cos(l),
            r * (math.cos(eps) * math.cos(b) * math.sin(l) - math.sin(eps) * math.sin(b)),
            r * (math.sin(eps) * math.cos(b) * math.sin(l) + math.cos(eps) * math.sin(b)))


def moon_j2000(s):
    return qrot(qconj(q_prec_j2000_to_mod(s)), moon_mod(s))


def sunlit_fraction(r_sat, r_sun):
    """Fraction of the solar disc visible from r_sat (Montenbruck & Gill 3.4.2)."""
    to_sun = sub(r_sun, r_sat)
    d_sun, d_earth = norm(to_sun), norm(r_sat)
    if d_earth <= R_EARTH:
        return 0.0
    a = math.asin(min(1.0, R_SUN / d_sun))
    b = math.asin(min(1.0, R_EARTH / d_earth))
    c = angle(neg(r_sat), to_sun)
    if c >= a + b:
        return 1.0
    if c <= b - a:
        return 0.0
    if c <= a - b:
        return 1.0 - (b * b) / (a * a)
    x = (c * c + a * a - b * b) / (2 * c)
    y = math.sqrt(max(0.0, a * a - x * x))
    area = (a * a * math.acos(max(-1.0, min(1.0, x / a)))
            + b * b * math.acos(max(-1.0, min(1.0, (c - x) / b))) - c * y)
    return 1.0 - area / (math.pi * a * a)


# ------------------------------------------------------------------ orbit
class CircularOrbit:
    def __init__(self, altitude_km, inc_deg, raan_deg, arglat_deg, epoch_s, j2=True):
        self.a = R_EARTH + altitude_km
        self.inc = inc_deg * DEG
        self.raan0 = raan_deg * DEG
        self.u0 = arglat_deg * DEG
        self.epoch = epoch_s
        self.n = math.sqrt(MU / self.a ** 3)
        self.raan_rate = (-1.5 * self.n * J2 * (R_EARTH_EQ / self.a) ** 2 * math.cos(self.inc)) if j2 else 0.0

    @property
    def period(self):
        return 2 * math.pi / self.n

    def state(self, s):
        dt = s - self.epoch
        u = self.u0 + self.n * dt
        O = self.raan0 + self.raan_rate * dt
        cu, su, cO, sO = math.cos(u), math.sin(u), math.cos(O), math.sin(O)
        ci, si = math.cos(self.inc), math.sin(self.inc)
        a, v = self.a, self.a * self.n
        r = (a * (cO * cu - sO * su * ci), a * (sO * cu + cO * su * ci), a * su * si)
        vel = (v * (-cO * su - sO * cu * ci), v * (-sO * su + cO * cu * ci), v * cu * si)
        return r, vel


def q_nadir(r, v):
    z = unit(neg(r))
    x = unit(sub(v, mul(z, dot(v, z))))
    y = cross(z, x)
    return qfromcols(x, y, z)


def q_sun_pointing(r, v, sun_dir):
    """Deployed wing cells face body -Z, so point -Z at the Sun."""
    z = unit(neg(sun_dir))
    x = sub(v, mul(z, dot(v, z)))
    if norm(x) < 1e-6:
        x = sub((0, 0, 1), mul(z, z[2]))
    x = unit(x)
    y = cross(z, x)
    return qfromcols(x, y, z)


# ------------------------------------------------- the slide-23 scenario
SCENARIO_EPOCH = datetime(2026, 8, 20, 12, 0, 0, tzinfo=timezone.utc)
SCENARIO_ALT_KM = 500.0
SCENARIO_INC_DEG = 98.0
# Chosen by solve_scenario() so the eclipse runs ~1980-4000 s like slide 23:
# beta = +31.0 deg, descending node at 09:43 local time (a morning ocean-colour orbit).
SCENARIO_RAAN_DEG = 295.012033      # right ascension of the ascending node at epoch
SCENARIO_ARGLAT_DEG = 150.366671    # argument of latitude at epoch

TUMBLE_Q0 = qaxis((1.0, 2.0, 3.0), 2.0)
TUMBLE_AXIS = unit((2.5, -3.5, 4.0))
TUMBLE_RATE_DEG = norm((2.5, -3.5, 4.0))   # deg/s right after separation
TUMBLE_TAU = 300.0                         # s, detumbling time constant
WING_DEPLOY_S = 30.0
ROLL_DEG = 25.0


class Scenario:
    """Attitude segments + deployment events, a pure function of time."""

    def __init__(self, orbit):
        self.orbit = orbit
        self.segments = []      # (t0, mode, q0, slew)
        self._add_segment(0.0, "TUMBLE_DECAY", TUMBLE_Q0, 0.0)
        self._add_mode(900.0, "NADIR", 300.0)
        self._add_mode(4300.0, "ROLL", 60.0)
        self._add_mode(4740.0, "NADIR", 60.0)
        self.events = [(600.0, "ANTENNA_DEPLOY"), (1300.0, "WING_MY_DEPLOY"), (1400.0, "WING_PY_DEPLOY")]

    def _add_segment(self, t0, mode, q0, slew):
        self.segments.append((t0, mode, q0, slew))

    def _add_mode(self, t0, mode, slew):
        q0 = self.attitude(t0)
        self._add_segment(t0, mode, q0, slew)

    def target(self, mode, t):
        s = self.orbit.epoch + t
        r, v = self.orbit.state(s)
        qn = q_nadir(r, v)
        if mode == "NADIR":
            return qn
        if mode == "ROLL":
            return qmul(qn, qaxis((1, 0, 0), ROLL_DEG * DEG))
        if mode == "SUN":
            return q_sun_pointing(r, v, unit(sub(sun_j2000(s), r)))
        raise ValueError(mode)

    def attitude(self, t):
        seg = self.segments[0]
        for sg in self.segments:
            if sg[0] <= t:
                seg = sg
        t0, mode, q0, slew = seg
        dt = max(0.0, t - t0)
        if mode == "TUMBLE_DECAY":
            ang = TUMBLE_RATE_DEG * DEG * TUMBLE_TAU * (1 - math.exp(-dt / TUMBLE_TAU))
            return qnormalize(qmul(q0, qaxis(TUMBLE_AXIS, ang)))
        tgt = self.target(mode, t)
        if slew > 0 and dt < slew:
            return qslerp(q0, tgt, smoothstep(dt / slew))
        return tgt

    def deploy(self, t):
        wing_my = wing_py = None
        antenna = False
        for te, ev in self.events:
            if te <= t:
                if ev == "WING_MY_DEPLOY" and wing_my is None:
                    wing_my = te
                elif ev == "WING_PY_DEPLOY" and wing_py is None:
                    wing_py = te
                elif ev == "ANTENNA_DEPLOY":
                    antenna = True

        def wing(te):
            if te is None:
                return {"state": "STOWED", "fraction": 0.0}
            f = (t - te) / WING_DEPLOY_S
            if f < 1.0:
                return {"state": "DEPLOYING", "fraction": round(max(0.0, f), 3)}
            return {"state": "DEPLOYED", "fraction": 1.0}
        return {"wing_my": wing(wing_my), "wing_py": wing(wing_py),
                "antenna": {"state": "DEPLOYED" if antenna else "STOWED"}}


def eclipse_intervals(orbit, t_end, step=1.0):
    out, start, prev = [], None, None
    t = 0.0
    while t <= t_end:
        s = orbit.epoch + t
        r, _ = orbit.state(s)
        ecl = sunlit_fraction(r, sun_j2000(s)) < 0.5
        if ecl and start is None:
            start = t
        if not ecl and start is not None:
            out.append((start, t))
            start = None
        prev = ecl
        t += step
    if start is not None:
        out.append((start, t_end))
    return out


def solve_scenario(entry=1980.0, duration=2020.0):
    """Find RAAN (sets the beta angle -> eclipse length) and the argument of
    latitude at epoch (sets when the eclipse starts) for the slide timeline."""
    epoch = secs_j2000(SCENARIO_EPOCH)
    sdir = unit(sun_j2000(epoch))
    ra_sun = math.degrees(math.atan2(sdir[1], sdir[0])) % 360

    def ecl_len(raan):
        o = CircularOrbit(SCENARIO_ALT_KM, SCENARIO_INC_DEG, raan, 0.0, epoch)
        iv = eclipse_intervals(o, o.period * 2.2, 2.0)
        full = [b - a for a, b in iv if a > 0]
        return full[0] if full else 0.0

    # Scan RAAN relative to the Sun, keeping the morning family: descending node
    # between 08:00 and 12:00 local time (an ocean-colour orbit, like Sentinel-3 at 10:00).
    best = None
    for k in range(0, 600):
        raan = (ra_sun + 120 + k * 0.1) % 360
        h = (math.sin(raan * DEG) * math.sin(SCENARIO_INC_DEG * DEG),
             -math.cos(raan * DEG) * math.sin(SCENARIO_INC_DEG * DEG), math.cos(SCENARIO_INC_DEG * DEG))
        beta = math.degrees(math.asin(dot(h, sdir)))
        # cylindrical estimate of eclipse length
        a = R_EARTH + SCENARIO_ALT_KM
        c = math.sqrt(1 - (R_EARTH / a) ** 2) / max(1e-9, math.cos(beta * DEG))
        if c >= 1:
            continue
        est = 2 * math.acos(c) / math.sqrt(MU / a ** 3)
        if best is None or abs(est - duration) < abs(best[1] - duration):
            best = (raan, est)
    raan = best[0]
    # refine with the conical model
    lo, hi = raan - 2.0, raan + 2.0
    for _ in range(30):
        mid = (lo + hi) / 2
        if (ecl_len(lo) - duration) * (ecl_len(mid) - duration) <= 0:
            hi = mid
        else:
            lo = mid
    raan = (lo + hi) / 2
    o = CircularOrbit(SCENARIO_ALT_KM, SCENARIO_INC_DEG, raan, 0.0, epoch)
    iv = eclipse_intervals(o, o.period * 2.2, 0.5)
    first = [a for a, b in iv if a > 0][0]
    u0 = math.degrees(o.n * (first - entry)) % 360
    return raan % 360, u0, ra_sun
