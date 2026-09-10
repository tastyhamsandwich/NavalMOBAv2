using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// A torpedo tube station, typically a centreline or beam mount with a restricted arc.
    /// </summary>
    [Serializable]
    public sealed class TorpedoMountSlot : EquipmentMountSlot
    {
        [SerializeField] private List<TorpedoLauncherDefinition> compatibleLaunchers = new List<TorpedoLauncherDefinition>();
        [SerializeField] private TorpedoLauncherDefinition stockLauncher;

        public IReadOnlyList<TorpedoLauncherDefinition> CompatibleLaunchers => compatibleLaunchers;
        public TorpedoLauncherDefinition StockLauncher => stockLauncher;

        public override bool IsEmptyOfOptions => compatibleLaunchers.Count == 0;

        public bool Allows(TorpedoLauncherDefinition launcher)
        {
            return launcher != null && compatibleLaunchers.Contains(launcher);
        }
    }
}
