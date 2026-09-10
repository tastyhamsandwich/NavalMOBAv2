using UnityEngine;

namespace NavalMOBA.Common
{
    /// <summary>
    /// A precomputed range table for one shell in one gun: elevation in, range and flight time
    /// out, and the inverse.
    ///
    /// This exists because the interesting direction is the one that has no closed form. Given
    /// an elevation, the range follows from integrating the trajectory. Given a range, finding
    /// the elevation that produces it means inverting that integration, and with drag in the
    /// model there is no algebra for it. Solving numerically per mount per tick would mean
    /// re-flying a full trajectory a dozen times to place one aim point.
    ///
    /// So the trajectories are flown once, up front, and stored. Lookups afterwards cost an
    /// array scan and a lerp. Real ships solved this the same way and for the same reason:
    /// the gunnery officer was reading a printed range table, not doing calculus.
    ///
    /// Build once per gun and shell combination and cache it. A table is immutable and carries
    /// no scene references, so it is safe to share between every ship firing that combination,
    /// and safe to build on the server.
    ///
    /// Note that maximum range is a property of this pairing, never of the gun alone. A heavy
    /// AP shell and a light HE shell from the same barrel have different tables and different
    /// maximum ranges, which is exactly the tradeoff the ammunition choice is meant to be.
    /// </summary>
    public sealed class FiringTable
    {
        /// <summary>One solved trajectory. Every field is an outcome of the elevation except the elevation itself.</summary>
        public readonly struct Entry
        {
            public Entry(float elevationDegrees, float rangeMeters, float flightTimeSeconds, float impactSpeedMetersPerSecond, float descentAngleDegrees)
            {
                ElevationDegrees = elevationDegrees;
                RangeMeters = rangeMeters;
                FlightTimeSeconds = flightTimeSeconds;
                ImpactSpeedMetersPerSecond = impactSpeedMetersPerSecond;
                DescentAngleDegrees = descentAngleDegrees;
            }

            public float ElevationDegrees { get; }
            public float RangeMeters { get; }
            public float FlightTimeSeconds { get; }
            public float ImpactSpeedMetersPerSecond { get; }
            public float DescentAngleDegrees { get; }

            public static Entry Lerp(in Entry from, in Entry to, float t)
            {
                return new Entry(
                    Mathf.Lerp(from.ElevationDegrees, to.ElevationDegrees, t),
                    Mathf.Lerp(from.RangeMeters, to.RangeMeters, t),
                    Mathf.Lerp(from.FlightTimeSeconds, to.FlightTimeSeconds, t),
                    Mathf.Lerp(from.ImpactSpeedMetersPerSecond, to.ImpactSpeedMetersPerSecond, t),
                    Mathf.Lerp(from.DescentAngleDegrees, to.DescentAngleDegrees, t));
            }
        }

        /// <summary>
        /// Sampling resolution. A quarter of a degree is finer than any gun can be laid and
        /// keeps interpolation error well under the dispersion the game applies afterwards.
        /// </summary>
        public const float DefaultStepDegrees = 0.25f;

        private readonly Entry[] entries;
        private readonly float minElevationDegrees;
        private readonly float stepDegrees;
        private readonly int peakIndex;

        private FiringTable(Entry[] tableEntries, float minElevation, float step, int peak)
        {
            entries = tableEntries;
            minElevationDegrees = minElevation;
            stepDegrees = step;
            peakIndex = peak;
        }

        /// <summary>Shortest range the gun can reach, at full depression. Targets inside this cannot be engaged.</summary>
        public float MinRangeMeters => entries[0].RangeMeters;

        /// <summary>
        /// Greatest range reachable within the mount's elevation limits. Note this is the peak
        /// of the curve, which is not necessarily at maximum elevation: with drag the optimum
        /// sits nearer 40 degrees than 45, so a mount that elevates to 85 loses range past the
        /// peak rather than gaining it.
        /// </summary>
        public float MaxRangeMeters => entries[peakIndex].RangeMeters;

        /// <summary>Elevation that achieves maximum range. What the automatic FCS lays to when a target is out of reach.</summary>
        public float MaxRangeElevationDegrees => entries[peakIndex].ElevationDegrees;

        public int SampleCount => entries.Length;

        /// <summary>
        /// Flies the whole elevation band once and stores the results. Expect a few hundred
        /// trajectory integrations, so do this at load and keep the result, never per shot.
        /// </summary>
        /// <param name="launchHeightMeters">Muzzle height above the waterline.</param>
        /// <param name="targetHeightMeters">Height solutions are scored against. Zero is the sea surface.</param>
        public static FiringTable Build(
            in Ballistics.Shell shell,
            float minElevationDegrees,
            float maxElevationDegrees,
            float launchHeightMeters,
            float targetHeightMeters = 0f,
            float stepDegrees = DefaultStepDegrees)
        {
            if (!shell.IsValid || maxElevationDegrees < minElevationDegrees || stepDegrees <= 0f)
            {
                return null;
            }

            int count = Mathf.Max(2, Mathf.CeilToInt((maxElevationDegrees - minElevationDegrees) / stepDegrees) + 1);
            var built = new Entry[count];

            int peak = 0;
            float peakRange = float.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                float elevation = Mathf.Min(minElevationDegrees + i * stepDegrees, maxElevationDegrees);
                Ballistics.Impact impact = Ballistics.Simulate(shell, elevation, launchHeightMeters, targetHeightMeters);

                built[i] = new Entry(
                    elevation,
                    impact.RangeMeters,
                    impact.FlightTimeSeconds,
                    impact.ImpactSpeedMetersPerSecond,
                    impact.DescentAngleDegrees);

                if (impact.RangeMeters > peakRange)
                {
                    peakRange = impact.RangeMeters;
                    peak = i;
                }
            }

            return new FiringTable(built, minElevationDegrees, stepDegrees, peak);
        }

        /// <summary>
        /// The elevation that puts a shell at the given range, on the low arc, plus the flight
        /// time and striking conditions that come with it.
        ///
        /// Low arc only. It is the shorter flight time of the two solutions, so it is what a
        /// gunner wants against a moving ship. A high arc lookup would search past the peak
        /// instead and is worth adding only if plunging fire becomes a deliberate choice.
        ///
        /// Returns false when the range is outside what the mount can reach, which the caller
        /// should surface as "will not reach" rather than silently laying to some other range.
        /// </summary>
        public bool TryGetSolutionForRange(float rangeMeters, out Entry solution)
        {
            solution = default;

            if (rangeMeters < MinRangeMeters || rangeMeters > MaxRangeMeters)
            {
                return false;
            }

            // Range rises monotonically up to the peak, so a straight scan of the rising side
            // finds the single low arc bracket. Cheap enough at a few hundred entries, and it
            // avoids assuming the curve is smooth enough for a bisection to be safe.
            for (int i = 1; i <= peakIndex; i++)
            {
                float lower = entries[i - 1].RangeMeters;
                float upper = entries[i].RangeMeters;

                if (rangeMeters > upper)
                {
                    continue;
                }

                float span = upper - lower;
                float t = span > Mathf.Epsilon ? (rangeMeters - lower) / span : 0f;
                solution = Entry.Lerp(entries[i - 1], entries[i], t);
                return true;
            }

            solution = entries[peakIndex];
            return true;
        }

        /// <summary>
        /// The forward direction: where a gun currently laid at this elevation will land.
        ///
        /// This is what the fall of shot marker reads. The marker is a direct lookup of the
        /// mount's live elevation, not a simulation, which is what makes it affordable to draw
        /// every frame on every turret. It also means the marker and the shell cannot disagree,
        /// because the table was built with the same integrator the shell flies with.
        /// </summary>
        public Entry SampleAtElevation(float elevationDegrees)
        {
            float position = (elevationDegrees - minElevationDegrees) / stepDegrees;
            int index = Mathf.Clamp(Mathf.FloorToInt(position), 0, entries.Length - 1);

            if (index >= entries.Length - 1)
            {
                return entries[entries.Length - 1];
            }

            return Entry.Lerp(entries[index], entries[index + 1], Mathf.Clamp01(position - index));
        }

        /// <summary>Range a gun laid at this elevation will reach. Convenience over SampleAtElevation.</summary>
        public float RangeAtElevation(float elevationDegrees)
        {
            return SampleAtElevation(elevationDegrees).RangeMeters;
        }
    }
}
