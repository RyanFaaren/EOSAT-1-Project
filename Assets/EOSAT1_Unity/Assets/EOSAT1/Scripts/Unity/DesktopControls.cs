// EOSAT-1 VR - mouse and keyboard, for working without a headset (and for the
// instructor's screen). Orbits the camera around the spacecraft. While a headset is
// active the headset moves the camera, but the keyboard shortcuts still work.
using Eosat1.Core;
using UnityEngine;

namespace Eosat1
{
    public sealed class DesktopControls : MonoBehaviour
    {
        public EosatApp app;
        public InfoPanel infoPanel;
        public Transform cameraTransform;
        [Tooltip("What the camera orbits (the spacecraft).")]
        public Transform target;

        public float distance = 3.4f;
        public float minDistance = 1.0f;
        public float maxDistance = 20f;
        public float degreesPerPixel = 0.25f;

        // preset views around the 1.4 m EOS SAT-1: yaw, pitch (degrees), distance (m).
        // In the default "Earth below" frame with the camera pointed at Earth, the solar
        // panels face you from behind (-Z) and the cameras look down.
        static readonly Vector3[] Views =
        {
            new Vector3(-22f, -28f, 3.4f),  // like the reference picture: from behind and below
            new Vector3(-30f, 14f, 3.6f),   // behind and to the left, Earth below
            new Vector3(90f, 6f, 3.6f),     // from the side: the wings span towards you
            new Vector3(0f, 78f, 4.0f),     // from above: the separation ring and star cameras
            new Vector3(28f, -62f, 3.0f),   // from below: the two DragonEye cameras
            new Vector3(180f, 10f, 3.6f),   // from ahead
            new Vector3(35f, 22f, 8.0f),    // wide
        };

        float yaw, pitch;
        int view;

        void Start()
        {
            SetView(0);
        }

        void SetView(int i)
        {
            view = (i % Views.Length + Views.Length) % Views.Length;
            yaw = Views[view].x;
            pitch = Views[view].y;
            distance = Views[view].z;
        }

        void LateUpdate()
        {
            // camera (the headset owns the camera when one is active)
            if (!UnityEngine.XR.XRSettings.isDeviceActive && cameraTransform != null && target != null)
            {
                if (InputShim.MouseHeld(0) || InputShim.MouseHeld(1))
                {
                    Vector2 d = InputShim.MouseDelta();
                    yaw += d.x * degreesPerPixel;
                    pitch = Mathf.Clamp(pitch - d.y * degreesPerPixel, -89f, 89f);
                }
                float scroll = InputShim.Scroll();
                if (Mathf.Abs(scroll) > 0.001f) distance = Mathf.Clamp(distance * Mathf.Pow(0.88f, scroll), minDistance, maxDistance);
                if (InputShim.Held(KeyCode.LeftArrow) && InputShim.Held(KeyCode.LeftShift)) yaw -= 60f * Time.unscaledDeltaTime;
                if (InputShim.Held(KeyCode.RightArrow) && InputShim.Held(KeyCode.LeftShift)) yaw += 60f * Time.unscaledDeltaTime;
                if (InputShim.Down(KeyCode.V)) SetView(view + 1);
                if (InputShim.Down(KeyCode.F)) SetView(0);

                Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
                cameraTransform.position = target.position - rot * Vector3.forward * distance;
                cameraTransform.rotation = rot;
            }

            // keyboard shortcuts work with or without a headset (instructor at the PC)
            if (app == null) return;
            bool shift = InputShim.Held(KeyCode.LeftShift) || InputShim.Held(KeyCode.RightShift);

            // time
            if (InputShim.Down(KeyCode.Space)) app.TogglePause();
            if (InputShim.Down(KeyCode.Equals) || InputShim.Down(KeyCode.KeypadPlus) || InputShim.Down(KeyCode.Period)) app.SpeedUp();
            if (InputShim.Down(KeyCode.Minus) || InputShim.Down(KeyCode.KeypadMinus) || InputShim.Down(KeyCode.Comma)) app.SlowDown();
            if (!shift && InputShim.Down(KeyCode.RightArrow)) app.JumpSeconds(60);
            if (!shift && InputShim.Down(KeyCode.LeftArrow)) app.JumpSeconds(-60);
            if (InputShim.Down(KeyCode.Backspace) || InputShim.Down(KeyCode.Home)) app.Restart();

            // commands (sandbox)
            if (InputShim.Down(KeyCode.Alpha1)) app.Command(SandboxCommand.DeployWingMinusY);
            if (InputShim.Down(KeyCode.Alpha2)) app.Command(SandboxCommand.DeployWingPlusY);
            if (InputShim.Down(KeyCode.Alpha3)) app.Command(SandboxCommand.DeployAntenna);
            if (InputShim.Down(KeyCode.Alpha4)) app.Command(SandboxCommand.ToggleAntennaJam);
            if (InputShim.Down(KeyCode.Alpha5)) app.Command(SandboxCommand.ResetDeployables);
            if (InputShim.Down(KeyCode.N)) app.Command(SandboxCommand.AttitudeNadir);
            if (InputShim.Down(KeyCode.S)) app.Command(SandboxCommand.AttitudeSun);
            if (InputShim.Down(KeyCode.R)) app.Command(SandboxCommand.AttitudeRollImaging);
            if (InputShim.Down(KeyCode.T)) app.Command(SandboxCommand.AttitudeTumble);

            // overlays and settings
            if (InputShim.Down(KeyCode.G)) app.ToggleAxes();
            if (InputShim.Down(KeyCode.B)) app.ToggleBoresight();
            if (InputShim.Down(KeyCode.L)) app.ToggleViewFrame();
            if (InputShim.Down(KeyCode.M)) app.CycleFeed();
            if ((InputShim.Down(KeyCode.H) || InputShim.Down(KeyCode.F1)) && infoPanel != null) infoPanel.Toggle();
        }
    }
}
