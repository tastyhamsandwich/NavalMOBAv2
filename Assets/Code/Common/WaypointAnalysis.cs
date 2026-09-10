namespace NavalMOBA.Common
{
    /// <summary>
    /// Informational read-out of whether the current waypoint can be reached with the
    /// ship's current turning capability. Cached on ShipMovementState for HUD display;
    /// not itself part of the deterministic movement math.
    /// </summary>
    public sealed class WaypointAnalysis
    {
        public WaypointReachability reachability;
        public float requiredTurnAngle;
        public float maxTurnAngleAtCurrentSpeed;
        public float maxTurnAngleAtReducedSpeed;
        public float recommendedThrottleReduction;
        public bool willRequireAutoThrottling;
        public bool willTriggerBreakOff;
        public string analysisDetails;
    }
}
