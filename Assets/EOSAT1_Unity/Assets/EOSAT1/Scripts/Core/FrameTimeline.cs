// EOSAT-1 VR - turns a ~1 Hz stream of frames into smooth motion.
//
// Course rules this implements (slide 24):
//   * One frame a second: interpolate between frames.
//   * When time is sped up: jump instead (frames far apart in sim time are not interpolated).
//   * Time jumps: the clock can go backwards when a saved point is reloaded -> start over.
//   * Duplicates and stale frames are dropped.
//
// The display runs about one frame interval behind the newest frame, so there is
// always a frame on each side to interpolate between. If the stream stalls, the
// picture holds on the last frame instead of guessing.
using System;
using System.Collections.Generic;

namespace Eosat1.Core
{
    /// <summary>Everything the scene needs for one rendered frame.</summary>
    public sealed class DisplayState
    {
        public bool Valid;
        public double SimTime;
        public long Seq = -1;
        public Vec3d PosEciKm;
        public bool HasVelocity;
        /// <summary>True when the velocity was worked out from positions rather than sent by the source.</summary>
        public bool VelocityEstimated;
        public Vec3d VelEciKmS;
        public QuatD QBodyToEci = QuatD.Identity;
        public bool HasSun;
        public Vec3d SunDirEci;
        public bool InEclipse;
        public double SunlitFraction = double.NaN;
        public DeployInfo WingMinusY;
        public DeployInfo WingPlusY;
        public DeployInfo Antenna;
        public double BodyRateDegS = double.NaN;

        /// <summary>Changes whenever the picture should snap instead of animate (time jump, restart).</summary>
        public int Discontinuity;
        public bool Interpolating;
        /// <summary>Wall-clock seconds since the newest frame arrived.</summary>
        public double DataAge;
        /// <summary>Sim seconds per wall second, as observed.</summary>
        public double SimRate = 1.0;

        public void CopyFrom(StateFrame f)
        {
            Valid = true;
            SimTime = f.SimTime;
            Seq = f.Seq;
            PosEciKm = f.PosEciKm;
            HasVelocity = f.HasVelocity;
            VelocityEstimated = false;
            VelEciKmS = f.VelEciKmS;
            QBodyToEci = f.QBodyToEci;
            HasSun = f.HasSun;
            SunDirEci = f.SunDirEci;
            InEclipse = f.InEclipse;
            SunlitFraction = f.SunlitFraction;
            WingMinusY = f.WingMinusY;
            WingPlusY = f.WingPlusY;
            Antenna = f.Antenna;
            BodyRateDegS = f.BodyRateDegS;
        }
    }

    public sealed class FrameTimeline
    {
        /// <summary>Frames further apart than this (sim seconds) are shown as a jump, not interpolated.</summary>
        public double MaxInterpolationGap = 6.0;
        /// <summary>Sim time going back by more than this is treated as a reload.</summary>
        public double BackwardJumpTolerance = 0.5;
        public int Capacity = 32;

        readonly List<StateFrame> frames = new List<StateFrame>();
        double displayT;
        bool hasDisplay;
        double simRate = 1.0;
        double lastArrival = double.NaN;
        StateFrame lastAccepted;

        public long Received, Duplicates, OutOfOrder, BackwardJumps, ForwardJumps, Resyncs;
        public int Discontinuity { get; private set; }
        public double SimRate { get { return simRate; } }
        public double LastArrival { get { return lastArrival; } }
        public int Count { get { return frames.Count; } }
        public double DisplayTime { get { return displayT; } }
        public StateFrame Newest { get { return frames.Count > 0 ? frames[frames.Count - 1] : null; } }

        public void Clear()
        {
            frames.Clear();
            hasDisplay = false;
            lastAccepted = null;
            lastArrival = double.NaN;
            simRate = 1.0;
            Discontinuity++;
        }

        public void Push(StateFrame f)
        {
            if (f == null) return;
            Received++;
            StateFrame last = lastAccepted;
            if (last != null)
            {
                double dt = f.SimTime - last.SimTime;
                if (f.Seq >= 0 && f.Seq == last.Seq && Math.Abs(dt) < 1e-6)
                {
                    Duplicates++;
                    return;
                }
                if (dt < -BackwardJumpTolerance)
                {
                    // Saved point reloaded (or the recording looped): forget the past.
                    BackwardJumps++;
                    frames.Clear();
                    CarryOver(last, f);
                    frames.Add(f);
                    lastAccepted = f;
                    lastArrival = f.ArrivalWall;
                    displayT = f.SimTime;
                    hasDisplay = true;
                    simRate = 1.0;
                    Discontinuity++;
                    return;
                }
                if (dt <= 1e-6)
                {
                    if (dt > -1e-6) Duplicates++;
                    else OutOfOrder++;
                    return;
                }
                CarryOver(last, f);
                double wallGap = f.ArrivalWall - lastArrival;
                if (!double.IsNaN(lastArrival) && wallGap > 0.02)
                {
                    double r = dt / wallGap;
                    if (r > simRate * 3.0 || r < simRate / 3.0) simRate = r;
                    else simRate += (r - simRate) * 0.3;
                    simRate = MathUtil.Clamp(simRate, 0.01, 100000.0);
                }
                if (dt > MaxInterpolationGap)
                {
                    f.JumpFromPrevious = true;
                    ForwardJumps++;
                }
            }
            frames.Add(f);
            while (frames.Count > Capacity) frames.RemoveAt(0);
            lastAccepted = f;
            lastArrival = f.ArrivalWall;
            if (!hasDisplay)
            {
                displayT = f.SimTime;
                hasDisplay = true;
                Discontinuity++;
            }
            else if (f.JumpFromPrevious)
            {
                displayT = f.SimTime;
                Discontinuity++;
            }
        }

        /// <summary>Fields a frame left out keep their previous values.</summary>
        static void CarryOver(StateFrame prev, StateFrame f)
        {
            if (!f.WingMinusY.Present) f.WingMinusY = prev.WingMinusY;
            if (!f.WingPlusY.Present) f.WingPlusY = prev.WingPlusY;
            if (!f.Antenna.Present) f.Antenna = prev.Antenna;
        }

        /// <summary>Advance the display clock by a wall-clock step.</summary>
        public void Advance(double dtWall)
        {
            int n = frames.Count;
            if (n == 0 || !hasDisplay) return;
            StateFrame newest = frames[n - 1];
            if (n == 1 || newest.JumpFromPrevious)
            {
                displayT = newest.SimTime;
                return;
            }
            double gap = newest.SimTime - frames[n - 2].SimTime;
            double lag = MathUtil.Clamp(gap, 0.0, MaxInterpolationGap);
            double target = newest.SimTime - lag;
            displayT += dtWall * simRate;
            double err = target - displayT;
            if (Math.Abs(err) > Math.Max(2.0, 3.0 * lag))
            {
                displayT = target;          // way off (e.g. after a stall): resync
                Resyncs++;
            }
            else
            {
                displayT += err * Math.Min(1.0, dtWall * 0.8);
            }
            if (displayT > newest.SimTime) displayT = newest.SimTime;
            if (displayT < frames[0].SimTime) displayT = frames[0].SimTime;
        }

        /// <summary>Fill <paramref name="s"/> with the interpolated state at the display time.</summary>
        public bool Sample(DisplayState s, double wallNow)
        {
            int n = frames.Count;
            if (n == 0)
            {
                s.Valid = false;
                return false;
            }
            int j = 0;
            for (int k = n - 1; k >= 0; k--)
            {
                if (frames[k].SimTime <= displayT)
                {
                    j = k;
                    break;
                }
            }
            StateFrame a = frames[j];
            StateFrame b = (j + 1 < n && !frames[j + 1].JumpFromPrevious) ? frames[j + 1] : null;
            s.CopyFrom(a);
            s.Interpolating = false;
            if (b != null && displayT > a.SimTime)
            {
                double span = b.SimTime - a.SimTime;
                double t = span > 1e-9 ? MathUtil.Clamp01((displayT - a.SimTime) / span) : 0.0;
                s.Interpolating = t > 0 && t < 1;
                s.SimTime = a.SimTime + span * t;
                s.PosEciKm = Vec3d.Lerp(a.PosEciKm, b.PosEciKm, t);
                s.QBodyToEci = QuatD.Slerp(a.QBodyToEci, b.QBodyToEci, t);
                if (a.HasSun && b.HasSun)
                {
                    s.SunDirEci = Vec3d.Lerp(a.SunDirEci, b.SunDirEci, t).Normalized;
                    s.HasSun = true;
                }
                if (t >= 0.5)
                {
                    s.InEclipse = b.InEclipse;
                    s.Seq = b.Seq;
                    s.Antenna = b.Antenna;
                }
                if (!double.IsNaN(a.SunlitFraction) && !double.IsNaN(b.SunlitFraction))
                    s.SunlitFraction = a.SunlitFraction + (b.SunlitFraction - a.SunlitFraction) * t;
                s.WingMinusY = LerpDeploy(a.WingMinusY, b.WingMinusY, t);
                s.WingPlusY = LerpDeploy(a.WingPlusY, b.WingPlusY, t);
                if (a.HasVelocity && b.HasVelocity)
                {
                    s.VelEciKmS = Vec3d.Lerp(a.VelEciKmS, b.VelEciKmS, t);
                    s.HasVelocity = true;
                }
                else if (span > 0 && span <= 120.0)
                {
                    s.VelEciKmS = (b.PosEciKm - a.PosEciKm) / span;
                    s.HasVelocity = true;
                    s.VelocityEstimated = true;
                }
                if (double.IsNaN(s.BodyRateDegS) && span > 0 && span <= 3.0)
                    s.BodyRateDegS = QuatD.Angle(a.QBodyToEci, b.QBodyToEci) * MathUtil.Rad2Deg / span;
            }
            else if (j > 0 && !a.JumpFromPrevious)
            {
                StateFrame p = frames[j - 1];
                double span = a.SimTime - p.SimTime;
                if (!s.HasVelocity && span > 0 && span <= 120.0)
                {
                    s.VelEciKmS = (a.PosEciKm - p.PosEciKm) / span;
                    s.HasVelocity = true;
                    s.VelocityEstimated = true;
                }
                if (double.IsNaN(s.BodyRateDegS) && span > 0 && span <= 3.0)
                    s.BodyRateDegS = QuatD.Angle(p.QBodyToEci, a.QBodyToEci) * MathUtil.Rad2Deg / span;
            }
            s.Discontinuity = Discontinuity;
            s.DataAge = double.IsNaN(lastArrival) ? double.PositiveInfinity : Math.Max(0.0, wallNow - lastArrival);
            s.SimRate = simRate;
            return true;
        }

        static DeployInfo LerpDeploy(DeployInfo a, DeployInfo b, double t)
        {
            if (!a.Present) return b;
            if (!b.Present) return a;
            DeployInfo r = t >= 0.5 ? b : a;
            r.Fraction = a.EffectiveFraction + (b.EffectiveFraction - a.EffectiveFraction) * t;
            if (r.State == DeployState.Stowed && r.Fraction > 0.0) r.State = DeployState.Deploying;
            if (r.State == DeployState.Deployed && r.Fraction < 1.0) r.State = DeployState.Deploying;
            return r;
        }
    }
}
