using UnityEngine;

namespace NavalMOBA.Common
{
    /// <summary>
    /// Deterministic ship movement math: throttle/EEP ramping, realistic speed-dependent
    /// turning, and waypoint navigation (right-click move-to-target with SteeringHold /
    /// BreakOff / AutomaticThrottling behaviors). Lives in Common (not Server) on purpose:
    /// once networking lands, the client runs this exact same Tick to predict locally, and
    /// the server runs it to authoritatively resolve. Both sides must produce identical
    /// output for the same state/input/config/deltaTime or reconciliation breaks.
    ///
    /// Ported from the original single-player ShipController prototype. EEP timers were
    /// coroutine-driven there; here they are plain countdowns advanced per tick so the same
    /// code can run headless on a server.
    /// </summary>
    public static class ShipMovementSimulation
    {
        private const float WaypointArrivalDistance = 15f;

        public static ShipMovementState Tick(
            ShipMovementState state,
            ShipMovementInput input,
            ShipMovementConfig config,
            float deltaTime)
        {
            float acceleration = config.accelerationKnots * ShipMovementConfig.KnotsToMetersPerSecond;
            float deceleration = config.decelerationKnots * ShipMovementConfig.KnotsToMetersPerSecond;
            float maxForwardSpeed = config.maxForwardSpeedKnots * ShipMovementConfig.KnotsToMetersPerSecond;
            float maxReverseSpeed = config.maxReverseSpeedKnots * ShipMovementConfig.KnotsToMetersPerSecond;

            state = TickThrottle(state, input, config, deltaTime);
            state = TickSpeed(state, config, maxForwardSpeed, maxReverseSpeed, acceleration, deceleration, deltaTime);
            state = TickTurningParameters(state, config);

            state.Position += HeadingToForward(state.HeadingDegrees) * state.CurrentSpeed * deltaTime;

            if (input.SetWaypointRequested)
            {
                state = SetWaypoint(state, config, input.WaypointWorldPosition);
            }

            if (state.HasWaypoint)
            {
                state = TickWaypointNavigation(state, config, deltaTime);
            }

            return state;
        }

        private static Vector3 HeadingToForward(float headingDegrees)
        {
            return Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;
        }

        // ---- Throttle / EEP ----

        private static ShipMovementState TickThrottle(
            ShipMovementState state,
            ShipMovementInput input,
            ShipMovementConfig config,
            float deltaTime)
        {
            bool speedUp = input.ThrottleUpHeld;
            bool slowDown = input.ThrottleDownHeld;

            // Throttle is a 0-100 engine order, so it must ramp in percent per second. It is
            // deliberately NOT driven by accelerationKnots (metres per second squared); the
            // two were conflated once and made full throttle take 65 seconds to reach.
            float throttleRate = config.throttleRatePercentPerSecond;

            if (speedUp && !slowDown)
            {
                float targetThrottle = state.EepActive ? config.eepMaxThrottle : config.maxThrottle;
                state.CurrentThrottle = Mathf.MoveTowards(state.CurrentThrottle, targetThrottle, throttleRate * deltaTime);

                if (!state.EepActive
                    && Mathf.Approximately(state.CurrentThrottle, config.maxThrottle)
                    && state.WaitingForSecondPress
                    && !state.EepOnCooldown)
                {
                    state = ActivateEep(state, config);
                    state.WaitingForSecondPress = false;
                }
            }
            else if (slowDown && !speedUp)
            {
                state.CurrentThrottle = Mathf.MoveTowards(state.CurrentThrottle, -config.maxThrottle, throttleRate * deltaTime);

                if (state.AtMaxThrottle && state.CurrentThrottle < config.maxThrottle)
                {
                    state.AtMaxThrottle = false;
                }

                if (state.EepActive && state.CurrentThrottle <= config.maxThrottle)
                {
                    state = EndEep(state, config);
                }
            }

            if (Mathf.Approximately(state.CurrentThrottle, config.maxThrottle))
            {
                if (!state.AtMaxThrottle)
                {
                    state.AtMaxThrottle = true;
                }

                if (!speedUp && state.AtMaxThrottle)
                {
                    state.WaitingForSecondPress = true;
                }
            }

            return TickEepTimers(state, config, deltaTime);
        }

        private static ShipMovementState ActivateEep(ShipMovementState state, ShipMovementConfig config)
        {
            if (state.EepActive || state.EepOnCooldown)
            {
                return state;
            }

            state.EepActive = true;
            state.EepDurationRemaining = config.eepDuration;
            return state;
        }

        private static ShipMovementState EndEep(ShipMovementState state, ShipMovementConfig config)
        {
            state.EepActive = false;
            state.EepDurationRemaining = 0f;
            state.CurrentThrottle = config.maxThrottle;
            state.EepOnCooldown = true;
            state.EepCooldownRemaining = config.eepCooldown;
            return state;
        }

        private static ShipMovementState TickEepTimers(ShipMovementState state, ShipMovementConfig config, float deltaTime)
        {
            if (state.EepActive)
            {
                state.EepDurationRemaining -= deltaTime;
                if (state.EepDurationRemaining <= 0f)
                {
                    state = EndEep(state, config);
                }
            }

            if (state.EepOnCooldown)
            {
                state.EepCooldownRemaining -= deltaTime;
                if (state.EepCooldownRemaining <= 0f)
                {
                    state.EepOnCooldown = false;
                    state.EepCooldownRemaining = 0f;
                }
            }

            return state;
        }

        // ---- Speed & turning ----

        private static ShipMovementState TickSpeed(
            ShipMovementState state,
            ShipMovementConfig config,
            float maxForwardSpeed,
            float maxReverseSpeed,
            float acceleration,
            float deceleration,
            float deltaTime)
        {
            float targetSpeed;

            if (state.CurrentThrottle >= 0f && state.CurrentThrottle <= config.maxThrottle)
            {
                targetSpeed = maxForwardSpeed * (state.CurrentThrottle / config.maxThrottle);
            }
            else if (state.CurrentThrottle > config.maxThrottle && state.EepActive)
            {
                targetSpeed = maxForwardSpeed * 1.15f * (state.CurrentThrottle / config.eepMaxThrottle);
            }
            else
            {
                targetSpeed = -(maxReverseSpeed * (state.CurrentThrottle / -config.maxThrottle));
            }

            float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(state.CurrentSpeed) ? acceleration : deceleration;
            state.CurrentSpeed = Mathf.MoveTowards(state.CurrentSpeed, targetSpeed, rate * deltaTime);
            return state;
        }

        private static ShipMovementState TickTurningParameters(ShipMovementState state, ShipMovementConfig config)
        {
            float speedMps = Mathf.Abs(state.CurrentSpeed);
            float turnRate = CalculateTurnRateAtSpeed(config, speedMps);

            state.CurrentTurnRateDegreesPerSecond = turnRate;

            // Radius is derived from the rate rather than the other way round. The rate is the
            // authored number, so deriving keeps the circle the waypoint planner reasons about
            // identical to the circle the ship actually flies. The old model computed them
            // independently and they could disagree.
            state.CurrentTurnRadius = turnRate > 0.01f
                ? speedMps / (turnRate * Mathf.Deg2Rad)
                : float.MaxValue;

            return state;
        }

        /// <summary>
        /// Rate of turn as a function of speed. Zero with no way on, ramping up to the peak
        /// rate at the hull's optimal turning fraction, then falling off to the degraded rate
        /// at 100 percent throttle.
        ///
        /// Both segments are linear. A quadratic turn circle was what made the old model
        /// unable to hit an authored degradation figure: the geometry dictated the falloff and
        /// the designer had no say in it.
        /// </summary>
        private static float CalculateTurnRateAtSpeed(ShipMovementConfig config, float speedMps)
        {
            if (speedMps < config.minimumSpeedForTurning)
            {
                return 0f;
            }

            float peakRate = config.baseTurnRateDegreesPerSecond;

            if (!config.useRealisticTurning)
            {
                return peakRate;
            }

            float topSpeedMps = config.maxForwardSpeedKnots * ShipMovementConfig.KnotsToMetersPerSecond;
            float optimalSpeedMps = topSpeedMps * config.optimalTurningSpeedFraction;

            if (speedMps <= optimalSpeedMps)
            {
                return peakRate * Mathf.InverseLerp(config.minimumSpeedForTurning, optimalSpeedMps, speedMps);
            }

            // InverseLerp clamps, so EEP overboost past 100 percent throttle costs no more
            // rate than flank does. Change to an unclamped ratio if overboost should hurt.
            float overspeed = Mathf.InverseLerp(optimalSpeedMps, topSpeedMps, speedMps);
            return peakRate * (1f - config.turnRateDegradationAtFullSpeed * overspeed);
        }

        private static float CalculateMaxTurnAngle(ShipMovementConfig config, float distance, float speed)
        {
            if (Mathf.Abs(speed) <= config.minimumSpeedForTurning)
            {
                return 180f;
            }

            float timeAvailable = distance / Mathf.Abs(speed);

            // Shares the one rate curve rather than recomputing a radius. The curve already
            // caps at the peak rate, so the old Min against baseTurnRateDegreesPerSecond is
            // now redundant.
            float turnRateAtSpeed = CalculateTurnRateAtSpeed(config, Mathf.Abs(speed));

            return Mathf.Min(turnRateAtSpeed * timeAvailable, 180f);
        }

        private static float CalculateThrottleReductionForTurn(
            ShipMovementConfig config,
            ShipMovementState state,
            float requiredAngle,
            float distance)
        {
            float timeNeeded = requiredAngle / config.baseTurnRateDegreesPerSecond;
            float requiredSpeed = distance / timeNeeded;
            float speedReduction = Mathf.Clamp01(requiredSpeed / Mathf.Abs(state.CurrentSpeed));
            return 1f - speedReduction;
        }

        private static bool CanReachWithTightTurn(ShipMovementConfig config, ShipMovementState state, Vector3 waypointPosition)
        {
            float distanceToWaypoint = Vector3.Distance(state.Position, waypointPosition);

            // Now that the flown radius is derived from rate it is 80m to 185m rather than the
            // authored 25m, so testing against the authored figure would accept destinations
            // the ship cannot physically make. Falls back to the authored value when stopped,
            // where the derived radius is infinite and would reject every waypoint.
            float turnRadius = state.CurrentTurnRadius < float.MaxValue
                ? Mathf.Max(config.minimumTurningRadius, state.CurrentTurnRadius)
                : config.minimumTurningRadius;

            return distanceToWaypoint > turnRadius;
        }

        // ---- Waypoint navigation ----

        private static ShipMovementState SetWaypoint(ShipMovementState state, ShipMovementConfig config, Vector3 point)
        {
            state.HasWaypoint = true;
            state.WaypointPosition = point;

            Vector2 shipPos2D = new Vector2(state.Position.x, state.Position.z);
            Vector2 targetPos2D = new Vector2(point.x, point.z);
            state.InitialDistanceToWaypoint = Vector2.Distance(shipPos2D, targetPos2D);
            state.ClosestDistanceToWaypoint = state.InitialDistanceToWaypoint;
            state.HasApproachedWaypoint = false;
            state.HasInitiatedTurn = false;
            state.IsRoughlyFacing = false;
            state.HasReachedTangentialBearing = false;
            state.IsMovingAwayAfterTangential = false;

            Vector3 targetDir = point - state.Position;
            targetDir.y = 0f;
            state.InitialAngleToWaypoint = Vector3.Angle(HeadingToForward(state.HeadingDegrees), targetDir);

            state.CurrentWaypointAnalysis = AnalyzeWaypointReachability(state, config, point);

            if (state.IsAutoThrottling)
            {
                state.CurrentThrottle = state.OriginalThrottleBeforeThrottling;
                state.IsAutoThrottling = false;
            }

            return state;
        }

        private static WaypointAnalysis AnalyzeWaypointReachability(
            ShipMovementState state,
            ShipMovementConfig config,
            Vector3 waypointPosition)
        {
            var analysis = new WaypointAnalysis();

            Vector3 targetDir = waypointPosition - state.Position;
            targetDir.y = 0f;
            analysis.requiredTurnAngle = Vector3.Angle(HeadingToForward(state.HeadingDegrees), targetDir);

            float distanceToWaypoint = Vector3.Distance(state.Position, waypointPosition);

            analysis.maxTurnAngleAtCurrentSpeed = CalculateMaxTurnAngle(config, distanceToWaypoint, state.CurrentSpeed);
            analysis.maxTurnAngleAtReducedSpeed = CalculateMaxTurnAngle(config, distanceToWaypoint, state.CurrentSpeed * 0.5f);

            if (analysis.requiredTurnAngle <= analysis.maxTurnAngleAtCurrentSpeed)
            {
                analysis.reachability = WaypointReachability.DirectlyReachable;
                analysis.willRequireAutoThrottling = false;
                analysis.willTriggerBreakOff = false;
                analysis.analysisDetails =
                    $"Directly reachable - Required: {analysis.requiredTurnAngle:F1}°, Available: {analysis.maxTurnAngleAtCurrentSpeed:F1}°";
            }
            else if (config.movementStyle == ShipMovementStyle.AutomaticThrottling
                     && analysis.requiredTurnAngle <= analysis.maxTurnAngleAtReducedSpeed)
            {
                analysis.reachability = WaypointReachability.RequiresThrottling;
                analysis.willRequireAutoThrottling = true;
                analysis.willTriggerBreakOff = false;
                analysis.recommendedThrottleReduction = CalculateThrottleReductionForTurn(config, state, analysis.requiredTurnAngle, distanceToWaypoint);
                analysis.analysisDetails =
                    $"Requires throttling - Required: {analysis.requiredTurnAngle:F1}°, Current speed allows: {analysis.maxTurnAngleAtCurrentSpeed:F1}°, Reduced speed allows: {analysis.maxTurnAngleAtReducedSpeed:F1}°";
            }
            else if ((config.movementStyle == ShipMovementStyle.BreakOffOnClosestApproach
                      || config.movementStyle == ShipMovementStyle.BreakOffOnFirstApproach)
                     && CanReachWithTightTurn(config, state, waypointPosition))
            {
                analysis.reachability = WaypointReachability.WillBreakOff;
                analysis.willRequireAutoThrottling = false;
                analysis.willTriggerBreakOff = config.movementStyle != ShipMovementStyle.SteeringHold;
                analysis.analysisDetails =
                    $"Will break off - Turn too sharp ({analysis.requiredTurnAngle:F1}°) for available distance. Break-off logic will activate.";
            }
            else
            {
                analysis.reachability = WaypointReachability.Unreachable;
                analysis.willRequireAutoThrottling = false;
                analysis.willTriggerBreakOff = true;
                analysis.analysisDetails =
                    $"Unreachable - Required turn ({analysis.requiredTurnAngle:F1}°) exceeds ship's capabilities at this distance.";
            }

            return analysis;
        }

        private static ShipMovementState TickWaypointNavigation(ShipMovementState state, ShipMovementConfig config, float deltaTime)
        {
            Vector3 targetDir = state.WaypointPosition - state.Position;
            targetDir.y = 0f;

            Vector2 shipPos2D = new Vector2(state.Position.x, state.Position.z);
            Vector2 targetPos2D = new Vector2(state.WaypointPosition.x, state.WaypointPosition.z);
            float distanceToTarget = Vector2.Distance(shipPos2D, targetPos2D);

            if (state.CurrentWaypointAnalysis != null)
            {
                WaypointAnalysis newAnalysis = AnalyzeWaypointReachability(state, config, state.WaypointPosition);
                if (newAnalysis.reachability != state.CurrentWaypointAnalysis.reachability)
                {
                    state.CurrentWaypointAnalysis = newAnalysis;
                }
            }

            if (distanceToTarget < state.ClosestDistanceToWaypoint)
            {
                state.ClosestDistanceToWaypoint = distanceToTarget;
            }

            if (distanceToTarget < WaypointArrivalDistance)
            {
                return ClearWaypoint(state);
            }

            switch (config.movementStyle)
            {
                case ShipMovementStyle.SteeringHold:
                    return PerformTurn(state, targetDir, state.CurrentTurnRateDegreesPerSecond, deltaTime, config);
                case ShipMovementStyle.BreakOffOnClosestApproach:
                    return HandleBreakOffOnClosestApproach(state, config, targetDir, distanceToTarget, deltaTime);
                case ShipMovementStyle.BreakOffOnFirstApproach:
                    return HandleBreakOffOnFirstApproach(state, config, targetDir, distanceToTarget, deltaTime);
                case ShipMovementStyle.AutomaticThrottling:
                    return HandleAutomaticThrottling(state, config, targetDir, deltaTime);
                default:
                    return state;
            }
        }

        private static ShipMovementState HandleBreakOffOnClosestApproach(
            ShipMovementState state,
            ShipMovementConfig config,
            Vector3 targetDir,
            float currentDistance,
            float deltaTime)
        {
            float currentAngleToTarget = Vector3.Angle(HeadingToForward(state.HeadingDegrees), targetDir);
            if (!state.HasInitiatedTurn && currentAngleToTarget < state.InitialAngleToWaypoint - 10f)
            {
                state.HasInitiatedTurn = true;
            }

            if (!state.IsRoughlyFacing && currentDistance < state.InitialDistanceToWaypoint - 2f)
            {
                state.IsRoughlyFacing = true;
            }

            if (state.HasInitiatedTurn && state.IsRoughlyFacing && state.HasApproachedWaypoint
                && currentDistance > state.ClosestDistanceToWaypoint + 0.5f)
            {
                return ClearWaypoint(state);
            }

            if (!state.HasApproachedWaypoint && currentDistance <= state.ClosestDistanceToWaypoint + 1f)
            {
                state.HasApproachedWaypoint = true;
            }

            return PerformTurn(state, targetDir, state.CurrentTurnRateDegreesPerSecond, deltaTime, config);
        }

        private static ShipMovementState HandleBreakOffOnFirstApproach(
            ShipMovementState state,
            ShipMovementConfig config,
            Vector3 targetDir,
            float currentDistance,
            float deltaTime)
        {
            Vector3 forward = HeadingToForward(state.HeadingDegrees);
            float currentAngleToTarget = Vector3.Angle(forward, targetDir);

            if (!state.HasInitiatedTurn
                && ((currentAngleToTarget < state.InitialAngleToWaypoint - 10f && state.InitialAngleToWaypoint >= 30f)
                    || (state.InitialAngleToWaypoint < 30f && currentAngleToTarget < state.InitialAngleToWaypoint - 3f)))
            {
                state.HasInitiatedTurn = true;
            }

            if (!state.IsRoughlyFacing && currentDistance < state.InitialDistanceToWaypoint - 2f)
            {
                state.IsRoughlyFacing = true;
            }

            if (state.IsRoughlyFacing && !state.HasReachedTangentialBearing)
            {
                Vector3 normalizedTargetDir = targetDir.normalized;
                Vector3 normalizedForward = forward.normalized;
                float dotProduct = Vector3.Dot(normalizedForward, normalizedTargetDir);

                if (Mathf.Abs(dotProduct) < 0.3f
                    && (currentDistance < state.InitialDistanceToWaypoint * 0.7f || state.InitialDistanceToWaypoint <= 75f))
                {
                    state.HasReachedTangentialBearing = true;
                }
            }

            if (state.HasReachedTangentialBearing && !state.IsMovingAwayAfterTangential)
            {
                if (currentDistance > state.ClosestDistanceToWaypoint + 1f)
                {
                    state.IsMovingAwayAfterTangential = true;
                }
            }

            if (state.IsRoughlyFacing && state.HasReachedTangentialBearing && state.IsMovingAwayAfterTangential)
            {
                return ClearWaypoint(state);
            }

            if (currentDistance < state.ClosestDistanceToWaypoint)
            {
                state.ClosestDistanceToWaypoint = currentDistance;
            }

            return PerformTurn(state, targetDir, state.CurrentTurnRateDegreesPerSecond, deltaTime, config);
        }

        private static ShipMovementState HandleAutomaticThrottling(
            ShipMovementState state,
            ShipMovementConfig config,
            Vector3 targetDir,
            float deltaTime)
        {
            float acceleration = config.accelerationKnots * ShipMovementConfig.KnotsToMetersPerSecond;
            float angleToTarget = Vector3.Angle(HeadingToForward(state.HeadingDegrees), targetDir);

            if (state.CurrentWaypointAnalysis != null && state.CurrentWaypointAnalysis.willRequireAutoThrottling)
            {
                if (!state.IsAutoThrottling && angleToTarget > 30f)
                {
                    state.OriginalThrottleBeforeThrottling = state.CurrentThrottle;
                    state.IsAutoThrottling = true;
                }

                if (state.IsAutoThrottling)
                {
                    float targetThrottle = state.OriginalThrottleBeforeThrottling * (1f - state.CurrentWaypointAnalysis.recommendedThrottleReduction);
                    state.CurrentThrottle = Mathf.MoveTowards(state.CurrentThrottle, targetThrottle, acceleration * deltaTime);

                    if (angleToTarget < 15f)
                    {
                        state.IsAutoThrottling = false;
                    }
                }
            }
            else
            {
                if (angleToTarget > 45f && state.CurrentSpeed > 5f && !state.IsAutoThrottling)
                {
                    state.OriginalThrottleBeforeThrottling = state.CurrentThrottle;
                    state.IsAutoThrottling = true;
                }

                if (state.IsAutoThrottling)
                {
                    float throttleReduction = Mathf.Clamp01(angleToTarget / 90f);
                    float targetThrottle = state.OriginalThrottleBeforeThrottling * (1f - throttleReduction * 0.7f);
                    state.CurrentThrottle = Mathf.MoveTowards(state.CurrentThrottle, targetThrottle, acceleration * deltaTime);

                    if (angleToTarget < 20f)
                    {
                        state.IsAutoThrottling = false;
                    }
                }
            }

            float adjustedTurnRate = state.IsAutoThrottling ? state.CurrentTurnRateDegreesPerSecond * 1.5f : state.CurrentTurnRateDegreesPerSecond;
            return PerformTurn(state, targetDir, adjustedTurnRate, deltaTime, config);
        }

        private static ShipMovementState PerformTurn(
            ShipMovementState state,
            Vector3 targetDir,
            float turnRate,
            float deltaTime,
            ShipMovementConfig config)
        {
            // A rudder needs water flowing over it. This gate is unconditional: it used to be
            // skipped whenever useRealisticTurning was off, which let a ship pirouette at a
            // dead stop. Turning off the realistic turn curve should flatten the rate, not
            // repeal the need for way on.
            if (Mathf.Abs(state.CurrentSpeed) >= config.minimumSpeedForTurning)
            {
                if (targetDir.sqrMagnitude > 0.0001f)
                {
                    float targetHeading = Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
                    state.HeadingDegrees = Mathf.MoveTowardsAngle(state.HeadingDegrees, targetHeading, turnRate * deltaTime);
                }
            }

            return state;
        }

        private static ShipMovementState ClearWaypoint(ShipMovementState state)
        {
            state.HasWaypoint = false;
            state.WaypointPosition = Vector3.zero;
            state.CurrentWaypointAnalysis = null;

            if (state.IsAutoThrottling)
            {
                state.CurrentThrottle = state.OriginalThrottleBeforeThrottling;
                state.IsAutoThrottling = false;
            }

            state.ClosestDistanceToWaypoint = float.MaxValue;
            state.HasApproachedWaypoint = false;
            state.HasInitiatedTurn = false;
            state.InitialAngleToWaypoint = 0f;
            state.InitialDistanceToWaypoint = 0f;
            state.IsRoughlyFacing = false;
            state.HasReachedTangentialBearing = false;
            state.IsMovingAwayAfterTangential = false;

            return state;
        }
    }
}
