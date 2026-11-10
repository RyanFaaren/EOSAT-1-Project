// EOSAT-1 VR - time scales, Earth orientation, Sun and Moon, Earth's shadow.
// Low-precision models (Astronomical Almanac / Vallado), plenty for a picture:
// Sun ~0.01 deg, Moon ~0.3 deg, Earth rotation and precession to arcseconds.
// The same formulas are in Tools/eosat_physics.py, which generated the sample orbit.
using System;

namespace Eosat1.Core
{
    /// <summary>Which inertial frame the simulator's "ECI" vectors are in.</summary>
    public enum EciConvention
    {
        /// <summary>J2000 / EME2000 (mean equator and equinox of 2000). Default.</summary>
        J2000 = 0,
        /// <summary>True-of-date or TEME (SGP4). Earth turns by sidereal time only.</summary>
        OfDate = 1,
    }

    public static class AstroTime
    {
        public static readonly DateTime J2000Utc = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>TT - UTC in seconds (TAI-UTC = 37 s since 2017, plus 32.184 s).</summary>
        public const double TtMinusUtc = 69.184;

        public const double SecondsPerDay = 86400.0;
        public const double SecondsPerCentury = 36525.0 * 86400.0;

        /// <summary>UTC seconds since 2000-01-01T12:00:00Z (the time unit used everywhere here).</summary>
        public static double SecondsSinceJ2000(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            return (utc - J2000Utc).TotalSeconds;
        }

        public static DateTime ToDateTimeUtc(double sJ2000)
        {
            if (!MathUtil.IsFinite(sJ2000)) return J2000Utc;
            sJ2000 = MathUtil.Clamp(sJ2000, -3.0e9, 3.0e9);
            return J2000Utc.AddTicks((long)Math.Round(sJ2000 * TimeSpan.TicksPerSecond));
        }

        public static string ToIso(double sJ2000)
        {
            return ToDateTimeUtc(sJ2000).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static double CenturiesTT(double sJ2000Utc)
        {
            return (sJ2000Utc + TtMinusUtc) / SecondsPerCentury;
        }

        /// <summary>Greenwich mean sidereal time (IAU-82) in radians, with UT1 ~ UTC.</summary>
        public static double GmstRad(double sJ2000Utc)
        {
            double tu = sJ2000Utc / SecondsPerCentury;
            double sec = 67310.54841 + (876600.0 * 3600.0 + 8640184.812866) * tu
                         + 0.093104 * tu * tu - 6.2e-6 * tu * tu * tu;
            sec %= SecondsPerDay;
            if (sec < 0) sec += SecondsPerDay;
            return sec / SecondsPerDay * 2.0 * Math.PI;
        }

        /// <summary>IAU-76 precession: rotates J2000 vectors into mean-of-date (r_mod = q r_j2000 q*).</summary>
        public static QuatD PrecessionJ2000ToMod(double sJ2000Utc)
        {
            double t = CenturiesTT(sJ2000Utc);
            const double as2r = Math.PI / (180.0 * 3600.0);
            double zeta = (2306.2181 * t + 0.30188 * t * t + 0.017998 * t * t * t) * as2r;
            double z = (2306.2181 * t + 1.09468 * t * t + 0.018203 * t * t * t) * as2r;
            double theta = (2004.3109 * t - 0.42665 * t * t - 0.041833 * t * t * t) * as2r;
            return QuatD.AxisAngle(Vec3d.UnitZ, z) * QuatD.AxisAngle(Vec3d.UnitY, -theta) * QuatD.AxisAngle(Vec3d.UnitZ, zeta);
        }

        /// <summary>
        /// Rotation from Earth-fixed (ECEF) to the chosen ECI frame. Polar motion and
        /// nutation are ignored (well under 0.01 deg).
        /// </summary>
        public static QuatD EcefToEci(double sJ2000Utc, EciConvention convention)
        {
            QuatD spin = QuatD.AxisAngle(Vec3d.UnitZ, GmstRad(sJ2000Utc));
            if (convention == EciConvention.OfDate) return spin;
            return PrecessionJ2000ToMod(sJ2000Utc).Conjugate * spin;
        }

        /// <summary>Rotation from J2000 to the chosen ECI frame (identity for J2000).</summary>
        public static QuatD J2000ToEci(double sJ2000Utc, EciConvention convention)
        {
            return convention == EciConvention.OfDate ? PrecessionJ2000ToMod(sJ2000Utc) : QuatD.Identity;
        }
    }

    public static class Ephemeris
    {
        public const double AuKm = 149597870.7;
        public const double EarthRadiusKm = 6371.0;       // mean radius: rendering and shadow
        public const double EarthEquatorialKm = 6378.137;
        public const double SunRadiusKm = 695700.0;
        public const double MoonRadiusKm = 1737.4;
        const double Deg = Math.PI / 180.0;

        static double SinD(double deg) { return Math.Sin(deg * Deg); }
        static double CosD(double deg) { return Math.Cos(deg * Deg); }

        /// <summary>Geocentric Sun position, mean equator and equinox of date, km.</summary>
        public static Vec3d SunOfDate(double sJ2000Utc)
        {
            double n = (sJ2000Utc + AstroTime.TtMinusUtc) / AstroTime.SecondsPerDay;
            double L = 280.460 + 0.9856474 * n;
            double g = (357.528 + 0.9856003 * n) * Deg;
            double lambda = (L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Deg;
            double eps = (23.439 - 0.0000004 * n) * Deg;
            double r = (1.00014 - 0.01671 * Math.Cos(g) - 0.00014 * Math.Cos(2 * g)) * AuKm;
            return new Vec3d(r * Math.Cos(lambda), r * Math.Cos(eps) * Math.Sin(lambda), r * Math.Sin(eps) * Math.Sin(lambda));
        }

        /// <summary>Geocentric Moon position, mean equator and equinox of date, km.</summary>
        public static Vec3d MoonOfDate(double sJ2000Utc)
        {
            double T = AstroTime.CenturiesTT(sJ2000Utc);
            double lam = 218.32 + 481267.8813 * T
                         + 6.29 * SinD(134.9 + 477198.85 * T) - 1.27 * SinD(259.2 - 413335.38 * T)
                         + 0.66 * SinD(235.7 + 890534.23 * T) + 0.21 * SinD(269.9 + 954397.70 * T)
                         - 0.19 * SinD(357.5 + 35999.05 * T) - 0.11 * SinD(186.6 + 966404.05 * T);
            double beta = 5.13 * SinD(93.3 + 483202.03 * T) + 0.28 * SinD(228.2 + 960400.87 * T)
                          - 0.28 * SinD(318.3 + 6003.18 * T) - 0.17 * SinD(217.6 - 407332.20 * T);
            double par = 0.9508 + 0.0518 * CosD(134.9 + 477198.85 * T) + 0.0095 * CosD(259.2 - 413335.38 * T)
                         + 0.0078 * CosD(235.7 + 890534.23 * T) + 0.0028 * CosD(269.9 + 954397.70 * T);
            double eps = (23.439291 - 0.0130042 * T) * Deg;
            double r = EarthEquatorialKm / Math.Sin(par * Deg);
            double l = lam * Deg, b = beta * Deg;
            double cb = Math.Cos(b), sb = Math.Sin(b), cl = Math.Cos(l), sl = Math.Sin(l);
            return new Vec3d(r * cb * cl,
                             r * (Math.Cos(eps) * cb * sl - Math.Sin(eps) * sb),
                             r * (Math.Sin(eps) * cb * sl + Math.Cos(eps) * sb));
        }

        /// <summary>Sun position in the chosen ECI frame, km.</summary>
        public static Vec3d SunEci(double sJ2000Utc, EciConvention convention)
        {
            Vec3d mod = SunOfDate(sJ2000Utc);
            return convention == EciConvention.OfDate ? mod : AstroTime.PrecessionJ2000ToMod(sJ2000Utc).Conjugate.Rotate(mod);
        }

        /// <summary>Moon position in the chosen ECI frame, km.</summary>
        public static Vec3d MoonEci(double sJ2000Utc, EciConvention convention)
        {
            Vec3d mod = MoonOfDate(sJ2000Utc);
            return convention == EciConvention.OfDate ? mod : AstroTime.PrecessionJ2000ToMod(sJ2000Utc).Conjugate.Rotate(mod);
        }

        /// <summary>
        /// Fraction of the solar disc visible from rSat (1 = full Sun, 0 = umbra),
        /// with a conical umbra/penumbra (Montenbruck and Gill 3.4.2). km, same frame.
        /// </summary>
        public static double SunlitFraction(Vec3d rSat, Vec3d rSun)
        {
            Vec3d toSun = rSun - rSat;
            double dSun = toSun.Length, dEarth = rSat.Length;
            if (dEarth <= EarthRadiusKm || dSun <= 0) return 0.0;
            double a = Math.Asin(Math.Min(1.0, SunRadiusKm / dSun));
            double b = Math.Asin(Math.Min(1.0, EarthRadiusKm / dEarth));
            double c = Vec3d.Angle(-rSat, toSun);
            if (c >= a + b) return 1.0;
            if (c <= b - a) return 0.0;
            if (c <= a - b) return 1.0 - (b * b) / (a * a);
            double x = (c * c + a * a - b * b) / (2.0 * c);
            double y = Math.Sqrt(Math.Max(0.0, a * a - x * x));
            double area = a * a * Math.Acos(MathUtil.Clamp(x / a, -1, 1))
                          + b * b * Math.Acos(MathUtil.Clamp((c - x) / b, -1, 1)) - c * y;
            return MathUtil.Clamp01(1.0 - area / (Math.PI * a * a));
        }

        /// <summary>Ecliptic north pole in J2000 (used as the Moon's rough spin axis).</summary>
        public static Vec3d EclipticPoleJ2000()
        {
            double eps = 23.4392911 * Deg;
            return new Vec3d(0, -Math.Sin(eps), Math.Cos(eps));
        }
    }
}
