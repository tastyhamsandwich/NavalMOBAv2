using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// A torpedo tube mount. Only the shared equipment fields are defined so far;
    /// tube count, reload, traverse arc, and the torpedo itself (a separate definition,
    /// since the same mount fires different marks) are still unspecified.
    /// </summary>
    [CreateAssetMenu(menuName = "Naval MOBA/Torpedo Launcher Definition", fileName = "NewTorpedoLauncherDefinition")]
    public sealed class TorpedoLauncherDefinition : EquipmentDefinition
    {
    }
}
