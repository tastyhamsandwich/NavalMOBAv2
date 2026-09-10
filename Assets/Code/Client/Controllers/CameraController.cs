using NavalMOBA.Client.Controllers;
using UnityEngine;
using UnityEngine.InputSystem;

public enum CameraMode
{
    FreeCamera,        // Focus point pans freely, ignores the ship
    ShipFollowing,     // Focus point tracks the ship, pan offset allowed
    ShipLocked         // Focus point tracks the ship exactly, no panning
}

/// <summary>
/// Fixed-orientation dolly camera.
///
/// The view vector is derived ONCE from <see cref="followOffset"/> and never changes.
/// Following, panning and zooming only ever translate the camera, they never re-aim it,
/// so the horizon cannot swing or roll under the player. The camera starts aimed at the
/// ship but is not slaved to it afterwards.
///
/// Position is reconstructed every frame from exactly two values:
///
///   focusPoint    - a point on a fixed horizontal plane (y == focusHeight). Following and
///                   panning move this in X/Z only, so neither can change the camera's Y.
///   dollyDistance - how far back along the fixed view vector the camera sits. Zoom changes
///                   only this, sliding the camera along its track. It is the only input
///                   that changes the camera's Y.
///
///   position = focusPoint - fixedForward * dollyDistance
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Camera Mode")]
    [SerializeField] private CameraMode currentCameraMode = CameraMode.ShipFollowing;

    [Header("Framing")]
    [Tooltip("Offset from the ship at startup. Defines both the permanent view angle and the starting dolly distance.")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 90f, -70f);
    [Tooltip("Seconds for the focus point to catch up to the ship. 0 = rigidly locked.")]
    [SerializeField] private float followSmoothTime = 0.15f;

    [Header("Zoom (dolly along the fixed view vector)")]
    [Tooltip("Metres of dolly travel per scroll notch.")]
    [SerializeField] private float zoomStepDistance = 15f;
    [SerializeField] private float minDollyDistance = 40f;
    [SerializeField] private float maxDollyDistance = 600f;
    [SerializeField] private float zoomSmoothTime = 0.12f;

    [Header("Panning")]
    [SerializeField] private float keyboardPanSpeed = 40f;
    [SerializeField] private float mouseDragPanSensitivity = 0.35f;
    [Tooltip("Scale pan speed by zoom so panning feels the same close in as far out.")]
    [SerializeField] private bool panSpeedScalesWithZoom = true;
    [Tooltip("How far the focus point may pan from the ship while following.")]
    [SerializeField] private float maxPanDistanceFromShip = 400f;

    [Header("Edge Panning")]
    [SerializeField] private bool enableMouseEdgePanning = true;
    [SerializeField] private float edgeThreshold = 24f;
    [SerializeField] private float edgePanSpeedMultiplier = 1f;

    [Header("Ship")]
    [SerializeField] private Transform playerShip;

    [Header("Debug")]
    [SerializeField] private bool showDebugOverlay = true;

    private ShipEntity cachedShipTelemetry;
    private Transform cachedTelemetrySource;

    // Fixed for the lifetime of the camera.
    private Quaternion fixedRotation;
    private Vector3 fixedForward;
    private Vector3 flatForward;
    private Vector3 flatRight;
    private float referenceDollyDistance;

    private float focusHeight;
    private Vector3 focusPoint;
    private Vector3 focusVelocity;
    private Vector3 panOffset;

    private float dollyDistance;
    private float targetDollyDistance;
    private float dollyVelocity;

    private bool panLeftHeld;
    private bool panRightHeld;
    private bool panUpHeld;
    private bool panDownHeld;
    private bool isMousePanning;

    private InputActions inputActions;

    private static Mouse CurrentMouse => Mouse.current;

    private void Awake()
    {
        inputActions = new InputActions();

        // The view vector looks from followOffset back toward the point it is measured from.
        // Computed once here and never touched again.
        fixedForward = (-followOffset).normalized;
        if (fixedForward.sqrMagnitude < 0.0001f)
        {
            fixedForward = Vector3.forward;
        }

        Vector3 rotationUp = Mathf.Abs(Vector3.Dot(fixedForward, Vector3.up)) > 0.999f
            ? Vector3.forward
            : Vector3.up;
        fixedRotation = Quaternion.LookRotation(fixedForward, rotationUp);

        flatForward = Vector3.ProjectOnPlane(fixedForward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }
        flatRight = Vector3.Cross(Vector3.up, flatForward);

        referenceDollyDistance = Mathf.Max(1f, followOffset.magnitude);
        targetDollyDistance = Mathf.Clamp(followOffset.magnitude, minDollyDistance, maxDollyDistance);
        dollyDistance = targetDollyDistance;

        if (playerShip != null)
        {
            BindFocusToShip();
        }
        else
        {
            focusHeight = 0f;
            focusPoint = transform.position + fixedForward * dollyDistance;
            focusPoint.y = focusHeight;
        }

        ApplyTransform();
    }

    private void OnEnable()
    {
        inputActions.Camera.Enable();

        inputActions.Camera.PanLeft.performed += OnPanLeftPerformed;
        inputActions.Camera.PanLeft.canceled += OnPanLeftCanceled;
        inputActions.Camera.PanRight.performed += OnPanRightPerformed;
        inputActions.Camera.PanRight.canceled += OnPanRightCanceled;
        inputActions.Camera.PanUp.performed += OnPanUpPerformed;
        inputActions.Camera.PanUp.canceled += OnPanUpCanceled;
        inputActions.Camera.PanDown.performed += OnPanDownPerformed;
        inputActions.Camera.PanDown.canceled += OnPanDownCanceled;

        inputActions.Camera.PlayerFollow.performed += OnCycleCameraMode;
        inputActions.Camera.ToggleEdgePanning.performed += OnToggleEdgePanning;
    }

    private void OnDisable()
    {
        inputActions.Camera.PanLeft.performed -= OnPanLeftPerformed;
        inputActions.Camera.PanLeft.canceled -= OnPanLeftCanceled;
        inputActions.Camera.PanRight.performed -= OnPanRightPerformed;
        inputActions.Camera.PanRight.canceled -= OnPanRightCanceled;
        inputActions.Camera.PanUp.performed -= OnPanUpPerformed;
        inputActions.Camera.PanUp.canceled -= OnPanUpCanceled;
        inputActions.Camera.PanDown.performed -= OnPanDownPerformed;
        inputActions.Camera.PanDown.canceled -= OnPanDownCanceled;

        inputActions.Camera.PlayerFollow.performed -= OnCycleCameraMode;
        inputActions.Camera.ToggleEdgePanning.performed -= OnToggleEdgePanning;

        inputActions.Camera.Disable();

        panLeftHeld = false;
        panRightHeld = false;
        panUpHeld = false;
        panDownHeld = false;
    }

    private void OnDestroy()
    {
        inputActions?.Dispose();
    }

    private void OnPanLeftPerformed(InputAction.CallbackContext _) => panLeftHeld = true;
    private void OnPanLeftCanceled(InputAction.CallbackContext _) => panLeftHeld = false;
    private void OnPanRightPerformed(InputAction.CallbackContext _) => panRightHeld = true;
    private void OnPanRightCanceled(InputAction.CallbackContext _) => panRightHeld = false;
    private void OnPanUpPerformed(InputAction.CallbackContext _) => panUpHeld = true;
    private void OnPanUpCanceled(InputAction.CallbackContext _) => panUpHeld = false;
    private void OnPanDownPerformed(InputAction.CallbackContext _) => panDownHeld = true;
    private void OnPanDownCanceled(InputAction.CallbackContext _) => panDownHeld = false;

    private void OnCycleCameraMode(InputAction.CallbackContext _) => CycleCameraMode();
    private void OnToggleEdgePanning(InputAction.CallbackContext _) => enableMouseEdgePanning = !enableMouseEdgePanning;

    private void Update()
    {
        UpdateZoom();
        UpdateFocusPoint();
        ApplyTransform();
    }

    private void UpdateZoom()
    {
        float scroll = inputActions.Camera.Zoom.ReadValue<float>();

        // Scroll is an impulse, not a continuous axis. Its raw magnitude is platform
        // dependent (often +-120, sometimes +-1), so step a fixed distance per notch and
        // never scale it by deltaTime or zoom becomes both framerate and platform dependent.
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetDollyDistance = Mathf.Clamp(
                targetDollyDistance - Mathf.Sign(scroll) * zoomStepDistance,
                minDollyDistance,
                maxDollyDistance);
        }

        dollyDistance = Mathf.SmoothDamp(dollyDistance, targetDollyDistance, ref dollyVelocity, zoomSmoothTime);
    }

    private void UpdateFocusPoint()
    {
        Vector3 panDelta = ReadPanDelta();
        panDelta.y = 0f;

        if (currentCameraMode == CameraMode.FreeCamera || playerShip == null)
        {
            focusPoint += panDelta;
            focusPoint.y = focusHeight;
            return;
        }

        if (currentCameraMode == CameraMode.ShipLocked)
        {
            panOffset = Vector3.zero;
        }
        else
        {
            panOffset += panDelta;
            panOffset.y = 0f;
            if (panOffset.magnitude > maxPanDistanceFromShip)
            {
                panOffset = panOffset.normalized * maxPanDistanceFromShip;
            }
        }

        Vector3 target = new Vector3(playerShip.position.x, focusHeight, playerShip.position.z) + panOffset;

        focusPoint = followSmoothTime > 0f
            ? Vector3.SmoothDamp(focusPoint, target, ref focusVelocity, followSmoothTime)
            : target;
        focusPoint.y = focusHeight;
    }

    private Vector3 ReadPanDelta()
    {
        if (currentCameraMode == CameraMode.ShipLocked)
        {
            isMousePanning = false;
            return Vector3.zero;
        }

        float zoomScale = panSpeedScalesWithZoom ? dollyDistance / referenceDollyDistance : 1f;

        Vector2 axis = new Vector2(
            (panRightHeld ? 1f : 0f) - (panLeftHeld ? 1f : 0f),
            (panUpHeld ? 1f : 0f) - (panDownHeld ? 1f : 0f));
        axis += ReadEdgePanAxis();
        axis = Vector2.ClampMagnitude(axis, 1f);

        Vector3 delta = (flatRight * axis.x + flatForward * axis.y)
                        * keyboardPanSpeed * zoomScale * Time.deltaTime;

        // Middle-mouse drag. The MousePan composite already gates the delta behind the
        // middle button, so a non-zero read means the player is dragging.
        Vector2 drag = inputActions.Camera.MousePan.ReadValue<Vector2>();
        isMousePanning = drag.sqrMagnitude > 0.0001f;
        if (isMousePanning)
        {
            // Negated so the world follows the cursor rather than fleeing it.
            delta += (flatRight * -drag.x + flatForward * -drag.y)
                     * mouseDragPanSensitivity * zoomScale;
        }

        return delta;
    }

    private Vector2 ReadEdgePanAxis()
    {
        Mouse mouse = CurrentMouse;
        if (!enableMouseEdgePanning || mouse == null)
        {
            return Vector2.zero;
        }

        Vector2 p = mouse.position.ReadValue();
        if (p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height)
        {
            return Vector2.zero;
        }

        Vector2 axis = Vector2.zero;

        if (p.x <= edgeThreshold)
        {
            axis.x = -Mathf.Clamp01((edgeThreshold - p.x) / edgeThreshold);
        }
        else if (p.x >= Screen.width - edgeThreshold)
        {
            axis.x = Mathf.Clamp01((p.x - (Screen.width - edgeThreshold)) / edgeThreshold);
        }

        if (p.y <= edgeThreshold)
        {
            axis.y = -Mathf.Clamp01((edgeThreshold - p.y) / edgeThreshold);
        }
        else if (p.y >= Screen.height - edgeThreshold)
        {
            axis.y = Mathf.Clamp01((p.y - (Screen.height - edgeThreshold)) / edgeThreshold);
        }

        return axis * edgePanSpeedMultiplier;
    }

    private void ApplyTransform()
    {
        Vector3 anchor = new Vector3(focusPoint.x, focusHeight, focusPoint.z);
        transform.SetPositionAndRotation(anchor - fixedForward * dollyDistance, fixedRotation);
    }

    private void BindFocusToShip()
    {
        focusHeight = playerShip.position.y;
        panOffset = Vector3.zero;
        focusVelocity = Vector3.zero;
        focusPoint = new Vector3(playerShip.position.x, focusHeight, playerShip.position.z);
    }

    // ---- Public interface ----

    public void SetPlayerShip(Transform ship)
    {
        playerShip = ship;
        if (ship == null)
        {
            return;
        }

        BindFocusToShip();
        ApplyTransform();
    }

    public void SetCameraMode(CameraMode mode)
    {
        // Carry the current focus across the transition so the camera never jumps.
        if (mode != CameraMode.FreeCamera && playerShip != null)
        {
            panOffset = mode == CameraMode.ShipLocked
                ? Vector3.zero
                : focusPoint - new Vector3(playerShip.position.x, focusHeight, playerShip.position.z);
            panOffset.y = 0f;
        }

        currentCameraMode = mode;
    }

    public void ResetShipPanning()
    {
        panOffset = Vector3.zero;
        focusVelocity = Vector3.zero;

        if (playerShip != null)
        {
            focusPoint = new Vector3(playerShip.position.x, focusHeight, playerShip.position.z);
            ApplyTransform();
        }
    }

    public CameraMode GetCurrentCameraMode() => currentCameraMode;

    public Vector3 GetShipPanOffset() => panOffset;

    public bool IsMousePanning() => isMousePanning;

    private void CycleCameraMode()
    {
        switch (currentCameraMode)
        {
            case CameraMode.FreeCamera:
                SetCameraMode(CameraMode.ShipFollowing);
                break;
            case CameraMode.ShipFollowing:
                SetCameraMode(CameraMode.ShipLocked);
                break;
            default:
                SetCameraMode(CameraMode.FreeCamera);
                break;
        }
    }

    private void OnGUI()
    {
        if (!showDebugOverlay || !Application.isPlaying)
        {
            return;
        }

        var style = new GUIStyle
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;

        GUILayout.BeginArea(new Rect(Screen.width - 260, 10, 250, 300));

        ShipEntity ship = ResolveShipTelemetry();
        if (ship != null)
        {
            GUILayout.Label($"Speed: {ship.SpeedKnots:F1} kn / {ship.MaxForwardSpeedKnots:F0} kn", style);
            GUILayout.Label($"Throttle: {ship.ThrottlePercent:F0}%", style);
            GUILayout.Label($"Heading: {ship.HeadingDegrees:F1}°", style);
            GUILayout.Label($"Turn Rate: {ship.TurnRateDegreesPerSecond:F1}°/s (max {ship.MaxTurnRateDegreesPerSecond:F1})", style);

            string eep = ship.EepActive
                ? $"EEP: ACTIVE {ship.EepTimerRemaining:F0}s"
                : ship.EepOnCooldown
                    ? $"EEP: cooldown {ship.EepTimerRemaining:F0}s"
                    : "EEP: ready";
            GUILayout.Label(eep, style);
            GUILayout.Label($"Waypoint: {(ship.HasWaypoint ? "set" : "none")}", style);
            GUILayout.Space(6);
        }

        GUILayout.Label($"Camera Mode: {currentCameraMode}", style);
        GUILayout.Label($"Dolly: {dollyDistance:F1}m / target {targetDollyDistance:F1}m", style);
        GUILayout.Label($"Camera Y: {transform.position.y:F2}  (zoom only)", style);
        GUILayout.Label($"Pan Offset: {panOffset.magnitude:F1}m", style);
        GUILayout.Label($"Edge Pan: {(enableMouseEdgePanning ? "On" : "Off")}", style);
        GUILayout.Space(6);
        GUILayout.Label("Tab - Cycle camera modes", style);
        GUILayout.Label("End - Toggle edge panning", style);
        GUILayout.Label("Numpad 4/6/8/2 - Pan", style);
        GUILayout.Label("Middle mouse drag - Pan", style);
        GUILayout.EndArea();
    }

    /// <summary>
    /// The camera is handed the ship as a bare Transform, and the ship is spawned at runtime,
    /// so the ShipEntity is resolved lazily and re-resolved if the target changes. Cached
    /// because OnGUI runs at least twice per frame.
    /// </summary>
    private ShipEntity ResolveShipTelemetry()
    {
        if (playerShip == null)
        {
            cachedShipTelemetry = null;
            cachedTelemetrySource = null;
            return null;
        }

        if (playerShip != cachedTelemetrySource)
        {
            cachedTelemetrySource = playerShip;
            cachedShipTelemetry = playerShip.GetComponentInParent<ShipEntity>();
        }

        return cachedShipTelemetry;
    }
}
