using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// Fields shared by everything a hull can mount: turrets, torpedo launchers, aircraft.
    /// Exists so ShipDefinition compatibility lists are strongly typed and so the
    /// displacement budget can be summed over a mixed loadout without per-type special
    /// casing. Not creatable on its own.
    /// </summary>
    public abstract class EquipmentDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Nation nation;

        [Header("Progression")]
        [SerializeField, Min(1)] private int levelUnlocked = 1;
        [SerializeField, Min(0)] private int cost;

        [Header("Mass")]
        [Tooltip("Installed weight in metric tons. Charged against the hull's displacement budget.")]
        [SerializeField, Min(0f)] private float displacementTons;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public Nation Nation => nation;
        public int LevelUnlocked => levelUnlocked;
        public int Cost => cost;
        public float DisplacementTons => displacementTons;
    }
}
