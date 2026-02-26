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

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Per-WorldInstance navmesh manager. Builds DotRecast navmesh tiles from
/// heightmap terrain + brush collision mesh data, provides instant
/// height queries and A* pathfinding over the navmesh surface.
/// </summary>
public class NavMeshManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // --- DotRecast navmesh ---
    private DtNavMesh _navMesh;
    private DtNavMeshQuery _navQuery;
    private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();

    // ReaderWriterLockSlim: queries (GetHeight, FindPath, Raycast) acquire read lock (concurrent),
    // tile mutations (AddTile, RemoveTile, ImportNavMesh) acquire write lock (exclusive).
    // This prevents AI tick freezes caused by navmesh build blocking height queries.
    private readonly ReaderWriterLockSlim _navLock = new();

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
    private const int MaxTiles = 8192;               // max tiles (16 tiles/cell × 2 layers × ~250 cells)
    private const int MaxPolysPerTile = 65535;
    private const int VertsPerPoly = 6;

    /// <summary>
    /// Number of navmesh tiles per game cell side.
    /// CELL_SIZE (1024m) / (NavTileSize * NavCellSize) (256m) = 4.
    /// Each game cell produces TilesPerCellSide² = 16 navmesh tiles.
    /// </summary>
    private const int TilesPerCellSide = WorldManager.CELL_SIZE / (int)(NavTileSize * NavCellSize); // 4

    /// <summary>
    /// True if the navmesh was loaded from a cached .bin file instead of built from geometry.
    /// When true, QueueBuildTile is skipped (cache already has all tiles).
    /// </summary>
    public bool LoadedFromCache { get; private set; }

    public NavMeshManager(WorldInstance world)
    {
        _world = world;
        InitNavMesh();
        TryLoadCache();

        // If not loaded from cache, queue build for any cells already loaded by PreLoadTerrain.
        // PreLoadTerrain runs before WorldInstance exists, so QueueBuildTile couldn't be called
        // during cell loading. We catch up here.
        if (!LoadedFromCache)
            QueueAllLoadedCells();
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

    /// <summary>
    /// Returns the cache file path for this world's navmesh: Data/NavMesh/{worldName}.bin
    /// </summary>
    public string GetCachePath()
    {
        var worldName = _world.Template?.Name ?? $"world_{_world.Template?.Id}";
        return Path.Combine(AAEmu.Commons.IO.FileManager.AppPath, "Data", "NavMesh", $"{worldName}.bin");
    }

    /// <summary>
    /// Tries to load navmesh from cache file on disk. If successful, sets LoadedFromCache=true
    /// and QueueBuildTile calls will be skipped.
    /// </summary>
    private void TryLoadCache()
    {
        var cachePath = GetCachePath();
        if (!File.Exists(cachePath))
            return;

        try
        {
            if (ImportNavMesh(cachePath))
            {
                LoadedFromCache = true;
                Logger.Info($"NavMesh: Loaded {TileCount} tiles from cache: {cachePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"NavMesh: Failed to load cache from {cachePath}, will build from geometry");
        }
    }

    /// <summary>
    /// Saves the current navmesh to the cache file for automatic loading on next boot.
    /// Creates the Data/NavMesh/ directory if it doesn't exist.
    /// </summary>
    public bool SaveCache()
    {
        var cachePath = GetCachePath();
        var dir = Path.GetDirectoryName(cachePath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        return ExportNavMesh(cachePath);
    }

    #region Build

    /// <summary>
    /// Queues navmesh build for all cells that were already loaded (e.g., by PreLoadTerrain).
    /// Called once during NavMeshManager construction to catch up on cells loaded before
    /// the WorldInstance/NavMeshManager existed.
    /// </summary>
    private void QueueAllLoadedCells()
    {
        var template = _world.Template;
        if (template == null)
            return;

        var count = 0;
        for (var cy = 0; cy < template.CellY; cy++)
        for (var cx = 0; cx < template.CellX; cx++)
        {
            var cell = template.Cells[cx, cy];
            if (cell?.Loaded == true)
            {
                _buildQueue.Enqueue(cell);
                count++;
            }
        }

        if (count > 0)
        {
            Logger.Info($"NavMesh: Queued {count} pre-loaded cells for build");
            lock (_buildLock)
            {
                if (_buildTask is null or { IsCompleted: true })
                    _buildTask = Task.Run(ProcessBuildQueue);
            }
        }
    }

    /// <summary>
    /// Queue a cell for async navmesh tile building.
    /// Skipped when navmesh was loaded from cache (all tiles already present).
    /// </summary>
    public void QueueBuildTile(WorldCell cell)
    {
        if (LoadedFromCache)
            return;

        _buildQueue.Enqueue(cell);

        lock (_buildLock)
        {
            if (_buildTask is { IsCompleted: false })
                return;
            _buildTask = Task.Run(ProcessBuildQueue);
        }
    }

    private int _cellsBuiltSinceLastSave;

    private void ProcessBuildQueue()
    {
        while (_buildQueue.TryDequeue(out var cell))
        {
            try
            {
                BuildTileFromCell(cell);
                _cellsBuiltSinceLastSave++;

                // Auto-save navmesh cache every 200 cells to protect against crashes.
                // The .bin is saved incrementally so progress is never lost.
                if (_cellsBuiltSinceLastSave >= 200 && HasData)
                {
                    _cellsBuiltSinceLastSave = 0;
                    try
                    {
                        SaveCache();
                        Logger.Info($"NavMesh: Auto-saved cache ({TileCount} tiles)");
                    }
                    catch (Exception saveEx)
                    {
                        Logger.Warn(saveEx, "NavMesh: Auto-save failed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to build navmesh tile for cell ({cell.CellX}, {cell.CellY})");
            }
        }

        // Final save after all cells are processed
        if (HasData)
        {
            try
            {
                SaveCache();
                Logger.Info($"NavMesh: Build complete — saved cache ({TileCount} tiles) to {GetCachePath()}");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "NavMesh: Final auto-save failed");
            }
        }
    }

    /// <summary>
    /// Builds navmesh tiles from heightmap terrain + brush collision meshes + voxels.
    /// Each 1024m game cell produces 4x4 = 16 navmesh tiles (256m each).
    /// Heightmap provides complete terrain coverage as the ground surface;
    /// brush meshes provide structural collision (building floors, walls, stair steps);
    /// voxels provide terrain modifications (caves, cliffs).
    /// In single-layer mode, heightmap quads under brush footprints are excluded
    /// so brush mesh surfaces take priority (avoids dual-surface conflicts).
    /// </summary>
    private void BuildTileFromCell(WorldCell cell)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var multiLayer = AppConfiguration.Instance.World.NavMeshMultiLayer;

        // --- Layer 0: Heightmap terrain + brush meshes (combined) ---
        var terrainVerts = new List<float>();
        var terrainFaces = new List<int>();

        // Feed both terrain and brushes into the same geometry — Recast voxelizes the
        // combined mesh and determines the walkable surface naturally.
        // No AABB filtering needed: heightmap is the actual ground, brushes add structure on top.
        var terrainTriCount = AddHeightmapTriangles(cell, terrainVerts, terrainFaces);

        // --- Layer 1 (multi-layer) or merged (single-layer): Brush + voxel geometry ---
        var brushVerts = new List<float>();
        var brushFaces = new List<int>();
        var objBrushCount = 0;
        var visBrushCount = 0;
        var voxelCount = 0;

        if (AppConfiguration.Instance.World.LoadBrushModels && cell.LoadedObjectDat != null)
        {
            HashSet<(int pathId, int px, int py, int pz)> processedBrushes = [];
            var targetVerts = multiLayer ? brushVerts : terrainVerts;
            var targetFaces = multiLayer ? brushFaces : terrainFaces;
            objBrushCount = AddBrushTriangles(cell, cell.LoadedObjectDat.PrefabsList, targetVerts, targetFaces, processedBrushes);
            if (cell.LoadedVisAreasDat != null && AppConfiguration.Instance.World.LoadVisAreasBrushes)
                visBrushCount = AddBrushTriangles(cell, cell.LoadedVisAreasDat.PrefabsList, targetVerts, targetFaces, processedBrushes);
        }

        if (cell.LoadedObjectDat != null)
        {
            var targetVerts = multiLayer ? brushVerts : terrainVerts;
            var targetFaces = multiLayer ? brushFaces : terrainFaces;
            voxelCount = AddVoxelTriangles(cell, cell.LoadedObjectDat.PrefabsList, targetVerts, targetFaces);
        }

        if (terrainVerts.Count == 0 && brushVerts.Count == 0)
        {
            Logger.Trace($"NavMesh: cell ({cell.CellX},{cell.CellY}) has no geometry, skipping");
            return;
        }

        // --- Build config (shared across all tiles) ---
        var walkableAreaMod = new RcAreaModification(RcRecast.RC_WALKABLE_AREA);
        var borderSize = (int)MathF.Ceiling(AgentRadius / NavCellSize) + 3;

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
            8,                  // regionMinSize — filters out tiny walkable patches on walls/ledges
            20,                 // regionMergeSize
            12.0f,              // edgeMaxLen
            1.3f,               // edgeMaxError
            VertsPerPoly,       // vertsPerPoly
            6.0f,               // detailSampleDist
            1.0f,               // detailSampleMaxError
            true,               // filterLowHangingObstacles
            true,               // filterLedgeSpans — marks wall tops/ledges with steep drops as non-walkable
            true,               // filterWalkableLowHeightSpans
            walkableAreaMod,    // walkableAreaMod
            true                // buildMeshDetail
        );

        var baseTileX = cell.CellX * TilesPerCellSide;
        var baseTileZ = cell.CellY * TilesPerCellSide;
        var totalPolys = 0;
        var totalVerts = 0;
        var tilesBuilt = 0;

        // --- Build layer 0 tiles (heightmap terrain + brushes in single-layer mode) ---
        if (terrainVerts.Count > 0)
        {
            var terrainGeom = new SimpleInputGeomProvider(terrainVerts.ToArray(), terrainFaces.ToArray());
            var bmin0 = terrainGeom.GetMeshBoundsMin();

            for (var sz = 0; sz < TilesPerCellSide; sz++)
            for (var sx = 0; sx < TilesPerCellSide; sx++)
            {
                try
                {
                    if (BuildSingleTile(terrainGeom, cfg, bmin0, baseTileX + sx, baseTileZ + sz, 0,
                            out var pc, out var vc))
                    {
                        totalPolys += pc;
                        totalVerts += vc;
                        tilesBuilt++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"NavMesh: tile ({baseTileX + sx},{baseTileZ + sz}) L0 failed: {ex.Message}");
                }
            }
        }

        // --- Build layer 1 tiles (brush/voxel) — only if multi-layer AND geometry exists ---
        if (multiLayer && brushVerts.Count > 0)
        {
            var brushGeom = new SimpleInputGeomProvider(brushVerts.ToArray(), brushFaces.ToArray());
            var bmin1 = brushGeom.GetMeshBoundsMin();

            for (var sz = 0; sz < TilesPerCellSide; sz++)
            for (var sx = 0; sx < TilesPerCellSide; sx++)
            {
                try
                {
                    if (BuildSingleTile(brushGeom, cfg, bmin1, baseTileX + sx, baseTileZ + sz, 1,
                            out var pc, out var vc))
                    {
                        totalPolys += pc;
                        totalVerts += vc;
                        tilesBuilt++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"NavMesh: tile ({baseTileX + sx},{baseTileZ + sz}) L1 failed: {ex.Message}");
                }
            }
        }

        sw.Stop();
        Logger.Info($"NavMesh: Built cell ({cell.CellX},{cell.CellY}) — " +
                    $"{tilesBuilt} tiles, {totalVerts} verts, {totalPolys} polys, " +
                    $"terrain={terrainTriCount}, brushes: obj={objBrushCount} vis={visBrushCount}, voxels={voxelCount}" +
                    $"{(multiLayer ? " [multi-layer]" : "")} — {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Builds a single navmesh tile from the geometry provider.
    /// tileLayer allows multiple tiles at the same (tileX, tileZ) for multi-floor support.
    /// </summary>
    private bool BuildSingleTile(SimpleInputGeomProvider geom, RcConfig cfg,
        RcVec3f bmin, int tileX, int tileZ, int tileLayer,
        out int polyCount, out int vertCount)
    {
        polyCount = 0;
        vertCount = 0;

        // bmin must be world origin (0,0,0) for X/Z so that
        // RcBuilderConfig computes tile bounds as: origin + tileX * tileSize * cellSize.
        var bmax = geom.GetMeshBoundsMax();
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
            tileLayer = tileLayer,
            buildBvTree = true
        };

        var meshData = DtNavMeshBuilder.CreateNavMeshData(navMeshParams);
        if (meshData == null)
            return false;

        // Add tile to navmesh — write lock (exclusive, blocks queries briefly)
        _navLock.EnterWriteLock();
        try
        {
            var existingRef = _navMesh.GetTileRefAt(tileX, tileZ, tileLayer);
            if (existingRef != 0)
                _navMesh.RemoveTile(existingRef);
            _navMesh.AddTile(meshData, 0, 0, out _);
        }
        finally { _navLock.ExitWriteLock(); }

        polyCount = pmesh.npolys;
        vertCount = pmesh.nverts;
        return true;
    }

    /// <summary>
    /// Generates terrain mesh from the cell's heightmap and adds triangles to the vertex/face lists.
    /// Provides complete ground coverage. Combined with brush meshes, Recast voxelizes
    /// both and determines the walkable surface naturally.
    /// Coordinates are in DotRecast Y-up: (gameX, height, gameY).
    /// Returns the number of triangles added.
    /// </summary>
    private static int AddHeightmapTriangles(WorldCell cell, List<float> verts, List<int> faces)
    {
        if (cell.HeightMap == null)
            return 0;

        var cellOffsetX = (float)(cell.CellX * WorldManager.CELL_SIZE);
        var cellOffsetY = (float)(cell.CellY * WorldManager.CELL_SIZE);
        var coeff = cell.Template.HeightMaxCoefficient;
        const int resolution = WorldManager.CELL_HMAP_RESOLUTION; // 512
        const int stride = 2; // 2 heightmap pixels = 4m per quad
        var gridW = resolution / stride + 1; // 257 vertices per side
        var baseVertIndex = verts.Count / 3;
        var count = 0;

        // Add all grid vertices (shared between quads for efficiency)
        for (var gy = 0; gy < gridW; gy++)
        {
            var sy = Math.Min(gy * stride, resolution - 1);
            var worldY = cellOffsetY + sy * 2f;

            for (var gx = 0; gx < gridW; gx++)
            {
                var sx = Math.Min(gx * stride, resolution - 1);
                var worldX = cellOffsetX + sx * 2f;
                var height = (float)(cell.HeightMap[sx, sy] / coeff);

                // DotRecast Y-up: (gameX, height, gameY)
                verts.Add(worldX);
                verts.Add(height);
                verts.Add(worldY);
            }
        }

        // Add quad faces (2 triangles per quad)
        for (var gy = 0; gy < gridW - 1; gy++)
        {
            for (var gx = 0; gx < gridW - 1; gx++)
            {
                var i00 = baseVertIndex + gy * gridW + gx;
                var i10 = i00 + 1;
                var i01 = i00 + gridW;
                var i11 = i01 + 1;

                faces.Add(i00); faces.Add(i11); faces.Add(i10);
                faces.Add(i00); faces.Add(i01); faces.Add(i11);
                count += 2;
            }
        }

        return count;
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

            // Physics proxy = real collision → always include.
            // Visual mesh fallback = limit triangle count to avoid oversized decorative geometry.
            var maxTris = AppConfiguration.Instance.World.LoadBrushMaxTriangles;
            if (!usedPhysicsProxy && maxTris > 0 && triangles.Count > maxTris)
                continue;

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
    /// With single-layer navmesh there's only ONE surface per XZ position — no risk
    /// of snapping to the wrong floor. 10m allows finding the navmesh surface even
    /// when the NPC spawns with an incorrect Z from the database.
    /// </summary>
    private const float VerticalSearchExtent = 5.0f;

    public float GetHeight(float x, float y, float z)
    {
        if (_navMesh == null)
            return 0f;

        // Game → DotRecast Y-up: (gameX, gameZ, gameY)
        var pos = new RcVec3f(x, z, y);
        var extents = new RcVec3f(2f, VerticalSearchExtent, 2f);

        _navLock.EnterReadLock();
        try
        {
            var status = _navQuery.FindNearestPoly(pos, extents, _filter, out var nearestRef, out var nearestPt, out _);
            if (status.Failed() || nearestRef == 0)
                return 0f;

            if (_navQuery.GetPolyHeight(nearestRef, new RcVec3f(pos.X, nearestPt.Y, pos.Z), out var exactH).Succeeded()
                && exactH > 0f)
                return exactH;

            return nearestPt.Y;
        }
        finally { _navLock.ExitReadLock(); }
    }

    /// <summary>
    /// Fast height query using a cached polygon reference. Avoids FindNearestPoly.
    /// Returns the navmesh surface height (game Z), or 0 if the poly ref is invalid.
    /// </summary>
    public float GetHeightOnPoly(float x, float y, float z, long polyRef)
    {
        if (_navMesh == null || polyRef == 0)
            return 0f;

        _navLock.EnterReadLock();
        try
        {
            if (!_navMesh.IsValidPolyRef(polyRef))
                return 0f;

            // Game → DotRecast Y-up
            var pos = new RcVec3f(x, z, y);
            if (_navQuery.GetPolyHeight(polyRef, pos, out var h).Succeeded() && h > 0f)
                return h; // DotRecast Y = game Z (height)

            return 0f;
        }
        finally { _navLock.ExitReadLock(); }
    }

    /// <summary>
    /// Moves from start toward end constrained to the navmesh surface.
    /// The movement slides along walls instead of clipping through them.
    /// Returns the constrained position in game coords, or null if not on navmesh.
    /// If polyRef is provided (non-zero), uses it as the starting polygon (avoids FindNearestPoly).
    /// On success, updates polyRef to the polygon the agent ended up on.
    /// </summary>
    public Vector3? MoveAlongSurface(float startX, float startY, float startZ,
        float endX, float endY, float endZ, ref long polyRef)
    {
        if (_navMesh == null)
            return null;

        // Game → DotRecast Y-up
        var startPos = new RcVec3f(startX, startZ, startY);
        var endPos = new RcVec3f(endX, endZ, endY);

        _navLock.EnterReadLock();
        try
        {
            var startRef = polyRef;

            // If no cached poly ref, find it
            if (startRef == 0 || !_navMesh.IsValidPolyRef(startRef))
            {
                var extents = new RcVec3f(2f, VerticalSearchExtent, 2f);
                var status = _navQuery.FindNearestPoly(startPos, extents, _filter,
                    out startRef, out var snappedPos, out _);
                if (status.Failed() || startRef == 0)
                    return null;

                // Use the snapped position as start (on the navmesh surface)
                startPos = snappedPos;
            }

            // Move constrained to navmesh surface
            Span<long> visited = stackalloc long[16];
            var moveStatus = _navQuery.MoveAlongSurface(startRef, startPos, endPos, _filter,
                out var resultPos, visited, out var visitedCount, 16);

            if (moveStatus.Failed())
                return null;

            // Update cached poly ref to the last polygon visited
            if (visitedCount > 0)
                polyRef = visited[visitedCount - 1];
            else
                polyRef = startRef;

            // Get exact height on the result polygon
            if (_navQuery.GetPolyHeight(polyRef, resultPos, out var exactH).Succeeded() && exactH > 0f)
                resultPos = new RcVec3f(resultPos.X, exactH, resultPos.Z);

            // DotRecast Y-up → game: X=X, Y=Z, Z=Y(height)
            return new Vector3(resultPos.X, resultPos.Z, resultPos.Y);
        }
        finally { _navLock.ExitReadLock(); }
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
        var extents = new RcVec3f(5f, VerticalSearchExtent, 5f);

        _navLock.EnterReadLock();
        try
        {
            _navQuery.FindNearestPoly(startPos, extents, _filter, out var startRef, out _, out _);
            _navQuery.FindNearestPoly(endPos, extents, _filter, out var endRef, out _, out _);

            if (startRef == 0 || endRef == 0)
                return result;

            var path = new long[256];
            _navQuery.FindPath(startRef, endRef, startPos, endPos, _filter,
                path.AsSpan(), out var pathCount, path.Length);

            if (pathCount <= 0)
                return result;

            var straightPath = new DtStraightPath[256];
            _navQuery.FindStraightPath(startPos, endPos, path.AsSpan(0, pathCount),
                pathCount, straightPath.AsSpan(), out var straightPathCount, 256, 0);

            for (var i = 0; i < straightPathCount; i++)
            {
                var p = straightPath[i].pos;
                result.Add(new Vector3(p.X, p.Z, p.Y));
            }
        }
        finally { _navLock.ExitReadLock(); }

        return result;
    }

    /// <summary>
    /// Smooths a path by removing unnecessary waypoints using navmesh raycasts.
    /// Equivalent to the client's BeautifyPath unbending: for each waypoint, tries to
    /// shortcut to the farthest reachable waypoint via clear line of sight on navmesh.
    /// This produces shorter, more natural paths (like the client's triangle-edge unbending).
    /// </summary>
    public List<Vector3> BeautifyPath(List<Vector3> path)
    {
        if (path.Count <= 2 || _navMesh == null)
            return path;

        var result = new List<Vector3> { path[0] };
        var current = 0;

        while (current < path.Count - 1)
        {
            // Try to shortcut as far ahead as possible (greedy)
            var farthest = current + 1;

            for (var ahead = path.Count - 1; ahead > current + 1; ahead--)
            {
                if (Raycast(path[current], path[ahead]))
                {
                    farthest = ahead;
                    break;
                }
            }

            result.Add(path[farthest]);
            current = farthest;
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

        _navLock.EnterReadLock();
        try
        {
            var status = _navQuery.FindNearestPoly(startPos, extents, _filter, out var startRef, out _, out _);
            if (status.Failed() || startRef == 0)
                return false;

            var path = new long[256];
            _navQuery.Raycast(startRef, startPos, endPos, _filter, out var t, out _, path.AsSpan(), out _, path.Length);

            clear = t >= 1.0f;
            return true;
        }
        finally { _navLock.ExitReadLock(); }
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
            _navLock.EnterReadLock();
            try
            {
                for (var i = 0; i < _navMesh.GetMaxTiles(); i++)
                {
                    var tile = _navMesh.GetTile(i);
                    if (tile?.data != null)
                        count++;
                }
            }
            finally { _navLock.ExitReadLock(); }
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
    public List<(Vector3 v0, Vector3 v1, Vector3 v2, int tileX, int tileZ, int tileLayer)> GetAllDetailTriangles()
    {
        var result = new List<(Vector3, Vector3, Vector3, int, int, int)>();
        if (_navMesh == null)
            return result;

        _navLock.EnterReadLock();
        try
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

                            result.Add((triVerts[0], triVerts[1], triVerts[2], header.x, header.y, header.layer));
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

                            result.Add((v0, v1, v2, header.x, header.y, header.layer));
                        }
                    }
                }
            }
        }
        finally { _navLock.ExitReadLock(); }

        return result;
    }

    /// <summary>
    /// Returns navmesh polygon edges near a position in game-space coordinates.
    /// Boundary edges (no neighbor) and internal edges are separated for different visualization.
    /// </summary>
    public (List<(Vector3 a, Vector3 b)> boundary, List<(Vector3 a, Vector3 b)> inner)
        GetPolygonEdgesNear(float x, float y, float z, float radius)
    {
        var boundary = new List<(Vector3, Vector3)>();
        var inner = new List<(Vector3, Vector3)>();
        if (_navMesh == null) return (boundary, inner);

        var radiusSq = radius * radius;

        _navLock.EnterReadLock();
        try
        {
            for (var i = 0; i < _navMesh.GetMaxTiles(); i++)
            {
                var tile = _navMesh.GetTile(i);
                if (tile?.data == null) continue;

                var header = tile.data.header;
                // Quick tile AABB reject (DotRecast Y-up: bmin/bmax)
                // Convert game pos to DotRecast to compare with tile bounds
                var tileCenterX = (header.bmin.X + header.bmax.X) * 0.5f;
                var tileCenterZ = (header.bmin.Z + header.bmax.Z) * 0.5f;
                var tileHalfX = (header.bmax.X - header.bmin.X) * 0.5f + radius;
                var tileHalfZ = (header.bmax.Z - header.bmin.Z) * 0.5f + radius;
                if (MathF.Abs(x - tileCenterX) > tileHalfX || MathF.Abs(y - tileCenterZ) > tileHalfZ)
                    continue;

                for (var j = 0; j < header.polyCount; j++)
                {
                    var poly = tile.data.polys[j];
                    if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                        continue;

                    for (var k = 0; k < poly.vertCount; k++)
                    {
                        var k2 = (k + 1) % poly.vertCount;
                        var idx0 = poly.verts[k] * 3;
                        var idx1 = poly.verts[k2] * 3;

                        // DotRecast Y-up verts → game coords: X=X, Y=Z, Z=Y
                        var a = new Vector3(
                            tile.data.verts[idx0],
                            tile.data.verts[idx0 + 2],
                            tile.data.verts[idx0 + 1]);
                        var b = new Vector3(
                            tile.data.verts[idx1],
                            tile.data.verts[idx1 + 2],
                            tile.data.verts[idx1 + 1]);

                        // Check midpoint distance to player
                        var mx = (a.X + b.X) * 0.5f - x;
                        var my = (a.Y + b.Y) * 0.5f - y;
                        if (mx * mx + my * my > radiusSq) continue;

                        var isBoundary = poly.neis[k] == 0;
                        if (isBoundary)
                            boundary.Add((a, b));
                        else
                            inner.Add((a, b));
                    }
                }
            }
        }
        finally { _navLock.ExitReadLock(); }

        return (boundary, inner);
    }

    #endregion Query

    #region Serialization

    /// <summary>
    /// Exports the built navmesh to a binary file using DotRecast's native format.
    /// The file can be loaded back via ImportNavMesh, or opened in Recast demo tools.
    /// </summary>
    public bool ExportNavMesh(string filePath)
    {
        if (_navMesh == null || !HasData)
            return false;

        _navLock.EnterReadLock();
        try
        {
            using var fs = File.Create(filePath);
            using var writer = new BinaryWriter(fs);
            var meshWriter = new DotRecast.Detour.Io.DtMeshSetWriter();
            meshWriter.Write(writer, _navMesh, RcByteOrder.LITTLE_ENDIAN, true);
        }
        finally { _navLock.ExitReadLock(); }

        return true;
    }

    /// <summary>
    /// Imports a navmesh from a binary file, replacing the current navmesh entirely.
    /// The file must be in DotRecast/Recast Navigation binary format.
    /// </summary>
    public bool ImportNavMesh(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        using var fs = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs);
        var meshReader = new DotRecast.Detour.Io.DtMeshSetReader();
        var loadedMesh = meshReader.Read(reader, VertsPerPoly);

        if (loadedMesh == null)
            return false;

        _navLock.EnterWriteLock();
        try
        {
            _navMesh = loadedMesh;
            _navQuery = new DtNavMeshQuery(_navMesh);
        }
        finally { _navLock.ExitWriteLock(); }

        Logger.Info($"NavMesh: Imported {TileCount} tiles from {filePath}");
        return true;
    }

    #endregion Serialization
}
