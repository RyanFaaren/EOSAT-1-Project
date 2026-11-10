// EOSAT-1 VR - circular orbit propagation and the "Earth below, flight ahead" frame.
using System;
using System.Collections.Generic;

namespace Eosat1.Core
{
    /// <summary>Circular orbit with J2 nodal drift. Matches Tools/eosat_physics.py.</summary>
    [Serializable]
    public sealed class CircularOrbit
    {
        public const double MuKm3s2 = 398600.4418;
        public const double J2 = 1.08262668e-3;

        public double SemiMajorAxisKm;
        public double InclinationRad;
        public double RaanAtEpochRad;
        public double ArgLatAtEpochRad;
        public double EpochS;            // UTC seconds since J2000
        public double RaanRateRadS;

        public static CircularOrbit Create(double altitudeKm, double inclinationDeg, double raanDeg, double argLatDeg,
                                           double epochS, bool j2Drift = true)
        {
            var o = new CircularOrbit
            {
                SemiMajorAxisKm = Ephemeris.EarthRadiusKm + altitudeKm,
                InclinationRad = inclinationDeg * MathUtil.Deg2Rad,
                RaanAtEpochRad = raanDeg * MathUtil.Deg2Rad,
                ArgLatAtEpochRad = argLatDeg * MathUtil.Deg2Rad,
                EpochS = epochS,
            };
            if (j2Drift)
            {
                double ratio = Ephemeris.EarthEquatorialKm / o.SemiMajorAxisKm;
                o.RaanRateRadS = -1.5 * o.MeanMotion * J2 * ratio * ratio * Math.Cos(o.InclinationRad);
            }
            return o;
        }

        public double MeanMotion
        {
            get { return Math.Sqrt(MuKm3s2 / (SemiMajorAxisKm * SemiMajorAxisKm * SemiMajorAxisKm)); }
        }

        public double PeriodS { get { return 2.0 * Math.PI / MeanMotion; } }

        public void StateAt(double sJ2000, out Vec3d posKm, out Vec3d velKmS)
        {
            double dt = sJ2000 - EpochS;
            double n = MeanMotion;
            double u = ArgLatAtEpochRad + n * dt;
            double raan = RaanAtEpochRad + RaanRateRadS * dt;
            double cu = Math.Cos(u), su = Math.Sin(u), cO = Math.Cos(raan), sO = Math.Sin(raan);
            double ci = Math.Cos(InclinationRad), si = Math.Sin(InclinationRad);
            double a = SemiMajorAxisKm, v = a * n;
            posKm = new Vec3d(a * (cO * cu - sO * su * ci), a * (sO * cu + cO * su * ci), a * su * si);
            velKmS = new Vec3d(v * (-cO * su - sO * cu * ci), v * (-sO * su + cO * cu * ci), v * cu * si);
        }
    }

    public static class Attitudes
    {
        /// <summary>Camera face (+Z) at Earth's centre, +X along the flight direction.</summary>
        public static QuatD Nadir(Vec3d r, Vec3d v)
        {
            Vec3d z = (-r).Normalized;
            Vec3d x = Vec3d.RejectFrom(v, z).Normalized;
            if (x.SqrLength < 0.5) x = AnyPerpendicular(z);
            Vec3d y = Vec3d.Cross(z, x);
            return QuatD.FromColumns(x, y, z);
        }

        /// <summary>
        /// Which way the deployed solar cells face, in body axes. EOS SAT-1 model: -X (the two
        /// panels fold out into one flat array on the rear). SatelliteView sets it from the model.
        /// </summary>
        public static Vec3d SunFacingAxisBody = new Vec3d(-1, 0, 0);

        /// <summary>
        /// Turn the deployed cells (SunFacingAxisBody) to the Sun, and keep the camera face (+Z)
        /// as close to Earth as that allows.
        /// </summary>
        public static QuatD SunPointing(Vec3d r, Vec3d v, Vec3d sunDir)
        {
            Vec3d a = SunFacingAxisBody.SqrLength > 0.25 ? SunFacingAxisBody.Normalized : new Vec3d(-1, 0, 0);
            // body triad: e1 along the cells, e2 the camera axis made perpendicular to them
            Vec3d e1 = a;
            Vec3d e2 = Vec3d.RejectFrom(Vec3d.UnitZ, e1);
            if (e2.SqrLength < 1e-8) e2 = AnyPerpendicular(e1);
            e2 = e2.Normalized;
            Vec3d e3 = Vec3d.Cross(e1, e2);
            // ECI triad: f1 at the Sun, f2 towards Earth's centre made perpendicular to it
            Vec3d f1 = sunDir.Normalized;
            Vec3d f2 = Vec3d.RejectFrom((-r).Normalized, f1);
            if (f2.SqrLength < 1e-8) f2 = Vec3d.RejectFrom(v, f1);
            if (f2.SqrLength < 1e-8) f2 = AnyPerpendicular(f1);
            f2 = f2.Normalized;
            Vec3d f3 = Vec3d.Cross(f1, f2);
            // rotation taking e_i to f_i; its columns are the body axes in ECI
            Vec3d cx = f1 * e1.x + f2 * e2.x + f3 * e3.x;
            Vec3d cy = f1 * e1.y + f2 * e2.y + f3 * e3.y;
            Vec3d cz = f1 * e1.z + f2 * e2.z + f3 * e3.z;
            return QuatD.FromColumns(cx, cy, cz);
        }

        public static Vec3d AnyPerpendicular(Vec3d n)
        {
            Vec3d a = Math.Abs(n.x) < 0.9 ? Vec3d.UnitX : Vec3d.UnitY;
            return Vec3d.RejectFrom(a, n.Normalized).Normalized;
        }
    }

    /// <summary>
    /// Estimates the orbit normal from the position history when the frames carry no
    /// velocity. Uses the widest baseline available (up to ~15 min) so that positions
    /// rounded to whole kilometres still give a direction good to a fraction of a degree.
    /// </summary>
    public sealed class OrbitNormalEstimator
    {
        struct Sample
        {
            public double T;
            public Vec3d R;
        }

        readonly List<Sample> samples = new List<Sample>();
        Vec3d normal;
        bool hasNormal;

        public double MinSpacingS = 5.0;
        public double MaxAgeS = 900.0;

        public bool HasNormal { get { return hasNormal; } }
        public Vec3d Normal { get { return normal; } }

        public void Reset()
        {
            samples.Clear();
            hasNormal = false;
        }

        /// <summary>Feed a position (and velocity if the frame had one).</summary>
        public void Add(double t, Vec3d r, bool hasVelocity, Vec3d v)
        {
            if (!r.IsFinite || r.SqrLength < 1.0) return;
            if (hasVelocity && v.IsFinite && v.SqrLength > 1e-6)
            {
                Vec3d h = Vec3d.Cross(r, v);
                if (h.SqrLength > 1e-6)
                {
                    normal = h.Normalized;
                    hasNormal = true;
                }
            }
            if (samples.Count > 0)
            {
                double last = samples[samples.Count - 1].T;
                if (t < last - 0.5) samples.Clear();            // clock went back: start again
                else if (t - last < MinSpacingS && samples.Count > 1) return;
            }
            samples.Add(new Sample { T = t, R = r });
            while (samples.Count > 2 && t - samples[0].T > MaxAgeS) samples.RemoveAt(0);
            if (hasVelocity) return;
            Sample first = samples[0];
            double ang = Vec3d.Angle(first.R, r);
            if (ang < 0.002 || ang > 2.9) return;              // too short a baseline, or nearly opposite
            Vec3d hn = Vec3d.Cross(first.R, r).Normalized;
            if (hasNormal && Vec3d.Dot(hn, normal) > 0.0)
            {
                // blend for smoothness; long baselines get more weight
                double wgt = MathUtil.Clamp(ang / 0.05, 0.05, 1.0);
                normal = Vec3d.Lerp(normal, hn, wgt).Normalized;
            }
            else
            {
                normal = hn;
                hasNormal = true;
            }
        }
    }

    public static class LocalFrame
    {
        /// <summary>
        /// Rotation from ECI into the viewer's "local vertical" frame, right-handed:
        /// +Z up (away from Earth), +Y the flight direction, +X = -orbit normal.
        /// In Unity axes (x, z, y) that is: up = +Y, flight = +Z (ahead of the viewer).
        /// </summary>
        public static QuatD EciToLocal(Vec3d r, Vec3d orbitNormal)
        {
            Vec3d z = r.Normalized;
            Vec3d x = -Vec3d.RejectFrom(orbitNormal, z);
            if (x.SqrLength < 1e-12) x = Attitudes.AnyPerpendicular(z);
            x = x.Normalized;
            Vec3d y = Vec3d.Cross(z, x);
            return QuatD.FromColumns(x, y, z).Conjugate;
        }

        /// <summary>Fallback heading when the orbit normal is not known yet: flight direction assumed north.</summary>
        public static Vec3d NormalAssumingNorthbound(Vec3d r)
        {
            Vec3d up = r.Normalized;
            Vec3d north = Vec3d.RejectFrom(Vec3d.UnitZ, up);
            if (north.SqrLength < 1e-9) north = Vec3d.RejectFrom(Vec3d.UnitX, up);
            return Vec3d.Cross(up, north.Normalized).Normalized;
        }
    }
}
