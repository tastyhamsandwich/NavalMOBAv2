using System;
using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// One physical station on a hull where equipment can be fitted. Concrete subclasses
    /// add the typed compatibility list for what that station accepts.
    ///
    /// Compatibility lives here rather than on the ship as a whole because stations are
    /// not interchangeable. A Fletcher's superfiring B mount has a barbette and arc that
    /// the aft mounts do not, so "what fits this ship" is never a single answer.
    /// </summary>
    [Serializable]
    public abstract class EquipmentMountSlot
    {
        [Tooltip("Station identifier used in UI and logs. Prefer historical mount numbers, for example Mount 51.")]
        [SerializeField] private string slotName;

        [Tooltip("Transform path of the hardpoint, relative to the ship prefab root. For example Ship Hull/TurretMount1/TurretMountPoint-F1")]
        [SerializeField] private string hardpointPath;

        public string SlotName => slotName;
        public string HardpointPath => hardpointPath;

        /// <summary>True when the station has no valid equipment authored and cannot be fitted.</summary>
        public abstract bool IsEmptyOfOptions { get; }
    }
}
