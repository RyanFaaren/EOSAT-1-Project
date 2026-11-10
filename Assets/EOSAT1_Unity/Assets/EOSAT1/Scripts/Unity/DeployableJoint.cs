// EOSAT-1 VR - one moving part (a wing hinge, an antenna element).
// fraction 0 = stowed pose, 1 = deployed: rotate deployedAngle degrees about localAxis
// (in the stowed frame) and optionally slide by deployedOffset.
// Works on any model: when the CAD model replaces the placeholder, put this on each
// hinge node, set the axis and angle, and press "Capture current pose as stowed".
using UnityEngine;

namespace Eosat1
{
    [DisallowMultipleComponent]
    public sealed class DeployableJoint : MonoBehaviour
    {
        [Tooltip("Hinge axis in this object's local axes, stowed pose.")]
        public Vector3 localAxis = Vector3.right;

        [Tooltip("Degrees turned when fully deployed (sign sets the direction).")]
        public float deployedAngle = 90f;

        [Tooltip("Optional slide when fully deployed, in the parent's axes (for telescoping parts).")]
        public Vector3 deployedOffset = Vector3.zero;

        [Range(0f, 1f)]
        [Tooltip("Preview in edit mode; driven by SatelliteView at runtime.")]
        public float fraction;

        [HideInInspector] public Quaternion stowedLocalRotation = Quaternion.identity;
        [HideInInspector] public Vector3 stowedLocalPosition;
        [HideInInspector] public bool hasStowedPose;

        float wobble;

        void Reset()
        {
            CaptureStowedPose();
        }

        void Awake()
        {
            if (!hasStowedPose) CaptureStowedPose();
        }

        [ContextMenu("Capture current pose as stowed")]
        public void CaptureStowedPose()
        {
            stowedLocalRotation = transform.localRotation;
            stowedLocalPosition = transform.localPosition;
            hasStowedPose = true;
            fraction = 0f;
        }

        /// <summary>Set how far out the part is (0..1); wobbleDegrees adds a shake on top (jams).</summary>
        public void Apply(float f, float wobbleDegrees)
        {
            fraction = Mathf.Clamp01(f);
            wobble = wobbleDegrees;
            Pose();
        }

        void Pose()
        {
            if (!hasStowedPose) CaptureStowedPose();
            Vector3 axis = localAxis.sqrMagnitude > 1e-8f ? localAxis.normalized : Vector3.right;
            transform.localRotation = stowedLocalRotation * Quaternion.AngleAxis(fraction * deployedAngle + wobble, axis);
            transform.localPosition = stowedLocalPosition + deployedOffset * fraction;
        }

        void OnValidate()
        {
            if (!Application.isPlaying && hasStowedPose) Pose();
        }
    }
}
