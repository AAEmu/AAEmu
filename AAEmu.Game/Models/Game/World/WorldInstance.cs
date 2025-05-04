using System;
using System.IO;
using System.Numerics;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers.World;
using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Instance of a World
/// </summary>
public class WorldInstance
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Instance Id for this world
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// Template of this world
    /// </summary>
    public WorldTemplate Template { get; set; }

    /// <summary>
    /// Collection of Region data
    /// </summary>
    public Region[,] Regions { get; set; }

    /// <summary>
    /// Physics handler
    /// </summary>
    public BoatPhysicsManager Physics { get; set; }

    /// <summary>
    /// Water definitions
    /// </summary>
    public WaterBodies Water { get; set; }

    /// <summary>
    /// Event handlers
    /// </summary>
    public WorldEvents Events { get; set; } = new();

    public SphereQuestManager SphereQuestManager { get; set; }
    ~WorldInstance()
    {
        Logger.Info($"WorldInstance {Id} - {Template.Name} ({Template.Id}) removed");
    }

    /// <summary>
    /// Checks if target position is inside a body of water
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public bool IsWater(Vector3 position) => IsWater(position, out _);

    /// <summary>
    /// Checks if target position is inside a body of water and returns it's flow direction (if available)
    /// </summary>
    /// <param name="point"></param>
    /// <param name="flowDirection"></param>
    /// <returns></returns>
    public bool IsWater(Vector3 point, out Vector3 flowDirection)
    {
        if (Water != null)
            return Water.IsWater(point, out flowDirection);

        flowDirection = Vector3.Zero;

        if (point.Z <= Template.OceanLevel)
            return true;

        // TODO: Check shapes
        return false;
    }

    /// <summary>
    /// Gets heightmap height at target position (not smoothened)
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public float GetRawHeightMapHeight(int x, int y)
    {
        // This is the old GetHeight()
        var sx = x / 2;
        var sy = y / 2;
        return (float)(Template.HeightMaps[sx, sy] / Template.HeightMaxCoefficient);
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
    /// Get Sector at specific offset
    /// </summary>
    /// <param name="sectorX">X offset of the Sector</param>
    /// <param name="sectorY">Y offset of the Sector</param>
    /// <returns></returns>
    public Region GetRegion(int sectorX, int sectorY)
    {
        if (Template.ValidRegion(sectorX, sectorY))
            if (Regions[sectorX, sectorY] == null)
                return Regions[sectorX, sectorY] = new Region(Id, sectorX, sectorY, 0);
            else
                return Regions[sectorX, sectorY];

        return null;
    }

    public Region GetRegionByPos(Vector3 pos)
    {
        var sectorX = (int)(pos.X / WorldManager.REGION_SIZE);
        var sectorY = (int)(pos.Y / WorldManager.REGION_SIZE);
        if (Template.ValidRegion(sectorX, sectorY))
            if (Regions[sectorX, sectorY] == null)
                return Regions[sectorX, sectorY] = new Region(Id, sectorX, sectorY, 0);
            else
                return Regions[sectorX, sectorY];

        return null;
    }

    /// <summary>
    /// Loads water body date for this world
    /// </summary>
    public void LoadWaterBodies()
    {
        // Try to load from saved json data
        var customFile = Path.Combine(FileManager.AppPath, "Data", "Worlds", Template.Name, "water_bodies.json");
        if (!File.Exists(customFile))
        {
            return;
        }

        Logger.Debug($"Loading water body data for {Template.Name} ({Template.Id})");
        if (WaterBodies.Load(customFile, out var newWater))
        {
            Water = newWater;
        }
    }
}
