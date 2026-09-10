using NavalMOBA.Client.Controllers;
using NavalMOBA.Client.Ship;
using NavalMOBA.Common;
using NavalMOBA.Common.SODefs;
using UnityEngine;

namespace NavalMOBA.Client.Loadout
{
    /// <summary>
    /// Instantiates the player's ship at game start and fits its loadout. Single-player
    /// prototype only; once networking lands this becomes "spawn the local player's ship
    /// once the server confirms the match/team assignment" rather than an unconditional
    /// Start() call, and the loadout arrives from the server instead of being built from
    /// the hull's stock fit.
    /// </summary>
    public sealed class ShipSpawner : MonoBehaviour
    {
        [SerializeField] private ShipDefinition shipDefinition;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private CameraController cameraController;

        private GameObject spawnedShip;

        public GameObject SpawnedShip => spawnedShip;
        public ShipMountSystem MountSystem { get; private set; }

        public bool HasShipSpawned()
        {
            return spawnedShip != null;
        }

        private void Start()
        {
            SpawnShip();
        }

        private void SpawnShip()
        {
            if (shipDefinition == null)
            {
                Debug.LogError("ShipSpawner: no ship definition assigned.", this);
                return;
            }

            if (shipDefinition.Prefab == null)
            {
                Debug.LogError($"ShipSpawner: ship definition '{shipDefinition.DisplayName}' has no prefab.", this);
                return;
            }

            Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            spawnedShip = Instantiate(shipDefinition.Prefab, position, rotation);
            spawnedShip.name = shipDefinition.Prefab.name;

            // Mobility before equipment. Both must land before the first simulation tick,
            // and Start runs a whole phase after the Awake this Instantiate just triggered,
            // so there is no window where the ship ticks unconfigured.
            ShipEntity entity = spawnedShip.GetComponent<ShipEntity>();
            if (entity == null)
            {
                Debug.LogError($"ShipSpawner: prefab '{spawnedShip.name}' has no ShipEntity, it will not move.", this);
            }
            else
            {
                entity.ApplyDefinition(shipDefinition);
            }

            MountSystem = spawnedShip.GetComponent<ShipMountSystem>();
            if (MountSystem == null)
            {
                Debug.LogError($"ShipSpawner: prefab '{spawnedShip.name}' has no ShipMountSystem, no equipment will be fitted.", this);
            }
            else
            {
                MountSystem.Build(shipDefinition, ShipLoadout.CreateStock(shipDefinition));
            }

            if (cameraController != null)
            {
                cameraController.SetPlayerShip(spawnedShip.transform);
            }
        }
    }
}
