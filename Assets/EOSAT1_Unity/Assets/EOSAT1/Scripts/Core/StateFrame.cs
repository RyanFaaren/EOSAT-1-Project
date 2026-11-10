// EOSAT-1 VR - the "state" frame from the simulator and its parser.
//
//   {"type":"state","seq":1812,"sim_time":"2026-08-20T12:30:12Z",
//    "orbit":{"pos_eci_km":[-4702,2516,4407]},
//    "attitude":{"q_body_to_eci":[0.123,-0.702,0.281,0.643]},
//    "sun":{"dir_eci":[-0.84,0.49,0.21],"in_eclipse":false},
//    "deploy":{"wing_my":{"state":"DEPLOYING","fraction":0.43},"antenna":{"state":"DEPLOYED"}}, ...}
//
// Rules from the course: skip any line you can't parse, ignore fields you don't know.
// Required: sim_time, orbit.pos_eci_km, attitude.q_body_to_eci. Everything else is optional;
// missing deploy states carry over from the previous frame, a missing Sun is computed.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Eosat1.Core
{
    public enum DeployState
    {
        Unknown = 0,
        Stowed = 1,
        Deploying = 2,
        Deployed = 3,
        Jammed = 4,
    }

    [Serializable]
    public struct DeployInfo
    {
        public bool Present;
        public DeployState State;
        public double Fraction;     // 0 stowed .. 1 fully out

        public static DeployInfo Of(DeployState s, double fraction)
        {
            return new DeployInfo { Present = true, State = s, Fraction = MathUtil.Clamp01(fraction) };
        }

        /// <summary>How far out it is, filling in a fraction when the frame only gave a state.</summary>
        public double EffectiveFraction
        {
            get
            {
                if (!Present) return 0;
                switch (State)
                {
                    case DeployState.Stowed: return 0;
                    case DeployState.Deployed: return 1;
                    default: return MathUtil.Clamp01(Fraction);
                }
            }
        }

        public string Label
        {
            get
            {
                if (!Present || State == DeployState.Unknown) return "?";
                if (State == DeployState.Deploying)
                    return "DEPLOYING " + Math.Round(EffectiveFraction * 100).ToString(CultureInfo.InvariantCulture) + "%";
                return State.ToString().ToUpperInvariant();
            }
        }

        public static DeployState ParseState(string s)
        {
            if (string.IsNullOrEmpty(s)) return DeployState.Unknown;
            switch (s.Trim().ToUpperInvariant())
            {
                case "STOWED": return DeployState.Stowed;
                case "DEPLOYING": return DeployState.Deploying;
                case "DEPLOYED": return DeployState.Deployed;
                case "JAMMED": return DeployState.Jammed;
                default: return DeployState.Unknown;
            }
        }
    }

    public enum MessageKind
    {
        Unknown = 0,
        Hello = 1,
        State = 2,
        Other = 3,
    }

    /// <summary>One state frame. Times are UTC seconds since J2000.</summary>
    [Serializable]
    public sealed class StateFrame
    {
        public long Seq = -1;
        public double SimTime;
        public Vec3d PosEciKm;
        public bool HasVelocity;
        public Vec3d VelEciKmS;
        public QuatD QBodyToEci = QuatD.Identity;
        public bool HasSun;
        public Vec3d SunDirEci;
        public bool HasEclipseFlag;
        public bool InEclipse;
        /// <summary>0..1 if the source provides it (the sandbox and the sample orbit do), else NaN.</summary>
        public double SunlitFraction = double.NaN;
        public DeployInfo WingMinusY;
        public DeployInfo WingPlusY;
        public DeployInfo Antenna;
        /// <summary>Body rate magnitude in deg/s if the source provides it, else NaN.</summary>
        public double BodyRateDegS = double.NaN;

        // set by the receiving side
        public double ArrivalWall;
        public bool JumpFromPrevious;

        public StateFrame Clone()
        {
            return (StateFrame)MemberwiseClone();
        }
    }

    public static class FrameParser
    {
        [ThreadStatic] static double[] buf3;
        [ThreadStatic] static double[] buf4;

        /// <summary>Parse one line. Returns false (with a reason) for anything unusable.</summary>
        public static bool TryParse(string line, out MessageKind kind, out StateFrame frame, out string error)
        {
            kind = MessageKind.Unknown;
            frame = null;
            error = null;
            if (line == null)
            {
                error = "empty line";
                return false;
            }
            object root;
            if (!MiniJson.TryParse(line, out root, out error)) return false;
            var d = root as Dictionary<string, object>;
            if (d == null)
            {
                error = "not a JSON object";
                return false;
            }
            string type;
            MiniJson.TryString(d, "type", out type);
            if (type == "hello")
            {
                kind = MessageKind.Hello;
                return true;
            }
            bool looksLikeState = type == null && d.ContainsKey("attitude") && d.ContainsKey("orbit");
            if (type != "state" && !looksLikeState)
            {
                kind = MessageKind.Other;
                return true;
            }
            kind = MessageKind.State;
            return TryParseState(d, out frame, out error);
        }

        static bool TryParseState(Dictionary<string, object> d, out StateFrame f, out string error)
        {
            if (buf3 == null) buf3 = new double[3];
            if (buf4 == null) buf4 = new double[4];
            f = new StateFrame();
            error = null;

            double seq;
            if (MiniJson.TryNumber(d, "seq", out seq) && seq >= 0 && seq < 9.0e15) f.Seq = (long)seq;

            string ts;
            double t;
            if (!MiniJson.TryString(d, "sim_time", out ts) || !Iso8601.TryParse(ts, out t))
            {
                error = "missing or unreadable sim_time";
                return false;
            }
            f.SimTime = t;

            var orbit = MiniJson.Obj(d, "orbit");
            if (!MiniJson.TryNumbers(orbit, "pos_eci_km", 3, buf3))
            {
                error = "missing orbit.pos_eci_km";
                return false;
            }
            f.PosEciKm = new Vec3d(buf3[0], buf3[1], buf3[2]);
            double rKm = f.PosEciKm.Length;
            if (rKm < 6000.0 || rKm > 500000.0)
            {
                error = "orbit.pos_eci_km is not a plausible position";
                return false;
            }
            if (MiniJson.TryNumbers(orbit, "vel_eci_km_s", 3, buf3) || MiniJson.TryNumbers(orbit, "vel_eci_kms", 3, buf3))
            {
                var v = new Vec3d(buf3[0], buf3[1], buf3[2]);
                if (v.Length > 0.1 && v.Length < 20.0)
                {
                    f.HasVelocity = true;
                    f.VelEciKmS = v;
                }
            }

            var att = MiniJson.Obj(d, "attitude");
            if (!MiniJson.TryNumbers(att, "q_body_to_eci", 4, buf4))
            {
                error = "missing attitude.q_body_to_eci";
                return false;
            }
            var q = new QuatD(buf4[0], buf4[1], buf4[2], buf4[3]);
            double qn = q.Norm;
            if (qn < 0.5 || qn > 1.5)
            {
                error = "attitude quaternion is not unit length";
                return false;
            }
            f.QBodyToEci = q.Normalized;
            double rate;
            if (MiniJson.TryNumbers(att, "rate_body_deg_s", 3, buf3))
                f.BodyRateDegS = new Vec3d(buf3[0], buf3[1], buf3[2]).Length;
            else if (MiniJson.TryNumber(att, "rate_deg_s", out rate))
                f.BodyRateDegS = Math.Abs(rate);

            var sun = MiniJson.Obj(d, "sun");
            if (MiniJson.TryNumbers(sun, "dir_eci", 3, buf3))
            {
                var s = new Vec3d(buf3[0], buf3[1], buf3[2]);
                if (s.Length > 0.1)
                {
                    f.HasSun = true;
                    f.SunDirEci = s.Normalized;
                }
            }
            bool ecl;
            if (MiniJson.TryBool(sun, "in_eclipse", out ecl))
            {
                f.HasEclipseFlag = true;
                f.InEclipse = ecl;
            }
            double frac;
            if (MiniJson.TryNumber(sun, "sunlit_fraction", out frac)) f.SunlitFraction = MathUtil.Clamp01(frac);

            var deploy = MiniJson.Obj(d, "deploy");
            f.WingMinusY = ReadDeploy(MiniJson.Obj(deploy, "wing_my"));
            f.WingPlusY = ReadDeploy(MiniJson.Obj(deploy, "wing_py"));
            f.Antenna = ReadDeploy(MiniJson.Obj(deploy, "antenna"));
            return true;
        }

        static DeployInfo ReadDeploy(Dictionary<string, object> o)
        {
            if (o == null) return new DeployInfo();
            string s;
            DeployState st = MiniJson.TryString(o, "state", out s) ? DeployInfo.ParseState(s) : DeployState.Unknown;
            double fr;
            bool hasFraction = MiniJson.TryNumber(o, "fraction", out fr);
            if (st == DeployState.Unknown && !hasFraction) return new DeployInfo();
            if (!hasFraction)
            {
                fr = st == DeployState.Deployed ? 1.0 : (st == DeployState.Deploying ? 0.5 : 0.0);
            }
            if (st == DeployState.Unknown) st = fr >= 1.0 ? DeployState.Deployed : (fr <= 0.0 ? DeployState.Stowed : DeployState.Deploying);
            return DeployInfo.Of(st, fr);
        }
    }

    /// <summary>Minimal ISO-8601 reader for "2026-08-20T12:30:12Z" (fractions and offsets allowed).</summary>
    public static class Iso8601
    {
        public static bool TryParse(string s, out double sJ2000)
        {
            sJ2000 = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            try
            {
                if (s.Length >= 19 && s[4] == '-' && s[7] == '-' && (s[10] == 'T' || s[10] == ' ' || s[10] == 't') &&
                    s[13] == ':' && s[16] == ':')
                {
                    int year = Int(s, 0, 4), month = Int(s, 5, 2), day = Int(s, 8, 2);
                    int hour = Int(s, 11, 2), minute = Int(s, 14, 2), second = Int(s, 17, 2);
                    int k = 19;
                    double frac = 0;
                    if (k < s.Length && (s[k] == '.' || s[k] == ','))
                    {
                        int start = ++k;
                        while (k < s.Length && s[k] >= '0' && s[k] <= '9') k++;
                        if (k > start)
                            frac = double.Parse("0." + s.Substring(start, Math.Min(k - start, 9)), CultureInfo.InvariantCulture);
                    }
                    double offset = 0;
                    if (k < s.Length)
                    {
                        char z = s[k];
                        if (z == 'Z' || z == 'z')
                        {
                            k++;
                        }
                        else if ((z == '+' || z == '-') && k + 3 <= s.Length)
                        {
                            int oh = Int(s, k + 1, 2);
                            int om = 0;
                            int after = k + 3;
                            if (after < s.Length && s[after] == ':') after++;
                            if (after + 2 <= s.Length) om = Int(s, after, 2);
                            offset = (z == '+' ? 1 : -1) * (oh * 3600 + om * 60);
                        }
                    }
                    if (second == 60) second = 59;   // leap second: close enough
                    var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                    sJ2000 = AstroTime.SecondsSinceJ2000(dt) + frac - offset;
                    return MathUtil.IsFinite(sJ2000);
                }
                DateTime parsed;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                {
                    sJ2000 = AstroTime.SecondsSinceJ2000(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
                    return true;
                }
            }
            catch (Exception)
            {
                // fall through
            }
            return false;
        }

        static int Int(string s, int start, int len)
        {
            int v = 0;
            for (int i = start; i < start + len; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') throw new FormatException("digit expected");
                v = v * 10 + (c - '0');
            }
            return v;
        }
    }
}
