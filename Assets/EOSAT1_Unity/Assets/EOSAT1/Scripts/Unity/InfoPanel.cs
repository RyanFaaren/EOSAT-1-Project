// EOSAT-1 VR - a small panel of text that answers the four questions from the course
// slides at a glance: Is it tumbling? Is the camera facing Earth? Are the wings in the
// sun? Did the antenna come out? Plus where it is, the time, and the link health.
// Desktop: drawn on screen (H toggles). Headset: a panel on the left controller (X toggles).
using System;
using System.Globalization;
using System.Text;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class InfoPanel : MonoBehaviour
    {
        public EosatApp app;
        [Tooltip("World-space text for the headset (shown on the left controller).")]
        public TextMesh worldText;
        public GameObject worldPanel;
        public bool showOnScreen = true;
        public bool showHelp = true;
        public float refreshInterval = 0.25f;

        public bool Visible { get; private set; }

        readonly StringBuilder sb = new StringBuilder(1024);
        string text = "";
        float nextRefresh;
        GUIStyle boxStyle, helpStyle;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
        }

        void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
        }

        void OnFontRebuilt(Font f)
        {
            if (worldText != null && f == worldText.font)
            {
                var r = worldText.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial != null) r.sharedMaterial.mainTexture = f.material.mainTexture;
            }
        }

        public void Toggle()
        {
            showOnScreen = !showOnScreen;
        }

        void Update()
        {
            bool headset = UnityEngine.XR.XRSettings.isDeviceActive;
            if (worldPanel != null && worldPanel.activeSelf != (headset && showOnScreen)) worldPanel.SetActive(headset && showOnScreen);
            Visible = showOnScreen;
            if (Time.unscaledTime < nextRefresh || app == null) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Build(app);
            if (worldText != null && headset) worldText.text = text;
        }

        void OnGUI()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive || app == null) return;
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                    fontSize = Mathf.Max(12, Screen.height / 60),
                    wordWrap = false,
                    padding = new RectOffset(10, 10, 8, 8),
                };
                boxStyle.normal.textColor = new Color(0.92f, 0.94f, 0.97f);
                helpStyle = new GUIStyle(boxStyle) { fontSize = Mathf.Max(11, Screen.height / 72) };
            }
            if (showOnScreen)
            {
                var content = new GUIContent(text);
                Vector2 size = boxStyle.CalcSize(content);
                GUI.Box(new Rect(10, 10, size.x, size.y), content, boxStyle);
            }
            if (showHelp)
            {
                string help = app.CanCommand
                    ? "Mouse drag orbit  wheel zoom  V views  F reset | Space pause  - = speed  <- -> jump  Backspace restart | " +
                      "1 2 wings  3 antenna  4 jam  5 stow | N nadir  S sun  R roll  T tumble | G axes  B boresight  L frame  M source  H panel"
                    : "Mouse drag orbit  wheel zoom  V views  F reset | " +
                      (app.CanControlTime ? "Space pause  - = speed  <- -> jump  Backspace restart | " : "") +
                      "G axes  B boresight  L frame  M source  H panel";
                var hc = new GUIContent(help);
                Vector2 hs = helpStyle.CalcSize(hc);
                GUI.Box(new Rect(10, Screen.height - hs.y - 10, Mathf.Min(hs.x, Screen.width - 20), hs.y), hc, helpStyle);
            }
            if (!string.IsNullOrEmpty(app.LastMessage) && Time.unscaledTime - app.LastMessageTime < 4f)
            {
                var mc = new GUIContent(app.LastMessage);
                Vector2 ms = helpStyle.CalcSize(mc);
                GUI.Box(new Rect((Screen.width - ms.x) * 0.5f, Screen.height * 0.12f, ms.x, ms.y), mc, helpStyle);
            }
        }

        // ------------------------------------------------------------------ text
        void Build(EosatApp a)
        {
            DisplayState s = a.State;
            sb.Length = 0;
            sb.Append("<b>EOSAT-1</b>  ");
            if (s.Valid) sb.Append(AstroTime.ToIso(s.SimTime).Replace('T', ' ').Replace("Z", " UTC"));
            else sb.Append("waiting for data");
            if (a.CanControlTime) sb.Append(a.paused ? "   <color=#ffcc55>PAUSED</color>" : "   x" + a.Speed.ToString("0.##", Inv));
            else if (s.Valid && s.SimRate > 1.5) sb.Append("   sim x" + s.SimRate.ToString("0", Inv));
            sb.Append('\n');
            AppendSource(a, s);
            if (!s.Valid)
            {
                text = sb.ToString();
                return;
            }

            // Is it tumbling?
            double rate = s.BodyRateDegS;
            sb.Append("Tumbling?        ");
            if (double.IsNaN(rate)) sb.Append("? (rate unknown at this data speed)");
            else if (rate > 0.5) sb.Append(Bad("YES")).Append("  ").Append(rate.ToString("0.0", Inv)).Append(" deg/s");
            else sb.Append(Good("no")).Append("  ").Append(rate.ToString("0.00", Inv)).Append(" deg/s");
            sb.Append('\n');

            // Is the camera facing Earth?
            Vec3d camEci = s.QBodyToEci.Rotate(Vec3d.UnitZ);
            double offNadir = Vec3d.Angle(camEci, -s.PosEciKm) * MathUtil.Rad2Deg;
            sb.Append("Camera on Earth? ");
            if (offNadir < 3.0) sb.Append(Good("YES"));
            else if (offNadir < 68.0) sb.Append(Warn("off-pointed"));
            else sb.Append(Bad("NO"));
            sb.Append("  ").Append(offNadir.ToString("0.0", Inv)).Append(" deg off nadir\n");

            // Are the wings in the sun?
            Vec3d sun = s.HasSun ? s.SunDirEci : Vec3d.Zero;
            bool eclipse = a.space != null ? a.space.Illumination < 0.5 : s.InEclipse;
            sb.Append("Wings in sun?    ");
            if (eclipse) sb.Append(Warn("ECLIPSE")).Append(" (Earth's shadow)  ");
            AppendWing("-Y", s.WingMinusY, -1, s, sun, eclipse);
            sb.Append("  ");
            AppendWing("+Y", s.WingPlusY, +1, s, sun, eclipse);
            sb.Append('\n');

            // Did the antenna come out?
            sb.Append("Antenna out?     ");
            if (!s.Antenna.Present) sb.Append("?");
            else if (s.Antenna.State == DeployState.Jammed) sb.Append(Bad("JAMMED"));
            else if (s.Antenna.State == DeployState.Deployed) sb.Append(Good("DEPLOYED"));
            else sb.Append(Warn(s.Antenna.Label));
            sb.Append('\n');

            // Where is it?
            QuatD toEcef = AstroTime.EcefToEci(s.SimTime, a.space != null ? a.space.eciConvention : EciConvention.J2000).Conjugate;
            Vec3d e = toEcef.Rotate(s.PosEciKm);
            double lat = Math.Asin(MathUtil.Clamp(e.z / e.Length, -1, 1)) * MathUtil.Rad2Deg;
            double lon = Math.Atan2(e.y, e.x) * MathUtil.Rad2Deg;
            double alt = s.PosEciKm.Length - Ephemeris.EarthRadiusKm;
            sb.Append("Over             ").Append(Math.Abs(lat).ToString("0.0", Inv)).Append(lat >= 0 ? " N  " : " S  ")
              .Append(Math.Abs(lon).ToString("0.0", Inv)).Append(lon >= 0 ? " E" : " W")
              .Append("   alt ").Append(alt.ToString("0", Inv)).Append(" km");
            if (a.ActiveFeed == FeedKind.Sandbox && a.Sandbox != null)
                sb.Append('\n').Append("Mode             ").Append(a.Sandbox.ModeName(a.SandboxTime));
            text = sb.ToString();
        }

        void AppendSource(EosatApp a, DisplayState s)
        {
            switch (a.ActiveFeed)
            {
                case FeedKind.Sandbox:
                    sb.Append("Source  SANDBOX (built-in simulator, takes commands)\n");
                    break;
                case FeedKind.Replay:
                    sb.Append("Source  REPLAY");
                    if (a.Replay != null)
                        sb.Append("  frame ").Append(a.Replay.Cursor).Append('/').Append(a.Replay.Count);
                    AppendTimelineStats(a.Timeline);
                    sb.Append('\n');
                    break;
                case FeedKind.Tcp:
                    TcpFeedClient l = a.Link;
                    sb.Append("Source  LIVE ").Append(a.tcpHost).Append(':').Append(a.tcpPort).Append("  ");
                    if (l == null) sb.Append(Bad("stopped"));
                    else if (!l.Connected) sb.Append(Bad("not connected"));
                    else if (s.Valid && s.DataAge > 2.0) sb.Append(Warn("STALE " + s.DataAge.ToString("0", Inv) + " s"));
                    else sb.Append(Good("connected"));
                    if (l != null)
                    {
                        sb.Append("  seq ").Append(l.LastSeq).Append("  skipped ").Append(l.ParseErrors);
                        sb.Append("  status 0x").Append(l.ComputeStatusWord().ToString("X4", Inv));
                    }
                    AppendTimelineStats(a.Timeline);
                    sb.Append('\n');
                    break;
            }
        }

        void AppendTimelineStats(FrameTimeline t)
        {
            if (t == null) return;
            if (t.Duplicates > 0) sb.Append("  dup ").Append(t.Duplicates);
            if (t.BackwardJumps > 0) sb.Append("  reloads ").Append(t.BackwardJumps);
            if (t.ForwardJumps > 0) sb.Append("  jumps ").Append(t.ForwardJumps);
        }

        void AppendWing(string name, DeployInfo w, int side, DisplayState s, Vec3d sunEci, bool eclipse)
        {
            sb.Append(name).Append(' ');
            if (!w.Present)
            {
                sb.Append('?');
                return;
            }
            if (w.State == DeployState.Stowed) sb.Append("stowed");
            else if (w.State == DeployState.Deployed) sb.Append("out");
            else sb.Append(w.Label.ToLowerInvariant());
            if (eclipse || !s.HasSun) return;
            Vec3d cellNormal;
            if (app == null || app.satelliteView == null || !app.satelliteView.TryGetCellNormalBody(side, out cellNormal))
                cellNormal = WingCellNormalBody(side, w.EffectiveFraction);
            double cosInc = Vec3d.Dot(s.QBodyToEci.Rotate(cellNormal), sunEci);
            if (cosInc > 0.05) sb.Append(' ').Append(Good("lit " + Math.Round(cosInc * 100).ToString(Inv) + "%"));
            else sb.Append(' ').Append(Warn("cells face away"));
        }

        /// <summary>
        /// Fallback when the model can't be asked (normally SatelliteView reads the panel itself):
        /// EOS SAT-1 panels lie on the two rear faces of the diamond-shaped body (cells facing
        /// out at +/-140.9 deg from +X) and fold out about the rear ridge by 39.1 deg each, until
        /// both face -X.
        /// </summary>
        public static Vec3d WingCellNormalBody(int side, double fraction)
        {
            double deg = side * (140.89 + 39.11 * MathUtil.Clamp01(fraction));
            double a = deg * MathUtil.Deg2Rad;
            return new Vec3d(Math.Cos(a), Math.Sin(a), 0);
        }

        static string Good(string s) { return "<color=#7CFC9A>" + s + "</color>"; }
        static string Warn(string s) { return "<color=#FFD166>" + s + "</color>"; }
        static string Bad(string s) { return "<color=#FF6B6B>" + s + "</color>"; }
    }
}
