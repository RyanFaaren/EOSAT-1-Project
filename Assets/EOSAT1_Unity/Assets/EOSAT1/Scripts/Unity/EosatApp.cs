// EOSAT-1 VR - the conductor. Picks the data source, keeps time, takes commands,
// and hands one DisplayState per rendered frame to the scene, the spacecraft and the
// info panel.
//
// Data sources (course slide 22):
//   Sandbox - built-in simulator flying the slide-23 orbit; takes commands. No network.
//   Replay  - a recording, one JSON object per line (the shipped sample orbit, or a
//             session recorded with vr_client_example.py).
//   Tcp     - the live link: vr_stub.py on a laptop, or the real simulator, port 8100.
//
// On a headset you can change the link without rebuilding: put eosat1_link.json in the
// app's persistent data folder, e.g. {"feed":"tcp","host":"192.168.1.20","port":8100}.
// Desktop builds also take -eosatFeed tcp -eosatHost 192.168.1.20 -eosatPort 8100.
using System;
using System.Collections.Generic;
using System.IO;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public enum FeedKind { Sandbox = 0, Replay = 1, Tcp = 2 }

    public enum SandboxStart { SlideScenario = 0, Now = 1, CustomUtc = 2 }

    [DefaultExecutionOrder(-50)]
    public sealed class EosatApp : MonoBehaviour
    {
        // own status bits (8-15) reported to the simulator
        public const int BitHeadsetActive = 1 << 8;
        public const int BitInterpolating = 1 << 9;
        public const int BitJumpMode = 1 << 10;
        public const int BitEclipseShown = 1 << 11;
        public const int BitJamShown = 1 << 12;
        public const int BitPanelShown = 1 << 13;

        [Header("Data source")]
        public FeedKind feed = FeedKind.Sandbox;

        [Header("Live link (simulator or vr_stub.py)")]
        public string tcpHost = "127.0.0.1";
        public int tcpPort = 8100;
        public bool sendStatus = true;

        [Header("Recording (Replay)")]
        [Tooltip("JSON Lines recording. The shipped one-orbit sample is eosat1_one_orbit.jsonl.txt.")]
        public TextAsset recording;
        [Tooltip("Optional file path; used instead of the TextAsset when it exists.")]
        public string recordingPath = "";
        public bool loopRecording = true;

        [Header("Sandbox")]
        public SandboxStart sandboxStart = SandboxStart.SlideScenario;
        [Tooltip("Used with CustomUtc, e.g. 2026-10-16T14:30:00Z")]
        public string customStartUtc = "2026-10-16T12:00:00Z";

        [Header("Time (Sandbox and Replay)")]
        public float[] speedSteps = { 0.5f, 1f, 2f, 5f, 10f, 30f, 60f, 120f, 300f };
        public int speedIndex = 1;
        public bool paused;

        [Header("Scene")]
        public SpaceScene space;
        public SatelliteView satelliteView;
        public InfoPanel infoPanel;

        public DisplayState State { get { return state; } }
        public FeedKind ActiveFeed { get { return active; } }
        public float Speed { get { return speedSteps.Length == 0 ? 1f : speedSteps[Mathf.Clamp(speedIndex, 0, speedSteps.Length - 1)]; } }
        public bool CanControlTime { get { return active != FeedKind.Tcp; } }
        public bool CanCommand { get { return active == FeedKind.Sandbox; } }
        public TcpFeedClient Link { get { return tcp; } }
        public FrameTimeline Timeline { get { return timeline; } }
        public JsonlReplay Replay { get { return replay; } }
        public SandboxSim Sandbox { get { return sandbox; } }
        public double SandboxTime { get { return sandboxT; } }
        public string LastMessage { get; private set; }
        public float LastMessageTime { get; private set; }

        readonly DisplayState state = new DisplayState();
        readonly FrameTimeline timeline = new FrameTimeline();
        readonly StateFrame sandboxFrame = new StateFrame();
        TcpFeedClient tcp;
        JsonlReplay replay;
        SandboxSim sandbox;
        double sandboxT;
        int sandboxDiscontinuity;
        FeedKind active;
        bool started;
        Action<StateFrame> pushToTimeline;

        void Awake()
        {
            Application.runInBackground = true;
            pushToTimeline = f => timeline.Push(f);
            ApplyLinkOverrides();
        }

        void Start()
        {
            SetFeed(feed);
            started = true;
        }

        void OnEnable()
        {
            if (started && active == FeedKind.Tcp && tcp == null) StartLink();
        }

        void OnDisable()
        {
            StopLink();
        }

        void OnApplicationQuit()
        {
            StopLink();
        }

        // ------------------------------------------------------------------ feeds
        public void CycleFeed()
        {
            SetFeed((FeedKind)(((int)active + 1) % 3));
        }

        public void SetFeed(FeedKind kind)
        {
            if (started && kind == active && (kind != FeedKind.Tcp || tcp != null)) return;
            if (active == FeedKind.Tcp && kind != FeedKind.Tcp) StopLink();
            active = kind;
            feed = kind;
            timeline.Clear();
            if (space != null) space.ResetHistory();
            state.Valid = false;
            switch (kind)
            {
                case FeedKind.Sandbox:
                    if (sandbox == null) CreateSandbox();
                    sandboxDiscontinuity++;
                    Say("Sandbox: built-in simulator (" + sandboxStart + ")");
                    break;
                case FeedKind.Replay:
                    if (replay == null) LoadRecording();
                    if (replay != null) replay.Restart(MonoClock.Now);
                    break;
                case FeedKind.Tcp:
                    StartLink();
                    break;
            }
        }

        void StartLink()
        {
            StopLink();
            tcp = new TcpFeedClient { Host = tcpHost, Port = tcpPort, SendStatus = sendStatus };
            tcp.Start();
            Say("Live link: connecting to " + tcpHost + ":" + tcpPort);
        }

        void StopLink()
        {
            if (tcp != null)
            {
                tcp.Stop();
                tcp = null;
            }
        }

        void CreateSandbox()
        {
            switch (sandboxStart)
            {
                case SandboxStart.Now:
                    sandbox = SandboxSim.StartingAt(DateTime.UtcNow);
                    break;
                case SandboxStart.CustomUtc:
                    double s;
                    sandbox = Iso8601.TryParse(customStartUtc, out s)
                        ? SandboxSim.StartingAt(AstroTime.ToDateTimeUtc(s))
                        : SandboxSim.SlideScenario();
                    break;
                default:
                    sandbox = SandboxSim.SlideScenario();
                    break;
            }
            sandboxT = 0;
        }

        void LoadRecording()
        {
            string text = null;
            string source = null;
            try
            {
                if (!string.IsNullOrEmpty(recordingPath) && File.Exists(recordingPath))
                {
                    text = File.ReadAllText(recordingPath);
                    source = recordingPath;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("EOSAT-1: could not read " + recordingPath + ": " + e.Message);
            }
            if (text == null && recording != null)
            {
                text = recording.text;
                source = recording.name;
            }
            if (text == null)
            {
                Say("Replay: no recording assigned");
                return;
            }
            replay = JsonlReplay.FromText(text);
            replay.Loop = loopRecording;
            Say("Replay: " + source + " (" + replay.Count + " frames, " + replay.ParseErrors + " unreadable lines)");
        }

        // ------------------------------------------------------------------ time
        public void SpeedUp() { SetSpeedIndex(speedIndex + 1); }
        public void SlowDown() { SetSpeedIndex(speedIndex - 1); }

        public void SetSpeedIndex(int i)
        {
            if (!CanControlTime)
            {
                Say("Time is set by the simulator");
                return;
            }
            speedIndex = Mathf.Clamp(i, 0, speedSteps.Length - 1);
            Say("Speed x" + Speed);
        }

        /// <summary>VR-friendly: 1x -> 10x -> 60x -> 1x.</summary>
        public void CycleSpeedCoarse()
        {
            float s = Speed;
            float next = s < 9f ? 10f : (s < 59f ? 60f : 1f);
            int best = 0;
            for (int i = 0; i < speedSteps.Length; i++)
                if (Mathf.Abs(speedSteps[i] - next) < Mathf.Abs(speedSteps[best] - next)) best = i;
            SetSpeedIndex(best);
        }

        public void TogglePause()
        {
            if (!CanControlTime)
            {
                Say("Time is set by the simulator");
                return;
            }
            paused = !paused;
            Say(paused ? "Paused" : "Running");
        }

        /// <summary>Jump sim time forwards or backwards (backwards = like reloading a saved point).</summary>
        public void JumpSeconds(double seconds)
        {
            if (active == FeedKind.Sandbox && sandbox != null)
            {
                sandboxT = Math.Max(0.0, sandboxT + seconds);
                if (seconds < 0) sandbox.ForgetManualCommandsAfter(sandboxT);
                sandboxDiscontinuity++;
                Say((seconds > 0 ? "Jumped +" : "Jumped ") + seconds + " s");
            }
            else if (active == FeedKind.Replay && replay != null)
            {
                replay.Seek(timeline.DisplayTime + seconds, MonoClock.Now);
                Say((seconds > 0 ? "Jumped +" : "Jumped ") + seconds + " s");
            }
            else Say("Time is set by the simulator");
        }

        public void Restart()
        {
            if (active == FeedKind.Sandbox)
            {
                CreateSandbox();
                sandboxDiscontinuity++;
                Say("Scenario restarted");
            }
            else if (active == FeedKind.Replay && replay != null)
            {
                replay.Restart(MonoClock.Now);
                Say("Recording restarted");
            }
            else Say("Restart the simulator from the instructor console");
        }

        // ------------------------------------------------------------------ commands (sandbox)
        public void Command(SandboxCommand cmd)
        {
            if (!CanCommand || sandbox == null)
            {
                Say("Commands go through mission control (switch to Sandbox to command locally)");
                return;
            }
            sandbox.Command(cmd, sandboxT);
            Say("Command: " + cmd);
        }

        public void DeployNextWing()
        {
            if (!state.Valid) return;
            if (state.WingMinusY.State == DeployState.Stowed || !state.WingMinusY.Present) Command(SandboxCommand.DeployWingMinusY);
            else if (state.WingPlusY.State == DeployState.Stowed || !state.WingPlusY.Present) Command(SandboxCommand.DeployWingPlusY);
            else Say("Both wings are already out");
        }

        public void DeployAntennaOrClearJam()
        {
            if (state.Valid && state.Antenna.State == DeployState.Jammed) Command(SandboxCommand.ToggleAntennaJam);
            else Command(SandboxCommand.DeployAntenna);
        }

        // ------------------------------------------------------------------ overlays
        public void ToggleAxes()
        {
            if (satelliteView == null) return;
            satelliteView.AxesVisible = !satelliteView.AxesVisible;
        }

        public void ToggleBoresight()
        {
            if (satelliteView == null) return;
            satelliteView.BoresightVisible = !satelliteView.BoresightVisible;
        }

        public void ToggleViewFrame()
        {
            if (space == null) return;
            space.viewFrame = space.viewFrame == SpaceScene.ViewFrame.EarthBelow
                ? SpaceScene.ViewFrame.Inertial : SpaceScene.ViewFrame.EarthBelow;
            Say(space.viewFrame == SpaceScene.ViewFrame.EarthBelow
                ? "View: Earth below, flight ahead" : "View: inertial (ECI axes, up = celestial north)");
        }

        public void Say(string message)
        {
            LastMessage = message;
            LastMessageTime = Time.unscaledTime;
            Debug.Log("EOSAT-1: " + message);
        }

        // ------------------------------------------------------------------ the loop
        void Update()
        {
            double now = MonoClock.Now;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            switch (active)
            {
                case FeedKind.Sandbox:
                    if (sandbox == null) CreateSandbox();
                    if (!paused) sandboxT += dt * Speed;
                    sandbox.Sample(sandboxT, sandboxFrame);
                    state.CopyFrom(sandboxFrame);
                    state.Discontinuity = sandboxDiscontinuity;
                    state.Interpolating = false;
                    state.DataAge = 0;
                    state.SimRate = paused ? 0 : Speed;
                    break;
                case FeedKind.Replay:
                    if (replay != null)
                    {
                        replay.Speed = Speed;
                        replay.Paused = paused;
                        replay.Update(now, pushToTimeline);
                    }
                    timeline.Advance(paused ? 0 : dt);
                    timeline.Sample(state, now);
                    break;
                case FeedKind.Tcp:
                    if (tcp != null)
                    {
                        StateFrame f;
                        int guard = 0;
                        while (guard++ < 500 && tcp.TryDequeue(out f)) timeline.Push(f);
                        string line;
                        while (tcp.TryDequeueLog(out line)) Say("link: " + line);
                    }
                    timeline.Advance(dt);
                    timeline.Sample(state, now);
                    break;
            }

            if (state.Valid)
            {
                if (space != null) space.Apply(state, dt);
                if (satelliteView != null) satelliteView.Apply(state, dt);
                if (space != null && satelliteView != null) space.FootprintEnabled = satelliteView.BoresightVisible;
            }

            if (tcp != null) tcp.OwnStatusBits = OwnBits();
        }

        void LateUpdate()
        {
            if (tcp != null) tcp.Heartbeat();
        }

        int OwnBits()
        {
            int bits = 0;
            if (UnityEngine.XR.XRSettings.isDeviceActive) bits |= BitHeadsetActive;
            if (state.Valid && state.Interpolating) bits |= BitInterpolating;
            FrameTimelineNewestJump(ref bits);
            if (space != null && space.Illumination < 0.5) bits |= BitEclipseShown;
            if (state.Valid && state.Antenna.State == DeployState.Jammed) bits |= BitJamShown;
            if (infoPanel != null && infoPanel.Visible) bits |= BitPanelShown;
            return bits;
        }

        void FrameTimelineNewestJump(ref int bits)
        {
            StateFrame n = timeline.Newest;
            if (n != null && n.JumpFromPrevious) bits |= BitJumpMode;
        }

        // ------------------------------------------------------------------ headset-side configuration
        void ApplyLinkOverrides()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "eosat1_link.json");
                if (File.Exists(path))
                {
                    object root;
                    string err;
                    if (MiniJson.TryParse(File.ReadAllText(path), out root, out err))
                    {
                        var d = root as Dictionary<string, object>;
                        string s;
                        double n;
                        if (MiniJson.TryString(d, "host", out s)) tcpHost = s;
                        if (MiniJson.TryNumber(d, "port", out n)) tcpPort = (int)n;
                        if (MiniJson.TryString(d, "feed", out s)) feed = ParseFeed(s, feed);
                        if (MiniJson.TryString(d, "recording", out s)) recordingPath = s;
                        Debug.Log("EOSAT-1: link settings from " + path);
                    }
                    else Debug.LogWarning("EOSAT-1: " + path + " is not valid JSON (" + err + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("EOSAT-1: could not read link settings: " + e.Message);
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-eosatHost") tcpHost = args[i + 1];
                else if (args[i] == "-eosatPort")
                {
                    int p;
                    if (int.TryParse(args[i + 1], out p)) tcpPort = p;
                }
                else if (args[i] == "-eosatFeed") feed = ParseFeed(args[i + 1], feed);
                else if (args[i] == "-eosatRecording") recordingPath = args[i + 1];
            }
        }

        static FeedKind ParseFeed(string s, FeedKind fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (s.Trim().ToLowerInvariant())
            {
                case "sandbox": return FeedKind.Sandbox;
                case "replay": return FeedKind.Replay;
                case "tcp":
                case "live": return FeedKind.Tcp;
                default: return fallback;
            }
        }
    }
}
