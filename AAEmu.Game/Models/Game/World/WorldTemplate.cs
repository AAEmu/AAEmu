using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Models.Game.World.Zones;
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
        var sx = (x % WorldManager.CELL_SIZE) / 2;
        var sy = (y % WorldManager.CELL_SIZE) / 2;
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
        return new System.Drawing.Rectangle(x - (x % 2), y - (y % 2), 2, 2);
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
}
