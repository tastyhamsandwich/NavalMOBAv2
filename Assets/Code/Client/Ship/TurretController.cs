using NavalMOBA.Common.SODefs;
using UnityEngine;

namespace NavalMOBA.Client.Ship
{
    /// <summary>
    /// Drives one gun mount's training and elevation. This is an actuator, not a fire
    /// control system: it exposes clamped setpoints and slews toward them at the rates in
    /// its TurretDefinition. Nothing here reads input or decides what to shoot at.
    ///
    /// Manual and Automatic FCS both write the same two setpoints, which is the whole point
    /// of the split. Manual accumulates them from held keys, Automatic computes them from a
    /// firing solution, and neither can produce motion the mount is not capable of.
    ///
    /// Angles are local to the mount. Traverse is degrees from the station's rest bearing,
    /// positive to starboard. Elevation is degrees above horizontal, positive up.
    /// </summary>
    public sealed class TurretController : MonoBehaviour
    {
        [Header("Pivots")]
        [Tooltip("Rotates in yaw about its local Y axis. Carries the turret mesh.")]
        [SerializeField] private Transform yawPivot;

        [Tooltip("Rotates in pitch about its local X axis. Child of the yaw pivot.")]
        [SerializeField] private Transform pitchPivot;

        [Tooltip("Projectile origins, one per barrel. Order matters for salvo sequencing.")]
        [SerializeField] private Transform[] muzzles;

        private TurretDefinition definition;
        private GunMountSlot station;

        private float restBearingDegrees;
        private float traverseDegrees;
        private float elevationDegrees;
        private float targetTraverseDegrees;
        private float targetElevationDegrees;

        public TurretDefinition Definition => definition;
        public GunMountSlot Station => station;
        public string StationName => station != null ? station.SlotName : string.Empty;
        public BatteryRole Battery => station != null ? station.Battery : BatteryRole.Primary;
        public Transform[] Muzzles => muzzles;

        /// <summary>Current traverse, in degrees from the station's rest bearing.</summary>
        public float TraverseDegrees => traverseDegrees;

        /// <summary>Current elevation above horizontal, in degrees.</summary>
        public float ElevationDegrees => elevationDegrees;

        /// <summary>Bearing the mount is trained to, relative to the ship's bow, 0 to 360.</summary>
        public float BearingRelativeToBowDegrees => Mathf.Repeat(restBearingDegrees + traverseDegrees, 360f);

        /// <summary>True once both axes have reached their setpoints within a tolerance the eye cannot see.</summary>
        public bool IsOnTarget =>
            Mathf.Abs(targetTraverseDegrees - traverseDegrees) < 0.05f &&
            Mathf.Abs(targetElevationDegrees - elevationDegrees) < 0.05f;

        public bool IsInitialized => definition != null && station != null && yawPivot != null && pitchPivot != null;

        /// <summary>
        /// Called by ShipMountSystem immediately after instantiation. Fails loudly rather
        /// than hunting for pivots by name at runtime: if a turret prefab is wired wrong,
        /// that is an authoring bug and should be visible the first time it spawns.
        /// </summary>
        public bool Initialize(TurretDefinition turretDefinition, GunMountSlot mountStation)
        {
            definition = turretDefinition;
            station = mountStation;

            if (definition == null || station == null)
            {
                Debug.LogError($"TurretController on '{name}': Initialize called with a null definition or station.", this);
                return false;
            }

            if (yawPivot == null || pitchPivot == null)
            {
                Debug.LogError($"TurretController on '{name}': yaw or pitch pivot is not assigned on the prefab.", this);
                return false;
            }

            if (muzzles == null || muzzles.Length == 0)
            {
                Debug.LogWarning($"TurretController on '{name}': no muzzles assigned, this mount cannot spawn projectiles.", this);
            }
            else if (muzzles.Length != definition.BarrelCount)
            {
                Debug.LogWarning($"TurretController on '{name}': {muzzles.Length} muzzles assigned but '{definition.DisplayName}' declares {definition.BarrelCount} barrels.", this);
            }

            restBearingDegrees = station.RestBearingDegrees;

            // Rest the mount trained fore or aft and level, matching how a ship sits when
            // not in action. Starting at the elevation midpoint would leave guns cocked at
            // 35 degrees on a ship at anchor.
            traverseDegrees = station.ClampTraverse(0f);
            elevationDegrees = Mathf.Clamp(0f, definition.MinElevationDegrees, definition.MaxElevationDegrees);
            targetTraverseDegrees = traverseDegrees;
            targetElevationDegrees = elevationDegrees;

            ApplyPivots();
            return true;
        }

        /// <summary>
        /// Sets the traverse setpoint, clamped to the station's arc. Returns false when the
        /// request was outside the arc, which the FCS can surface as "will not bear".
        /// </summary>
        public bool SetTargetTraverse(float degreesFromRest)
        {
            if (station == null)
            {
                return false;
            }

            float clamped = station.ClampTraverse(degreesFromRest);
            targetTraverseDegrees = clamped;
            return Mathf.Abs(clamped - degreesFromRest) < 0.01f;
        }

        /// <summary>Sets the elevation setpoint, clamped to the mount's elevation limits.</summary>
        public bool SetTargetElevation(float degreesAboveHorizontal)
        {
            if (definition == null)
            {
                return false;
            }

            float clamped = Mathf.Clamp(degreesAboveHorizontal, definition.MinElevationDegrees, definition.MaxElevationDegrees);
            targetElevationDegrees = clamped;
            return Mathf.Abs(clamped - degreesAboveHorizontal) < 0.01f;
        }

        /// <summary>
        /// Moves the traverse setpoint at the mount's own training rate, for rate-style
        /// manual control where the player holds a key. Keeps manual and automatic on one
        /// code path: this only nudges the setpoint, it never moves the mount directly.
        /// </summary>
        public void NudgeTraverse(float direction, float deltaTime)
        {
            if (definition == null)
            {
                return;
            }

            SetTargetTraverse(targetTraverseDegrees + direction * definition.TraverseRateDegreesPerSecond * deltaTime);
        }

        public void NudgeElevation(float direction, float deltaTime)
        {
            if (definition == null)
            {
                return;
            }

            SetTargetElevation(targetElevationDegrees + direction * definition.ElevationRateDegreesPerSecond * deltaTime);
        }

        /// <summary>
        /// Converts a world bearing into this station's traverse frame and sets it.
        /// Takes the ship's heading rather than holding a reference to the hull, so the
        /// actuator stays independent of the ship and is trivially testable.
        /// </summary>
        public bool SetTargetWorldBearing(float worldBearingDegrees, float shipHeadingDegrees)
        {
            float relativeToBow = Mathf.DeltaAngle(shipHeadingDegrees, worldBearingDegrees);
            return SetTargetTraverse(Mathf.DeltaAngle(restBearingDegrees, relativeToBow));
        }

        /// <summary>Whether the station's arc can bear on a world bearing at all, without moving.</summary>
        public bool CanBearOn(float worldBearingDegrees, float shipHeadingDegrees)
        {
            if (station == null)
            {
                return false;
            }

            float relativeToBow = Mathf.DeltaAngle(shipHeadingDegrees, worldBearingDegrees);
            float required = Mathf.DeltaAngle(restBearingDegrees, relativeToBow);
            return required >= station.MinTraverseDegrees && required <= station.MaxTraverseDegrees;
        }

        /// <summary>
        /// Advances both axes toward their setpoints. Driven by ShipMountSystem on the
        /// simulation tick, not by Update, so turret motion is deterministic and replayable
        /// alongside ship movement.
        /// </summary>
        public void Slew(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            traverseDegrees = Mathf.MoveTowards(
                traverseDegrees,
                targetTraverseDegrees,
                definition.TraverseRateDegreesPerSecond * deltaTime);

            elevationDegrees = Mathf.MoveTowards(
                elevationDegrees,
                targetElevationDegrees,
                definition.ElevationRateDegreesPerSecond * deltaTime);

            ApplyPivots();
        }

        private void ApplyPivots()
        {
            // The rest bearing is folded into the yaw pivot rather than pre-rotating the
            // hardpoint, so an aft mount is authored as restBearing 180 and the ship prefab
            // keeps every hardpoint at identity rotation.
            yawPivot.localRotation = Quaternion.Euler(0f, restBearingDegrees + traverseDegrees, 0f);

            // Negated: a positive rotation about local X pitches the forward axis down, and
            // elevation is measured upward.
            pitchPivot.localRotation = Quaternion.Euler(-elevationDegrees, 0f, 0f);
        }
    }
}
