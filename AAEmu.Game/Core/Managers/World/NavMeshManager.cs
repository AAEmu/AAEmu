using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Game.Models;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models.Game.World;

using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

using Jitter2.LinearMath;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Per-WorldInstance navmesh manager. Builds DotRecast navmesh tiles from
/// heightmap + collision mesh data, provides instant height queries and
/// A* pathfinding over the navmesh surface.
/// </summary>
public class NavMeshManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // --- DotRecast navmesh ---
    private DtNavMesh _navMesh;
    private DtNavMeshQuery _navQuery;
    private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();

    private readonly WorldInstance _world;
    private readonly ConcurrentQueue<WorldCell> _buildQueue = new();
    private Task _buildTask;
    private readonly object _buildLock = new();

    // --- Build parameters ---
    private const float NavCellSize = 2.0f;       // horizontal voxel size (matches heightmap 2m resolution)
    private const float NavCellHeight = 0.25f;     // vertical voxel size
    private const float AgentHeight = 2.0f;
    private const float AgentRadius = 0.5f;
    private const float AgentMaxClimb = 1.0f;
    private const float AgentMaxSlope = 50.0f;
    private const int NavTileSize = 512;            // voxels per tile edge (512 * 2.0 = 1024m = 1 cell)
    private const int MaxTiles = 1024;              // max tiles in navmesh
    private const int MaxPolysPerTile = 65535;      // max polygons per tile
    private const int VertsPerPoly = 6;

    public NavMeshManager(WorldInstance world)
    {
        _world = world;
        InitNavMesh();
    }

    private void InitNavMesh()
    {
        var navParams = new DtNavMeshParams
        {
            orig = new RcVec3f(0f, 0f, 0f),
            tileWidth = NavTileSize * NavCellSize,    // 1024 world units
            tileHeight = NavTileSize * NavCellSize,   // 1024 world units
            maxTiles = MaxTiles,
            maxPolys = MaxPolysPerTile
        };

        _navMesh = new DtNavMesh();
        _navMesh.Init(navParams, VertsPerPoly);

        _navQuery = new DtNavMeshQuery(_navMesh);
    }

    #region Build

    /// <summary>
    /// Queue a cell for async navmesh tile building.
    /// </summary>
    public void QueueBuildTile(WorldCell cell)
    {
        _buildQueue.Enqueue(cell);

        lock (_buildLock)
        {
            if (_buildTask is { IsCompleted: false })
                return;
            _buildTask = Task.Run(ProcessBuildQueue);
        }
    }

    private void ProcessBuildQueue()
    {
        while (_buildQueue.TryDequeue(out var cell))
        {
            try
            {
                BuildTileFromCell(cell);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to build navmesh tile for cell ({cell.CellX}, {cell.CellY})");
            }
        }
    }

    /// <summary>
    /// Builds a navmesh tile from a cell's heightmap.
    /// Only terrain geometry is included — brush collision meshes are excluded
    /// to avoid creating walkable surfaces on building roofs/upper floors,
    /// which causes NPCs to flicker between Z positions. Interior height
    /// detection is handled by GeoData (BAI mesh) as fallback in the
    /// GetHeight chain.
    /// </summary>
    private void BuildTileFromCell(WorldCell cell)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Collect terrain triangles only (no brush meshes).
        // Brush meshes create multi-floor navmesh surfaces in buildings,
        // causing FindNearestPoly to return wrong-floor Z values.
        var verts = new List<float>();
        var faces = new List<int>();

        AddHeightmapTriangles(cell, verts, faces);

        if (verts.Count == 0)
        {
            Logger.Trace($"NavMesh: cell ({cell.CellX},{cell.CellY}) has no geometry, skipping");
            return;
        }

        var vertArray = verts.ToArray();
        var faceArray = faces.ToArray();

        // 2. Create input geometry provider
        var geom = new SimpleInputGeomProvider(vertArray, faceArray);
        var bmin = geom.GetMeshBoundsMin();
        var bmax = geom.GetMeshBoundsMax();

        // 3. Build Recast navmesh
        var walkableAreaMod = new RcAreaModification(RcRecast.RC_WALKABLE_AREA);

        var borderSize = (int)MathF.Ceiling(AgentRadius / NavCellSize);

        var cfg = new RcConfig(
            true,               // useTiles
            NavTileSize,        // tileSizeX
            NavTileSize,        // tileSizeZ
            borderSize,         // borderSize
            RcPartition.WATERSHED,
            NavCellSize,        // cellSize
            NavCellHeight,      // cellHeight
            AgentMaxSlope,      // agentMaxSlope
            AgentHeight,        // agentHeight
            AgentRadius,        // agentRadius
            AgentMaxClimb,      // agentMaxClimb
            8,                  // regionMinSize
            20,                 // regionMergeSize
            12.0f,              // edgeMaxLen
            1.3f,               // edgeMaxError
            VertsPerPoly,       // vertsPerPoly
            6.0f,               // detailSampleDist
            1.0f,               // detailSampleMaxError
            true,               // filterLowHangingObstacles
            true,               // filterLedgeSpans
            true,               // filterWalkableLowHeightSpans
            walkableAreaMod,    // walkableAreaMod
            true                // buildMeshDetail
        );

        // Tile coordinates: tileX = cellX, tileZ = cellY (DotRecast Z = game Y)
        var tileX = cell.CellX;
        var tileZ = cell.CellY;

        // Build the tile — bmin must be world origin (0,0,0) for X/Z so that
        // RcBuilderConfig computes tile bounds as: origin + tileX * tileSize * cellSize.
        // Using the geometry's local bmin would double-offset the tile bounds.
        var worldBmin = new RcVec3f(0f, bmin.Y, 0f);
        var bcfg = new RcBuilderConfig(cfg, worldBmin, bmax, tileX, tileZ);
        var builder = new RcBuilder();
        var result = builder.Build(geom, bcfg, false);

        var pmesh = result.Mesh;
        var dmesh = result.MeshDetail;

        if (pmesh == null || pmesh.npolys == 0)
        {
            Logger.Trace($"NavMesh: cell ({cell.CellX},{cell.CellY}) produced empty poly mesh");
            return;
        }

        // 4. Set flags on all polys
        for (var i = 0; i < pmesh.npolys; i++)
        {
            pmesh.flags[i] = 1; // walkable
        }

        // 5. Create Detour navmesh data
        var navMeshParams = new DtNavMeshCreateParams
        {
            verts = pmesh.verts,
            vertCount = pmesh.nverts,
            polys = pmesh.polys,
            polyAreas = pmesh.areas,
            polyFlags = pmesh.flags,
            polyCount = pmesh.npolys,
            nvp = pmesh.nvp,
            detailMeshes = dmesh?.meshes,
            detailVerts = dmesh?.verts,
            detailVertsCount = dmesh?.nverts ?? 0,
            detailTris = dmesh?.tris,
            detailTriCount = dmesh?.ntris ?? 0,
            bmin = pmesh.bmin,
            bmax = pmesh.bmax,
            cs = NavCellSize,
            ch = NavCellHeight,
            walkableHeight = AgentHeight,
            walkableRadius = AgentRadius,
            walkableClimb = AgentMaxClimb,
            tileX = tileX,
            tileZ = tileZ,
            tileLayer = 0,
            buildBvTree = true
        };

        var meshData = DtNavMeshBuilder.CreateNavMeshData(navMeshParams);
        if (meshData == null)
        {
            Logger.Warn($"NavMesh: Failed to create navmesh data for cell ({cell.CellX},{cell.CellY})");
            return;
        }

        // 6. Add tile to navmesh (thread-safe via lock)
        lock (_navMesh)
        {
            var existingRef = _navMesh.GetTileRefAt(tileX, 0, tileZ);
            if (existingRef != 0)
                _navMesh.RemoveTile(existingRef);
            _navMesh.AddTile(meshData, 0, 0, out _);
        }

        sw.Stop();
        Logger.Info($"NavMesh: Built tile ({cell.CellX},{cell.CellY}) — " +
                    $"{pmesh.nverts} verts, {pmesh.npolys} polys, " +
                    $"{verts.Count / 3} input verts, {faces.Count / 3} input tris — " +
                    $"{sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Generates terrain triangles from the cell's heightmap.
    /// Coordinates are in DotRecast Y-up: (gameX, gameZ_height, gameY).
    /// </summary>
    private static void AddHeightmapTriangles(WorldCell cell, List<float> verts, List<int> faces)
    {
        if (cell.HeightMap == null)
            return;

        var cellWorldX = (float)(cell.CellX * WorldManager.CELL_SIZE);
        var cellWorldY = (float)(cell.CellY * WorldManager.CELL_SIZE);
        var coeff = cell.Template.HeightMaxCoefficient;

        // HeightMap is 512x512, each sample covers 2m x 2m
        // Generate a triangle grid with stride of 4 samples (8m) for performance
        // This gives 128x128 = 16K quads = 32K triangles per cell
        const int stride = 4; // sample stride (4 samples = 8m)
        const int resolution = WorldManager.CELL_HMAP_RESOLUTION; // 512

        // Pre-compute vertex grid
        var gridW = resolution / stride + 1;
        var gridH = resolution / stride + 1;
        var baseVertIndex = verts.Count / 3;

        for (var gy = 0; gy < gridH; gy++)
        {
            var sy = Math.Min(gy * stride, resolution - 1);
            var worldY = cellWorldY + sy * 2f;

            for (var gx = 0; gx < gridW; gx++)
            {
                var sx = Math.Min(gx * stride, resolution - 1);
                var worldX = cellWorldX + sx * 2f;
                var height = (float)(cell.HeightMap[sx, sy] / coeff);

                // DotRecast Y-up: (gameX, height, gameY)
                verts.Add(worldX);
                verts.Add(height);
                verts.Add(worldY);
            }
        }

        // Generate triangle indices
        for (var gy = 0; gy < gridH - 1; gy++)
        {
            for (var gx = 0; gx < gridW - 1; gx++)
            {
                var i00 = baseVertIndex + gy * gridW + gx;
                var i10 = i00 + 1;
                var i01 = i00 + gridW;
                var i11 = i01 + 1;

                // Triangle 1: (00, 10, 01)
                faces.Add(i00);
                faces.Add(i10);
                faces.Add(i01);

                // Triangle 2: (10, 11, 01)
                faces.Add(i10);
                faces.Add(i11);
                faces.Add(i01);
            }
        }
    }

    /// <summary>
    /// Extracts collision triangles from brushes in a prefabs list,
    /// transforms them to world coordinates, and adds to the vertex/face lists.
    /// Coordinates are in DotRecast Y-up: (gameX, gameZ_height, gameY).
    /// </summary>
    private static void AddBrushTriangles(WorldCell cell, IEnumerable<ObjectDataBase> prefabsList,
        List<float> verts, List<int> faces)
    {
        if (cell.StatObjsFiles == null || cell.MaterialListFiles == null)
            return;

        var cellOffsetX = (float)(cell.CellX * WorldManager.CELL_SIZE);
        var cellOffsetY = (float)(cell.CellY * WorldManager.CELL_SIZE);

        foreach (var objectData in prefabsList)
        {
            if (objectData is not ObjectDataType1Brush brush)
                continue;

            // Skip small objects
            var roughSize = Vector3.Distance(brush.StartPos, brush.EndPos);
            if (roughSize < AppConfiguration.Instance.World.LoadBrushMinimumSize)
                continue;

            // Resolve paths
            if (brush.PathId < 0 || brush.PathId >= cell.StatObjsFiles.MaterialList.Count)
                continue;
            if (brush.MaterialId < 0 || brush.MaterialId >= cell.MaterialListFiles.MaterialsList.Count)
                continue;

            var modelPath = cell.StatObjsFiles.MaterialList[brush.PathId];
            var materialPath = cell.MaterialListFiles.MaterialsList[brush.MaterialId];

            if (modelPath == "game/objects/nodraw" || materialPath == "game/objects/nodraw")
                continue;

            // Load triangles (cached, in Jitter/DotRecast Y-up local model space)
            var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath);
            if (triangles == null || triangles.Count == 0)
                continue;

            // Build rotation matrix (same Y<->Z swap as PhysicsManager.CreateBrushShapes)
            // CryEngine Matrix3x4 rows: M*1=X, M*2=Y, M*3=Z(up)
            // DotRecast/Jitter Y-up: col1=X, col2=Y(up), col3=Z(depth)
            var m = brush.Matrix3X4;
            var r00 = m.M11; var r01 = m.M31; var r02 = m.M21;
            var r10 = m.M13; var r11 = m.M33; var r12 = m.M23;
            var r20 = m.M12; var r21 = m.M32; var r22 = m.M22;

            // Translation (Y-up): brushX=M14, brushY(height)=M34, brushZ(depth)=M24
            // Plus cell offset: cellOffsetX and cellOffsetY (in game Y = DotRecast Z)
            var tx = m.M14 + cellOffsetX;
            var ty = m.M34;              // height — no cell offset
            var tz = m.M24 + cellOffsetY;

            var baseIdx = verts.Count / 3;

            foreach (var tri in triangles)
            {
                // Transform each vertex: world = rotation * local + translation
                AddTransformedVertex(verts, tri.V0, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz);
                AddTransformedVertex(verts, tri.V1, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz);
                AddTransformedVertex(verts, tri.V2, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz);
            }

            // Add face indices (3 vertices per triangle, sequential)
            for (var i = 0; i < triangles.Count; i++)
            {
                faces.Add(baseIdx + i * 3);
                faces.Add(baseIdx + i * 3 + 1);
                faces.Add(baseIdx + i * 3 + 2);
            }
        }
    }

    /// <summary>
    /// Transforms a JVector vertex by rotation matrix + translation and appends to the vertex list.
    /// </summary>
    private static void AddTransformedVertex(List<float> verts, JVector v,
        float r00, float r01, float r02,
        float r10, float r11, float r12,
        float r20, float r21, float r22,
        float tx, float ty, float tz)
    {
        // rotation * v + translation (all in Y-up space)
        verts.Add(r00 * v.X + r01 * v.Y + r02 * v.Z + tx);
        verts.Add(r10 * v.X + r11 * v.Y + r12 * v.Z + ty);
        verts.Add(r20 * v.X + r21 * v.Y + r22 * v.Z + tz);
    }

    #endregion Build

    #region Query

    /// <summary>
    /// Gets the navmesh surface height at the given game world position.
    /// Returns 0 if no navmesh data is available at that position.
    /// </summary>
    /// <param name="x">Game world X</param>
    /// <param name="y">Game world Y</param>
    /// <param name="z">Game world Z (height)</param>
    /// <summary>
    /// Maximum vertical distance between query Z and result Z.
    /// Prevents snapping to a different floor in multi-story buildings.
    /// Navmesh is terrain-only (no building meshes), so there's only one surface
    /// at any XY position. Large vertical extent is safe — no multi-floor conflicts.
    /// Must be large enough to handle cliffs and steep terrain (e.g., 30m+ elevation changes).
    /// </summary>
    private const float VerticalSearchExtent = 100.0f;

    public float GetHeight(float x, float y, float z)
    {
        if (_navMesh == null)
            return 0f;

        // Game → DotRecast Y-up: (gameX, gameZ, gameY)
        var pos = new RcVec3f(x, z, y);
        // Large vertical extent is safe because navmesh is terrain-only (single surface).
        // This ensures terrain is always found even on steep cliffs/hills.
        var extents = new RcVec3f(2f, VerticalSearchExtent, 2f);

        lock (_navMesh)
        {
            var status = _navQuery.FindNearestPoly(pos, extents, _filter, out var nearestRef, out var nearestPt, out _);
            if (status.Failed() || nearestRef == 0)
                return 0f;

            // nearestPt.Y is height in DotRecast Y-up = game Z
            return nearestPt.Y;
        }
    }

    /// <summary>
    /// Finds a path on the navmesh between two game world positions.
    /// Returns a list of waypoints in game world coordinates, or empty if no path found.
    /// </summary>
    public List<Vector3> FindPath(Vector3 start, Vector3 end)
    {
        var result = new List<Vector3>();
        if (_navMesh == null)
            return result;

        // Game → DotRecast Y-up
        var startPos = new RcVec3f(start.X, start.Z, start.Y);
        var endPos = new RcVec3f(end.X, end.Z, end.Y);
        var extents = new RcVec3f(5f, 20f, 5f);

        lock (_navMesh)
        {
            _navQuery.FindNearestPoly(startPos, extents, _filter, out var startRef, out _, out _);
            _navQuery.FindNearestPoly(endPos, extents, _filter, out var endRef, out _, out _);

            if (startRef == 0 || endRef == 0)
                return result;

            // Find polygon path
            var path = new long[256];
            _navQuery.FindPath(startRef, endRef, startPos, endPos, _filter,
                path.AsSpan(), out var pathCount, path.Length);

            if (pathCount <= 0)
                return result;

            // Find straight path (actual waypoints with correct Z)
            var straightPath = new DtStraightPath[256];
            _navQuery.FindStraightPath(startPos, endPos, path.AsSpan(0, pathCount),
                pathCount, straightPath.AsSpan(), out var straightPathCount, 256, 0);

            // Convert DotRecast Y-up → game coords
            for (var i = 0; i < straightPathCount; i++)
            {
                var p = straightPath[i].pos;
                result.Add(new Vector3(p.X, p.Z, p.Y)); // game(X, Y, Z) = RC(X, Z, Y)
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the number of tiles that actually contain navmesh data.
    /// </summary>
    public int TileCount
    {
        get
        {
            if (_navMesh == null) return 0;
            var count = 0;
            lock (_navMesh)
            {
                for (var i = 0; i < _navMesh.GetMaxTiles(); i++)
                {
                    var tile = _navMesh.GetTile(i);
                    if (tile?.data != null)
                        count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Returns true if the navmesh has any tiles with actual data.
    /// </summary>
    public bool HasData => TileCount > 0;

    #endregion Query
}
