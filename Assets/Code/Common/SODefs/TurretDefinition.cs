using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// A gun mount. Naming follows barrel count and gun: 1x5in-38_Mk21 is a single
    /// 5 inch /38 calibre Mk21 mount.
    ///
    /// A mount carries no battery role. Whether it serves as primary or secondary is a
    /// property of the hull station it is fitted to, so it lives on GunMountSlot.
    ///
    /// Anti-air capability is likewise not declared here. It emerges from elevation combined
    /// with the mount being able to chamber a round that can actually kill an aircraft, which
    /// is why CanEngageAircraft reads both and neither on its own.
    ///
    /// Maximum range is absent on purpose. It is a property of this gun paired with a specific
    /// shell, resolved by FiringTable, because shell weight and drag decide how far a round
    /// carries just as much as muzzle velocity does.
    /// </summary>
    [CreateAssetMenu(menuName = "Naval MOBA/Turret Definition", fileName = "NewTurretDefinition")]
    public sealed class TurretDefinition : EquipmentDefinition
    {
        /// <summary>
        /// Elevation in degrees a mount must reach before engaging aircraft is physically
        /// plausible. Balance figure, not a historical constant: real dual purpose guns
        /// reached 75 to 85 degrees while battleship main batteries stopped near 20 to 30,
        /// so anything in the 45 to 60 band separates them cleanly.
        /// </summary>
        public const float AntiAirMinimumElevationDegrees = 55f;

        [Header("Ordnance")]
        [Tooltip("Bore diameter in millimetres. 5in/38 is 127mm.")]
        [SerializeField, Min(1f)] private float caliberMillimeters = 127f;

        [Tooltip("Barrel length expressed in calibres, the /38 in 5in/38.")]
        [SerializeField, Min(1f)] private float barrelLengthCalibers = 38f;

        [SerializeField, Min(1)] private int barrelCount = 1;

        [Tooltip("Seconds for a full reload cycle at baseline crew skill. Sailor veterancy scales this down.")]
        [SerializeField, Min(0.1f)] private float reloadSeconds = 4f;

        [Tooltip("Muzzle velocity in metres per second for a standard weight round. Individual shells scale this by their own multiplier, so a heavy AP round leaves the barrel slower than this figure.")]
        [SerializeField, Min(1f)] private float muzzleVelocityMetersPerSecond = 792f;

        [Tooltip("Which classes of round this mount can fire. Combined with a matching bore, this is the entire ammunition compatibility rule, so new shells need no edits here.")]
        [SerializeField] private AmmoType compatibleAmmoTypes = AmmoType.HighExplosive | AmmoType.ArmorPiercing;

        [Header("Traverse and Elevation")]
        [SerializeField, Min(0.1f)] private float traverseRateDegreesPerSecond = 30f;
        [SerializeField, Min(0.1f)] private float elevationRateDegreesPerSecond = 15f;

        [Tooltip("Depression limit in degrees, negative below horizontal.")]
        [SerializeField, Range(-30f, 0f)] private float minElevationDegrees = -15f;

        [SerializeField, Range(0f, 90f)] private float maxElevationDegrees = 85f;

        public float CaliberMillimeters => caliberMillimeters;
        public float BarrelLengthCalibers => barrelLengthCalibers;
        public int BarrelCount => barrelCount;
        public float ReloadSeconds => reloadSeconds;
        public float MuzzleVelocityMetersPerSecond => muzzleVelocityMetersPerSecond;
        public AmmoType CompatibleAmmoTypes => compatibleAmmoTypes;
        public float TraverseRateDegreesPerSecond => traverseRateDegreesPerSecond;
        public float ElevationRateDegreesPerSecond => elevationRateDegreesPerSecond;
        public float MinElevationDegrees => minElevationDegrees;
        public float MaxElevationDegrees => maxElevationDegrees;

        /// <summary>
        /// Whether the mount can point high enough to track aircraft. Necessary but not
        /// sufficient: the fire control resolver must also confirm the loaded shell is
        /// effective against air targets, such as a timed airburst or proximity fuze.
        /// </summary>
        public bool HasAntiAirElevation => maxElevationDegrees >= AntiAirMinimumElevationDegrees;

        /// <summary>
        /// Whether this mount is a credible anti-air weapon. Requires both the elevation to
        /// track an aircraft and clearance to fire a proximity fuzed round, because a gun that
        /// points at the sky with nothing but armour piercing shot is not an AA mount.
        /// </summary>
        public bool CanEngageAircraft => HasAntiAirElevation && (compatibleAmmoTypes & AmmoType.AntiAir) != 0;

        private void OnValidate()
        {
            if (maxElevationDegrees < minElevationDegrees)
            {
                maxElevationDegrees = minElevationDegrees;
            }
        }
    }
}
