using NavalMOBA.Common.SODefs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NavalMOBA.Client.Ship
{
    /// <summary>
    /// Manual fire control. Reads the player's gunnery keys and moves the setpoints of the
    /// currently selected mounts. It owns no rotation math and touches no transforms: every
    /// command goes through TurretController's clamped setpoints, so the player can never
    /// train a mount outside its arc or elevate past its limits.
    ///
    /// Control is rate style, not aim style. Holding a key walks the setpoint at the mount's
    /// own training rate, which means a heavy triple turret answers slower than a light
    /// single mount for free, straight out of the equipment data.
    ///
    /// Two traverse controls, both routed through the same setpoint. A and D rotate every
    /// selected mount the same rotational direction, which spreads the battery because fore
    /// and aft mounts measure traverse from opposite rest bearings. Q and E instead train the
    /// whole battery onto one beam, port and starboard respectively, which requires the two
    /// groups to rotate opposite ways.
    ///
    /// Selection replaces the nine hardcoded turret group lists in the legacy manager. Fore
    /// and aft are derived from each station's rest bearing rather than authored per hull, so
    /// adding a ship requires no group bookkeeping at all.
    /// </summary>
    [RequireComponent(typeof(ShipMountSystem))]
    public sealed class ShipFireControl : MonoBehaviour, IMountDirector
    {
        public enum GunSelection
        {
            All,
            Primary,
            Secondary,
            Forward,
            Aft
        }

        /// <summary>
        /// A station is treated as forward facing when its rest bearing is within 90 degrees
        /// of the bow. This is what makes the fore/aft split emergent from mount data.
        /// </summary>
        private const float ForwardArcHalfAngleDegrees = 90f;

        [Tooltip("Which mounts respond to gunnery input at spawn.")]
        [SerializeField] private GunSelection defaultSelection = GunSelection.All;

        private InputActions inputActions;
        private ShipMountSystem mountSystem;
        private GunSelection selection;

        /// <summary>Which group the player currently has selected.</summary>
        public GunSelection Selection => selection;

        private void Awake()
        {
            inputActions = new InputActions();
            mountSystem = GetComponent<ShipMountSystem>();
            selection = defaultSelection;
        }

        private void OnEnable()
        {
            inputActions.Ship.Enable();
            mountSystem.SetDirector(this);
        }

        private void OnDisable()
        {
            inputActions.Ship.Disable();

            // Only release control if we still hold it, so a disabled component cannot
            // detach a director that replaced it.
            if (mountSystem != null)
            {
                mountSystem.SetDirector(null);
            }
        }

        private void OnDestroy()
        {
            inputActions?.Dispose();
        }

        /// <summary>
        /// Group selection is edge triggered, so it is read per frame rather than per tick.
        /// At 30Hz a tap between ticks would otherwise be dropped.
        /// </summary>
        private void Update()
        {
            InputActions.ShipActions ship = inputActions.Ship;

            if (ship.SelectAllWeapons.WasPressedThisFrame())
            {
                selection = GunSelection.All;
            }
            else if (ship.SelectPrimaryWeapons.WasPressedThisFrame())
            {
                selection = GunSelection.Primary;
            }
            else if (ship.SelectSecondaryWeapons.WasPressedThisFrame())
            {
                selection = GunSelection.Secondary;
            }
            else if (ship.SelectFrontWeapons.WasPressedThisFrame())
            {
                selection = GunSelection.Forward;
            }
            else if (ship.SelectRearWeapons.WasPressedThisFrame())
            {
                selection = GunSelection.Aft;
            }
        }

        public void UpdateSetpoints(float deltaTime)
        {
            InputActions.ShipActions ship = inputActions.Ship;

            // Traverse is measured clockwise from the station's rest bearing, so a positive
            // nudge trains clockwise for every mount regardless of which way it rests.
            float traverse = Axis(ship.RotateTurretsClockwise, ship.RotateTurretsCounterClockwise);

            // Beam training. Positive is starboard, negative is port, as the player sees it.
            float beam = Axis(ship.CounterRotateTurretsOutwards, ship.CounterRotateTurretsInwards);

            float elevation = Axis(ship.RaiseGunElevation, ship.LowerGunElevation);

            if (traverse == 0f && beam == 0f && elevation == 0f)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<ShipMountSystem.MountInstance> mounts = mountSystem.GunMounts;

            for (int i = 0; i < mounts.Count; i++)
            {
                ShipMountSystem.MountInstance mount = mounts[i];
                if (!mount.IsEquipped || !IsSelected(mount.Station))
                {
                    continue;
                }

                TurretController turret = mount.Controller;

                if (traverse != 0f)
                {
                    turret.NudgeTraverse(traverse, deltaTime);
                }

                if (beam != 0f)
                {
                    // Converging on one beam requires the two groups to rotate opposite ways,
                    // because traverse is measured from each station's own rest bearing. A
                    // forward mount reaches port by traversing to -90, an aft mount resting at
                    // 180 reaches the same bearing by traversing to +90. This sign is geometry,
                    // not preference, so it is derived rather than authored.
                    float groupSign = IsForward(mount.Station) ? 1f : -1f;
                    turret.NudgeTraverse(beam * groupSign, deltaTime);
                }

                if (elevation != 0f)
                {
                    turret.NudgeElevation(elevation, deltaTime);
                }
            }
        }

        private static float Axis(InputAction positive, InputAction negative)
        {
            float value = 0f;

            if (positive.IsPressed())
            {
                value += 1f;
            }

            if (negative.IsPressed())
            {
                value -= 1f;
            }

            return value;
        }

        private static bool IsForward(GunMountSlot station)
        {
            return Mathf.Abs(Mathf.DeltaAngle(0f, station.RestBearingDegrees)) < ForwardArcHalfAngleDegrees;
        }

        /// <summary>
        /// Whether a station currently answers the player's gunnery keys. Public because the
        /// aimline display has to colour exactly the mounts that are under command, and
        /// duplicating the rule there is how the display and the controls drift apart.
        /// </summary>
        public bool IsSelected(GunMountSlot station)
        {
            switch (selection)
            {
                case GunSelection.All:
                    return true;
                case GunSelection.Primary:
                    return station.Battery == BatteryRole.Primary;
                case GunSelection.Secondary:
                    return station.Battery == BatteryRole.Secondary;
                case GunSelection.Forward:
                    return IsForward(station);
                case GunSelection.Aft:
                    return !IsForward(station);
                default:
                    return false;
            }
        }
    }
}
