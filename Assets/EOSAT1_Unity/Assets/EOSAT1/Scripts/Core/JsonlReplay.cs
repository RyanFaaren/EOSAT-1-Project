// EOSAT-1 VR - plays a recording (one JSON object per line) as if it were arriving live.
// Works with the shipped one-orbit sample and with sessions recorded by the course's
// vr_client_example.py. Frames are delivered in file order, so a recording that contains
// a save-point reload replays that reload too.
using System;
using System.Collections.Generic;

namespace Eosat1.Core
{
    public sealed class JsonlReplay
    {
        readonly List<StateFrame> frames = new List<StateFrame>();
        int cursor;
        double nextDue;
        bool started;

        public double Speed = 1.0;
        public bool Loop = true;
        public bool Paused;
        public int ParseErrors { get; private set; }
        public int LinesRead { get; private set; }
        public string Hello { get; private set; }
        public int Count { get { return frames.Count; } }
        public int Cursor { get { return cursor; } }
        public double FirstTime { get { return frames.Count > 0 ? frames[0].SimTime : 0; } }
        public double LastTime { get { return frames.Count > 0 ? frames[frames.Count - 1].SimTime : 0; } }

        public static JsonlReplay FromText(string text)
        {
            var r = new JsonlReplay();
            if (string.IsNullOrEmpty(text)) return r;
            int pos = 0;
            while (pos < text.Length)
            {
                int nl = text.IndexOf('\n', pos);
                if (nl < 0) nl = text.Length;
                string line = text.Substring(pos, nl - pos).Trim();
                pos = nl + 1;
                if (line.Length == 0 || line[0] == '#') continue;
                r.LinesRead++;
                MessageKind kind;
                StateFrame f;
                string err;
                if (!FrameParser.TryParse(line, out kind, out f, out err))
                {
                    r.ParseErrors++;
                    continue;
                }
                if (kind == MessageKind.State && f != null) r.frames.Add(f);
                else if (kind == MessageKind.Hello && r.Hello == null) r.Hello = line;
            }
            return r;
        }

        public void Restart(double wallNow)
        {
            cursor = 0;
            nextDue = wallNow;
            started = true;
        }

        /// <summary>Jump to the first frame at or after a sim time (a time jump, like reloading a saved point).</summary>
        public void Seek(double simTime, double wallNow)
        {
            if (frames.Count == 0) return;
            int lo = 0, hi = frames.Count - 1;
            if (simTime <= frames[0].SimTime) hi = 0;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].SimTime < simTime) lo = mid + 1;
                else hi = mid;
            }
            cursor = hi;
            nextDue = wallNow;
            started = true;
        }

        /// <summary>Deliver every frame whose time has come. Returns how many were emitted.</summary>
        public int Update(double wallNow, Action<StateFrame> emit)
        {
            if (frames.Count == 0) return 0;
            if (!started) Restart(wallNow);
            if (Paused)
            {
                nextDue = wallNow;
                return 0;
            }
            double speed = Math.Max(0.01, Speed);
            int emitted = 0;
            while (wallNow >= nextDue && emitted < 2000)
            {
                if (cursor >= frames.Count)
                {
                    if (!Loop) return emitted;
                    cursor = 0;
                }
                StateFrame f = frames[cursor].Clone();
                f.ArrivalWall = nextDue;
                f.JumpFromPrevious = false;
                emit(f);
                emitted++;
                int next = cursor + 1;
                double gap = 1.0;
                if (next < frames.Count)
                {
                    double d = frames[next].SimTime - frames[cursor].SimTime;
                    gap = (d > 0 && d < 600) ? d / speed : 1.0;
                }
                cursor = next;
                nextDue += gap;
                if (nextDue < wallNow - 2.0) nextDue = wallNow;   // don't replay a long backlog
            }
            return emitted;
        }
    }
}
