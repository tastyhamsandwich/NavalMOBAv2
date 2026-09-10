using System.Collections.Generic;
using NavalMOBA.Common.SODefs;

namespace NavalMOBA.Common
{
    /// <summary>
    /// A resolved decision about what is fitted where on one hull. Plain C#, no Unity
    /// dependency beyond the definitions it points at, so the headless server can build and
    /// validate one without a scene.
    ///
    /// This is the payload a client eventually sends when it picks a ship, which is exactly
    /// why Validate exists: the server rebuilds this object from untrusted input and
    /// rejects it against the hull definition before instantiating anything.
    /// </summary>
    public sealed class ShipLoadout
    {
        private readonly Dictionary<string, TurretDefinition> gunAssignments =
            new Dictionary<string, TurretDefinition>();

        public ShipLoadout(ShipDefinition hull)
        {
            Hull = hull;
        }

        public ShipDefinition Hull { get; }

        public IReadOnlyDictionary<string, TurretDefinition> GunAssignments => gunAssignments;

        /// <summary>
        /// Builds the stock fit: every gun station gets its authored default. This is what
        /// the prototype spawner uses and what a new player starts with.
        /// </summary>
        public static ShipLoadout CreateStock(ShipDefinition hull)
        {
            var loadout = new ShipLoadout(hull);
            if (hull == null)
            {
                return loadout;
            }

            IReadOnlyList<GunMountSlot> mounts = hull.GunMounts;
            for (int i = 0; i < mounts.Count; i++)
            {
                GunMountSlot mount = mounts[i];
                if (mount.StockTurret != null)
                {
                    loadout.gunAssignments[mount.SlotName] = mount.StockTurret;
                }
            }

            return loadout;
        }

        /// <summary>Assigns a turret to a station. Pass null to leave the station empty.</summary>
        public void AssignGun(string slotName, TurretDefinition turret)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return;
            }

            if (turret == null)
            {
                gunAssignments.Remove(slotName);
                return;
            }

            gunAssignments[slotName] = turret;
        }

        public TurretDefinition GetGun(string slotName)
        {
            return slotName != null && gunAssignments.TryGetValue(slotName, out TurretDefinition turret)
                ? turret
                : null;
        }

        /// <summary>Total weight of hull plus everything fitted, in metric tons.</summary>
        public float TotalDisplacementTons
        {
            get
            {
                if (Hull == null)
                {
                    return 0f;
                }

                float total = Hull.EmptyDisplacementTons;
                foreach (KeyValuePair<string, TurretDefinition> entry in gunAssignments)
                {
                    if (entry.Value != null)
                    {
                        total += entry.Value.DisplacementTons;
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// Checks the loadout against the hull. Every failure is collected rather than
        /// returning on the first, so authoring mistakes surface all at once instead of one
        /// per iteration.
        /// </summary>
        public bool Validate(List<string> errors)
        {
            errors?.Clear();

            if (Hull == null)
            {
                errors?.Add("Loadout has no hull definition.");
                return false;
            }

            bool valid = true;

            foreach (KeyValuePair<string, TurretDefinition> entry in gunAssignments)
            {
                GunMountSlot mount = Hull.FindGunMount(entry.Key);
                if (mount == null)
                {
                    errors?.Add($"Hull '{Hull.DisplayName}' has no gun station named '{entry.Key}'.");
                    valid = false;
                    continue;
                }

                if (!mount.Allows(entry.Value))
                {
                    errors?.Add($"'{entry.Value.DisplayName}' is not compatible with station '{entry.Key}'.");
                    valid = false;
                }
            }

            float total = TotalDisplacementTons;
            if (total > Hull.MaximumDisplacementTons)
            {
                errors?.Add($"Loadout displaces {total:F0}t, over the {Hull.MaximumDisplacementTons:F0}t limit.");
                valid = false;
            }

            return valid;
        }
    }
}
