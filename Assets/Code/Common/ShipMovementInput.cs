using UnityEngine;

namespace NavalMOBA.Common
{
    /// <summary>
    /// Player-issued movement commands. Once networking lands, these are what the client
    /// sends to the server as an RPC; the server is the only one that advances real state.
    /// </summary>
    public struct ShipMovementInput
    {
        public bool ThrottleUpHeld;
        public bool ThrottleDownHeld;
        public bool SetWaypointRequested;
        public Vector3 WaypointWorldPosition;
    }
}
