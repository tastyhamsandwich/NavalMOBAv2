using UnityEngine;

namespace NavalMOBA.Common
{
    /// <summary>
    /// Authoritative movement state for a ship. World space is continuous, not tile-based.
    /// </summary>
    public struct ShipMovementState
    {
        public Vector3 Position;
        public float HeadingDegrees;
        public float CurrentSpeed;

        // Throttle / EEP (emergency engine power overboost).
        public float CurrentThrottle;
        public bool AtMaxThrottle;
        public bool WaitingForSecondPress;
        public bool EepActive;
        public bool EepOnCooldown;
        public float EepDurationRemaining;
        public float EepCooldownRemaining;

        // Turning.
        public float CurrentTurnRateDegreesPerSecond;
        public float CurrentTurnRadius;

        // Waypoint navigation.
        public bool HasWaypoint;
        public Vector3 WaypointPosition;
        public float ClosestDistanceToWaypoint;
        public bool HasApproachedWaypoint;
        public bool HasInitiatedTurn;
        public float InitialAngleToWaypoint;
        public float InitialDistanceToWaypoint;
        public bool IsRoughlyFacing;
        public bool HasReachedTangentialBearing;
        public bool IsMovingAwayAfterTangential;
        public bool IsAutoThrottling;
        public float OriginalThrottleBeforeThrottling;

        /// <summary>Cached HUD/debug read-out; informational only, not physics-critical.</summary>
        public WaypointAnalysis CurrentWaypointAnalysis;
    }
}
