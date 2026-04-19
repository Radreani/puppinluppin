using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Bridges legacy <see cref="KeyCode"/> bindings (Inspector / editor) to the new Input System.
/// Falls back to the legacy Input Manager when enabled so bindings work with "Both" or if the Input System keyboard is unavailable.
/// </summary>
static class FpsKeyBindingInput
{
    static Keyboard ResolveKeyboard()
    {
        if (Keyboard.current != null)
            return Keyboard.current;
        foreach (var d in InputSystem.devices)
        {
            if (d is Keyboard k)
                return k;
        }

        return null;
    }

    static Mouse ResolveMouse()
    {
        if (Mouse.current != null)
            return Mouse.current;
        foreach (var d in InputSystem.devices)
        {
            if (d is Mouse m)
                return m;
        }

        return null;
    }

    /// <summary>Maps <see cref="KeyCode.A"/>–<see cref="KeyCode.Z"/> to Input System keys (avoids fragile Enum.TryParse on key names).</summary>
    static readonly Key[] KeyCodeLetterToKey =
    {
        Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J,
        Key.K, Key.L, Key.M, Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T,
        Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z
    };

    /// <summary>Scales raw pointer delta so <see cref="FPSCharacterController.mouseSensitivity"/> stays in a similar range to the old Input Manager.</summary>
    const float MouseDeltaScale = 0.08f;

    public static Vector2 GetMouseLookDelta(float sensitivity)
    {
        var m = ResolveMouse();
        if (m == null)
            return Vector2.zero;
        return m.delta.ReadValue() * (sensitivity * MouseDeltaScale);
    }

    public static bool IsPressed(KeyCode code)
    {
        if (code == KeyCode.None)
            return false;

        if (TryGetMouseButton(code, out var btn) && btn != null)
        {
            if (btn.isPressed)
                return true;
        }
        else
        {
            var kb = ResolveKeyboard();
            if (kb != null && TryKeyCodeToKey(code, out var key))
            {
                if (kb[key].isPressed)
                    return true;
            }
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (code >= KeyCode.Mouse0 && code <= KeyCode.Mouse6)
            return Input.GetMouseButton(code - KeyCode.Mouse0);
        return Input.GetKey(code);
#else
        return false;
#endif
    }

    public static bool WasPressedThisFrame(KeyCode code)
    {
        if (code == KeyCode.None)
            return false;

        if (TryGetMouseButton(code, out var btn) && btn != null)
        {
            if (btn.wasPressedThisFrame)
                return true;
        }
        else
        {
            var kb = ResolveKeyboard();
            if (kb != null && TryKeyCodeToKey(code, out var key))
            {
                if (kb[key].wasPressedThisFrame)
                    return true;
            }
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (code >= KeyCode.Mouse0 && code <= KeyCode.Mouse6)
            return Input.GetMouseButtonDown(code - KeyCode.Mouse0);
        return Input.GetKeyDown(code);
#else
        return false;
#endif
    }

    static bool TryGetMouseButton(KeyCode code, out ButtonControl button)
    {
        button = null;
        var m = ResolveMouse();
        if (m == null)
            return false;
        switch (code)
        {
            case KeyCode.Mouse0:
                button = m.leftButton;
                return true;
            case KeyCode.Mouse1:
                button = m.rightButton;
                return true;
            case KeyCode.Mouse2:
                button = m.middleButton;
                return true;
            case KeyCode.Mouse3:
                button = m.backButton;
                return true;
            case KeyCode.Mouse4:
                button = m.forwardButton;
                return true;
            default:
                return false;
        }
    }

    static bool TryKeyCodeToKey(KeyCode k, out Key key)
    {
        key = Key.None;
        if (k == KeyCode.None)
            return false;

        if (k >= KeyCode.JoystickButton0)
            return false;

        if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9)
        {
            key = (Key)((int)Key.Numpad0 + (k - KeyCode.Keypad0));
            return true;
        }

        if (k >= KeyCode.A && k <= KeyCode.Z)
        {
            key = KeyCodeLetterToKey[k - KeyCode.A];
            return true;
        }

        switch (k)
        {
            case KeyCode.Return:
                key = Key.Enter;
                return true;
            case KeyCode.KeypadEnter:
                key = Key.NumpadEnter;
                return true;
            case KeyCode.LeftControl:
                key = Key.LeftCtrl;
                return true;
            case KeyCode.RightControl:
                key = Key.RightCtrl;
                return true;
            case KeyCode.KeypadDivide:
                key = Key.NumpadDivide;
                return true;
            case KeyCode.KeypadMultiply:
                key = Key.NumpadMultiply;
                return true;
            case KeyCode.KeypadMinus:
                key = Key.NumpadMinus;
                return true;
            case KeyCode.KeypadPlus:
                key = Key.NumpadPlus;
                return true;
            case KeyCode.KeypadPeriod:
                key = Key.NumpadPeriod;
                return true;
            case KeyCode.KeypadEquals:
                key = Key.NumpadEquals;
                return true;
            case KeyCode.Alpha0:
                key = Key.Digit0;
                return true;
            case KeyCode.Alpha1:
                key = Key.Digit1;
                return true;
            case KeyCode.Alpha2:
                key = Key.Digit2;
                return true;
            case KeyCode.Alpha3:
                key = Key.Digit3;
                return true;
            case KeyCode.Alpha4:
                key = Key.Digit4;
                return true;
            case KeyCode.Alpha5:
                key = Key.Digit5;
                return true;
            case KeyCode.Alpha6:
                key = Key.Digit6;
                return true;
            case KeyCode.Alpha7:
                key = Key.Digit7;
                return true;
            case KeyCode.Alpha8:
                key = Key.Digit8;
                return true;
            case KeyCode.Alpha9:
                key = Key.Digit9;
                return true;
            default:
                return Enum.TryParse(k.ToString(), ignoreCase: true, out key) && key != Key.None;
        }
    }
}
