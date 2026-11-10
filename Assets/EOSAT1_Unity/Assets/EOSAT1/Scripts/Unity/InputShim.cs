// EOSAT-1 VR - keyboard and mouse that work with either input setting.
// Unity 6 projects default to the new Input System, where UnityEngine.Input throws;
// older projects use the legacy Input Manager. This reads whichever is active
// (Edit > Project Settings > Player > Active Input Handling).
using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Eosat1
{
    public static class InputShim
    {
        public static bool Down(KeyCode k)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(k);
#elif ENABLE_INPUT_SYSTEM
            KeyControl c = Control(k);
            return c != null && c.wasPressedThisFrame;
#else
            return false;
#endif
        }

        public static bool Held(KeyCode k)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(k);
#elif ENABLE_INPUT_SYSTEM
            KeyControl c = Control(k);
            return c != null && c.isPressed;
#else
            return false;
#endif
        }

        /// <summary>Mouse movement this frame, in pixels.</summary>
        public static Vector2 MouseDelta()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;   // axis = pixels x 0.1
#elif ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            return Vector2.zero;
#endif
        }

        /// <summary>Scroll wheel, in notches (positive = away from you).</summary>
        public static float Scroll()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
            return 0f;
#endif
        }

        /// <summary>0 left, 1 right, 2 middle.</summary>
        public static bool MouseHeld(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#elif ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return false;
            if (button == 0) return Mouse.current.leftButton.isPressed;
            if (button == 1) return Mouse.current.rightButton.isPressed;
            return Mouse.current.middleButton.isPressed;
#else
            return false;
#endif
        }

#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
        static KeyControl Control(KeyCode k)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return null;
            Key key = Map(k);
            return key == Key.None ? null : kb[key];
        }

        static Key Map(KeyCode k)
        {
            // Letters are in alphabetical order in both enums; digits are not (Key.Digit0 comes after Digit9).
            if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
            switch (k)
            {
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2;
                case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4;
                case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.Alpha6: return Key.Digit6;
                case KeyCode.Alpha7: return Key.Digit7;
                case KeyCode.Alpha8: return Key.Digit8;
                case KeyCode.Alpha9: return Key.Digit9;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.KeypadMinus: return Key.NumpadMinus;
                case KeyCode.KeypadPlus: return Key.NumpadPlus;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.Home: return Key.Home;
                case KeyCode.F1: return Key.F1;
                default: return Key.None;
            }
        }
#endif
    }
}
