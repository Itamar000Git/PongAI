using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Which device currently steers the human paddle.</summary>
public enum PaddleInputMode
{
    None,       // nothing used yet (or reset): the paddle only stops
    Keyboard,   // W / S
    Pointer,    // mouse: the cursor's height is the target
    Touch       // one gameplay finger: its height is the target
}

public enum PaddleIntentKind
{
    None,       // no input: the paddle slows to a stop
    Axis,       // keyboard: -1 .. +1 movement direction
    TargetY     // pointer / touch: a world-space Y to move toward (never teleported to)
}

/// <summary>
/// What the human wants this frame. It is only intent: PlayerPaddleController turns it into movement with the
/// one shared speed / acceleration / bounds model, so every device is equally strong.
/// </summary>
public readonly struct PaddleIntent
{
    public readonly PaddleIntentKind Kind;
    public readonly float Axis;
    public readonly float TargetY;

    private PaddleIntent(PaddleIntentKind kind, float axis, float targetY)
    {
        Kind = kind;
        Axis = axis;
        TargetY = targetY;
    }

    public static PaddleIntent None => default;
    public static PaddleIntent FromAxis(float axis) => new PaddleIntent(PaddleIntentKind.Axis, axis, 0f);
    public static PaddleIntent FromTarget(float targetY) => new PaddleIntent(PaddleIntentKind.TargetY, 0f, targetY);
}

/// <summary>
/// Reads keyboard, mouse and touch through the Unity Input System and reduces them to one PaddleIntent per frame.
/// It never moves anything. PlayerPaddleController owns it, polls it every frame while enabled, and resets it
/// whenever it is (re)enabled (start, PLAY AGAIN, RESTART MATCH).
///
/// PRIORITY (evaluated every frame, first match wins; the device that was used last keeps control):
///   1. TOUCH    - a valid gameplay finger is down. Its height is the target.
///   2. KEYBOARD - W or S is held. W/S always beat the mouse, so the two never fight.
///   3. MOUSE    - the cursor moved (>= mouseMoveThresholdPixels) since the previous frame. Its height is the target.
///   otherwise the previous mode stays: Keyboard = axis 0 (paddle stops), Pointer = keep following the cursor
///   height, Touch = finger released, the paddle stops. A resting mouse never pulls the paddle after keyboard play.
///
/// GAMEPLAY TOUCH: a touch counts only if it starts inside the camera's viewport and NOT over interactive UI
/// (whatever a click would be delivered to: buttons, sliders, toggles - checked with the UI raycast, no hard-coded
/// buttons). That is decided once when the finger first appears and stays fixed, so a finger that began on a
/// button never becomes paddle input by sliding off it. Only ONE finger steers at a time: the earliest valid
/// finger that is still down. Extra fingers are ignored; if the steering finger lifts, the next earliest valid
/// finger takes over (its height only sets a new target - the paddle still moves at normal speed).
/// Fingers that are already down when the reader is reset are ignored until they are lifted.
/// </summary>
public class PaddleInputReader : MonoBehaviour
{
    [Tooltip("Camera that maps the screen to the court. Defaults to Camera.main.")]
    [SerializeField] private Camera gameplayCamera;
    [Tooltip("Cursor movement (pixels between two frames) that counts as 'the mouse was moved'.")]
    [SerializeField, Min(0f)] private float mouseMoveThresholdPixels = 1f;

    private struct TrackedTouch
    {
        public int Id;
        public bool Valid;
        public int Order;
        public Vector2 Position;
        public bool Seen;
    }

    private readonly List<TrackedTouch> touches = new List<TrackedTouch>(8);
    private readonly HashSet<int> blockedTouchIds = new HashSet<int>();
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(8);
    private int orderCounter;
    private Vector2 lastMousePosition;
    private bool haveMousePosition;

    public PaddleInputMode ActiveMode { get; private set; }
    public PaddleIntent Intent { get; private set; }
    /// <summary>Id of the finger steering the paddle, or -1.</summary>
    public int ActiveTouchId { get; private set; } = -1;
    /// <summary>Fingers currently down that are valid gameplay touches (steering one included).</summary>
    public int ValidTouchCount { get; private set; }
    /// <summary>Fingers currently down in total (valid or not).</summary>
    public int TouchCount => touches.Count;

    /// <summary>Forgets modes and fingers so nothing carries over a restart. Fingers still down stay ignored until lifted.</summary>
    public void ResetState()
    {
        ActiveMode = PaddleInputMode.None;
        Intent = PaddleIntent.None;
        ActiveTouchId = -1;
        ValidTouchCount = 0;
        touches.Clear();
        blockedTouchIds.Clear();
        orderCounter = 0;

        Touchscreen screen = Touchscreen.current;
        if (screen != null)
        {
            var list = screen.touches;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].press.isPressed) blockedTouchIds.Add(list[i].touchId.ReadValue());
            }
        }

        Mouse mouse = Mouse.current;
        haveMousePosition = mouse != null;
        if (haveMousePosition) lastMousePosition = mouse.position.ReadValue();
    }

    /// <summary>Reads all devices once and returns this frame's intent. Target heights are clamped to [minY, maxY].</summary>
    public PaddleIntent Poll(float minY, float maxY)
    {
        bool touching = PollTouches(out Vector2 touchPosition);

        Keyboard keyboard = Keyboard.current;
        bool keyboardHeld = false;
        float axis = 0f;
        if (keyboard != null)
        {
            bool up = keyboard.wKey.isPressed, down = keyboard.sKey.isPressed;
            keyboardHeld = up || down;
            axis = (up ? 1f : 0f) - (down ? 1f : 0f);
        }

        Mouse mouse = Mouse.current;
        Vector2 mousePosition = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        bool mouseMoved = false;
        if (mouse != null)
        {
            mouseMoved = haveMousePosition && (mousePosition - lastMousePosition).sqrMagnitude >= mouseMoveThresholdPixels * mouseMoveThresholdPixels;
            lastMousePosition = mousePosition;
            haveMousePosition = true;
        }

        if (touching) ActiveMode = PaddleInputMode.Touch;
        else if (keyboardHeld) ActiveMode = PaddleInputMode.Keyboard;
        else if (mouseMoved) ActiveMode = PaddleInputMode.Pointer;

        switch (ActiveMode)
        {
            case PaddleInputMode.Touch:
                Intent = touching ? PaddleIntent.FromTarget(Mathf.Clamp(ScreenToWorldY(touchPosition), minY, maxY)) : PaddleIntent.None;
                break;
            case PaddleInputMode.Keyboard:
                Intent = PaddleIntent.FromAxis(axis);
                break;
            case PaddleInputMode.Pointer:
                Intent = PaddleIntent.FromTarget(Mathf.Clamp(ScreenToWorldY(mousePosition), minY, maxY));
                break;
            default:
                Intent = PaddleIntent.None;
                break;
        }
        return Intent;
    }

    // ---------------------------------------------------------------- touch

    /// <summary>Updates the tracked fingers and returns the steering finger's screen position, if there is one.</summary>
    private bool PollTouches(out Vector2 position)
    {
        position = default;
        for (int i = 0; i < touches.Count; i++)
        {
            TrackedTouch t = touches[i];
            t.Seen = false;
            touches[i] = t;
        }

        Touchscreen screen = Touchscreen.current;
        if (screen != null)
        {
            var list = screen.touches;
            for (int i = 0; i < list.Count; i++)
            {
                TouchControl control = list[i];
                if (!control.press.isPressed) continue;

                int id = control.touchId.ReadValue();
                Vector2 pos = control.position.ReadValue();
                int index = FindTouch(id);
                if (index < 0)
                {
                    bool valid = !blockedTouchIds.Contains(id) && IsGameplayPoint(pos);
                    touches.Add(new TrackedTouch { Id = id, Valid = valid, Order = ++orderCounter });
                    index = touches.Count - 1;
                }

                TrackedTouch t = touches[index];
                t.Position = pos;
                t.Seen = true;
                touches[index] = t;
            }
        }

        // Fingers that are no longer down are forgotten; a blocked id is freed once its finger is lifted.
        for (int i = touches.Count - 1; i >= 0; i--)
        {
            if (!touches[i].Seen) touches.RemoveAt(i);
        }
        if (blockedTouchIds.Count > 0)
        {
            blockedTouchIds.RemoveWhere(id => !IsTouchDown(id));
        }

        int best = -1;
        ValidTouchCount = 0;
        for (int i = 0; i < touches.Count; i++)
        {
            if (!touches[i].Valid) continue;
            ValidTouchCount++;
            if (best < 0 || touches[i].Order < touches[best].Order) best = i;
        }

        if (best < 0)
        {
            ActiveTouchId = -1;
            return false;
        }

        ActiveTouchId = touches[best].Id;
        position = touches[best].Position;
        return true;
    }

    private int FindTouch(int id)
    {
        for (int i = 0; i < touches.Count; i++)
        {
            if (touches[i].Id == id) return i;
        }
        return -1;
    }

    private static bool IsTouchDown(int id)
    {
        Touchscreen screen = Touchscreen.current;
        if (screen == null) return false;

        var list = screen.touches;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].press.isPressed && list[i].touchId.ReadValue() == id) return true;
        }
        return false;
    }

    /// <summary>Inside the gameplay camera's viewport and not over interactive UI.</summary>
    private bool IsGameplayPoint(Vector2 screenPosition)
    {
        Camera cam = GameplayCamera();
        if (cam != null && !cam.pixelRect.Contains(screenPosition)) return false;
        return !IsOverInteractiveUi(screenPosition);
    }

    /// <summary>
    /// True if a press here would be delivered to a UI handler (button, slider, toggle...). Mirrors what the UI
    /// input module does: the topmost raycast hit, then up its parents for a pointer handler.
    /// </summary>
    private bool IsOverInteractiveUi(Vector2 screenPosition)
    {
        EventSystem system = EventSystem.current;
        if (system == null) return false;

        raycastResults.Clear();
        system.RaycastAll(new PointerEventData(system) { position = screenPosition }, raycastResults);
        if (raycastResults.Count == 0) return false;

        GameObject top = raycastResults[0].gameObject;
        return top != null && (ExecuteEvents.GetEventHandler<IPointerDownHandler>(top) != null
                               || ExecuteEvents.GetEventHandler<IPointerClickHandler>(top) != null);
    }

    // ---------------------------------------------------------------- mapping

    private Camera GameplayCamera()
    {
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        return gameplayCamera;
    }

    /// <summary>Screen pixel height -> world Y on the court (orthographic camera; X is irrelevant).</summary>
    public float ScreenToWorldY(Vector2 screenPosition)
    {
        Camera cam = GameplayCamera();
        if (cam == null) return 0f;

        float depth = Mathf.Abs(cam.transform.position.z);
        return cam.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth)).y;
    }
}
