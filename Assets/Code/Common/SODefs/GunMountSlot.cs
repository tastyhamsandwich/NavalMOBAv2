using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavalMOBA.Common.SODefs
{
    /// <summary>
    /// A gun station. Declares which battery it serves and the exact set of mounts that
    /// physically fit it.
    /// </summary>
    [Serializable]
    public sealed class GunMountSlot : EquipmentMountSlot
    {
        [SerializeField] private BatteryRole battery = BatteryRole.Primary;

        [Tooltip("Every mount that fits this station. Anything absent from this list is rejected by the server.")]
        [SerializeField] private List<TurretDefinition> compatibleTurrets = new List<TurretDefinition>();

        [Tooltip("Fitted by default on a stock hull. Must also appear in the compatible list.")]
        [SerializeField] private TurretDefinition stockTurret;

        [Header("Arc")]
        [Tooltip("Bearing the mount rests at with zero traverse, in degrees clockwise from the bow. 0 for forward mounts, 180 for aft mounts. Applied as a yaw offset at spawn, so hardpoints do not need to be pre-rotated.")]
        [SerializeField, Range(-180f, 180f)] private float restBearingDegrees;

        [Tooltip("Traverse limit to port, in degrees relative to the rest bearing. Negative.")]
        [SerializeField, Range(-180f, 0f)] private float minTraverseDegrees = -150f;

        [Tooltip("Traverse limit to starboard, in degrees relative to the rest bearing. Positive.")]
        [SerializeField, Range(0f, 180f)] private float maxTraverseDegrees = 150f;

        public BatteryRole Battery => battery;
        public IReadOnlyList<TurretDefinition> CompatibleTurrets => compatibleTurrets;
        public TurretDefinition StockTurret => stockTurret;
        public float RestBearingDegrees => restBearingDegrees;
        public float MinTraverseDegrees => minTraverseDegrees;
        public float MaxTraverseDegrees => maxTraverseDegrees;

        /// <summary>Clamps a traverse angle, relative to the rest bearing, into this station's arc.</summary>
        public float ClampTraverse(float traverseDegrees)
        {
            return Mathf.Clamp(traverseDegrees, minTraverseDegrees, maxTraverseDegrees);
        }

        public override bool IsEmptyOfOptions => compatibleTurrets.Count == 0;

        public bool Allows(TurretDefinition turret)
        {
            return turret != null && compatibleTurrets.Contains(turret);
        }
    }
}
