using System.Collections.Generic;
using UnityEngine;

namespace NavalMOBA.Common
{
    /// <summary>
    /// Fixed-rate tick driver, decoupled from frame rate. This is the per-tick update loop
    /// the simulation runs against. Today it runs client-side only for prototyping; once
    /// Photon Fusion (or equivalent) lands, the server drives its own ticker at this same
    /// interval and this one becomes the client-side prediction loop.
    /// </summary>
    public sealed class SimulationTicker : MonoBehaviour
    {
        [SerializeField] private float tickRate = 30f;

        private readonly List<ITickable> tickables = new List<ITickable>();
        private float tickAccumulator;

        public static SimulationTicker Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Register(ITickable tickable)
        {
            if (!tickables.Contains(tickable))
            {
                tickables.Add(tickable);
            }
        }

        public void Unregister(ITickable tickable)
        {
            tickables.Remove(tickable);
        }

        private void Update()
        {
            float tickInterval = 1f / tickRate;
            tickAccumulator += Time.deltaTime;

            while (tickAccumulator >= tickInterval)
            {
                for (int i = 0; i < tickables.Count; i++)
                {
                    tickables[i].Tick(tickInterval);
                }

                tickAccumulator -= tickInterval;
            }
        }
    }
}
