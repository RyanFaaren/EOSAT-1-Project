// EOSAT-1 VR - puts the Earth, Sun, Moon and stars where they really are, as seen from
// the spacecraft, and turns the spacecraft exactly as the simulator says.
//
// The spacecraft stays at this object's origin, life size. Everything else is placed by
// direction and shrunk around the viewer: 1 km of the real world = metresPerKm Unity
// metres (default 1 m). Shrinking around the viewer keeps every angle exact, so the
// Earth fills the same part of the sky it would from 500 km, its edge sits 22 deg below
// the horizontal, the Sun and Moon are their true 0.5 deg across, and with the Earth's
// surface ~500 m away stereo vision still reads it as "very far".
//
// View frame "Earth below" (default): Unity down = towards Earth's centre, Unity forward
// (+Z) = flight direction, so the viewer rides behind the spacecraft with the planet under
// their feet. "Inertial" keeps the ECI axes fixed (Unity up = celestial north), handy for
// checking the axis conversion.
using System;
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    [ExecuteAlways]
    public sealed class SpaceScene : MonoBehaviour
    {
        public enum ViewFrame { EarthBelow = 0, Inertial = 1 }

        [Header("Scene objects (children of this transform)")]
        public Transform satellite;
        [Tooltip("Unit-radius sphere, Earth-fixed axes (lon 0 on local +X, north on +Y).")]
        public Transform earth;
        public Transform moon;
        [Tooltip("Unit-radius star sphere in J2000 axes.")]
        public Transform stars;
        [Tooltip("Camera-facing quad; its material's _DiscRadius sets the Sun's size within it.")]
        public Transform sunDisc;
        public Light sunLight;
        public Light earthshineLight;
        [Tooltip("Ring marking where the camera boresight meets the Earth.")]
        public Transform footprint;
        public Camera[] cameras = new Camera[0];

        [Header("Frames")]
        public ViewFrame viewFrame = ViewFrame.EarthBelow;
        [Tooltip("Which inertial frame the simulator's ECI vectors are in. Check the ICD; J2000 is the usual choice.")]
        public EciConvention eciConvention = EciConvention.J2000;

        [Header("Scale")]
        [Tooltip("Unity metres per real kilometre for the Earth, Moon and Sun (the spacecraft stays life size).")]
        public float metresPerKm = 1.0f;
        [Tooltip("Sun, Moon and stars are drawn just beyond the Earth's horizon, at this multiple of its distance.")]
        public float skyBeyondHorizon = 1.12f;
        [Tooltip("Size of the quad around the Sun's disc (room for the glow).")]
        public float sunQuadToDisc = 24f;
        public float footprintSizeKm = 30f;

        [Header("Light")]
        public float sunIntensity = 1.5f;
        [Tooltip("Light from the sunlit Earth below, on the Earth-facing side of the spacecraft.")]
        public float earthshineMax = 0.35f;
        [Tooltip("Sim seconds to fade the Sun in or out at eclipse entry/exit when the source only sends a flag.")]
        public float eclipseFadeSimSeconds = 8f;
        [Tooltip("Smoothing of the view frame (seconds); hides noise when velocity must be estimated.")]
        public float frameSmoothing = 0.4f;

        public bool FootprintEnabled { get; set; }
        public double Illumination { get { return illum; } }
        /// <summary>The frame currently used: rotation from ECI into this transform's (simulator-axes) frame.</summary>
        public QuatD SceneFromEci { get { return qSE; } }

        QuatD qSE = QuatD.Identity;
        bool hasFrame;
        double illum = 1.0;
        int lastDiscontinuity = int.MinValue;
        ViewFrame lastViewFrame;
        readonly OrbitNormalEstimator normals = new OrbitNormalEstimator();
        double lastNormalFeedT = double.NaN;

        static readonly int SunDirId = Shader.PropertyToID("_EOSAT_SunDir");
        static readonly int SunVisId = Shader.PropertyToID("_EOSAT_SunVisibility");
        static readonly int EarthCenterId = Shader.PropertyToID("_EOSAT_EarthCenter");
        static readonly int EarthRadiusId = Shader.PropertyToID("_EOSAT_EarthRadius");
        static readonly int AtmoScaleId = Shader.PropertyToID("_EOSAT_AtmoScale");

        const double SunAngularRadiusRad = 0.2666 * Math.PI / 180.0;

        void OnEnable()
        {
            if (!Application.isPlaying) PushGlobalsFromTransforms();
        }

        void Update()
        {
            // In the editor (not playing) keep the planet shaders lit from wherever the Sun light points.
            if (!Application.isPlaying) PushGlobalsFromTransforms();
        }

        void PushGlobalsFromTransforms()
        {
            if (sunLight != null)
            {
                Vector3 toSun = -sunLight.transform.forward;
                Shader.SetGlobalVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            }
            Shader.SetGlobalFloat(SunVisId, 1f);
            if (earth != null)
            {
                Shader.SetGlobalVector(EarthCenterId, earth.position);
                Shader.SetGlobalFloat(EarthRadiusId, earth.lossyScale.x);
                Shader.SetGlobalFloat(AtmoScaleId, 22f * metresPerKm * transform.lossyScale.x);
            }
        }

        /// <summary>Forget estimated orbit history (new feed).</summary>
        public void ResetHistory()
        {
            normals.Reset();
            hasFrame = false;
            lastNormalFeedT = double.NaN;
        }

        public void Apply(DisplayState s, float dt)
        {
            if (s == null || !s.Valid) return;
            bool snap = s.Discontinuity != lastDiscontinuity || !hasFrame || lastViewFrame != viewFrame;
            lastDiscontinuity = s.Discontinuity;
            lastViewFrame = viewFrame;

            Vec3d r = s.PosEciKm;
            double rLen = r.Length;
            if (!(rLen > Ephemeris.EarthRadiusKm + 50.0) || !r.IsFinite) return;

            // ---------------------------------------------------------------- view frame
            if (double.IsNaN(lastNormalFeedT) || s.SimTime != lastNormalFeedT)
            {
                normals.Add(s.SimTime, r, s.HasVelocity && !s.VelocityEstimated, s.VelEciKmS);
                lastNormalFeedT = s.SimTime;
            }
            Vec3d h;
            if (s.HasVelocity && !s.VelocityEstimated) h = Vec3d.Cross(r, s.VelEciKmS).Normalized;
            else if (normals.HasNormal) h = normals.Normal;
            else h = LocalFrame.NormalAssumingNorthbound(r);

            QuatD target = viewFrame == ViewFrame.EarthBelow ? LocalFrame.EciToLocal(r, h) : QuatD.Identity;
            bool exactVelocity = s.HasVelocity && !s.VelocityEstimated;
            if (snap || exactVelocity || viewFrame == ViewFrame.Inertial) qSE = target;
            else
            {
                // smooth out small steps in the estimated orbit normal; faster when time runs faster
                double tau = Math.Max(0.005, frameSmoothing / Math.Max(1.0, s.SimRate));
                qSE = QuatD.Slerp(qSE, target, 1.0 - Math.Exp(-dt / tau));
            }
            hasFrame = true;

            double km = metresPerKm;
            double sim = s.SimTime;

            // ---------------------------------------------------------------- spacecraft
            QuatD qSB = qSE * s.QBodyToEci;
            if (satellite != null)
            {
                satellite.localPosition = Vector3.zero;
                satellite.localRotation = SimToUnity.Rot(qSB);
            }

            // ---------------------------------------------------------------- Earth
            double R = Ephemeris.EarthRadiusKm;
            Vec3d earthCenter = qSE.Rotate(-r) * km;
            if (earth != null)
            {
                earth.localPosition = SimToUnity.Vec(earthCenter);
                earth.localRotation = SimToUnity.Rot(qSE * AstroTime.EcefToEci(sim, eciConvention));
                earth.localScale = Vector3.one * (float)(R * km);
            }
            double horizon = Math.Sqrt(Math.Max(1.0, rLen * rLen - R * R)) * km;
            double sky = horizon * Math.Max(1.01, skyBeyondHorizon);

            // ---------------------------------------------------------------- Sun
            Vec3d sunDirEci = s.HasSun ? s.SunDirEci : (Ephemeris.SunEci(sim, eciConvention) - r).Normalized;
            Vec3d sunScene = qSE.Rotate(sunDirEci).Normalized;
            Vector3 sunLocal = SimToUnity.Vec(sunScene).normalized;
            Vector3 sunWorld = transform.TransformDirection(sunLocal);

            double litTarget;
            bool exact = !double.IsNaN(s.SunlitFraction);
            if (exact) litTarget = s.SunlitFraction;
            else if (s.HasSun) litTarget = s.InEclipse ? 0.0 : 1.0;
            else litTarget = Ephemeris.SunlitFraction(r, Ephemeris.SunEci(sim, eciConvention));
            if (snap || exact) illum = litTarget;
            else
            {
                double rate = Math.Max(0.4, Math.Max(0.05, s.SimRate) / Math.Max(0.5, eclipseFadeSimSeconds));
                illum = MoveTowards(illum, litTarget, rate * dt);
            }

            if (sunLight != null)
            {
                sunLight.transform.rotation = Quaternion.LookRotation(-sunWorld, Mathf.Abs(sunWorld.y) > 0.99f ? Vector3.forward : Vector3.up);
                sunLight.intensity = (float)(sunIntensity * illum);
                bool lit = illum > 0.001;
                if (sunLight.enabled != lit) sunLight.enabled = lit;
            }
            if (sunDisc != null)
            {
                float d = (float)(sky * 0.985);
                sunDisc.localPosition = sunLocal * d;
                float size = (float)(2.0 * d * Math.Tan(SunAngularRadiusRad)) * sunQuadToDisc;
                sunDisc.localScale = new Vector3(size, size, size);
                Camera cam = MainCamera();
                if (cam != null) sunDisc.rotation = Quaternion.LookRotation(sunDisc.position - cam.transform.position);
            }
            Shader.SetGlobalVector(SunDirId, new Vector4(sunWorld.x, sunWorld.y, sunWorld.z, 0f));
            Shader.SetGlobalFloat(SunVisId, (float)illum);

            // ---------------------------------------------------------------- Earthshine
            Vec3d up = qSE.Rotate(r / rLen);
            if (earthshineLight != null)
            {
                double day = MathUtil.Clamp01(0.5 + 0.6 * Vec3d.Dot(r / rLen, sunDirEci));
                Vector3 upWorld = transform.TransformDirection(SimToUnity.Vec(up));
                earthshineLight.transform.rotation = Quaternion.LookRotation(upWorld, Mathf.Abs(upWorld.z) > 0.99f ? Vector3.up : Vector3.forward);
                earthshineLight.intensity = (float)(earthshineMax * day);
                bool on = day > 0.01;
                if (earthshineLight.enabled != on) earthshineLight.enabled = on;
            }

            // ---------------------------------------------------------------- Moon
            if (moon != null)
            {
                Vec3d rMoon = Ephemeris.MoonEci(sim, eciConvention);
                Vec3d rel = rMoon - r;
                double dist = rel.Length;
                float d = (float)(sky * 0.97);
                moon.localPosition = SimToUnity.Vec(qSE.Rotate(rel / dist)) * d;
                moon.localScale = Vector3.one * (float)(d * Ephemeris.MoonRadiusKm / dist);
                Vec3d xm = (-rMoon).Normalized;    // near side faces Earth
                Vec3d pole = AstroTime.J2000ToEci(sim, eciConvention).Rotate(Ephemeris.EclipticPoleJ2000());
                Vec3d zm = Vec3d.RejectFrom(pole, xm).Normalized;
                Vec3d ym = Vec3d.Cross(zm, xm);
                moon.localRotation = SimToUnity.Rot(qSE * QuatD.FromColumns(xm, ym, zm));
            }

            // ---------------------------------------------------------------- stars
            if (stars != null)
            {
                stars.localPosition = Vector3.zero;
                stars.localRotation = SimToUnity.Rot(qSE * AstroTime.J2000ToEci(sim, eciConvention));
                stars.localScale = Vector3.one * (float)sky;
            }

            // ---------------------------------------------------------------- where the camera looks
            if (footprint != null)
            {
                bool show = false;
                if (FootprintEnabled)
                {
                    Vector3 c = SimToUnity.Vec(earthCenter);
                    Vector3 dir = SimToUnity.Vec(qSB.Rotate(Vec3d.UnitZ)).normalized;
                    float radius = (float)(R * km);
                    float b = -Vector3.Dot(dir, c);
                    float disc = b * b - (c.sqrMagnitude - radius * radius);
                    if (disc > 0f)
                    {
                        float tHit = -b - Mathf.Sqrt(disc);
                        if (tHit > 0f)
                        {
                            Vector3 hit = dir * tHit;
                            footprint.localPosition = hit;
                            footprint.localRotation = Quaternion.FromToRotation(Vector3.up, (hit - c).normalized);
                            footprint.localScale = Vector3.one * (float)(footprintSizeKm * km);
                            show = true;
                        }
                    }
                }
                if (footprint.gameObject.activeSelf != show) footprint.gameObject.SetActive(show);
            }

            // ---------------------------------------------------------------- atmosphere and cameras
            if (earth != null)
            {
                Shader.SetGlobalVector(EarthCenterId, earth.position);
                Shader.SetGlobalFloat(EarthRadiusId, (float)(R * km * transform.lossyScale.x));
                Shader.SetGlobalFloat(AtmoScaleId, (float)(22.0 * km * transform.lossyScale.x));
            }
            float far = (float)(sky * 1.12 + 20.0);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && Mathf.Abs(cameras[i].farClipPlane - far) > 1f) cameras[i].farClipPlane = far;
            }
        }

        Camera MainCamera()
        {
            for (int i = 0; i < cameras.Length; i++)
                if (cameras[i] != null && cameras[i].isActiveAndEnabled) return cameras[i];
            return Camera.main;
        }

        static double MoveTowards(double a, double b, double maxStep)
        {
            if (Math.Abs(b - a) <= maxStep) return b;
            return a + Math.Sign(b - a) * maxStep;
        }
    }
}
