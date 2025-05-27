using System;
using System.IO;
using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.ClientData;

using NLog;

namespace AAEmu.Game.Models.Game.World;

public class WorldCell
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    
    public WorldTemplate Template { get; init; }
    private int CellX { get; init; }
    private int CellY { get; init; }
    public bool Loaded { get; private set; }
    private bool Loading { get; set; }
    private System.Drawing.Point CellOffset { get; set; }
    internal ushort[,] HeightMap { get; set; }

    public WorldCell(int cellX, int cellY, WorldTemplate template)
    {
        CellX = cellX;
        CellY = cellY;
        Template = template;
        CellOffset = new System.Drawing.Point(CellX * WorldManager.CELL_SIZE, CellY * WorldManager.CELL_SIZE);
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
        var nodes = hmap.Nodes
            .OrderBy(cell => cell.BoxHeightmap.Min.X)
            .ThenBy(cell => cell.BoxHeightmap.Min.Y)
            .Where(x => x.pHMData.Length > 0)
            .ToList();

        // Read nodes into heightmap array

        #region ReadNodes

        for (ushort sectorX = 0; sectorX < WorldManager.SECTORS_PER_CELL; sectorX++) // 16x16 sectors / cell
        for (ushort sectorY = 0; sectorY < WorldManager.SECTORS_PER_CELL; sectorY++)
        for (ushort unitX = 0; unitX < WorldManager.SECTOR_HMAP_RESOLUTION; unitX++) // sector = 32x32 unit size
        for (ushort unitY = 0; unitY < WorldManager.SECTOR_HMAP_RESOLUTION; unitY++)
        {
            var node = nodes[sectorX * WorldManager.SECTORS_PER_CELL + sectorY];
            var oX = sectorX * WorldManager.SECTOR_HMAP_RESOLUTION + unitX;
            var oY = sectorY * WorldManager.SECTOR_HMAP_RESOLUTION + unitY;

            var height = node.GetHeight(unitX, unitY);
            var value = (ushort)(height * Template.HeightMaxCoefficient);

            HeightMap[oX, oY] = value;
        }
        #endregion

        #region update_physics_hmap

        // Update Physics world's heightmaps
        // TODO: Merge local heightmap into physics engine
        foreach (var worldInstance in WorldManager.Instance.GetWorlds())
        {
            if (worldInstance == null || worldInstance.Template.Name != Template.Name)
                continue;
            worldInstance.Physics?.AddHeightMapCellBody(CellX, CellY);
        }
        #endregion
        return true;
    }
}
