using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// An aircraft station: a catapult, a hangar bay, or a flight deck complement.
    /// </summary>
    [Serializable]
    public sealed class AircraftMountSlot : EquipmentMountSlot
    {
        [SerializeField] private List<AircraftDefinition> compatibleAircraft = new List<AircraftDefinition>();
        [SerializeField] private AircraftDefinition stockAircraft;

        [Tooltip("How many airframes this station holds.")]
        [SerializeField, Min(1)] private int capacity = 1;

        public IReadOnlyList<AircraftDefinition> CompatibleAircraft => compatibleAircraft;
        public AircraftDefinition StockAircraft => stockAircraft;
        public int Capacity => capacity;

        public override bool IsEmptyOfOptions => compatibleAircraft.Count == 0;

        public bool Allows(AircraftDefinition aircraft)
        {
            return aircraft != null && compatibleAircraft.Contains(aircraft);
        }
    }
}
