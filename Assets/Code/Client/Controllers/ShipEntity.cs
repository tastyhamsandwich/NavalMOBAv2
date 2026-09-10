using NavalMOBA.Common;
using NavalMOBA.Common.SODefs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NavalMOBA.Client.Controllers
{
    /// <summary>
    /// Client-side driver for ship movement. Captures the unorthodox control scheme
    /// (F/V throttle with EEP overboost on a second press at max throttle, right-click to
    /// set a move-to waypoint) and hands it to the shared, deterministic
    /// ShipMovementSimulation in Common. This class owns no movement math itself -- only
    /// input capture, applying the resulting state to the transform, and waypoint
    /// visualization. Once networking lands, input capture here becomes an RPC to the
    /// server instead of a direct simulation call.
    /// </summary>
    public sealed class ShipEntity : MonoBehaviour, ITickable
    {
        [Header("Waypoint Visuals")]
        [Tooltip("World Y of the sea surface. Move orders are projected onto this plane.")]
        [SerializeField] private float seaLevel;
        [SerializeField] private GameObject waypointIndicatorPrefab;
        [SerializeField] private Material waypointLineMaterial;
        [SerializeField] private Color waypointLineColor = Color.cyan;
        [SerializeField] private float waypointLineWidth = 2f;

        private InputActions inputActions;
        private ShipMovementState state;

        private ShipDefinition definition;
        private ShipMovementConfig config;

        private LineRenderer waypointLine;
        private GameObject waypointIndicator;

        private bool pendingWaypointRequested;
        private Vector3 pendingWaypointPosition;

        private float measuredTurnRateDegreesPerSecond;

        /// <summary>Current speed over ground in knots. Negative when going astern.</summary>
        public float SpeedKnots => state.CurrentSpeed * ShipMovementConfig.MetersPerSecondToKnots;

        /// <summary>
        /// Heading change actually applied last tick, in degrees per second. This is measured
        /// output, not the capability figure -- it reads zero when the ship is holding course,
        /// which is what you want when diagnosing why a turn is not happening.
        /// </summary>
        public float TurnRateDegreesPerSecond => measuredTurnRateDegreesPerSecond;

        /// <summary>Turn rate the hull could sustain at its current speed. Upper bound on the above.</summary>
        public float MaxTurnRateDegreesPerSecond => state.CurrentTurnRateDegreesPerSecond;

        /// <summary>Engine order, -100 (full astern) to 115 (EEP overboost).</summary>
        public float ThrottlePercent => state.CurrentThrottle;

        public float HeadingDegrees => state.HeadingDegrees;
        public bool EepActive => state.EepActive;
        public bool EepOnCooldown => state.EepOnCooldown;
        public float EepTimerRemaining => state.EepActive ? state.EepDurationRemaining : state.EepCooldownRemaining;
        public bool HasWaypoint => state.HasWaypoint;
        public float MaxForwardSpeedKnots => config != null ? config.maxForwardSpeedKnots : 0f;

        /// <summary>The hull this ship was spawned from. Null until <see cref="ApplyDefinition"/> runs.</summary>
        public ShipDefinition Definition => definition;

        /// <summary>
        /// Adopts the hull's authored mobility. Called by the spawner immediately after
        /// instantiation, before the first simulation tick. Until this runs the ship has no
        /// movement config at all and will not move, which is deliberate: a ship that silently
        /// fell back to hardcoded defaults would look like it worked and quietly steam at the
        /// wrong speed for the rest of the match.
        /// </summary>
        public void ApplyDefinition(ShipDefinition shipDefinition)
        {
            if (shipDefinition == null)
            {
                Debug.LogError($"ShipEntity on '{name}': ApplyDefinition called with a null definition.", this);
                return;
            }

            definition = shipDefinition;
            config = shipDefinition.CreateMovementConfig();
        }

        private void Awake()
        {
            inputActions = new InputActions();
            state.Position = transform.position;
            state.HeadingDegrees = transform.eulerAngles.y;
        }

        private void Start()
        {
            if (config == null)
            {
                Debug.LogError($"ShipEntity on '{name}': no ShipDefinition was applied, this ship will not move. The spawner must call ApplyDefinition.", this);
            }

            SetupWaypointLine();
        }

        private void OnEnable()
        {
            inputActions.Ship.Enable();
            SimulationTicker.Instance?.Register(this);
        }

        private void OnDisable()
        {
            inputActions.Ship.Disable();
            SimulationTicker.Instance?.Unregister(this);
        }

        private void OnDestroy()
        {
            inputActions?.Dispose();
        }

        private void Update()
        {
            // Right-click detection is per-frame (not per-tick) so a quick click is never
            // missed; the resulting world point is buffered and consumed on the next tick.
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());

            // The sea is an analytic plane, not geometry. Physics.Raycast would require the
            // scene to carry a collider for the water surface (it carries none, the sea is a
            // SpriteRenderer) and would tie move orders to render-side setup that a headless
            // server will not have. Intersecting the plane directly is exact and collider-free.
            var seaPlane = new Plane(Vector3.up, new Vector3(0f, seaLevel, 0f));
            if (seaPlane.Raycast(ray, out float distanceAlongRay))
            {
                pendingWaypointRequested = true;
                pendingWaypointPosition = ray.GetPoint(distanceAlongRay);
            }
        }

        public void Tick(float deltaTime)
        {
            if (config == null)
            {
                return;
            }

            ShipMovementInput input = BuildInput();
            bool hadWaypointBefore = state.HasWaypoint;
            float headingBefore = state.HeadingDegrees;

            state = ShipMovementSimulation.Tick(state, input, config, deltaTime);

            measuredTurnRateDegreesPerSecond = deltaTime > 0f
                ? Mathf.DeltaAngle(headingBefore, state.HeadingDegrees) / deltaTime
                : 0f;

            transform.position = state.Position;
            transform.rotation = Quaternion.Euler(0f, state.HeadingDegrees, 0f);

            if (input.SetWaypointRequested)
            {
                ShowWaypointIndicator(state.WaypointPosition);
            }

            if (state.HasWaypoint)
            {
                UpdateWaypointLine();
            }
            else if (hadWaypointBefore)
            {
                HideWaypointVisuals();
            }
        }

        private ShipMovementInput BuildInput()
        {
            var input = new ShipMovementInput
            {
                ThrottleUpHeld = inputActions.Ship.IncreaseSpeed.IsPressed(),
                ThrottleDownHeld = inputActions.Ship.DecreaseSpeed.IsPressed()
            };

            if (pendingWaypointRequested)
            {
                input.SetWaypointRequested = true;
                input.WaypointWorldPosition = pendingWaypointPosition;
                pendingWaypointRequested = false;
            }

            return input;
        }

        private void SetupWaypointLine()
        {
            var lineObject = new GameObject("WaypointLine");
            lineObject.transform.SetParent(transform);

            waypointLine = lineObject.AddComponent<LineRenderer>();
            // UpdateWaypointLine feeds world coordinates, but LineRenderer interprets
            // positions in local space by default. Without this the line is drawn in the
            // ship's local space and swings wildly as the ship turns.
            waypointLine.useWorldSpace = true;
            waypointLine.textureMode = LineTextureMode.Tile;
            waypointLine.alignment = LineAlignment.View;
            waypointLine.material = waypointLineMaterial != null ? waypointLineMaterial : new Material(Shader.Find("Sprites/Default"));
            waypointLine.startColor = waypointLineColor;
            waypointLine.endColor = waypointLineColor;
            waypointLine.startWidth = waypointLineWidth;
            waypointLine.endWidth = waypointLineWidth;
            waypointLine.positionCount = 2;
            waypointLine.enabled = false;
        }

        private void ShowWaypointIndicator(Vector3 position)
        {
            if (waypointIndicator != null)
            {
                Destroy(waypointIndicator);
            }

            waypointIndicator = waypointIndicatorPrefab != null
                ? Instantiate(waypointIndicatorPrefab, position, Quaternion.identity)
                : CreateDefaultWaypointIndicator(position);

            if (waypointLine != null)
            {
                waypointLine.enabled = true;
            }
        }

        private GameObject CreateDefaultWaypointIndicator(Vector3 position)
        {
            var indicator = new GameObject("WaypointIndicator");
            indicator.transform.position = position;

            // Sized for a ~114m destroyer viewed from a few hundred metres up. A 1m marker
            // is a sub-pixel dot at that framing.
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.transform.SetParent(indicator.transform);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localScale = new Vector3(25f, 0.25f, 25f);
            ring.GetComponent<Renderer>().material.color = waypointLineColor;
            Destroy(ring.GetComponent<Collider>());

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(indicator.transform);
            marker.transform.localPosition = new Vector3(0f, 6f, 0f);
            marker.transform.localScale = Vector3.one * 6f;
            marker.GetComponent<Renderer>().material.color = Color.white;
            Destroy(marker.GetComponent<Collider>());

            return indicator;
        }

        private void UpdateWaypointLine()
        {
            if (waypointLine == null)
            {
                return;
            }

            waypointLine.SetPosition(0, transform.position);
            waypointLine.SetPosition(1, state.WaypointPosition);
        }

        private void HideWaypointVisuals()
        {
            if (waypointIndicator != null)
            {
                Destroy(waypointIndicator);
                waypointIndicator = null;
            }

            if (waypointLine != null)
            {
                waypointLine.enabled = false;
            }
        }
    }
}
