using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Game.Models;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Loaders;
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
/// BAI type 2 terrain topology + brush collision mesh data, provides instant
/// height queries and A* pathfinding over the navmesh surface.
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
    private const float AgentMaxSlope = 75.0f;  // raised from 50° — stairs/ramps in ArcheAge often exceed 50°
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
    /// Builds navmesh tiles from a cell's BAI type 2 terrain + brush collision meshes.
    /// Each 1024m game cell produces 4x4 = 16 navmesh tiles (256m each).
    /// BAI type 2 provides authored terrain topology; brush meshes provide full
    /// building geometry (floors, stairs, walls). DotRecast determines walkability
    /// via agent slope/height/climb parameters.
    /// </summary>
    private void BuildTileFromCell(WorldCell cell)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Collect ALL geometry for this cell (BAI terrain + brushes)
        var verts = new List<float>();
        var faces = new List<int>();

        AddBaiType2Triangles(cell, verts, faces);

        // Add brush collision triangles from object.dat + visareas.dat.
        // visareas.dat contains interior structural geometry (floors, stairs) not present in object.dat.
        // CryEngine portal volumes inside visareas.dat have 90° X-rotation (M33≈0) and are skipped
        // via the portalOrientation filter in AddBrushTriangles.
        var objBrushCount = 0;
        var visBrushCount = 0;
        if (AppConfiguration.Instance.World.LoadBrushModels && cell.LoadedObjectDat != null)
        {
            // Position-based dedup: quantize world position to 0.1m grid to catch
            // float precision differences between object.dat and visareas.dat copies.
            HashSet<(int pathId, int px, int py, int pz)> processedBrushes = [];
            objBrushCount = AddBrushTriangles(cell, cell.LoadedObjectDat.PrefabsList, verts, faces, processedBrushes);
            if (cell.LoadedVisAreasDat != null && AppConfiguration.Instance.World.LoadVisAreasBrushes)
                visBrushCount = AddBrushTriangles(cell, cell.LoadedVisAreasDat.PrefabsList, verts, faces, processedBrushes);
        }

        // Add voxel terrain mesh (Type 6) — carved caves, shaped cliffs, terrain modifications.
        // Voxels contain embedded compressed mesh data that's already parsed by ObjectDataType6Voxel.
        var voxelCount = 0;
        if (cell.LoadedObjectDat != null)
            voxelCount = AddVoxelTriangles(cell, cell.LoadedObjectDat.PrefabsList, verts, faces);

        // NOTE: ForbiddenAreas are NOT added as navmesh walls here.
        // They're handled by AiGeodataManager.LinePassesThroughForbiddenArea and
        // CheckImpossibleWalk in the BAI A* system. Adding them as physical walls
        // caused 20m barriers to block staircase geometry that was correctly built
        // from brush collision meshes.

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
            2,                  // regionMinSize — lowered from 8: stair treads are small regions (~2-4 cells)
            20,                 // regionMergeSize
            12.0f,              // edgeMaxLen
            1.3f,               // edgeMaxError
            VertsPerPoly,       // vertsPerPoly
            6.0f,               // detailSampleDist
            1.0f,               // detailSampleMaxError
            true,               // filterLowHangingObstacles
            false,              // filterLedgeSpans — disabled: stair treads have adjacent drops (outer edge) and
                                //   would be filtered as "ledges". Building floors/stairs need this off so elevated
                                //   surfaces with drop-offs remain walkable. Outdoor cliff edges are acceptable.
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
                    $"{inputVertCount} input verts, {inputTriCount} input tris, " +
                    $"brushes: obj={objBrushCount} vis={visBrushCount}, voxels={voxelCount} — " +
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
    /// Extracts BAI type 2 (terrain topology) triangles from the cell's BAI data.
    /// These are authored navmesh triangles from CryEngine that represent the walkable
    /// terrain surface, replacing the raw heightmap grid.
    /// Coordinates are in DotRecast Y-up: (gameX, gameZ_height, gameY).
    /// </summary>
    private static void AddBaiType2Triangles(WorldCell cell, List<float> verts, List<int> faces)
    {
        // Deduplicate BAI loaders — zone-mode assigns the same loader to all 16 positions
        var visitedLoaders = new HashSet<BaseBaiLoader>();

        for (var by = 0; by < 4; by++)
        {
            for (var bx = 0; bx < 4; bx++)
            {
                var baiLoader = cell.BaiLoader[bx, by];
                if (baiLoader == null || !visitedLoaders.Add(baiLoader))
                    continue;

                foreach (var netMission in baiLoader.NetMissionReaders)
                {
                    // Find matching VertexMission for vertex data (paired by ZoneId)
                    var vertexMission = baiLoader.VertexMissionReaders
                        .FirstOrDefault(v => v.ZoneId == netMission.ZoneId);

                    if (vertexMission == null || vertexMission.ObstacleDataDescriptorList.Count == 0)
                        continue;

                    var obstacles = vertexMission.ObstacleDataDescriptorList;

                    foreach (var (_, node) in netMission.NodeDescriptorList)
                    {
                        // Only type 2 = terrain/topology walkable surface
                        if (node.Type != 2)
                            continue;

                        // Validate obstacle indices
                        if (node.Obstacle.Length < 3 ||
                            node.Obstacle[0] < 0 || node.Obstacle[0] >= obstacles.Count ||
                            node.Obstacle[1] < 0 || node.Obstacle[1] >= obstacles.Count ||
                            node.Obstacle[2] < 0 || node.Obstacle[2] >= obstacles.Count)
                            continue;

                        // Get triangle vertices (already in world coordinates — ReaderPointOffset pre-applied)
                        var v0 = obstacles[node.Obstacle[0]].Pos;
                        var v1 = obstacles[node.Obstacle[1]].Pos;
                        var v2 = obstacles[node.Obstacle[2]].Pos;

                        // Game (X, Y, Z) → DotRecast Y-up (X, Z_height, Y)
                        var idx = verts.Count / 3;
                        verts.Add(v0.X); verts.Add(v0.Z); verts.Add(v0.Y);
                        verts.Add(v1.X); verts.Add(v1.Z); verts.Add(v1.Y);
                        verts.Add(v2.X); verts.Add(v2.Z); verts.Add(v2.Y);
                        faces.Add(idx);
                        faces.Add(idx + 1);
                        faces.Add(idx + 2);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extracts collision triangles from brushes in a prefabs list,
    /// transforms them to world coordinates, and adds to the vertex/face lists.
    /// ALL faces are included (floors, stairs, walls) — DotRecast determines
    /// walkability via agent slope/height/climb parameters.
    /// Coordinates are in DotRecast Y-up: (gameX, gameZ_height, gameY).
    /// Uses processedBrushes set to skip duplicate brushes across object.dat and visareas.dat.
    /// Returns the number of brushes actually added.
    /// </summary>
    private static int AddBrushTriangles(WorldCell cell, IEnumerable<ObjectDataBase> prefabsList,
        List<float> verts, List<int> faces,
        HashSet<(int pathId, int px, int py, int pz)> processedBrushes)
    {
        if (cell.StatObjsFiles == null || cell.MaterialListFiles == null)
            return 0;

        var cellOffsetX = (float)(cell.CellX * WorldManager.CELL_SIZE);
        var cellOffsetY = (float)(cell.CellY * WorldManager.CELL_SIZE);
        var brushCount = 0;

        foreach (var objectData in prefabsList)
        {
            if (objectData is not ObjectDataType1Brush brush)
                continue;

            // Skip small objects
            var roughSize = Vector3.Distance(brush.StartPos, brush.EndPos);
            if (roughSize < AppConfiguration.Instance.World.LoadBrushMinimumSize)
                continue;

            // Skip CryEngine portal/occlusion volumes: their M33≈0 means the object's local Z-axis
            // is nearly horizontal — these are flat portal quads, not walkable geometry.
            if (Math.Abs(brush.Matrix3X4.M33) < 0.1f)
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

            // Deduplicate by quantized world position (0.1m grid).
            // Object.dat and visareas.dat may contain the same brush with tiny float
            // precision differences in the matrix — exact hash would miss these duplicates.
            var m = brush.Matrix3X4;
            var worldX = m.M14 + cellOffsetX;
            var worldY = m.M24 + cellOffsetY;
            var worldZ = m.M34;
            if (!processedBrushes.Add((brush.PathId, (int)(worldX * 10f), (int)(worldY * 10f), (int)(worldZ * 10f))))
                continue;

            // Load triangles (cached, in Jitter/DotRecast Y-up local model space)
            var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath, out var usedPhysicsProxy);
            if (triangles == null || triangles.Count == 0)
                continue;

            // Only filter by triangle count for visual mesh fallbacks.
            // Physics proxy geometry is intentional collision data — always include regardless of count.
            var maxTris = AppConfiguration.Instance.World.LoadBrushMaxTriangles;
            if (!usedPhysicsProxy && maxTris > 0 && triangles.Count > maxTris)
            {
                Logger.Trace($"[NavMesh] Skipping '{modelPath}' — visual mesh {triangles.Count} tris exceeds limit {maxTris}");
                continue;
            }

            // Build rotation matrix: CryEngine Z-up column-vector convention → DotRecast Y-up
            // CryEngine: wx = M11*lx + M12*ly + M13*lz, wy = M21*..., wz = M31*...
            // DotRecast: outX=wx, outY=wz(height), outZ=wy(depth)
            // Model vertices are Y↔Z swapped: v.X=lx, v.Y=lz, v.Z=ly
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

                var idx = verts.Count / 3;
                verts.Add(wx0); verts.Add(wy0); verts.Add(wz0);
                verts.Add(wx1); verts.Add(wy1); verts.Add(wz1);
                verts.Add(wx2); verts.Add(wy2); verts.Add(wz2);
                faces.Add(idx);
                faces.Add(idx + 1);
                faces.Add(idx + 2);
            }

            brushCount++;
        }

        return brushCount;
    }

    /// <summary>
    /// Extracts mesh triangles from Type 6 voxel objects (terrain modifications like caves, cliffs).
    /// Voxels contain embedded compressed mesh data parsed by ObjectDataType6Voxel.
    /// Transforms vertices by the voxel's Matrix3x4 + cell offset to DotRecast Y-up world coords.
    /// Returns the number of voxels actually added.
    /// </summary>
    private static int AddVoxelTriangles(WorldCell cell, IEnumerable<ObjectDataBase> prefabsList,
        List<float> verts, List<int> faces)
    {
        var cellOffsetX = (float)(cell.CellX * WorldManager.CELL_SIZE);
        var cellOffsetY = (float)(cell.CellY * WorldManager.CELL_SIZE);
        var voxelCount = 0;

        foreach (var objectData in prefabsList)
        {
            if (objectData is not ObjectDataType6Voxel voxel)
                continue;

            // Parse the compressed mesh data (decompresses zlib, reads vertices + indices)
            if (!voxel.Parse())
                continue;

            var reader = voxel.MeshReader;
            if (reader == null || reader.Vertices.Count < 3 || reader.Indices.Count < 3)
                continue;

            // Build rotation matrix: same coordinate swap as brushes.
            // CryEngine Z-up → DotRecast Y-up, vertices may be in model-local Z-up space.
            var m = voxel.Matrix3X4;
            var r00 = m.M11; var r01 = m.M13; var r02 = m.M12;
            var r10 = m.M31; var r11 = m.M33; var r12 = m.M32;
            var r20 = m.M21; var r21 = m.M23; var r22 = m.M22;

            var tx = m.M14 + cellOffsetX;
            var ty = m.M34;
            var tz = m.M24 + cellOffsetY;

            // Voxel vertices are Vector3 — treat same as brush JVector (model-local, Y↔Z swapped)
            for (var i = 0; i + 2 < reader.Indices.Count; i += 3)
            {
                var i0 = reader.Indices[i];
                var i1 = reader.Indices[i + 1];
                var i2 = reader.Indices[i + 2];

                if (i0 >= reader.Vertices.Count || i1 >= reader.Vertices.Count || i2 >= reader.Vertices.Count)
                    continue;

                var v0 = reader.Vertices[i0];
                var v1 = reader.Vertices[i1];
                var v2 = reader.Vertices[i2];

                // Transform: world = rotation * local + translation
                // Local vertices: v.X=lx, v.Y=lz(height), v.Z=ly (Y↔Z swapped like brush models)
                var wx0 = r00 * v0.X + r01 * v0.Y + r02 * v0.Z + tx;
                var wy0 = r10 * v0.X + r11 * v0.Y + r12 * v0.Z + ty;
                var wz0 = r20 * v0.X + r21 * v0.Y + r22 * v0.Z + tz;
                var wx1 = r00 * v1.X + r01 * v1.Y + r02 * v1.Z + tx;
                var wy1 = r10 * v1.X + r11 * v1.Y + r12 * v1.Z + ty;
                var wz1 = r20 * v1.X + r21 * v1.Y + r22 * v1.Z + tz;
                var wx2 = r00 * v2.X + r01 * v2.Y + r02 * v2.Z + tx;
                var wy2 = r10 * v2.X + r11 * v2.Y + r12 * v2.Z + ty;
                var wz2 = r20 * v2.X + r21 * v2.Y + r22 * v2.Z + tz;

                var idx = verts.Count / 3;
                verts.Add(wx0); verts.Add(wy0); verts.Add(wz0);
                verts.Add(wx1); verts.Add(wy1); verts.Add(wz1);
                verts.Add(wx2); verts.Add(wy2); verts.Add(wz2);
                faces.Add(idx);
                faces.Add(idx + 1);
                faces.Add(idx + 2);
            }

            voxelCount++;
        }

        return voxelCount;
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
    /// Maximum vertical distance between query Z and result Z for GetHeight.
    /// NPCs are always within ~1m of their surface (Z is corrected every move step),
    /// so 2m is sufficient to find the surface below without accidentally snapping
    /// to a higher floor (e.g. 2nd floor 3-5m above the current position).
    /// A larger value (e.g. 10m) caused NPCs to snap to the wrong floor in
    /// multi-level buildings and fly over staircases.
    /// </summary>
    private const float VerticalSearchExtent = 2.0f;

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

            // Prefer GetPolyHeight: interpolates from detail mesh vertices at the exact XZ
            // position, which is more accurate than nearestPt.Y (quantized poly mesh).
            // Falls back to nearestPt.Y if the query point is outside the polygon boundary.
            float returnZ;
            if (_navQuery.GetPolyHeight(nearestRef, new RcVec3f(pos.X, nearestPt.Y, pos.Z), out var exactH).Succeeded()
                && exactH > 0f)
                returnZ = exactH;
            else
                returnZ = nearestPt.Y;

            // Reject surfaces more than AgentMaxClimb (1m) above the query Z.
            // Stair treads rise ~0.3m per step; 1m allows any single climbable step.
            // This prevents an NPC on the ground floor from being snapped up to a
            // higher floor (e.g. during combat when the player is on a platform above).
            // Downward snapping is unrestricted — allows detecting ground below.
            if (returnZ > pos.Y + AgentMaxClimb)
                return 0f;

            return returnZ;
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

    /// <summary>
    /// Exports all navmesh detail triangles as game-coordinate vertices.
    /// Each triangle is returned as 3 Vector3 in game space (X, Y, Z where Z=height).
    /// tileX/tileZ identify which navmesh tile the triangle belongs to.
    /// </summary>
    public List<(Vector3 v0, Vector3 v1, Vector3 v2, int tileX, int tileZ)> GetAllDetailTriangles()
    {
        var result = new List<(Vector3, Vector3, Vector3, int, int)>();
        if (_navMesh == null)
            return result;

        lock (_navMesh)
        {
            for (var i = 0; i < _navMesh.GetMaxTiles(); i++)
            {
                var tile = _navMesh.GetTile(i);
                if (tile?.data == null)
                    continue;

                var meshData = tile.data;
                var header = meshData.header;

                // Use detail mesh triangles for accurate surface representation
                if (meshData.detailMeshes != null && meshData.detailTris != null)
                {
                    for (var j = 0; j < header.polyCount; j++)
                    {
                        var poly = meshData.polys[j];
                        var dm = meshData.detailMeshes[j];

                        for (var k = 0; k < dm.triCount; k++)
                        {
                            var triBase = (dm.triBase + k) * 4;
                            var triVerts = new Vector3[3];

                            for (var m = 0; m < 3; m++)
                            {
                                var vi = meshData.detailTris[triBase + m];
                                float vx, vy, vz;

                                if (vi < poly.vertCount)
                                {
                                    // Base polygon vertex
                                    var baseIdx = poly.verts[vi] * 3;
                                    vx = meshData.verts[baseIdx];
                                    vy = meshData.verts[baseIdx + 1];
                                    vz = meshData.verts[baseIdx + 2];
                                }
                                else
                                {
                                    // Detail vertex
                                    var detailIdx = (dm.vertBase + vi - poly.vertCount) * 3;
                                    vx = meshData.detailVerts[detailIdx];
                                    vy = meshData.detailVerts[detailIdx + 1];
                                    vz = meshData.detailVerts[detailIdx + 2];
                                }

                                // DotRecast Y-up → game: X=X, Y=Z, Z=Y(height)
                                triVerts[m] = new Vector3(vx, vz, vy);
                            }

                            result.Add((triVerts[0], triVerts[1], triVerts[2], header.x, header.y));
                        }
                    }
                }
                else
                {
                    // No detail mesh — triangulate base polygons (fan from vertex 0)
                    for (var j = 0; j < header.polyCount; j++)
                    {
                        var poly = meshData.polys[j];
                        if (poly.vertCount < 3) continue;

                        var v0Idx = poly.verts[0] * 3;
                        var v0 = new Vector3(
                            meshData.verts[v0Idx],
                            meshData.verts[v0Idx + 2],
                            meshData.verts[v0Idx + 1]);

                        for (var k = 1; k < poly.vertCount - 1; k++)
                        {
                            var v1Idx = poly.verts[k] * 3;
                            var v2Idx = poly.verts[k + 1] * 3;

                            var v1 = new Vector3(
                                meshData.verts[v1Idx],
                                meshData.verts[v1Idx + 2],
                                meshData.verts[v1Idx + 1]);
                            var v2 = new Vector3(
                                meshData.verts[v2Idx],
                                meshData.verts[v2Idx + 2],
                                meshData.verts[v2Idx + 1]);

                            result.Add((v0, v1, v2, header.x, header.y));
                        }
                    }
                }
            }
        }

        return result;
    }

    #endregion Query
}
