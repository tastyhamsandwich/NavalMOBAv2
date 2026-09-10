using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// An embarked aircraft type, catapult scout or carrier squadron alike. Only the
    /// shared equipment fields are defined so far; role, speed, sortie endurance,
    /// spotting radius, and ordnance are still unspecified.
    /// </summary>
    [CreateAssetMenu(menuName = "Naval MOBA/Aircraft Definition", fileName = "NewAircraftDefinition")]
    public sealed class AircraftDefinition : EquipmentDefinition
    {
    }
}
