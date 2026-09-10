using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// One type of round. Naming follows bore and purpose: 5in-38_AP_Mk46.
    ///
    /// Deliberately not an EquipmentDefinition. Ammunition is not fitted to a station and its
    /// weight is per round rather than installed, so inheriting that base class would let
    /// shells leak into the loadout's installed displacement total and would saddle every
    /// shell with a prefab field it has no use for. Magazine weight is a function of how many
    /// rounds the player chose to carry, which is loadout data, not shell data.
    ///
    /// Maximum range is not here and cannot be. It is an outcome of this shell in a specific
    /// gun, computed by FiringTable, because the bore supplies the frontal area and the barrel
    /// supplies the base muzzle velocity.
    /// </summary>
    [CreateAssetMenu(menuName = "Naval MOBA/Ammo Definition", fileName = "NewAmmoDefinition")]
    public sealed class AmmoDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName;
        [SerializeField] private Nation nation;

        [Header("Progression")]
        [SerializeField, Min(1)] private int levelUnlocked = 1;

        [Tooltip("Price of a single round. Multiplied by the magazine allocation the player chooses.")]
        [SerializeField, Min(0)] private int costPerRound;

        [Header("Classification")]
        [Tooltip("Exactly one type per shell. The flags exist so a gun can declare several.")]
        [SerializeField] private AmmoType ammoType = AmmoType.HighExplosive;

        [Tooltip("Bore this round is made for, in millimetres. Must match the gun's calibre. This is the whole compatibility check, so no per-turret ammunition lists are needed.")]
        [SerializeField, Min(1f)] private float caliberMillimeters = 127f;

        [Header("Ballistics")]
        [Tooltip("Complete round weight in kilograms. Drives drag, and therefore range and retained velocity. Charged against displacement once multiplied by the round count.")]
        [SerializeField, Min(0.1f)] private float shellMassKg = 25f;

        [Tooltip("Dimensionless drag coefficient. Tune this until the firing table reports the historical maximum range for the gun, which is the only sane way to author it: a real shell's drag varies with Mach number, and this single figure is the game-level stand-in for that whole curve. Around 0.15 for a well streamlined AP shell, higher for a blunt HE round.")]
        [SerializeField, Min(0f)] private float dragCoefficient = 0.2f;

        [Tooltip("Scales the gun's muzzle velocity. Below one for a heavy shell, because a fixed propellant charge accelerates more mass to less speed. This is the knob that stops a heavy shell being strictly better than a light one: it trades a flat, fast, short range trajectory for range and retained striking velocity.")]
        [SerializeField, Range(0.5f, 1.5f)] private float muzzleVelocityMultiplier = 1f;

        [Header("Damage")]
        [Tooltip("Damage on a penetrating hit, before armour, impact angle, and sailor skill are applied.")]
        [SerializeField, Min(0f)] private float maxDamage = 300f;

        [Tooltip("Armour defeated in millimetres when striking at the reference velocity below. Scaled by actual striking velocity, so this figure alone is not what the shell achieves at range.")]
        [SerializeField, Min(0f)] private float penetrationMillimeters = 40f;

        [Tooltip("Striking velocity the penetration figure above was quoted at. Usually the gun's muzzle velocity, which makes the number a point blank rating.")]
        [SerializeField, Min(1f)] private float referenceVelocityMetersPerSecond = 792f;

        [Header("High Explosive")]
        [Tooltip("Chance of starting a fire on a hit, 0 to 1. Fires are the mechanism by which HE hurts a target it cannot penetrate.")]
        [SerializeField, Range(0f, 1f)] private float fireChance = 0.08f;

        [Header("Proximity Fuze")]
        [Tooltip("Burst radius in metres for anti-air rounds. Zero for shells that require a direct hit.")]
        [SerializeField, Min(0f)] private float proximityFuzeRadiusMeters;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Nation Nation => nation;
        public int LevelUnlocked => levelUnlocked;
        public int CostPerRound => costPerRound;
        public AmmoType AmmoType => ammoType;
        public float CaliberMillimeters => caliberMillimeters;
        public float ShellMassKg => shellMassKg;
        public float DragCoefficient => dragCoefficient;
        public float MuzzleVelocityMultiplier => muzzleVelocityMultiplier;
        public float MaxDamage => maxDamage;
        public float PenetrationMillimeters => penetrationMillimeters;
        public float ReferenceVelocityMetersPerSecond => referenceVelocityMetersPerSecond;
        public float FireChance => fireChance;
        public float ProximityFuzeRadiusMeters => proximityFuzeRadiusMeters;

        /// <summary>
        /// Whether this round needs to actually strike its target. A proximity fuzed shell does
        /// not, which is why anti-air damage resolves through a burst radius rather than through
        /// armour penetration. The two paths diverge here rather than inside the damage model.
        /// </summary>
        public bool IsProximityFuzed => proximityFuzeRadiusMeters > 0f;

        /// <summary>Magazine weight in metric tons for a given allocation, for the displacement budget.</summary>
        public float MagazineTons(int rounds)
        {
            return Mathf.Max(0, rounds) * shellMassKg * 0.001f;
        }

        /// <summary>
        /// Combines this round with the gun firing it into the struct the integrator wants.
        /// Bore comes from the gun, not from this asset: the shell's calibre exists only to
        /// verify it fits, and trusting it for frontal area would let an authoring mismatch
        /// quietly change the ballistics.
        /// </summary>
        public Ballistics.Shell CreateShell(TurretDefinition gun)
        {
            if (gun == null)
            {
                return default;
            }

            return new Ballistics.Shell(
                shellMassKg,
                gun.CaliberMillimeters,
                dragCoefficient,
                gun.MuzzleVelocityMetersPerSecond * muzzleVelocityMultiplier);
        }

        /// <summary>Whether this round physically fits and is of a type the gun can fire.</summary>
        public bool FitsGun(TurretDefinition gun)
        {
            return gun != null
                && Mathf.Approximately(caliberMillimeters, gun.CaliberMillimeters)
                && (gun.CompatibleAmmoTypes & ammoType) == ammoType;
        }
    }
}
