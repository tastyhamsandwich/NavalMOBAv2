using UnityEngine;

namespace NavalMOBA.Common
{
    /// <summary>
    /// Exterior ballistics for gun-launched projectiles. Pure maths, no Unity objects, so the
    /// headless server can re-run any shot the client claims to have fired.
    ///
    /// Lives in Common rather than Client/Ballistics on purpose. Hit resolution is the single
    /// most attractive thing in this game to cheat at, so the server must own the same
    /// integrator, to the bit, that the client flew the shell with.
    ///
    /// Air drag is modelled and is not optional decoration. Three things in the design
    /// document are impossible without it:
    ///
    /// 1. Shell weight affecting maximum range. In a vacuum, range is v^2 sin(2t) / g, in
    ///    which mass does not appear at all. Drag deceleration is proportional to area over
    ///    mass, so a heavier shell of the same bore sheds velocity more slowly and flies
    ///    further. That ratio is the only reason shell weight matters.
    /// 2. Armour penetration falling off with range. Under gravity alone a trajectory is a
    ///    symmetric parabola, so a shell returns to firing height at exactly its muzzle speed.
    ///    Penetration would be identical at 2 km and 20 km.
    /// 3. Plunging fire arriving steeply. A vacuum trajectory descends at exactly its launch
    ///    angle. Drag makes the descending leg steeper than the ascending one, which is what
    ///    lets long-range fire find deck armour instead of the belt.
    ///
    /// Setting a shell's drag coefficient to zero recovers exact vacuum behaviour, so the
    /// simpler model is still reachable if it is ever wanted.
    /// </summary>
    public static class Ballistics
    {
        public const float GravityMetersPerSecondSquared = 9.81f;

        /// <summary>Sea level air density. Ships fight at sea level, so this is a constant.</summary>
        public const float AirDensityKgPerCubicMeter = 1.225f;

        /// <summary>
        /// Fixed integration step, in seconds. Deliberately not the frame or tick delta.
        /// A trajectory integrated at a different step is a different trajectory, so the
        /// firing table, the flying shell, and the server's re-simulation must all use this
        /// value or the aim point will not agree with where the shell actually lands.
        /// </summary>
        public const float IntegrationStepSeconds = 1f / 60f;

        /// <summary>
        /// How sharply penetration scales with striking velocity, from the DeMarre family of
        /// empirical formulae. Penetration is authored at a reference velocity and scaled by
        /// the velocity ratio raised to this power.
        /// </summary>
        public const float PenetrationVelocityExponent = 1.4f;

        /// <summary>Roughly 200 seconds of flight. A shell still airborne past this is a bug, not a shot.</summary>
        private const int MaxIntegrationSteps = 12000;

        /// <summary>
        /// Everything the integrator needs about one round in one gun. Assembled from a
        /// TurretDefinition and an AmmoDefinition together, because neither owns a complete
        /// answer: the gun supplies bore and base muzzle velocity, the shell supplies mass,
        /// drag, and its own velocity multiplier.
        /// </summary>
        public readonly struct Shell
        {
            public Shell(float massKg, float caliberMillimeters, float dragCoefficient, float muzzleVelocityMetersPerSecond)
            {
                MassKg = massKg;
                CaliberMillimeters = caliberMillimeters;
                DragCoefficient = dragCoefficient;
                MuzzleVelocityMetersPerSecond = muzzleVelocityMetersPerSecond;
            }

            public float MassKg { get; }
            public float CaliberMillimeters { get; }
            public float DragCoefficient { get; }
            public float MuzzleVelocityMetersPerSecond { get; }

            /// <summary>
            /// The combined constant in drag acceleration = factor * speed * velocity. Derived
            /// from bore area and mass rather than authored, so there is no free fudge number:
            /// the drag coefficient is the only knob, and shell weight feeds through it
            /// automatically.
            /// </summary>
            public float DragFactor
            {
                get
                {
                    if (MassKg <= 0f || DragCoefficient <= 0f)
                    {
                        return 0f;
                    }

                    // Millimetres of diameter to metres of radius in one step.
                    float radiusMeters = CaliberMillimeters * 0.0005f;
                    float frontalAreaSquareMeters = Mathf.PI * radiusMeters * radiusMeters;
                    return 0.5f * AirDensityKgPerCubicMeter * DragCoefficient * frontalAreaSquareMeters / MassKg;
                }
            }

            public bool IsValid => MassKg > 0f && CaliberMillimeters > 0f && MuzzleVelocityMetersPerSecond > 0f;
        }

        /// <summary>Where and how hard a simulated trajectory arrived.</summary>
        public readonly struct Impact
        {
            public Impact(bool reachedTargetHeight, float rangeMeters, float flightTimeSeconds, float impactSpeedMetersPerSecond, float descentAngleDegrees)
            {
                ReachedTargetHeight = reachedTargetHeight;
                RangeMeters = rangeMeters;
                FlightTimeSeconds = flightTimeSeconds;
                ImpactSpeedMetersPerSecond = impactSpeedMetersPerSecond;
                DescentAngleDegrees = descentAngleDegrees;
            }

            /// <summary>False when the shell was still airborne when the simulation gave up.</summary>
            public bool ReachedTargetHeight { get; }

            /// <summary>Horizontal distance travelled, in metres.</summary>
            public float RangeMeters { get; }

            public float FlightTimeSeconds { get; }

            /// <summary>Striking velocity. This, not muzzle velocity, drives armour penetration.</summary>
            public float ImpactSpeedMetersPerSecond { get; }

            /// <summary>
            /// Angle below horizontal on arrival, positive downward. Selects which armour the
            /// shell tests: a steep arrival is nearly normal to the deck and heavily oblique to
            /// the belt, and a flat arrival is the reverse.
            /// </summary>
            public float DescentAngleDegrees { get; }
        }

        /// <summary>Instantaneous acceleration on a shell: gravity plus quadratic drag opposing motion.</summary>
        public static Vector3 AccelerationAt(Vector3 velocity, float dragFactor)
        {
            var acceleration = new Vector3(0f, -GravityMetersPerSecondSquared, 0f);

            if (dragFactor > 0f)
            {
                // Quadratic in speed, directed against the velocity vector. Written as
                // velocity * (factor * magnitude) rather than normalising, which would divide
                // by zero at the apex of a purely vertical shot.
                acceleration -= velocity * (dragFactor * velocity.magnitude);
            }

            return acceleration;
        }

        /// <summary>
        /// Advances one shell by one step, midpoint method. Called by the firing table, by the
        /// live projectile, and by the server's re-simulation, which is the whole reason it is
        /// a single public function: a solver that predicts with different maths than the
        /// projectile flies with produces an aim point that is quietly always wrong.
        ///
        /// Plain Euler was not good enough here. Over a 60 second battleship trajectory it
        /// accumulates enough error to move the impact point by tens of metres, which is the
        /// same order as the dispersion the game is trying to model.
        /// </summary>
        public static void Step(ref Vector3 position, ref Vector3 velocity, float dragFactor, float deltaTime)
        {
            Vector3 acceleration = AccelerationAt(velocity, dragFactor);
            Vector3 midVelocity = velocity + acceleration * (deltaTime * 0.5f);
            Vector3 midAcceleration = AccelerationAt(midVelocity, dragFactor);

            position += (velocity + midAcceleration * (deltaTime * 0.5f)) * deltaTime;
            velocity += midAcceleration * deltaTime;
        }

        /// <summary>
        /// Flies one shell in the vertical plane and reports where it crossed the target
        /// height on the way down. Downrange distance is +Z and altitude is +Y, so the result
        /// is independent of bearing and can be rotated onto any azimuth by the caller.
        /// </summary>
        /// <param name="launchHeightMeters">Muzzle height above the waterline.</param>
        /// <param name="targetHeightMeters">Height the shell is being scored against. Zero is the sea surface.</param>
        public static Impact Simulate(in Shell shell, float elevationDegrees, float launchHeightMeters, float targetHeightMeters)
        {
            if (!shell.IsValid)
            {
                return default;
            }

            float dragFactor = shell.DragFactor;
            float radians = elevationDegrees * Mathf.Deg2Rad;

            var position = new Vector3(0f, launchHeightMeters, 0f);
            var velocity = new Vector3(0f, Mathf.Sin(radians), Mathf.Cos(radians)) * shell.MuzzleVelocityMetersPerSecond;

            float elapsed = 0f;

            for (int step = 0; step < MaxIntegrationSteps; step++)
            {
                Vector3 previousPosition = position;
                Vector3 previousVelocity = velocity;

                Step(ref position, ref velocity, dragFactor, IntegrationStepSeconds);
                elapsed += IntegrationStepSeconds;

                // Descending requirement matters for a gun firing from a mast height at a
                // target above it: the shell can be below target height on the way up.
                if (position.y > targetHeightMeters || velocity.y >= 0f)
                {
                    continue;
                }

                // Interpolate to the crossing so range and flight time are not quantised to
                // the integration step. At 800 m/s one whole step is 13 metres of range.
                float drop = previousPosition.y - position.y;
                float fraction = drop > Mathf.Epsilon
                    ? Mathf.Clamp01((previousPosition.y - targetHeightMeters) / drop)
                    : 0f;

                Vector3 impactPosition = Vector3.Lerp(previousPosition, position, fraction);
                Vector3 impactVelocity = Vector3.Lerp(previousVelocity, velocity, fraction);
                float impactTime = elapsed - IntegrationStepSeconds * (1f - fraction);

                return new Impact(
                    true,
                    impactPosition.z,
                    impactTime,
                    impactVelocity.magnitude,
                    DescentAngleDegrees(impactVelocity));
            }

            return new Impact(false, position.z, elapsed, velocity.magnitude, DescentAngleDegrees(velocity));
        }

        /// <summary>Angle below horizontal of a velocity vector, positive downward.</summary>
        public static float DescentAngleDegrees(Vector3 velocity)
        {
            float horizontal = new Vector2(velocity.x, velocity.z).magnitude;
            return -Mathf.Atan2(velocity.y, horizontal) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Penetration in millimetres of armour at a given striking velocity, scaled from the
        /// figure authored on the shell. Authoring "350mm at 700 m/s" stays legible to a
        /// designer in a way that a raw DeMarre constant never does.
        /// </summary>
        public static float PenetrationMillimeters(float referencePenetrationMillimeters, float referenceVelocityMetersPerSecond, float impactSpeedMetersPerSecond)
        {
            if (referenceVelocityMetersPerSecond <= 0f || impactSpeedMetersPerSecond <= 0f)
            {
                return 0f;
            }

            float ratio = impactSpeedMetersPerSecond / referenceVelocityMetersPerSecond;
            return referencePenetrationMillimeters * Mathf.Pow(ratio, PenetrationVelocityExponent);
        }

        /// <summary>
        /// Armour thickness as the shell actually experiences it, given how obliquely it
        /// arrives. Oblique impacts present a longer path through the same plate.
        ///
        /// Deck armour is horizontal, so a shell descending at 90 degrees strikes it square
        /// and a flat shell barely grazes it. Belt armour is vertical, so the relationship
        /// inverts. Passing the plate orientation rather than having two functions keeps that
        /// symmetry visible.
        /// </summary>
        /// <param name="angleFromPlateNormalDegrees">Zero is a square hit. Approaching 90 is a graze.</param>
        public static float EffectiveThicknessMillimeters(float nominalThicknessMillimeters, float angleFromPlateNormalDegrees)
        {
            // Clamped short of 90 because the secant diverges there, and a true graze should
            // be resolved as a ricochet by the damage model rather than as infinite armour.
            float clamped = Mathf.Clamp(Mathf.Abs(angleFromPlateNormalDegrees), 0f, 85f);
            return nominalThicknessMillimeters / Mathf.Cos(clamped * Mathf.Deg2Rad);
        }

        /// <summary>Impact obliquity against horizontal deck armour, from the shell's descent angle.</summary>
        public static float DeckObliquityDegrees(float descentAngleDegrees)
        {
            return 90f - Mathf.Abs(descentAngleDegrees);
        }

        /// <summary>
        /// Impact obliquity against vertical belt armour. Combines the descent angle with how
        /// far off the beam the shell arrived, so a shell hitting a ship bow-on is fighting far
        /// more effective plate than one hitting it broadside.
        /// </summary>
        public static float BeltObliquityDegrees(float descentAngleDegrees, float azimuthOffBeamDegrees)
        {
            float descent = Mathf.Abs(descentAngleDegrees) * Mathf.Deg2Rad;
            float azimuth = Mathf.Abs(azimuthOffBeamDegrees) * Mathf.Deg2Rad;

            // Angle between the shell's path and the plate normal, which is horizontal and
            // perpendicular to the hull side.
            float cosine = Mathf.Cos(descent) * Mathf.Cos(azimuth);
            return Mathf.Acos(Mathf.Clamp01(cosine)) * Mathf.Rad2Deg;
        }
    }
}
