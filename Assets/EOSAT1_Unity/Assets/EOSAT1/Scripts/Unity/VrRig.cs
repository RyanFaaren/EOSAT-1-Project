// EOSAT-1 VR - a light headset rig that needs no extra packages beyond XR Plug-in
// Management + OpenXR: it reads the headset and controllers through UnityEngine.XR.
// Walk around the spacecraft for real (it is life size), or use the sticks.
//
// Left stick   move (where you look)      Right stick  left/right snap turn, up/down rise/sink
// A            deploy the next wing       B            deploy antenna / clear a jam
// X            info panel on/off          Y            time 1x -> 10x -> 60x
// Left grip    pause                      Menu         restart the scenario
// Right trigger body axes                 Left trigger camera boresight
// Commands only act in Sandbox mode; with the simulator they come from mission control.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Eosat1
{
    public sealed class VrRig : MonoBehaviour
    {
        public EosatApp app;
        public InfoPanel infoPanel;
        [Tooltip("Parent of the tracked head and hands; lifted to eye height if the runtime has no floor origin.")]
        public Transform cameraOffset;
        public Transform head;
        public Transform leftHand;
        public Transform rightHand;

        public float moveSpeed = 0.7f;
        public float verticalSpeed = 0.5f;
        public float snapTurnDegrees = 30f;
        public float fallbackEyeHeight = 1.5f;
        public float deadZone = 0.25f;

        readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        float nextOriginCheck;
        bool floorConfirmed;
        bool turnLatched;
        bool prevA, prevB, prevX, prevY, prevTrigR, prevTrigL, prevMenu, prevGripL;

        void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        void Update()
        {
            bool active = XRSettings.isDeviceActive;
            SetActive(leftHand, active);
            SetActive(rightHand, active);
            if (!active)
            {
                floorConfirmed = false;
                return;
            }
            EnsureFloorOrigin();
            UpdatePoses();
            Locomotion();
            Buttons();
        }

        [BeforeRenderOrder(100)]
        void OnBeforeRender()
        {
            if (XRSettings.isDeviceActive) UpdatePoses();
        }

        static void SetActive(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        void EnsureFloorOrigin()
        {
            if (floorConfirmed || Time.unscaledTime < nextOriginCheck) return;
            nextOriginCheck = Time.unscaledTime + 1f;
            SubsystemManager.GetSubsystems(subsystems);
            bool floor = false;
            for (int i = 0; i < subsystems.Count; i++)
            {
                XRInputSubsystem s = subsystems[i];
                if (s == null || !s.running) continue;
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                {
                    if (s.GetTrackingOriginMode() != TrackingOriginModeFlags.Floor)
                        s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    if (s.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor) floor = true;
                }
            }
            if (cameraOffset != null) cameraOffset.localPosition = new Vector3(0f, floor ? 0f : fallbackEyeHeight, 0f);
            floorConfirmed = floor;
        }

        void UpdatePoses()
        {
            InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (hmd.isValid && head != null)
            {
                Vector3 p;
                Quaternion q;
                if (hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out p) ||
                    hmd.TryGetFeatureValue(CommonUsages.devicePosition, out p))
                    head.localPosition = p;
                if (hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out q) ||
                    hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out q))
                    head.localRotation = q;
            }
            TrackHand(XRNode.LeftHand, leftHand);
            TrackHand(XRNode.RightHand, rightHand);
        }

        static void TrackHand(XRNode node, Transform t)
        {
            if (t == null) return;
            InputDevice d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return;
            Vector3 p;
            Quaternion q;
            if (d.TryGetFeatureValue(CommonUsages.devicePosition, out p)) t.localPosition = p;
            if (d.TryGetFeatureValue(CommonUsages.deviceRotation, out q)) t.localRotation = q;
        }

        void Locomotion()
        {
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            float dt = Time.unscaledDeltaTime;
            Vector2 stick;
            if (left.isValid && left.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick) && stick.magnitude > deadZone && head != null)
            {
                Vector3 fwd = head.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
                fwd.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                transform.position += (fwd * stick.y + side * stick.x) * moveSpeed * dt;
            }
            if (right.isValid && right.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick))
            {
                if (Mathf.Abs(stick.x) > 0.7f)
                {
                    if (!turnLatched && head != null)
                    {
                        transform.RotateAround(head.position, Vector3.up, Mathf.Sign(stick.x) * snapTurnDegrees);
                        turnLatched = true;
                    }
                }
                else if (Mathf.Abs(stick.x) < 0.3f)
                {
                    turnLatched = false;
                }
                if (Mathf.Abs(stick.y) > deadZone && Mathf.Abs(stick.x) < 0.5f)
                {
                    Vector3 pos = transform.position;
                    pos.y = Mathf.Clamp(pos.y + stick.y * verticalSpeed * dt, -3f, 3f);
                    transform.position = pos;
                }
            }
        }

        void Buttons()
        {
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (Pressed(right, CommonUsages.primaryButton, ref prevA) && app != null) app.DeployNextWing();
            if (Pressed(right, CommonUsages.secondaryButton, ref prevB) && app != null) app.DeployAntennaOrClearJam();
            if (Pressed(left, CommonUsages.primaryButton, ref prevX) && infoPanel != null) infoPanel.Toggle();
            if (Pressed(left, CommonUsages.secondaryButton, ref prevY) && app != null) app.CycleSpeedCoarse();
            if (Pressed(right, CommonUsages.triggerButton, ref prevTrigR) && app != null) app.ToggleAxes();
            if (Pressed(left, CommonUsages.triggerButton, ref prevTrigL) && app != null) app.ToggleBoresight();
            if (Pressed(left, CommonUsages.menuButton, ref prevMenu) && app != null) app.Restart();
            if (Pressed(left, CommonUsages.gripButton, ref prevGripL) && app != null) app.TogglePause();
        }

        static bool Pressed(InputDevice d, InputFeatureUsage<bool> usage, ref bool previous)
        {
            bool now = false;
            bool value;
            if (d.isValid && d.TryGetFeatureValue(usage, out value)) now = value;
            bool edge = now && !previous;
            previous = now;
            if (edge) d.SendHapticImpulse(0u, 0.35f, 0.05f);
            return edge;
        }
    }
}
