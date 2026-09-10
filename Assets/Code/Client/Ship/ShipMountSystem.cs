using System.Collections.Generic;
using NavalMOBA.Common;
using NavalMOBA.Common.SODefs;
using UnityEngine;

namespace NavalMOBA.Client.Ship
{
    /// <summary>
    /// Something that decides where this ship's mounts should point. Implemented by the
    /// manual fire control, and later by an automatic FCS or an AI gunner.
    ///
    /// The mount system pulls this once per tick instead of the director registering with
    /// the ticker itself. Two separate tick registrations would make "setpoints written
    /// before mounts slew" depend on component order, which is exactly the ordering
    /// fragility this class exists to avoid.
    /// </summary>
    public interface IMountDirector
    {
        void UpdateSetpoints(float deltaTime);
    }

    /// <summary>
    /// Owns the equipment fitted to one hull. Resolves each station's hardpoint on the ship
    /// instance, instantiates the assigned turret there, and drives every mount on the
    /// simulation tick.
    ///
    /// Ticking happens here rather than in each TurretController so mounts always slew in
    /// station order. Registering fifty turrets with the ticker individually would make the
    /// order depend on spawn timing, which a server-authoritative rewind cannot tolerate.
    /// </summary>
    public sealed class ShipMountSystem : MonoBehaviour, ITickable
    {
        public sealed class MountInstance
        {
            public MountInstance(GunMountSlot station, GameObject equipment, TurretController controller)
            {
                Station = station;
                Equipment = equipment;
                Controller = controller;
            }

            public GunMountSlot Station { get; }
            public GameObject Equipment { get; }
            public TurretController Controller { get; }

            public string StationName => Station.SlotName;
            public bool IsEquipped => Equipment != null && Controller != null;
        }

        private readonly List<MountInstance> gunMounts = new List<MountInstance>();

        private ShipDefinition definition;
        private ShipLoadout loadout;
        private IMountDirector director;

        public ShipDefinition Definition => definition;
        public ShipLoadout Loadout => loadout;
        public IReadOnlyList<MountInstance> GunMounts => gunMounts;

        private void OnEnable()
        {
            SimulationTicker.Instance?.Register(this);
        }

        private void OnDisable()
        {
            SimulationTicker.Instance?.Unregister(this);
        }

        /// <summary>
        /// Fits the loadout to this hull. Validates first and refuses the whole loadout on
        /// failure rather than fitting what it can: a half-armed ship is worse than an
        /// obvious error, and this same path will run on the server against client input.
        /// </summary>
        public bool Build(ShipDefinition shipDefinition, ShipLoadout shipLoadout)
        {
            if (shipDefinition == null)
            {
                Debug.LogError($"ShipMountSystem on '{name}': no ship definition supplied.", this);
                return false;
            }

            shipLoadout ??= ShipLoadout.CreateStock(shipDefinition);

            var errors = new List<string>();
            if (!shipLoadout.Validate(errors))
            {
                Debug.LogError($"ShipMountSystem on '{name}': loadout rejected for '{shipDefinition.DisplayName}':\n{string.Join("\n", errors)}", this);
                return false;
            }

            Clear();

            definition = shipDefinition;
            loadout = shipLoadout;

            bool allFitted = true;
            IReadOnlyList<GunMountSlot> stations = definition.GunMounts;

            for (int i = 0; i < stations.Count; i++)
            {
                GunMountSlot station = stations[i];
                TurretDefinition turret = loadout.GetGun(station.SlotName);

                if (turret == null)
                {
                    continue;
                }

                if (!FitGun(station, turret))
                {
                    allFitted = false;
                }
            }

            Debug.Log($"ShipMountSystem on '{name}': fitted {gunMounts.Count} of {stations.Count} gun stations on '{definition.DisplayName}'.", this);
            return allFitted;
        }

        private bool FitGun(GunMountSlot station, TurretDefinition turret)
        {
            if (turret.Prefab == null)
            {
                Debug.LogError($"ShipMountSystem on '{name}': turret '{turret.DisplayName}' has no prefab.", this);
                return false;
            }

            Transform hardpoint = ResolveHardpoint(station);
            if (hardpoint == null)
            {
                return false;
            }

            GameObject instance = Instantiate(turret.Prefab, hardpoint, false);
            instance.name = $"{station.SlotName} - {turret.name}";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            TurretController controller = instance.GetComponent<TurretController>();
            if (controller == null)
            {
                Debug.LogError($"ShipMountSystem on '{name}': turret prefab '{turret.Prefab.name}' has no TurretController.", this);
                Destroy(instance);
                return false;
            }

            if (!controller.Initialize(turret, station))
            {
                Destroy(instance);
                return false;
            }

            gunMounts.Add(new MountInstance(station, instance, controller));
            return true;
        }

        private Transform ResolveHardpoint(GunMountSlot station)
        {
            if (string.IsNullOrEmpty(station.HardpointPath))
            {
                Debug.LogError($"ShipMountSystem on '{name}': station '{station.SlotName}' has no hardpoint path.", this);
                return null;
            }

            Transform hardpoint = transform.Find(station.HardpointPath);
            if (hardpoint == null)
            {
                Debug.LogError($"ShipMountSystem on '{name}': station '{station.SlotName}' points at '{station.HardpointPath}', which does not exist under '{name}'.", this);
            }

            return hardpoint;
        }

        public void Clear()
        {
            for (int i = 0; i < gunMounts.Count; i++)
            {
                if (gunMounts[i].Equipment != null)
                {
                    Destroy(gunMounts[i].Equipment);
                }
            }

            gunMounts.Clear();
            loadout = null;
        }

        public void Tick(float deltaTime)
        {
            // Setpoints first, then actuators, so a key held during this tick moves the mount
            // during this tick rather than one tick later.
            director?.UpdateSetpoints(deltaTime);

            for (int i = 0; i < gunMounts.Count; i++)
            {
                gunMounts[i].Controller.Slew(deltaTime);
            }
        }

        /// <summary>
        /// Sets who aims this ship's mounts. Pass null to release control, which leaves every
        /// mount holding its last setpoint rather than snapping back to rest.
        /// </summary>
        public void SetDirector(IMountDirector value)
        {
            director = value;
        }

        public MountInstance FindMount(string stationName)
        {
            for (int i = 0; i < gunMounts.Count; i++)
            {
                if (gunMounts[i].StationName == stationName)
                {
                    return gunMounts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Collects the turrets of one battery into the caller's list. Takes a list to fill
        /// rather than returning a new one, because fire control queries this every tick and
        /// allocating a List per battery per tick is exactly the garbage that shows up as
        /// frame hitches later.
        /// </summary>
        public void GetTurrets(BatteryRole battery, List<TurretController> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            for (int i = 0; i < gunMounts.Count; i++)
            {
                if (gunMounts[i].Station.Battery == battery)
                {
                    results.Add(gunMounts[i].Controller);
                }
            }
        }

        public void GetAllTurrets(List<TurretController> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            for (int i = 0; i < gunMounts.Count; i++)
            {
                results.Add(gunMounts[i].Controller);
            }
        }
    }
}
