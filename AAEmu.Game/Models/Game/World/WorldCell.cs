using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.ClientData;
using Jitter2.LinearMath;
using NLog;

namespace AAEmu.Game.Models.Game.World;

public class WorldCell
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private WorldTemplate Template { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public bool Loaded { get; private set; }
    private bool Loading { get; set; }
    private Vector3 CellOffset { get; set; }
    internal ushort[,] HeightMap { get; private set; }
    private float MinHeight { get; set; }
    private float MaxHeight { get; set; }

    /// <summary>
    /// Bounding box for use in Jitter
    /// </summary>
    public JBoundingBox BoundingBox { get; private set; }

    public WorldCell(int cellX, int cellY, WorldTemplate template)
    {
        CellX = cellX;
        CellY = cellY;
        Template = template;
        CellOffset = new Vector3(CellX * WorldManager.CELL_SIZE, CellY * WorldManager.CELL_SIZE, 0f);
        // Default bounding box
        BoundingBox = new JBoundingBox(
            new JVector(CellOffset.X, 0f, CellOffset.Y), 
            new JVector(CellOffset.X + WorldManager.CELL_SIZE, 0f, CellOffset.Y + WorldManager.CELL_SIZE)
            );
    }


    /// <summary>
    /// Checks if the cell is loaded and loads it if it hasn't 
    /// </summary>
    /// <returns></returns>
    public WorldCell VerifyCellLoaded()
    {
        if (Loaded)
            return this;

        if (!Loading)
        {
            Loading = true;
            // Assign heightmap array
            HeightMap = new ushort[WorldManager.CELL_HMAP_RESOLUTION, WorldManager.CELL_HMAP_RESOLUTION];
            // Load data
            Loaded = LoadCellHeightMapFromClientData();
            Loading = false;
        }
        return this;
    }

    /// <summary>
    /// Loads a given Cell worth of heightmap data
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    private bool LoadCellHeightMapFromClientData()
    {
        var cellFileName = $"{CellX:000}_{CellY:000}";
        var heightMapFile = Path.Combine("game", "worlds", Template.Name, "cells", cellFileName, "client", "terrain", "heightmap.dat");
        if (!ClientFileManager.FileExists(heightMapFile))
        {
            return true;
        }

        using var stream = ClientFileManager.GetFileStream(heightMapFile);
        if (stream == null)
        {
            return true;
        }

        // Logger.Debug($"Loading {heightMapFile}");

        // Read the cell hmap data
        using var br = new BinaryReader(stream);
        var hmap = new Hmap();

        if (hmap.Read(br, false) < 0)
        {
            Logger.Error($"Error reading {heightMapFile}");
            return false;
        }

        // Sort nodes by position
        var sortedNodes = hmap.Nodes
            .OrderBy(cell => cell.BoxHeightmap.Min.X)
            .ThenBy(cell => cell.BoxHeightmap.Min.Y)
            .Where(x => x.pHMData.Length > 0)
            .ToList();

        // Read nodes into heightmap array
        #region ReadNodes

        MinHeight = float.MaxValue;
        MaxHeight = 0f;
        for (ushort sectorX = 0; sectorX < WorldManager.SECTORS_PER_CELL; sectorX++) // 16x16 sectors / cell
        for (ushort sectorY = 0; sectorY < WorldManager.SECTORS_PER_CELL; sectorY++)
        for (ushort unitX = 0; unitX < WorldManager.SECTOR_HMAP_RESOLUTION; unitX++) // sector = 32x32 unit size
        for (ushort unitY = 0; unitY < WorldManager.SECTOR_HMAP_RESOLUTION; unitY++)
        {
            var node = sortedNodes[sectorX * WorldManager.SECTORS_PER_CELL + sectorY];
            var oX = sectorX * WorldManager.SECTOR_HMAP_RESOLUTION + unitX;
            var oY = sectorY * WorldManager.SECTOR_HMAP_RESOLUTION + unitY;

            var height = node.GetHeight(unitX, unitY);
            var value = (ushort)(height * Template.HeightMaxCoefficient);

            HeightMap[oX, oY] = value;
            MinHeight = MathF.Min((float)(value / Template.HeightMaxCoefficient), MinHeight);
            MaxHeight = MathF.Max((float)(value / Template.HeightMaxCoefficient), MaxHeight);
        }
        #endregion

        // Update bounding box
        BoundingBox = new JBoundingBox(
            new JVector(CellOffset.X, MinHeight, CellOffset.Y), 
            new JVector(CellOffset.X + WorldManager.CELL_SIZE, MaxHeight, CellOffset.Y + WorldManager.CELL_SIZE)
        );

        #region update_physics_hmap

        // Update Physics world's heightmaps
        // TODO: Merge local heightmap into physics engine
        foreach (var worldInstance in WorldManager.Instance.GetWorldsByTemplate(Template.Id))
        {
            worldInstance.Physics?.UpdateHeightMapFromCellBody(this);
        }
        #endregion
        return true;
    }

    /// <summary>
    /// Gets heightmap height at target data position, converted to float, but not smoothened
    /// </summary>
    /// <param name="heightMapDataX"></param>
    /// <param name="heightMapDataY"></param>
    /// <returns></returns>
    public float GetHeightMapDataInCell(int heightMapDataX, int heightMapDataY)
    {
        if (HeightMap == null ||
            heightMapDataX < 0 || heightMapDataX > WorldManager.CELL_HMAP_RESOLUTION ||
            heightMapDataY < 0 || heightMapDataY > WorldManager.CELL_HMAP_RESOLUTION)
        {
            return 0f; // out of bounds or not loaded
        }

        return (float)(HeightMap[heightMapDataX, heightMapDataX] / Template.HeightMaxCoefficient);
    }

    /// <summary>
    /// Gets height at target world position
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns>Returns 0 if it's outside of this cell's bounds, otherwise returns non-smoothened height</returns>
    public float GetHeight(int x, int y)
    {
        var xx = (int)(x - CellOffset.X) / 2;
        var yy = (int)(y - CellOffset.Y) / 2;
        return GetHeightMapDataInCell(xx, yy);
    }
}
