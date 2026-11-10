// EOSAT-1 VR - double-precision vector and quaternion maths.
// Everything in Scripts/Core is plain C# (no UnityEngine), in the simulator's
// right-handed conventions. Conversion to Unity's left-handed, Y-up axes happens
// in exactly one place: Scripts/Unity/SimToUnity.cs.
using System;
using System.Globalization;

namespace Eosat1.Core
{
    /// <summary>3-vector in a right-handed simulator frame (ECI, body, local-vertical).</summary>
    [Serializable]
    public struct Vec3d
    {
        public double x, y, z;

        public Vec3d(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d UnitX = new Vec3d(1, 0, 0);
        public static readonly Vec3d UnitY = new Vec3d(0, 1, 0);
        public static readonly Vec3d UnitZ = new Vec3d(0, 0, 1);

        public double Length { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public double SqrLength { get { return x * x + y * y + z * z; } }

        public Vec3d Normalized
        {
            get
            {
                double l = Length;
                return l > 1e-300 ? new Vec3d(x / l, y / l, z / l) : Zero;
            }
        }

        public bool IsFinite
        {
            get
            {
                return !(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) ||
                         double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z));
            }
        }

        public static Vec3d operator +(Vec3d a, Vec3d b) { return new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3d operator -(Vec3d a, Vec3d b) { return new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3d operator -(Vec3d a) { return new Vec3d(-a.x, -a.y, -a.z); }
        public static Vec3d operator *(Vec3d a, double s) { return new Vec3d(a.x * s, a.y * s, a.z * s); }
        public static Vec3d operator *(double s, Vec3d a) { return new Vec3d(a.x * s, a.y * s, a.z * s); }
        public static Vec3d operator /(Vec3d a, double s) { return new Vec3d(a.x / s, a.y / s, a.z / s); }

        public static double Dot(Vec3d a, Vec3d b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static Vec3d Cross(Vec3d a, Vec3d b)
        {
            return new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) { return a + (b - a) * t; }

        /// <summary>Angle between two vectors in radians, accurate near 0 and pi.</summary>
        public static double Angle(Vec3d a, Vec3d b)
        {
            return Math.Atan2(Cross(a, b).Length, Dot(a, b));
        }

        /// <summary>Component of v perpendicular to the unit vector n.</summary>
        public static Vec3d RejectFrom(Vec3d v, Vec3d n)
        {
            return v - n * Dot(v, n);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", x, y, z);
        }
    }

    /// <summary>
    /// Hamilton quaternion stored scalar-last (x, y, z, w), the simulator's order.
    /// Active rotation: v' = q v q*. Composition a * b applies b first, then a.
    /// </summary>
    [Serializable]
    public struct QuatD
    {
        public double x, y, z, w;

        public QuatD(double x, double y, double z, double w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static readonly QuatD Identity = new QuatD(0, 0, 0, 1);

        public static QuatD operator *(QuatD a, QuatD b)
        {
            return new QuatD(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        public QuatD Conjugate { get { return new QuatD(-x, -y, -z, w); } }

        public double Norm { get { return Math.Sqrt(x * x + y * y + z * z + w * w); } }

        public QuatD Normalized
        {
            get
            {
                double n = Norm;
                return n > 1e-300 ? new QuatD(x / n, y / n, z / n, w / n) : Identity;
            }
        }

        public bool IsFinite
        {
            get
            {
                return !(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) || double.IsNaN(w) ||
                         double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z) || double.IsInfinity(w));
            }
        }

        /// <summary>Rotate a vector: q v q*.</summary>
        public Vec3d Rotate(Vec3d v)
        {
            var qv = new Vec3d(x, y, z);
            Vec3d t = 2.0 * Vec3d.Cross(qv, v);
            return v + w * t + Vec3d.Cross(qv, t);
        }

        public static double Dot(QuatD a, QuatD b) { return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w; }

        public static QuatD AxisAngle(Vec3d axis, double angleRad)
        {
            Vec3d n = axis.Normalized;
            double s = Math.Sin(angleRad * 0.5);
            return new QuatD(n.x * s, n.y * s, n.z * s, Math.Cos(angleRad * 0.5));
        }

        /// <summary>Spherical interpolation along the shorter arc.</summary>
        public static QuatD Slerp(QuatD a, QuatD b, double t)
        {
            double d = Dot(a, b);
            if (d < 0)
            {
                b = new QuatD(-b.x, -b.y, -b.z, -b.w);
                d = -d;
            }
            if (d > 0.9995)
            {
                return new QuatD(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t,
                                 a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t).Normalized;
            }
            double th0 = Math.Acos(d);
            double s = Math.Sin(th0);
            double s0 = Math.Sin(th0 - th0 * t) / s;
            double s1 = Math.Sin(th0 * t) / s;
            return new QuatD(a.x * s0 + b.x * s1, a.y * s0 + b.y * s1,
                             a.z * s0 + b.z * s1, a.w * s0 + b.w * s1).Normalized;
        }

        /// <summary>Rotation angle in radians (0..pi) that takes attitude a to attitude b.</summary>
        public static double Angle(QuatD a, QuatD b)
        {
            double d = Math.Abs(Dot(a.Normalized, b.Normalized));
            if (d > 1.0) d = 1.0;
            return 2.0 * Math.Acos(d);
        }

        /// <summary>
        /// Rotation whose matrix has the given orthonormal columns, i.e. it maps the
        /// local unit axes onto c0, c1, c2. With the body axes expressed in ECI as the
        /// columns, this is the body-to-ECI attitude.
        /// </summary>
        public static QuatD FromColumns(Vec3d c0, Vec3d c1, Vec3d c2)
        {
            double m00 = c0.x, m01 = c1.x, m02 = c2.x;
            double m10 = c0.y, m11 = c1.y, m12 = c2.y;
            double m20 = c0.z, m21 = c1.z, m22 = c2.z;
            double tr = m00 + m11 + m22;
            QuatD q;
            if (tr > 0)
            {
                double s = Math.Sqrt(tr + 1.0) * 2.0;
                q = new QuatD((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
                q = new QuatD(0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
                q = new QuatD((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
                q = new QuatD((m02 + m20) / s, (m12 + m21) / s, 0.25 * s, (m10 - m01) / s);
            }
            return q.Normalized;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:0.0000}, {1:0.0000}, {2:0.0000}, {3:0.0000}]", x, y, z, w);
        }
    }

    public static class MathUtil
    {
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        public static double SmoothStep01(double x)
        {
            x = Clamp01(x);
            return x * x * (3.0 - 2.0 * x);
        }

        public static bool IsFinite(double v) { return !(double.IsNaN(v) || double.IsInfinity(v)); }
    }
}
