// EOSAT-1 VR - shows what is deployed: two solar wings and the UHF antenna.
// Wing states STOWED -> DEPLOYING (fraction 0..1 over ~30 s) -> DEPLOYED.
// Antenna STOWED / DEPLOYED / JAMMED. A jam is made obvious: one element stuck
// part-way and shaking, a pulsing red halo, and a floating label.
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class SatelliteView : MonoBehaviour
    {
        [Header("Moving parts")]
        public DeployableJoint[] wingMinusY = new DeployableJoint[0];
        public DeployableJoint[] wingPlusY = new DeployableJoint[0];
        [Tooltip("Elements that deploy normally.")]
        public DeployableJoint[] antennaElements = new DeployableJoint[0];
        [Tooltip("The element that sticks when the antenna is JAMMED.")]
        public DeployableJoint antennaJamElement;
        [Range(0f, 1f)] public float jammedFraction = 0.35f;

        [Header("Jam warning")]
        public Renderer jamHalo;
        public GameObject jamLabel;

        [Header("Overlays")]
        public GameObject bodyAxes;
        public GameObject boresight;

        [Header("Solar cells (for the info panel and sun pointing)")]
        [Tooltip("The solar panel objects of each wing; their local +Z is the side with the cells.")]
        public Transform cellsMinusY;
        public Transform cellsPlusY;
        [Tooltip("Which way the cells face, in body axes, once the wings are out. EOS SAT-1 model: -X.")]
        public Vector3 deployedCellNormalBody = new Vector3(-1f, 0f, 0f);

        [Header("Feel")]
        [Tooltip("Seconds for the model to catch up when the data jumps.")]
        public float catchUpTime = 0.25f;

        float shownMy, shownPy, shownAntenna, shownJam;
        int lastDiscontinuity = int.MinValue;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            // the sandbox's "point the wings at the Sun" mode uses the real model's cell direction
            var n = new Vec3d(deployedCellNormalBody.x, deployedCellNormalBody.y, deployedCellNormalBody.z);
            if (n.SqrLength > 0.25) Attitudes.SunFacingAxisBody = n.Normalized;
        }

        /// <summary>
        /// Direction the cells of one wing face right now, in body axes (side -1 = wing -Y,
        /// +1 = wing +Y), read from the model itself.
        /// </summary>
        public bool TryGetCellNormalBody(int side, out Vec3d normal)
        {
            normal = Vec3d.Zero;
            Transform t = side < 0 ? cellsMinusY : cellsPlusY;
            if (t == null) return false;
            Vector3 local = transform.InverseTransformDirection(t.forward);
            normal = SimToUnity.ToSim(local).Normalized;
            return normal.SqrLength > 0.5;
        }

        public bool AxesVisible
        {
            get { return bodyAxes != null && bodyAxes.activeSelf; }
            set { if (bodyAxes != null) bodyAxes.SetActive(value); }
        }

        public bool BoresightVisible
        {
            get { return boresight != null && boresight.activeSelf; }
            set { if (boresight != null) boresight.SetActive(value); }
        }

        public void Apply(DisplayState s, float dt)
        {
            if (s == null || !s.Valid) return;
            bool snap = s.Discontinuity != lastDiscontinuity;
            lastDiscontinuity = s.Discontinuity;
            float k = snap ? 1f : 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, catchUpTime));

            shownMy = Mathf.Lerp(shownMy, (float)s.WingMinusY.EffectiveFraction, k);
            shownPy = Mathf.Lerp(shownPy, (float)s.WingPlusY.EffectiveFraction, k);

            bool jammed = s.Antenna.Present && s.Antenna.State == DeployState.Jammed;
            float antennaTarget = (float)s.Antenna.EffectiveFraction;
            if (jammed) antennaTarget = 1f;
            shownAntenna = Mathf.Lerp(shownAntenna, antennaTarget, k);
            shownJam = Mathf.Lerp(shownJam, jammed ? jammedFraction : antennaTarget, k);

            Drive(wingMinusY, shownMy, 0f);
            Drive(wingPlusY, shownPy, 0f);
            Drive(antennaElements, shownAntenna, 0f);
            if (antennaJamElement != null)
            {
                float shake = jammed ? Mathf.Sin(Time.time * 37f) * 2.5f + Mathf.Sin(Time.time * 11f) * 1.5f : 0f;
                antennaJamElement.Apply(shownJam, shake);
            }

            if (jamLabel != null && jamLabel.activeSelf != jammed) jamLabel.SetActive(jammed);
            if (jamHalo != null)
            {
                if (jamHalo.enabled != jammed) jamHalo.enabled = jammed;
                if (jammed)
                {
                    if (mpb == null) mpb = new MaterialPropertyBlock();
                    float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 6f);
                    mpb.SetColor(ColorId, new Color(1f, 0.12f, 0.08f, 1f) * pulse);
                    jamHalo.SetPropertyBlock(mpb);
                }
            }
        }

        static void Drive(DeployableJoint[] joints, float f, float wobble)
        {
            if (joints == null) return;
            for (int i = 0; i < joints.Length; i++)
                if (joints[i] != null) joints[i].Apply(f, wobble);
        }
    }
}
