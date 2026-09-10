using System.Collections.Generic;
using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// Immutable authored data for one hull. This is the spawn contract: the spawner
    /// resolves a ShipDefinition, instantiates Prefab, then walks the mount slots and
    /// fits equipment at each hardpoint. Nothing here changes at runtime, so every member
    /// is read only; per match mutable state lives on the ship instance.
    ///
    /// Speed and turn rate here are the "base" figures, meaning a clean hull with
    /// baseline crew. Sailor skill, damage, and loadout weight modify them at runtime.
    ///
    /// Equipment compatibility is per station, not per ship. See EquipmentMountSlot.
    /// </summary>
    [CreateAssetMenu(menuName = "Naval MOBA/Ship Definition", fileName = "NewShipDefinition")]
    public sealed class ShipDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name only, for example Fletcher. The designator prefix comes from Nation and Class.")]
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private ShipClass shipClass = ShipClass.Destroyer;
        [SerializeField] private Nation nation = Nation.UnitedStates;

        [Header("Progression")]
        [SerializeField, Min(1)] private int levelUnlocked = 1;
        [SerializeField, Min(0)] private int cost;

        [Header("Displacement")]
        [Tooltip("Bare hull weight in metric tons, no equipment fitted.")]
        [SerializeField, Min(0f)] private float emptyDisplacementTons = 2100f;

        [Tooltip("Full load limit in metric tons. Empty displacement plus fitted equipment may not exceed this.")]
        [SerializeField, Min(0f)] private float maximumDisplacementTons = 2900f;

        [Header("Mobility")]
        [Tooltip("Top speed ahead in knots at 100 percent throttle, before EEP overboost.")]
        [SerializeField, Min(0f)] private float maxBaseSpeedKnots = 36f;

        [Tooltip("Top speed astern in knots. Warships back down far slower than they steam ahead.")]
        [SerializeField, Min(0f)] private float maxReverseSpeedKnots = 5f;

        [Tooltip("Peak rate of turn in degrees per second, achieved at the optimal turning speed fraction below. Falls off above that speed.")]
        [SerializeField, Min(0f)] private float maxBaseTurnRateDegreesPerSecond = 15f;

        [Header("Mobility - Hull Inertia")]
        [Tooltip("How fast the hull gains speed, in knots per second. This is mass, not the telegraph. Keep it well below the throttle rate or the lever becomes the bottleneck and the ship feels weightless.")]
        [SerializeField, Min(0.01f)] private float accelerationKnots = 3f;

        [Tooltip("How fast the hull loses speed, in knots per second.")]
        [SerializeField, Min(0.01f)] private float decelerationKnots = 3f;

        [Tooltip("How fast holding the throttle keys moves the engine order, in percent per second. At 25 it takes 4s to go stop to flank.")]
        [SerializeField, Min(0.01f)] private float throttleRatePercentPerSecond = 25f;

        [Header("Mobility - Emergency Engine Power")]
        [SerializeField, Min(0f)] private float maxThrottle = 100f;
        [SerializeField, Min(0f)] private float eepMaxThrottle = 115f;
        [SerializeField, Min(0f)] private float eepDuration = 20f;
        [SerializeField, Min(0f)] private float eepCooldown = 15f;

        [Header("Mobility - Turning")]
        [Tooltip("Below this speed in METRES PER SECOND the rudder has no water flowing over it and the ship will not turn. 0.5 is about 1 knot.")]
        [SerializeField, Min(0f)] private float minimumSpeedForTurning = 0.5f;

        [Tooltip("Fraction of top speed at which this hull turns best. 0.75 means peak rate of turn at three quarters throttle. Dimensionless, so it means the same thing on a 20kn battleship and a 36kn destroyer.")]
        [SerializeField, Range(0.1f, 1f)] private float optimalTurningSpeedFraction = 0.75f;

        [Tooltip("Rate of turn lost at 100 percent throttle, as a fraction of the peak. 0.33 means flank speed turns at two thirds of the best rate. Zero removes the speed versus manoeuvre tradeoff.")]
        [SerializeField, Range(0f, 0.9f)] private float turnRateDegradationAtFullSpeed = 0.33f;

        [Tooltip("Tightest turn circle in metres. Used only by waypoint planning to reject destinations that are too close to reach. The circle the ship actually flies is derived from its rate of turn.")]
        [SerializeField, Min(0.01f)] private float minimumTurningRadius = 25f;

        [Tooltip("On: rate of turn follows the speed curve above. Off: the hull turns at the peak rate whenever it has way on. Debug only.")]
        [SerializeField] private bool useRealisticTurning = true;

        [SerializeField] private ShipMovementStyle movementStyle = ShipMovementStyle.SteeringHold;

        [Header("Sailor Slots")]
        [SerializeField, Min(0)] private int primaryGunnerySailorSlots = 2;
        [SerializeField, Min(0)] private int secondaryGunnerySailorSlots = 1;
        [Tooltip("Non gunnery crew: engineering, damage control, spotting, and similar.")]
        [SerializeField, Min(0)] private int supportSailorSlots = 3;

        [Header("Mount Slots")]
        [SerializeField] private List<GunMountSlot> gunMounts = new List<GunMountSlot>();
        [SerializeField] private List<TorpedoMountSlot> torpedoMounts = new List<TorpedoMountSlot>();
        [SerializeField] private List<AircraftMountSlot> aircraftMounts = new List<AircraftMountSlot>();

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public ShipClass ShipClass => shipClass;
        public Nation Nation => nation;

        public int LevelUnlocked => levelUnlocked;
        public int Cost => cost;

        public float EmptyDisplacementTons => emptyDisplacementTons;
        public float MaximumDisplacementTons => maximumDisplacementTons;

        /// <summary>Tonnage available for fitted equipment. Zero means a fixed loadout.</summary>
        public float EquipmentDisplacementBudgetTons => Mathf.Max(0f, maximumDisplacementTons - emptyDisplacementTons);

        public float MaxBaseSpeedKnots => maxBaseSpeedKnots;
        public float MaxBaseTurnRateDegreesPerSecond => maxBaseTurnRateDegreesPerSecond;

        /// <summary>
        /// Builds the mobility bundle the shared movement simulation runs against. The
        /// definition is the single source of truth: nothing authors these numbers on the
        /// ship prefab, so a hull cannot disagree with its own data sheet.
        ///
        /// A fresh instance every call, deliberately. Handing out one shared config would let
        /// a runtime modifier (damage, sailor skill, flooding) on one ship mutate every ship
        /// of that class in the match.
        /// </summary>
        public ShipMovementConfig CreateMovementConfig()
        {
            return new ShipMovementConfig
            {
                maxForwardSpeedKnots = maxBaseSpeedKnots,
                maxReverseSpeedKnots = maxReverseSpeedKnots,
                accelerationKnots = accelerationKnots,
                decelerationKnots = decelerationKnots,
                throttleRatePercentPerSecond = throttleRatePercentPerSecond,
                maxThrottle = maxThrottle,
                eepMaxThrottle = eepMaxThrottle,
                eepDuration = eepDuration,
                eepCooldown = eepCooldown,
                baseTurnRateDegreesPerSecond = maxBaseTurnRateDegreesPerSecond,
                minimumSpeedForTurning = minimumSpeedForTurning,
                optimalTurningSpeedFraction = optimalTurningSpeedFraction,
                turnRateDegradationAtFullSpeed = turnRateDegradationAtFullSpeed,
                minimumTurningRadius = minimumTurningRadius,
                useRealisticTurning = useRealisticTurning,
                movementStyle = movementStyle
            };
        }

        public int PrimaryGunnerySailorSlots => primaryGunnerySailorSlots;
        public int SecondaryGunnerySailorSlots => secondaryGunnerySailorSlots;
        public int SupportSailorSlots => supportSailorSlots;
        public int TotalSailorSlots => primaryGunnerySailorSlots + secondaryGunnerySailorSlots + supportSailorSlots;

        public IReadOnlyList<GunMountSlot> GunMounts => gunMounts;
        public IReadOnlyList<TorpedoMountSlot> TorpedoMounts => torpedoMounts;
        public IReadOnlyList<AircraftMountSlot> AircraftMounts => aircraftMounts;

        /// <summary>Asset name prefix this hull should use, for example "USN_DD_".</summary>
        public string DesignatorPrefix => NavalDesignators.HullPrefix(nation, shipClass);

        public GunMountSlot FindGunMount(string slotName)
        {
            for (int i = 0; i < gunMounts.Count; i++)
            {
                if (gunMounts[i].SlotName == slotName)
                {
                    return gunMounts[i];
                }
            }

            return null;
        }

        /// <summary>Number of gun stations serving the given battery.</summary>
        public int CountGunMounts(BatteryRole battery)
        {
            int count = 0;
            for (int i = 0; i < gunMounts.Count; i++)
            {
                if (gunMounts[i].Battery == battery)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Total weight of the stock loadout, hull included. The authoring check that a
        /// default fit is actually within the hull's limit.
        /// </summary>
        public float StockDisplacementTons
        {
            get
            {
                float total = emptyDisplacementTons;

                for (int i = 0; i < gunMounts.Count; i++)
                {
                    TurretDefinition turret = gunMounts[i].StockTurret;
                    if (turret != null)
                    {
                        total += turret.DisplacementTons;
                    }
                }

                for (int i = 0; i < torpedoMounts.Count; i++)
                {
                    TorpedoLauncherDefinition launcher = torpedoMounts[i].StockLauncher;
                    if (launcher != null)
                    {
                        total += launcher.DisplacementTons;
                    }
                }

                for (int i = 0; i < aircraftMounts.Count; i++)
                {
                    AircraftDefinition aircraft = aircraftMounts[i].StockAircraft;
                    if (aircraft != null)
                    {
                        total += aircraft.DisplacementTons * aircraftMounts[i].Capacity;
                    }
                }

                return total;
            }
        }

        private void OnValidate()
        {
            if (maximumDisplacementTons < emptyDisplacementTons)
            {
                maximumDisplacementTons = emptyDisplacementTons;
            }

            if (eepMaxThrottle < maxThrottle)
            {
                eepMaxThrottle = maxThrottle;
            }

            for (int i = 0; i < gunMounts.Count; i++)
            {
                GunMountSlot mount = gunMounts[i];
                if (mount.StockTurret != null && !mount.Allows(mount.StockTurret))
                {
                    Debug.LogWarning($"{name}: gun mount '{mount.SlotName}' has stock turret '{mount.StockTurret.name}' that is not in its compatible list.", this);
                }
            }

            if (StockDisplacementTons > maximumDisplacementTons)
            {
                Debug.LogWarning($"{name}: stock loadout is {StockDisplacementTons:F0}t, over the {maximumDisplacementTons:F0}t limit.", this);
            }
        }
    }
}
