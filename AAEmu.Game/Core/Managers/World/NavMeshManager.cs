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
/// heightmap + brush collision mesh data, provides instant height queries and
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
    private const float NavCellSize = 0.5f;        // horizontal voxel size (captures stair steps ~0.3m)
    private const float NavCellHeight = 0.2f;       // vertical voxel size (captures stair height ~0.2m)
    private const float AgentHeight = 2.0f;
    private const float AgentRadius = 0.5f;
    private const float AgentMaxClimb = 1.0f;
    private const float AgentMaxSlope = 50.0f;
    private const int NavTileSize = 512;             // voxels per tile edge (512 * 0.5 = 256m per tile)
    private const int MaxTiles = 4096;               // max tiles (16 tiles/cell × ~250 cells)
    private const int MaxPolysPerTile = 65535;
    private const int VertsPerPoly = 6;

    /// <summary>
    /// Number of navmesh tiles per game cell side.
    /// CELL_SIZE (1024m) / (NavTileSize * NavCellSize) (256m) = 4.
    /// Each game cell produces TilesPerCellSide² = 16 navmesh tiles.
    /// </summary>
    private const int TilesPerCellSide = WorldManager.CELL_SIZE / (int)(NavTileSize * NavCellSize); // 4

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
            tileWidth = NavTileSize * NavCellSize,    // 256 world units
            tileHeight = NavTileSize * NavCellSize,   // 256 world units
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
    /// Builds navmesh tiles from a cell's heightmap + brush collision meshes.
    /// Each 1024m game cell produces 4x4 = 16 navmesh tiles (256m each).
    /// Brush meshes provide building/stair/ramp geometry; multi-floor Z issues
    /// are handled by the GetHeight priority chain (7 sources) rather than navmesh alone.
    /// </summary>
    private void BuildTileFromCell(WorldCell cell)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Collect ALL geometry for this cell (heightmap + brushes)
        var verts = new List<float>();
        var faces = new List<int>();

        AddHeightmapTriangles(cell, verts, faces);

        // Add brush collision walls (vertical faces only — floors/roofs/stairs filtered out)
        // Track processed brush positions to avoid duplicates between object.dat and visareas.dat
        HashSet<(int pathId, int txBits, int tzBits)> processedBrushes = null;
        if (AppConfiguration.Instance.World.LoadBrushModels && cell.LoadedObjectDat != null)
        {
            processedBrushes = [];
            AddBrushTriangles(cell, cell.LoadedObjectDat.PrefabsList, verts, faces, processedBrushes);
        }

        // Add indoor/visarea brush collision meshes (building interiors), skipping duplicates
        if (AppConfiguration.Instance.World.LoadBrushModels && cell.LoadedVisAreasDat != null)
        {
            processedBrushes ??= [];
            AddBrushTriangles(cell, cell.LoadedVisAreasDat.PrefabsList, verts, faces, processedBrushes);
        }

        // Add forbidden areas as tall walls — NPCs cannot enter these zones
        if (AppConfiguration.Instance.World.GeoDataMode)
        {
            AddForbiddenAreaWalls(cell, verts, faces);
        }

        if (verts.Count == 0)
        {
            Logger.Trace($"NavMesh: cell ({cell.CellX},{cell.CellY}) has no geometry, skipping");
            return;
        }

        var vertArray = verts.ToArray();
        var faceArray = faces.ToArray();
        var inputVertCount = verts.Count / 3;
        var inputTriCount = faces.Count / 3;

        // 2. Create input geometry provider (shared across all sub-tiles)
        var geom = new SimpleInputGeomProvider(vertArray, faceArray);
        var bmin = geom.GetMeshBoundsMin();
        var bmax = geom.GetMeshBoundsMax();

        // 3. Build RcConfig (shared across all sub-tiles)
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

        // 4. Build 4x4 = 16 sub-tiles per cell
        // Tile coordinates: baseTileX = cellX * 4, baseTileZ = cellY * 4
        var baseTileX = cell.CellX * TilesPerCellSide;
        var baseTileZ = cell.CellY * TilesPerCellSide;
        var totalPolys = 0;
        var totalVerts = 0;
        var tilesBuilt = 0;

        for (var sz = 0; sz < TilesPerCellSide; sz++)
        {
            for (var sx = 0; sx < TilesPerCellSide; sx++)
            {
                var tileX = baseTileX + sx;
                var tileZ = baseTileZ + sz;

                if (BuildSingleTile(geom, cfg, bmin, bmax, tileX, tileZ,
                        out var polyCount, out var vertCount))
                {
                    totalPolys += polyCount;
                    totalVerts += vertCount;
                    tilesBuilt++;
                }
            }
        }

        sw.Stop();
        Logger.Info($"NavMesh: Built cell ({cell.CellX},{cell.CellY}) — " +
                    $"{tilesBuilt}/{TilesPerCellSide * TilesPerCellSide} tiles, " +
                    $"{totalVerts} verts, {totalPolys} polys, " +
                    $"{inputVertCount} input verts, {inputTriCount} input tris — " +
                    $"{sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Builds a single navmesh tile from the shared geometry provider.
    /// </summary>
    private bool BuildSingleTile(SimpleInputGeomProvider geom, RcConfig cfg,
        RcVec3f bmin, RcVec3f bmax, int tileX, int tileZ,
        out int polyCount, out int vertCount)
    {
        polyCount = 0;
        vertCount = 0;

        // bmin must be world origin (0,0,0) for X/Z so that
        // RcBuilderConfig computes tile bounds as: origin + tileX * tileSize * cellSize.
        var worldBmin = new RcVec3f(0f, bmin.Y, 0f);
        var bcfg = new RcBuilderConfig(cfg, worldBmin, bmax, tileX, tileZ);
        var builder = new RcBuilder();
        var result = builder.Build(geom, bcfg, false);

        var pmesh = result.Mesh;
        var dmesh = result.MeshDetail;

        if (pmesh == null || pmesh.npolys == 0)
            return false;

        // Set flags on all polys
        for (var i = 0; i < pmesh.npolys; i++)
            pmesh.flags[i] = 1; // walkable

        // Create Detour navmesh data
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
            return false;

        // Add tile to navmesh (thread-safe via lock)
        lock (_navMesh)
        {
            var existingRef = _navMesh.GetTileRefAt(tileX, 0, tileZ);
            if (existingRef != 0)
                _navMesh.RemoveTile(existingRef);
            _navMesh.AddTile(meshData, 0, 0, out _);
        }

        polyCount = pmesh.npolys;
        vertCount = pmesh.nverts;
        return true;
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
        // Generate a triangle grid with stride of 2 samples (4m) for detail
        // This gives 256x256 = 65K quads = 131K triangles per cell
        const int stride = 2; // sample stride (2 samples = 4m)
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
    /// Uses processedBrushes set to skip duplicate brushes across object.dat and visareas.dat.
    /// </summary>
    private static void AddBrushTriangles(WorldCell cell, IEnumerable<ObjectDataBase> prefabsList,
        List<float> verts, List<int> faces,
        HashSet<(int pathId, int txBits, int tzBits)> processedBrushes)
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

            // Deduplicate: same model at same world position = same brush
            var txBits = BitConverter.SingleToInt32Bits(brush.Matrix3X4.M14);
            var tzBits = BitConverter.SingleToInt32Bits(brush.Matrix3X4.M24);
            if (!processedBrushes.Add((brush.PathId, txBits, tzBits)))
                continue;

            // Load triangles (cached, in Jitter/DotRecast Y-up local model space)
            var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath);
            if (triangles == null || triangles.Count == 0)
                continue;

            // Build rotation matrix: CryEngine Z-up column-vector convention → DotRecast Y-up
            // CryEngine: wx = M11*lx + M12*ly + M13*lz, wy = M21*..., wz = M31*...
            // DotRecast: outX=wx, outY=wz(height), outZ=wy(depth)
            // Model vertices are Y↔Z swapped: v.X=lx, v.Y=lz, v.Z=ly
            var m = brush.Matrix3X4;
            var r00 = m.M11; var r01 = m.M13; var r02 = m.M12;
            var r10 = m.M31; var r11 = m.M33; var r12 = m.M32;
            var r20 = m.M21; var r21 = m.M23; var r22 = m.M22;

            // Translation (Y-up): brushX=M14, brushY(height)=M34, brushZ(depth)=M24
            // Plus cell offset: cellOffsetX and cellOffsetY (in game Y = DotRecast Z)
            var tx = m.M14 + cellOffsetX;
            var ty = m.M34;              // height — no cell offset
            var tz = m.M24 + cellOffsetY;

            foreach (var tri in triangles)
            {
                // Transform each vertex: world = rotation * local + translation
                var wx0 = r00 * tri.V0.X + r01 * tri.V0.Y + r02 * tri.V0.Z + tx;
                var wy0 = r10 * tri.V0.X + r11 * tri.V0.Y + r12 * tri.V0.Z + ty;
                var wz0 = r20 * tri.V0.X + r21 * tri.V0.Y + r22 * tri.V0.Z + tz;
                var wx1 = r00 * tri.V1.X + r01 * tri.V1.Y + r02 * tri.V1.Z + tx;
                var wy1 = r10 * tri.V1.X + r11 * tri.V1.Y + r12 * tri.V1.Z + ty;
                var wz1 = r20 * tri.V1.X + r21 * tri.V1.Y + r22 * tri.V1.Z + tz;
                var wx2 = r00 * tri.V2.X + r01 * tri.V2.Y + r02 * tri.V2.Z + tx;
                var wy2 = r10 * tri.V2.X + r11 * tri.V2.Y + r12 * tri.V2.Z + ty;
                var wz2 = r20 * tri.V2.X + r21 * tri.V2.Y + r22 * tri.V2.Z + tz;

                // Face normal filter: keep only WALLS (steep faces), skip floors/roofs/stairs.
                // In Y-up space, horizontal faces have |normalY| ≈ 1.
                // Skip faces where normalY² > 50% of total normal length² (angle < 45° from horizontal).
                // This keeps brush geometry as collision walls only — terrain heightmap is the walking surface.
                var e1x = wx1 - wx0; var e1y = wy1 - wy0; var e1z = wz1 - wz0;
                var e2x = wx2 - wx0; var e2y = wy2 - wy0; var e2z = wz2 - wz0;
                var nx = e1y * e2z - e1z * e2y;
                var ny = e1z * e2x - e1x * e2z;
                var nz = e1x * e2y - e1y * e2x;
                var nLenSq = nx * nx + ny * ny + nz * nz;
                if (nLenSq > 0.001f && ny * ny / nLenSq > 0.5f)
                    continue; // horizontal face (floor/roof/stair) — skip

                var idx = verts.Count / 3;
                verts.Add(wx0); verts.Add(wy0); verts.Add(wz0);
                verts.Add(wx1); verts.Add(wy1); verts.Add(wz1);
                verts.Add(wx2); verts.Add(wy2); verts.Add(wz2);
                faces.Add(idx);
                faces.Add(idx + 1);
                faces.Add(idx + 2);
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

    /// <summary>
    /// Adds tall vertical walls along the perimeter of each BAI forbidden area.
    /// This prevents the navmesh from having walkable polygons crossing into forbidden zones.
    /// NPCs will path around the walls instead of through the forbidden area.
    /// </summary>
    private static void AddForbiddenAreaWalls(WorldCell cell, List<float> verts, List<int> faces)
    {
        const float wallHeight = 20f; // tall enough to block any agent

        // Iterate all BAI loaders in this cell (4x4 grid of path sectors)
        foreach (var bai in cell.BaiLoader)
        {
            if (bai == null)
                continue;

            foreach (var areaMission in bai.AreasMissionReaders)
            {
                AddForbiddenWallsFromList(areaMission.ForbiddenAreasList, verts, faces, wallHeight);
                AddForbiddenWallsFromList(areaMission.DesignerForbiddenAreasList, verts, faces, wallHeight);
            }
        }
    }

    private static void AddForbiddenWallsFromList(
        List<Models.CryEngine.Mission.AiShape> areas,
        List<float> verts, List<int> faces, float wallHeight)
    {
        foreach (var area in areas)
        {
            if (area.Points.Count < 3)
                continue;

            // Create vertical wall quads along each edge of the polygon
            for (var i = 0; i < area.Points.Count; i++)
            {
                var p0 = area.Points[i];
                var p1 = area.Points[(i + 1) % area.Points.Count];

                // Base height: use the point's Z (ground level from BAI data)
                var baseY0 = p0.Z;
                var baseY1 = p1.Z;
                var topY0 = baseY0 + wallHeight;
                var topY1 = baseY1 + wallHeight;

                // DotRecast Y-up: (gameX, gameZ_height, gameY)
                var idx = verts.Count / 3;

                // Bottom-left (p0 base)
                verts.Add(p0.X); verts.Add(baseY0); verts.Add(p0.Y);
                // Bottom-right (p1 base)
                verts.Add(p1.X); verts.Add(baseY1); verts.Add(p1.Y);
                // Top-right (p1 top)
                verts.Add(p1.X); verts.Add(topY1); verts.Add(p1.Y);
                // Top-left (p0 top)
                verts.Add(p0.X); verts.Add(topY0); verts.Add(p0.Y);

                // Two triangles for the quad
                faces.Add(idx);     faces.Add(idx + 1); faces.Add(idx + 2);
                faces.Add(idx);     faces.Add(idx + 2); faces.Add(idx + 3);
            }
        }
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
    /// With brush collision meshes creating multiple floors, a large extent would
    /// snap to the wrong floor. 10m is enough to find the current floor surface
    /// while avoiding cross-floor snapping.
    /// </summary>
    private const float VerticalSearchExtent = 10.0f;

    public float GetHeight(float x, float y, float z)
    {
        if (_navMesh == null)
            return 0f;

        // Game → DotRecast Y-up: (gameX, gameZ, gameY)
        var pos = new RcVec3f(x, z, y);
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
        var extents = new RcVec3f(5f, 10f, 5f);

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
    /// Casts a walkability ray along the navmesh surface from start to end.
    /// Returns true if the ray reaches the end position (clear line of sight on navmesh).
    /// Returns false if blocked by a wall, cliff, or navmesh edge.
    /// </summary>
    public bool Raycast(Vector3 start, Vector3 end)
    {
        return TryRaycast(start, end, out var clear) && clear;
    }

    /// <summary>
    /// Tri-state raycast: returns true if the navmesh can answer (poly found at start),
    /// with 'clear' indicating whether the ray reached the end (no wall hit).
    /// Returns false if no navmesh poly found near start (caller should use fallback).
    /// </summary>
    public bool TryRaycast(Vector3 start, Vector3 end, out bool clear)
    {
        clear = false;
        if (_navMesh == null)
            return false;

        // Game → DotRecast Y-up
        var startPos = new RcVec3f(start.X, start.Z, start.Y);
        var endPos = new RcVec3f(end.X, end.Z, end.Y);
        var extents = new RcVec3f(5f, VerticalSearchExtent, 5f);

        lock (_navMesh)
        {
            var status = _navQuery.FindNearestPoly(startPos, extents, _filter, out var startRef, out _, out _);
            if (status.Failed() || startRef == 0)
                return false; // no navmesh data here — caller should use fallback

            var path = new long[256];
            _navQuery.Raycast(startRef, startPos, endPos, _filter, out var t, out _, path.AsSpan(), out _, path.Length);

            // t >= 1.0 means the ray reached the end position (no wall hit)
            clear = t >= 1.0f;
            return true; // navmesh answered — result is reliable
        }
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
