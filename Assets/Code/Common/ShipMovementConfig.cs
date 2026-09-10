using System;

namespace NavalMOBA.Common
{
    /// <summary>
    /// Tunable per-ship mobility stats. Placeholder until the real ship data-driven config
    /// (Loadout/Content/Ships ScriptableObject system) exists; for now this is configured
    /// directly in the Inspector on ShipEntity.
    /// </summary>
    [Serializable]
    public sealed class ShipMovementConfig
    {
        public const float KnotsToMetersPerSecond = 0.514444f;
        public const float MetersPerSecondToKnots = 1.94384f;

        public float maxForwardSpeedKnots = 20f;
        public float maxReverseSpeedKnots = 5f;

        /// <summary>
        /// How fast the hull can actually change speed, in knots per second. This is hull
        /// inertia, not the engine order telegraph -- it governs how long the ship takes to
        /// reach the speed the throttle is asking for. Keep this well below
        /// throttleRatePercentPerSecond or the throttle becomes the bottleneck and the ship
        /// feels like it has no mass.
        /// </summary>
        public float accelerationKnots = 3f;
        public float decelerationKnots = 3f;

        /// <summary>
        /// How fast holding the throttle keys moves the engine order, in throttle percent
        /// per second. Distinct from accelerationKnots: this is the lever moving, that is
        /// the ship responding. At 25 it takes 4s to go stop-to-flank and 8s to slam from
        /// full ahead to full astern.
        /// </summary>
        public float throttleRatePercentPerSecond = 25f;

        public float maxThrottle = 100f;
        public float eepMaxThrottle = 115f;
        public float eepDuration = 20f;
        public float eepCooldown = 15f;

        /// <summary>
        /// Peak rate of turn in degrees per second, reached at
        /// <see cref="optimalTurningSpeedFraction"/> of top speed. This is the headline
        /// manoeuvrability number and the only one expressed in degrees.
        /// </summary>
        public float baseTurnRateDegreesPerSecond = 15f;

        /// <summary>Speed in metres per second below which the rudder has no authority at all. 0.5 is about 1 knot.</summary>
        public float minimumSpeedForTurning = 0.5f;

        /// <summary>
        /// Fraction of top speed at which the hull turns best. Dimensionless on purpose:
        /// every hull gets the same handling character regardless of how fast it is, and
        /// there is no knots-versus-metres-per-second trap for whoever authors the next ship.
        /// </summary>
        public float optimalTurningSpeedFraction = 0.75f;

        /// <summary>
        /// How much rate of turn is lost at 100 percent throttle relative to the peak.
        /// 0.33 means flank speed turns at two thirds of the best rate. Zero removes the
        /// speed-versus-manoeuvre tradeoff entirely.
        /// </summary>
        public float turnRateDegradationAtFullSpeed = 0.33f;

        /// <summary>
        /// Tightest turn circle in metres, used by waypoint planning to decide whether a
        /// destination is reachable at all. The circle the ship actually flies is derived
        /// from its rate of turn and speed, not from this.
        /// </summary>
        public float minimumTurningRadius = 25f;

        /// <summary>Off makes the hull turn at <see cref="baseTurnRateDegreesPerSecond"/> at any speed above the stall threshold. Debug only.</summary>
        public bool useRealisticTurning = true;

        public ShipMovementStyle movementStyle = ShipMovementStyle.SteeringHold;
    }
}
