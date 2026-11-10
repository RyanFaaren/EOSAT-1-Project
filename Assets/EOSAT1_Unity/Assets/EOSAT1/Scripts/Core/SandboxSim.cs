// EOSAT-1 VR - a small, interactive stand-in for the simulator, used when no live link
// is running (open days, demos, development). It flies the same one-orbit script as the
// sample file (course slide 23) and accepts commands: deploy each wing, deploy the
// antenna, inject an antenna jam, point at Earth / the Sun / roll to image, tumble.
//
// Everything is a pure function of time plus a list of timestamped events, so time can
// run at any speed and jump backwards (like reloading a saved point). Commands issued
// "in the future" of a backwards jump are forgotten, as a reload would.
// Formulas match Tools/eosat_physics.py.
using System;
using System.Collections.Generic;

namespace Eosat1.Core
{
    public enum SandboxCommand
    {
        DeployWingMinusY,
        DeployWingPlusY,
        DeployAntenna,
        ToggleAntennaJam,
        ResetDeployables,
        AttitudeNadir,
        AttitudeSun,
        AttitudeRollImaging,
        AttitudeTumble,
    }

    public sealed class SandboxSim
    {
        public const double WingDeploySeconds = 30.0;
        public const double RollDegrees = 25.0;
        public const double AltitudeKm = 500.0;
        public const double InclinationDeg = 98.0;

        // The slide-23 scenario: 2026-08-20 12:00 UTC, eclipse ~1980-4000 s, descending node 09:43.
        public static readonly DateTime SlideEpochUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        public const double SlideRaanDeg = 295.012033;
        public const double SlideArgLatDeg = 150.366671;
        public const double LtanHours = 21.71;

        enum Mode { TumbleDecay, Tumble, Nadir, Sun, Roll }

        struct Segment
        {
            public double T0;
            public Mode Mode;
            public QuatD Q0;
            public double Slew;
        }

        struct Event
        {
            public double T;
            public SandboxCommand Cmd;
            public bool Scripted;
            public int Order;
            public double Slew;   // NaN = work it out from the angle to go
        }

        static readonly QuatD TumbleQ0 = QuatD.AxisAngle(new Vec3d(1, 2, 3), 2.0);
        static readonly Vec3d DecayAxis = new Vec3d(2.5, -3.5, 4.0).Normalized;
        static readonly double DecayRateRad = new Vec3d(2.5, -3.5, 4.0).Length * MathUtil.Deg2Rad;
        const double DecayTau = 300.0;
        static readonly Vec3d SpinAxis = new Vec3d(0.3, -1.0, 0.6).Normalized;
        const double SpinRateRad = 4.0 * MathUtil.Deg2Rad;

        public readonly CircularOrbit Orbit;
        readonly List<Event> events = new List<Event>();
        readonly List<Segment> segments = new List<Segment>();
        bool dirty = true;
        int order;

        /// <summary>UTC seconds since J2000 at scenario time 0.</summary>
        public double EpochS { get { return Orbit.EpochS; } }
        public bool HasManualCommands { get; private set; }

        public SandboxSim(CircularOrbit orbit, bool withScript)
        {
            Orbit = orbit;
            if (withScript) LoadScript();
        }

        public static SandboxSim SlideScenario()
        {
            double epoch = AstroTime.SecondsSinceJ2000(SlideEpochUtc);
            return new SandboxSim(CircularOrbit.Create(AltitudeKm, InclinationDeg, SlideRaanDeg, SlideArgLatDeg, epoch), true);
        }

        /// <summary>Same orbit family (morning sun-synchronous) starting at any date.</summary>
        public static SandboxSim StartingAt(DateTime utc)
        {
            double epoch = AstroTime.SecondsSinceJ2000(utc);
            Vec3d sun = Ephemeris.SunEci(epoch, EciConvention.J2000);
            double raSun = Math.Atan2(sun.y, sun.x) * MathUtil.Rad2Deg;
            double raan = raSun + (LtanHours - 12.0) * 15.0;
            return new SandboxSim(CircularOrbit.Create(AltitudeKm, InclinationDeg, raan, SlideArgLatDeg, epoch), true);
        }

        void LoadScript()
        {
            AddEvent(600.0, SandboxCommand.DeployAntenna, true, double.NaN);
            AddEvent(900.0, SandboxCommand.AttitudeNadir, true, 300.0);
            AddEvent(1300.0, SandboxCommand.DeployWingMinusY, true, double.NaN);
            AddEvent(1400.0, SandboxCommand.DeployWingPlusY, true, double.NaN);
            AddEvent(4300.0, SandboxCommand.AttitudeRollImaging, true, 60.0);
            AddEvent(4740.0, SandboxCommand.AttitudeNadir, true, 60.0);
        }

        void AddEvent(double t, SandboxCommand cmd, bool scripted, double slew)
        {
            events.Add(new Event { T = t, Cmd = cmd, Scripted = scripted, Order = order++, Slew = slew });
            dirty = true;
            sortedDirty = true;
        }

        readonly List<Event> sortedEvents = new List<Event>();
        bool sortedDirty = true;

        List<Event> SortedEvents()
        {
            if (sortedDirty)
            {
                sortedEvents.Clear();
                sortedEvents.AddRange(events);
                sortedEvents.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Order.CompareTo(b.Order));
                sortedDirty = false;
            }
            return sortedEvents;
        }

        static bool IsAttitude(SandboxCommand c)
        {
            return c == SandboxCommand.AttitudeNadir || c == SandboxCommand.AttitudeSun ||
                   c == SandboxCommand.AttitudeRollImaging || c == SandboxCommand.AttitudeTumble;
        }

        /// <summary>Issue a command at scenario time t (seconds since the scenario start).</summary>
        public void Command(SandboxCommand cmd, double t)
        {
            if (IsAttitude(cmd))
            {
                // the operator takes over pointing: drop any later attitude plans
                events.RemoveAll(e => IsAttitude(e.Cmd) && e.T > t);
                sortedDirty = true;
            }
            AddEvent(t, cmd, false, double.NaN);
            HasManualCommands = true;
        }

        /// <summary>After a backwards time jump: commands issued later than t never happened.</summary>
        public void ForgetManualCommandsAfter(double t)
        {
            int removed = events.RemoveAll(e => !e.Scripted && e.T > t);
            if (removed > 0)
            {
                dirty = true;
                sortedDirty = true;
            }
            HasManualCommands = events.Exists(e => !e.Scripted);
        }

        // ------------------------------------------------------------------ attitude
        void Rebuild()
        {
            segments.Clear();
            segments.Add(new Segment { T0 = 0.0, Mode = Mode.TumbleDecay, Q0 = TumbleQ0, Slew = 0 });
            var att = events.FindAll(e => IsAttitude(e.Cmd));
            att.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Order.CompareTo(b.Order));
            dirty = false;   // Evaluate below must not recurse into Rebuild
            foreach (Event e in att)
            {
                QuatD q0 = Evaluate(e.T);
                Mode m = ModeOf(e.Cmd);
                double slew = 0;
                if (m != Mode.Tumble)
                {
                    if (!double.IsNaN(e.Slew)) slew = e.Slew;
                    else
                    {
                        double angDeg = QuatD.Angle(q0, Target(m, e.T)) * MathUtil.Rad2Deg;
                        slew = MathUtil.Clamp(angDeg / 1.5 + 10.0, 15.0, 200.0);
                    }
                }
                segments.Add(new Segment { T0 = e.T, Mode = m, Q0 = q0, Slew = slew });
            }
        }

        static Mode ModeOf(SandboxCommand c)
        {
            switch (c)
            {
                case SandboxCommand.AttitudeSun: return Mode.Sun;
                case SandboxCommand.AttitudeRollImaging: return Mode.Roll;
                case SandboxCommand.AttitudeTumble: return Mode.Tumble;
                default: return Mode.Nadir;
            }
        }

        QuatD Target(Mode m, double t)
        {
            Vec3d r, v;
            Orbit.StateAt(EpochS + t, out r, out v);
            QuatD nadir = Attitudes.Nadir(r, v);
            switch (m)
            {
                case Mode.Roll:
                    return nadir * QuatD.AxisAngle(Vec3d.UnitX, RollDegrees * MathUtil.Deg2Rad);
                case Mode.Sun:
                    Vec3d sun = Ephemeris.SunEci(EpochS + t, EciConvention.J2000);
                    return Attitudes.SunPointing(r, v, (sun - r).Normalized);
                default:
                    return nadir;
            }
        }

        QuatD Evaluate(double t)
        {
            Segment seg = segments[0];
            for (int k = segments.Count - 1; k >= 0; k--)
            {
                if (segments[k].T0 <= t)
                {
                    seg = segments[k];
                    break;
                }
            }
            double dt = Math.Max(0.0, t - seg.T0);
            switch (seg.Mode)
            {
                case Mode.TumbleDecay:
                {
                    double ang = DecayRateRad * DecayTau * (1.0 - Math.Exp(-dt / DecayTau));
                    return (seg.Q0 * QuatD.AxisAngle(DecayAxis, ang)).Normalized;
                }
                case Mode.Tumble:
                    return (seg.Q0 * QuatD.AxisAngle(SpinAxis, SpinRateRad * dt)).Normalized;
                default:
                {
                    QuatD target = Target(seg.Mode, t);
                    if (seg.Slew > 0 && dt < seg.Slew) return QuatD.Slerp(seg.Q0, target, MathUtil.SmoothStep01(dt / seg.Slew));
                    return target;
                }
            }
        }

        public QuatD AttitudeAt(double t)
        {
            if (dirty) Rebuild();
            return Evaluate(t);
        }

        /// <summary>Name of the pointing mode at time t, for the info panel.</summary>
        public string ModeName(double t)
        {
            if (dirty) Rebuild();
            Segment seg = segments[0];
            for (int k = segments.Count - 1; k >= 0; k--)
            {
                if (segments[k].T0 <= t)
                {
                    seg = segments[k];
                    break;
                }
            }
            bool slewing = seg.Slew > 0 && t - seg.T0 < seg.Slew;
            switch (seg.Mode)
            {
                case Mode.TumbleDecay: return "DETUMBLING (after separation)";
                case Mode.Tumble: return "TUMBLING (commanded)";
                case Mode.Sun: return slewing ? "SLEWING TO SUN" : "SUN POINTING";
                case Mode.Roll: return slewing ? "ROLLING 25 deg TO IMAGE" : "IMAGING (25 deg roll)";
                default: return slewing ? "TURNING TO FACE EARTH" : "NADIR (camera on Earth)";
            }
        }

        // ------------------------------------------------------------------ deployables
        void DeployStates(double t, out DeployInfo wingMy, out DeployInfo wingPy, out DeployInfo antenna)
        {
            double myT = double.NaN, pyT = double.NaN;
            bool antennaOut = false, jam = false;
            List<Event> sorted = SortedEvents();
            for (int k = 0; k < sorted.Count; k++)
            {
                Event e = sorted[k];
                if (e.T > t) break;
                switch (e.Cmd)
                {
                    case SandboxCommand.DeployWingMinusY: if (double.IsNaN(myT)) myT = e.T; break;
                    case SandboxCommand.DeployWingPlusY: if (double.IsNaN(pyT)) pyT = e.T; break;
                    case SandboxCommand.DeployAntenna: antennaOut = true; break;
                    case SandboxCommand.ToggleAntennaJam: jam = !jam; break;
                    case SandboxCommand.ResetDeployables:
                        myT = double.NaN;
                        pyT = double.NaN;
                        antennaOut = false;
                        jam = false;
                        break;
                }
            }
            wingMy = Wing(myT, t);
            wingPy = Wing(pyT, t);
            antenna = DeployInfo.Of(jam ? DeployState.Jammed : (antennaOut ? DeployState.Deployed : DeployState.Stowed),
                                    jam ? 0.35 : (antennaOut ? 1.0 : 0.0));
        }

        static DeployInfo Wing(double startT, double t)
        {
            if (double.IsNaN(startT)) return DeployInfo.Of(DeployState.Stowed, 0);
            double f = (t - startT) / WingDeploySeconds;
            if (f < 1.0) return DeployInfo.Of(DeployState.Deploying, Math.Max(0.0, f));
            return DeployInfo.Of(DeployState.Deployed, 1.0);
        }

        // ------------------------------------------------------------------ the frame
        /// <summary>The full state at scenario time t (seconds since the scenario start).</summary>
        public void Sample(double t, StateFrame f)
        {
            double s = EpochS + t;
            Vec3d r, v;
            Orbit.StateAt(s, out r, out v);
            f.Seq = (long)Math.Floor(Math.Max(0.0, t));
            f.SimTime = s;
            f.PosEciKm = r;
            f.HasVelocity = true;
            f.VelEciKmS = v;
            f.QBodyToEci = AttitudeAt(t);
            Vec3d sun = Ephemeris.SunEci(s, EciConvention.J2000);
            double lit = Ephemeris.SunlitFraction(r, sun);
            f.HasSun = true;
            f.SunDirEci = (sun - r).Normalized;
            f.HasEclipseFlag = true;
            f.InEclipse = lit < 0.5;
            f.SunlitFraction = lit;
            DeployInfo my, py, ant;
            DeployStates(t, out my, out py, out ant);
            f.WingMinusY = my;
            f.WingPlusY = py;
            f.Antenna = ant;
            f.BodyRateDegS = QuatD.Angle(AttitudeAt(t - 0.25), AttitudeAt(t + 0.25)) * MathUtil.Rad2Deg / 0.5;
            f.JumpFromPrevious = false;
        }
    }
}
