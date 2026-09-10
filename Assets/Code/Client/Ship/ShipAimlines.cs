using System.Collections.Generic;
using NavalMOBA.Common.SODefs;
using UnityEngine;

namespace NavalMOBA.Client.Ship
{
    /// <summary>
    /// Draws one flat bearing line per gun mount, out to the limit of the player's vision.
    /// These show training only, never elevation: a mount at 5 degrees and the same mount at
    /// 70 degrees draw an identical line. Elevation is communicated by the fall of shot
    /// marker instead, which is the whole reason the pair works as an aiming instrument.
    ///
    /// Presentation only. This reads mount state and never writes it, so it can be stripped
    /// from a headless server build without touching gunnery.
    ///
    /// Updated in LateUpdate rather than on the simulation tick because it is a render concern.
    /// The underlying turrets only move at tick rate, so the lines inherit that cadence
    /// regardless, but drawing here keeps presentation off the deterministic path.
    /// </summary>
    [RequireComponent(typeof(ShipMountSystem))]
    [RequireComponent(typeof(ShipFireControl))]
    public sealed class ShipAimlines : MonoBehaviour
    {
        [Header("Appearance")]
        [Tooltip("Colour of mounts currently answering the player's gunnery keys.")]
        [SerializeField] private Color selectedColor = new Color(0.2f, 0.65f, 1f, 1f);

        [Tooltip("Colour of mounts in the displayed battery that are not in the current selection. Still drawn, so the player can see where the rest of the battery is pointing.")]
        [SerializeField] private Color unselectedColor = new Color(0.55f, 0.55f, 0.55f, 0.65f);

        [SerializeField, Min(0.01f)] private float lineWidth = 2f;

        [Tooltip("Optional. Falls back to Sprites/Default, which respects the per-line vertex colours. An assigned material is used exactly as authored, including its render queue.")]
        [SerializeField] private Material lineMaterial;

        [Header("Range")]
        [Tooltip("How far from the mount the line starts, in metres. Cosmetic only: keeps the lines from sprouting out of the turret itself and clear of the hull.")]
        [SerializeField, Min(0f)] private float standoffMeters = 35f;

        [Tooltip("How far the lines are drawn, in metres, measured from the mount. Placeholder for the ship's spotting range once vision exists.")]
        [SerializeField, Min(1f)] private float lineLengthMeters = 2000f;

        private ShipMountSystem mountSystem;
        private ShipFireControl fireControl;

        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Material runtimeMaterial;
        private Transform lineRoot;

        private void Awake()
        {
            mountSystem = GetComponent<ShipMountSystem>();
            fireControl = GetComponent<ShipFireControl>();
        }

        private void OnDestroy()
        {
            // Only the material this component manufactured. A material assigned in the
            // inspector is a shared asset and must not be destroyed.
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        private void LateUpdate()
        {
            IReadOnlyList<ShipMountSystem.MountInstance> mounts = mountSystem.GunMounts;

            // Mounts are fitted during the spawner's Start, a phase after this component's
            // Awake, so the line pool is built on first use rather than up front.
            if (lines.Count != mounts.Count)
            {
                RebuildLines(mounts.Count);
            }

            if (mounts.Count == 0)
            {
                return;
            }

            BatteryRole displayedBattery = DisplayedBattery();
            float shipHeading = transform.eulerAngles.y;

            for (int i = 0; i < mounts.Count; i++)
            {
                ShipMountSystem.MountInstance mount = mounts[i];
                LineRenderer line = lines[i];

                if (!mount.IsEquipped || mount.Station.Battery != displayedBattery)
                {
                    line.enabled = false;
                    continue;
                }

                TurretController turret = mount.Controller;
                Vector3 origin = mount.Equipment.transform.position;

                // Bearing relative to the bow already folds in the station's rest bearing, so
                // an aft mount needs no special case here.
                float worldBearing = shipHeading + turret.BearingRelativeToBowDegrees;
                float radians = worldBearing * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

                line.enabled = true;

                // Clamped so a standoff authored larger than the range cannot invert the line.
                float far = Mathf.Max(lineLengthMeters, standoffMeters + 1f);
                line.SetPosition(0, origin + direction * standoffMeters);
                line.SetPosition(1, origin + direction * far);

                Color color = fireControl.IsSelected(mount.Station) ? selectedColor : unselectedColor;
                line.startColor = color;
                line.endColor = color;
            }
        }

        /// <summary>
        /// Which battery's lines are on screen. Secondaries stay hidden until the player
        /// selects them, at which point the primaries hide instead, so the display never
        /// shows two batteries of lines at once.
        /// </summary>
        private BatteryRole DisplayedBattery()
        {
            return fireControl.Selection == ShipFireControl.GunSelection.Secondary
                ? BatteryRole.Secondary
                : BatteryRole.Primary;
        }

        private void RebuildLines(int count)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null)
                {
                    Destroy(lines[i].gameObject);
                }
            }

            lines.Clear();

            if (count == 0)
            {
                return;
            }

            if (lineRoot == null)
            {
                var rootObject = new GameObject("Aimlines");
                lineRoot = rootObject.transform;
                lineRoot.SetParent(transform, false);
            }

            Material material = ResolveMaterial();

            for (int i = 0; i < count; i++)
            {
                var lineObject = new GameObject($"Aimline {i}");
                lineObject.transform.SetParent(lineRoot, false);

                LineRenderer line = lineObject.AddComponent<LineRenderer>();

                // Positions are fed in world space. Left local, the lines would swing with the
                // hull instead of holding the bearing they were given.
                line.useWorldSpace = true;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.positionCount = 2;
                line.startWidth = lineWidth;
                line.endWidth = lineWidth;
                line.receiveShadows = false;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.sharedMaterial = material;
                line.enabled = false;

                lines.Add(line);
            }
        }

        private Material ResolveMaterial()
        {
            if (lineMaterial != null)
            {
                return lineMaterial;
            }

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    Debug.LogError($"{nameof(ShipAimlines)} on '{name}': could not find the Sprites/Default shader and no line material was assigned. Aimlines will not be visible.", this);
                    return null;
                }

                // One material shared by every line on this ship. Colour varies per line
                // through LineRenderer vertex colours, so this never needs duplicating.
                runtimeMaterial = new Material(shader) { name = "Aimline (Runtime)" };

                // The water is a transparent Shader Graph: same queue as Sprites/Default, and
                // it writes no depth. Transparent renderers sort by camera distance to their
                // bounds centre, which for a 2 km line sits about 1 km downrange, so the line
                // sorts as "far", draws before the nearer water tiles, and the water then
                // alpha-blends over the top of it. That is the washed-out or missing line.
                // Overlay draws after all transparency, so the water can no longer paint over
                // it from any angle. Depth testing is left alone on purpose: opaque geometry
                // such as the hull still occludes lines on the far side, which reads as a
                // useful depth cue rather than a bug.
                runtimeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            }

            return runtimeMaterial;
        }
    }
}
