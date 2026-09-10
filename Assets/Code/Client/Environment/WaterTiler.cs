using UnityEngine;

namespace NavalMOBA.Client.Environment
{
    /// <summary>
    /// Builds a grid of water tiles at game start from a single source tile, then centres the
    /// whole grid on <see cref="groupCenter"/>.
    ///
    /// This lives on its own GameObject rather than on the tile itself. If the component sat on
    /// the tile, every clone would carry it and tile again on its own Start, recursing until the
    /// editor died.
    ///
    /// Placeholder for a real streamed/scrolling ocean. Every tile is built up front and none
    /// are culled or recycled, so cost scales with columns * rows: a 25x25 grid of 50m tiles is
    /// a 1250m square but also 625 renderers. This is a test harness for judging speed and
    /// scale, not a playable map.
    /// </summary>
    public sealed class WaterTiler : MonoBehaviour
    {
        [Tooltip("The tile to replicate. Reparented into the grid and reused as one of the cells.")]
        [SerializeField] private Transform sourceTile;

        [Header("Grid")]
        [SerializeField, Min(1)] private int columns = 5;
        [SerializeField, Min(1)] private int rows = 5;

        [Tooltip("World point the centre of the finished grid is placed at.")]
        [SerializeField] private Vector3 groupCenter = Vector3.zero;

        [Header("Tile Size")]
        [Tooltip("Leave at zero to measure the tile from its renderer bounds. Set explicitly only if the mesh has padding or overhang that makes the bounds the wrong spacing.")]
        [SerializeField] private Vector2 tileSizeOverride = Vector2.zero;

        private void Start()
        {
            if (sourceTile == null)
            {
                Debug.LogError($"{nameof(WaterTiler)} on '{name}' has no source tile assigned. No water was built.", this);
                return;
            }

            Vector2 tileSize = ResolveTileSize();
            if (tileSize.x <= 0f || tileSize.y <= 0f)
            {
                Debug.LogError($"{nameof(WaterTiler)} could not determine a tile size for '{sourceTile.name}' (got {tileSize}). Set tileSizeOverride.", this);
                return;
            }

            var container = new GameObject("WaterField");
            container.transform.SetPositionAndRotation(groupCenter, Quaternion.identity);

            // Offsets are measured from the grid's centre, so an odd count puts a tile's centre
            // dead on the origin and an even count puts a seam there. Both centre correctly.
            float halfSpanX = (columns - 1) * 0.5f;
            float halfSpanZ = (rows - 1) * 0.5f;

            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    var localPosition = new Vector3(
                        (column - halfSpanX) * tileSize.x,
                        0f,
                        (row - halfSpanZ) * tileSize.y);

                    // Reuse the source as the first cell instead of cloning it and leaving the
                    // original sitting off-grid as a duplicate at its old position.
                    Transform tile = (column == 0 && row == 0)
                        ? sourceTile
                        : Instantiate(sourceTile, container.transform);

                    tile.SetParent(container.transform);
                    tile.localPosition = localPosition;
                    tile.localRotation = Quaternion.identity;
                    tile.name = $"WaterTile_{column}_{row}";
                }
            }

            Debug.Log($"Water field built: {columns}x{rows} tiles of {tileSize.x}m x {tileSize.y}m, " +
                      $"{columns * tileSize.x}m x {rows * tileSize.y}m total, centred on {groupCenter}.");
        }

        private Vector2 ResolveTileSize()
        {
            if (tileSizeOverride.x > 0f && tileSizeOverride.y > 0f)
            {
                return tileSizeOverride;
            }

            // Renderer bounds are world space and already include the tile's scale, which is
            // what spacing needs. Mesh bounds would be local and would tile wrongly if the
            // tile were ever scaled.
            var renderer = sourceTile.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                return Vector2.zero;
            }

            Vector3 size = renderer.bounds.size;
            return new Vector2(size.x, size.z);
        }
    }
}
