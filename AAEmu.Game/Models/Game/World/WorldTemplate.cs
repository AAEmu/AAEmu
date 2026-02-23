using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Template of a World
/// </summary>
public class WorldTemplate
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// TemplateId for this world
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// World name
    /// </summary>
    public virtual string Name { get; set; }

    /// <summary>
    /// Max height for this world's map data
    /// </summary>
    public float MaxHeight { get; set; }

    /// <summary>
    /// Height Coefficient
    /// </summary>
    public virtual double HeightMaxCoefficient { get; set; }
    /// <summary>
    /// Height of the ocean surface for this world
    /// </summary>
    public float OceanLevel { get; set; } = 100f;
    /// <summary>
    /// World X size in Cells (1024m)
    /// </summary>
    public int CellX { get; set; }
    /// <summary>
    /// World Y size in Cells (1024m)
    /// </summary>
    public int CellY { get; set; }
    /// <summary>
    /// Default spawn location for this world (not used when creating new characters)
    /// </summary>
    public WorldSpawnPosition SpawnPosition { get; set; } = new();

    public WorldCell[,] Cells { get; set; } = new WorldCell[1, 1];
    // <summary>
    // Raw Heightmap data for this world
    // </summary>
    // public virtual ushort[,] HeightMaps { get; set; }

    // <summary>
    // List of what cells have been loaded/processed
    // </summary>
    // public virtual bool[,] LoadedCells { get; set; }

    /// <summary>
    /// Collection of ZoneKeys per Region
    /// </summary>
    public uint[,] ZoneKeyByRegions { get; set; }
    
    /// <summary>
    /// List of levels inside this world (Zone Keys)
    /// </summary>
    public List<uint> ZoneKeys { get; set; } = [];

    /// <summary>
    /// Xml data for this world
    /// </summary>
    public XmlWorld XmlWorld { get; set; } = new();

    /// <summary>
    /// XML Zone data
    /// </summary>
    public ConcurrentDictionary<uint, XmlWorldZone> XmlWorldZones;

    /// <summary>
    /// List of SubZones in this world (zoneId, list)
    /// </summary>
    public Dictionary<uint, List<Area>> SubZones { get; set; } = [];
    /// <summary>
    /// List of housing zones in this world (zoneId, list)
    /// </summary>
    public Dictionary<uint, List<Area>> HousingZones { get; set; } = []; 

    /// <summary>
    /// Handles navmesh data
    /// </summary>
    public AiGeoDataManager GeoData { get; set; }

    /// <summary>
    /// Custom building floor zones (loaded from building_floors.json)
    /// </summary>
    public BuildingFloorManager BuildingFloors { get; set; }

    /// <summary>
    /// Brush bounding boxes indexed by path tile (256x256 units).
    /// Populated from object.dat during cell loading.
    /// </summary>
    private readonly ConcurrentDictionary<(uint, uint), List<BrushBounds>> _brushBoundsIndex = new();
    private readonly Lock _brushLock = new();

    /// <summary>
    /// Adds a brush bounding box to the spatial index.
    /// </summary>
    public void AddBrushBounds(BrushBounds bounds)
    {
        // Index by the path tile that contains the brush center
        var cx = (bounds.MinX + bounds.MaxX) / 2f;
        var cy = (bounds.MinY + bounds.MaxY) / 2f;
        var tileX = (uint)MathF.Floor(cx / 256f);
        var tileY = (uint)MathF.Floor(cy / 256f);

        var list = _brushBoundsIndex.GetOrAdd((tileX, tileY), _ => []);
        lock (_brushLock)
        {
            list.Add(bounds);
        }
    }

    /// <summary>
    /// Gets the floor height from brush bounding boxes at the given position.
    /// Returns 0 if no brush contains this position.
    /// </summary>
    public float GetBrushFloorHeight(float x, float y, float z)
    {
        var tileX = (uint)MathF.Floor(x / 256f);
        var tileY = (uint)MathF.Floor(y / 256f);

        var bestZ = 0f;
        var bestDist = float.MaxValue;

        // Check current tile and adjacent tiles (brush may span tile boundaries)
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var tx = (uint)((int)tileX + dx);
            var ty = (uint)((int)tileY + dy);

            if (!_brushBoundsIndex.TryGetValue((tx, ty), out var brushes))
                continue;

            lock (_brushLock)
            {
                foreach (var b in brushes)
                {
                    // Check if point is inside brush XY bounds
                    if (x < b.MinX || x > b.MaxX || y < b.MinY || y > b.MaxY)
                        continue;

                    // Point is inside brush XY. Check vertical containment.
                    // The NPC should be between the floor (MinZ) and ceiling (MaxZ).
                    if (z < b.MinZ - 5f || z > b.MaxZ + 5f)
                        continue;

                    // Find the brush floor closest to current Z (from below)
                    var dist = MathF.Abs(z - b.MinZ);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestZ = b.MinZ;
                    }
                }
            }
        }

        return bestZ;
    }

    /// <summary>
    /// Gets all brush bounds near a position for debug visualization.
    /// </summary>
    public List<BrushBounds> GetNearbyBrushBounds(float x, float y, float radius)
    {
        var result = new List<BrushBounds>();
        var radiusSq = radius * radius;

        var minTileX = (uint)MathF.Floor((x - radius) / 256f);
        var maxTileX = (uint)MathF.Floor((x + radius) / 256f);
        var minTileY = (uint)MathF.Floor((y - radius) / 256f);
        var maxTileY = (uint)MathF.Floor((y + radius) / 256f);

        for (var ty = minTileY; ty <= maxTileY; ty++)
        for (var tx = minTileX; tx <= maxTileX; tx++)
        {
            if (!_brushBoundsIndex.TryGetValue((tx, ty), out var brushes))
                continue;

            lock (_brushLock)
            {
                foreach (var b in brushes)
                {
                    var cx = (b.MinX + b.MaxX) / 2f;
                    var cy = (b.MinY + b.MaxY) / 2f;
                    var dx = x - cx;
                    var dy = y - cy;
                    if (dx * dx + dy * dy <= radiusSq)
                        result.Add(b);
                }
            }
        }

        return result;
    }

    public int BrushBoundsCount
    {
        get
        {
            var count = 0;
            foreach (var list in _brushBoundsIndex.Values)
                count += list.Count;
            return count;
        }
    }

    /// <summary>
    /// ZoneKey, BaiLoader
    /// </summary>
    public Dictionary<uint, BaseBaiLoader> ZoneBaiLoader { get; init; } = [];
    /// <summary>
    /// (PathX, PathY), BaiLoader
    /// </summary>
    public Dictionary<(uint, uint), BaseBaiLoader> PathBaiLoader { get; init; } = [];

    /// <summary>
    /// Gets heightmap height at target position (not smoothened)
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public float GetRawHeightMapHeight(int x, int y)
    {
        var cellX = x / WorldManager.CELL_SIZE;
        var cellY = y / WorldManager.CELL_SIZE;
        if (cellX < 0 || cellX > CellX || cellY < 0 || cellY > CellY)
            return 0f; // out of bounds
        var cell = Cells[cellX, cellY].VerifyCellLoaded();
        var sx = x % WorldManager.CELL_SIZE / 2;
        var sy = y % WorldManager.CELL_SIZE / 2;
        return (float)(cell.HeightMap[sx, sy] / HeightMaxCoefficient);
    }

    /// <summary>
    /// Line linear interpolation
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="target">value 0 to 1</param>
    /// <returns></returns>
    private static float Lerp(float start, float end, float target)
    {
        return start + (end - start) * target;
    }

    /// <summary>
    /// Square linear interpolation
    /// </summary>
    /// <param name="cX0Y0">Bottom-Left</param>
    /// <param name="cX1Y0">Bottom-Right</param>
    /// <param name="cX0Y1">Top-Left</param>
    /// <param name="cX1Y1">Top-Right</param>
    /// <param name="tx">value 0 to 1</param>
    /// <param name="ty">value 0 to 1</param>
    /// <returns></returns>
    private static float Blerp(float cX0Y0, float cX1Y0, float cX0Y1, float cX1Y1, float tx, float ty)
    {
        return Lerp(Lerp(cX0Y0, cX1Y0, tx), Lerp(cX0Y1, cX1Y1, tx), ty);
    }

    /// <summary>
    /// Picks the nearest 4 points of a square that contain target position
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    private static System.Drawing.Rectangle FindNearestSignificantPoints(int x, int y)
    {
        return new System.Drawing.Rectangle(x - x % 2, y - y % 2, 2, 2);
    }

    /// <summary>
    /// Gets height at target position using interpolation
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public float GetHeight(float x, float y)
    {
        // return GetRawHeightMapHeight((int)x, (int)y); // <-- the old way we used to do things

        // Get bordering points
        var border = FindNearestSignificantPoints((int)Math.Floor(x), (int)Math.Floor(y));

        // Get heights for these points
        var heightTl = GetRawHeightMapHeight(border.Left, border.Top);
        var heightTr = GetRawHeightMapHeight(border.Right, border.Top);
        var heightBl = GetRawHeightMapHeight(border.Left, border.Bottom);
        var heightBr = GetRawHeightMapHeight(border.Right, border.Bottom);
        var offX = (x - border.Left) / 2;
        var offY = (y - border.Top) / 2;
        var height = Blerp(heightTl, heightTr, heightBl, heightBr, offX, offY); // bilinear interpolation

        return height;
    }

    /// <summary>
    /// Checks if target sector offset is within the world's bounds
    /// </summary>
    /// <param name="sectorX"></param>
    /// <param name="sectorY"></param>
    /// <returns></returns>
    public bool ValidRegion(int sectorX, int sectorY)
    {
        return sectorX >= 0 && sectorX < CellX * WorldManager.SECTORS_PER_CELL && sectorY >= 0 && sectorY < CellY * WorldManager.SECTORS_PER_CELL;
    }

    /// <summary>
    /// Gets target cell
    /// </summary>
    /// <param name="cellX"></param>
    /// <param name="cellY"></param>
    /// <returns>Returns the cell, or null if the given index is out of bounds for this world</returns>
    public WorldCell GetCell(int cellX, int cellY)
    {
        if (cellX < 0 || cellX > CellX || cellY < 0 || cellY > CellY)
            return null;
        return Cells[cellX, cellY];
    }

    public void LoadZoneBaiFiles()
    {
        if (!AppConfiguration.Instance.World.GeoDataMode)
            return; // Don't load navmesh if GeoDataMode is disabled

        foreach (var zoneKey in ZoneKeys)
        {
            var worldFolder = Path.Combine("game", "worlds", Name, "zone", zoneKey.ToString());
            var baiFilesList = ClientFileManager.GetFilesInDirectory(worldFolder, "*.bai", false).ToArray();
            if (baiFilesList.Length <= 0)
                continue;

            var zoneBaiLoader = new BaseBaiLoader(this);
            zoneBaiLoader.LoadBaiFilesFromFolder(zoneKey.ToString());
            ZoneBaiLoader.Add(zoneKey, zoneBaiLoader);
        }
    }

    public BaseBaiLoader GetBaiByPos(Vector3 pos)
    {
        if (ZoneBaiLoader.Count > 0)
            return ZoneBaiLoader.Values.First(); // TODO: Pick the actually correct zone

        // First verify if target cell is loaded
        var cellPos = pos.ToCellIndex();
        var cell = Cells[cellPos.Item1, cellPos.Item2];
        cell.VerifyCellLoaded();
        // Return value from the main paths dictionary
        var pathsPos = pos.ToPathsIndex();
        return PathBaiLoader.GetValueOrDefault(((uint)pathsPos.Item1, (uint)pathsPos.Item2));
    }
}
