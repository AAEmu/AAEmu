using System.Globalization;
using System.Numerics;
using System.Xml;
using AAEmu.Commons.Utils.XML;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.ClientData;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Objects;
using Jitter2.LinearMath;
using NLog;

namespace AAEmu.Game.Models.Game.World;

public class WorldCell
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public WorldTemplate Template { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public bool Loaded { get; private set; }
    private bool Loading { get; set; }
    private Vector3 CellOffset { get; set; }
    internal ushort[,] HeightMap { get; private set; }
    private float MinHeight { get; set; }
    private float MaxHeight { get; set; }

    /// <summary>
    /// Bai files data to use in this cell
    /// </summary>
    public BaseBaiLoader[,] BaiLoader { get; set; }

    /// <summary>
    /// Parsed object.dat data (kept in memory for mesh loading)
    /// </summary>
    public ObjectsFile LoadedObjectDat { get; set; }

    /// <summary>
    /// Parsed visareas.dat data
    /// </summary>
    public VisAreasFile LoadedVisAreasDat { get; set; }

    /// <summary>
    /// Material list (material_list.dat) — maps MaterialId to .mtl path
    /// </summary>
    public MaterialsListFile MaterialListFiles { get; set; }

    /// <summary>
    /// Static objects list (statobjs.dat) — maps PathId to .cgf path
    /// </summary>
    public MaterialsFile StatObjsFiles { get; set; }

    /// <summary>
    /// AreaShape entities from entities.xml — 3D polygon areas for navigation.
    /// </summary>
    public List<CellAreaShape> AreaShapes { get; set; } = [];

    /// <summary>
    /// Cover point data from cover.ctc (AI combat cover).
    /// </summary>
    public CoverCtcFile LoadedCoverCtc { get; set; }

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
        BaiLoader = new BaseBaiLoader[4, 4]
        {
            { null, null, null, null },
            { null, null, null, null },
            { null, null, null, null },
            { null, null, null, null }
        };
    }

    /// <summary>
    /// Load the *.bai files for this Cell if GeoDataMode is enabled 
    /// </summary>
    private void LoadBaiFiles()
    {
        if (!AppConfiguration.Instance.World.GeoDataMode)
            return; // Don't load navmesh if GeoDataMode is disabled

        // If we already loaded zone bai data for this world template, then don't try to load the cell one
        // Instead reference the zone bai directly
        if (Template.ZoneBaiLoader.Count > 0)
        {
            // This is zone grabbing is just an estimate, but should be good enough for this purpose
            // Ideally you'd check all 256 sectors in the cell and take it's median, but feels like overkill here
            var zoneKey = Template.ZoneKeyByRegions[CellX * WorldManager.SECTORS_PER_CELL, CellY * WorldManager.SECTORS_PER_CELL];
            if (Template.ZoneBaiLoader.TryGetValue(zoneKey, out var parentZoneBaiLoader))
            {
                // Assign it for all paths in this cell
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < 4; x++)
                    {
                        BaiLoader[x, y] = parentZoneBaiLoader;
                    }
                }
            }
            else
            {
                Logger.Warn($"WorldTemplate {Template.Name} has Zone bai files data loaded ({Template.ZoneBaiLoader.Count} zones), but could not find a matching file for this cell {zoneKey}");
            }
            return;
        }

        // Load the 4x4 grid of path folders into this cell
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var pathX = (uint)(CellX * 4 + x);
                var pathY = (uint)(CellY * 4 + y);
                var pathFolder = $"{pathX:000}_{pathY:000}";
                var pathBaiLoader = new BaseBaiLoader(Template);
                pathBaiLoader.LoadBaiFilesFromFolder(pathFolder); // (x != 0 || y != 0)
                BaiLoader[x, y] = pathBaiLoader;
                Template.PathBaiLoader.Add((pathX, pathY), pathBaiLoader);
            }
        }
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
            LoadBaiFiles();
            LoadObjectDat();
            LoadEntitiesXml();
            LoadCoverCtc();
            Loaded = LoadCellHeightMapFromClientData();
            Loading = false;

            // Queue navmesh tile build (needs both heightmap + object.dat loaded)
            if (Loaded)
            {
                foreach (var worldInstance in WorldManager.Instance.GetWorldsByTemplate(Template.Id).ToArray())
                    worldInstance.NavMesh?.QueueBuildTile(this);
            }
        }
        else
        {
            // Another thread is loading this cell — wait for it to finish
            while (Loading && !Loaded)
                Thread.Sleep(50);
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

    /// <summary>
    /// Loads object.dat and auxiliary data files from this cell's client data.
    /// Extracts brush bounding boxes and prepares data for 3D mesh loading.
    /// </summary>
    private void LoadObjectDat()
    {
        var cellFileName = $"{CellX:000}_{CellY:000}";
        var cellFolder = Path.Combine("game", "worlds", Template.Name, "cells", cellFileName);

        // Load material_list.dat (maps MaterialId -> .mtl path)
        var materialListFile = Path.Combine(cellFolder, "client", "material_list.dat");
        if (ClientFileManager.FileExists(materialListFile))
        {
            var materialList = new MaterialsListFile(materialListFile);
            materialList.ReadFile();
            MaterialListFiles = materialList;
        }

        // Load statobjs.dat (maps PathId -> .cgf model path)
        var statObjFile = Path.Combine(cellFolder, "client", "statobjs.dat");
        if (ClientFileManager.FileExists(statObjFile))
        {
            var statObjs = new MaterialsFile(statObjFile);
            statObjs.ReadFile();
            StatObjsFiles = statObjs;
        }

        // Load object.dat
        var objectDatFile = Path.Combine(cellFolder, "client", "object.dat");
        if (ClientFileManager.FileExists(objectDatFile))
        {
            var objects = new ObjectsFile(objectDatFile);
            if (objects.ReadFile())
                LoadedObjectDat = objects;
            else if (objects.AssetPathsList.Count > 0 || objects.PrefabsList.Count > 0)
                Logger.Error($"Error loading {objectDatFile}, only {objects.AssetPathsList.Count} assets and {objects.PrefabsList.Count} prefabs read");
        }

        // Load visareas.dat
        var visAreasDatFile = Path.Combine(cellFolder, "client", "visareas.dat");
        if (ClientFileManager.FileExists(visAreasDatFile))
        {
            var visObjects = new VisAreasFile(visAreasDatFile);
            if (visObjects.ReadFile())
                LoadedVisAreasDat = visObjects;
            else if (visObjects.AssetPathsList.Count > 0 || visObjects.PrefabsList.Count > 0 || visObjects.VisAreas.Count > 0)
                Logger.Error($"Error loading {visAreasDatFile}");
        }

        // Extract brush bounding boxes (AABB index, fast pre-filter)
        if (LoadedObjectDat != null)
            ExtractBrushBounds(LoadedObjectDat);

        // Diagnostic: log what was loaded for this cell
        var statCount = StatObjsFiles?.MaterialList.Count ?? -1;
        var matCount = MaterialListFiles?.MaterialsList.Count ?? -1;
        var objCount = LoadedObjectDat?.PrefabsList.Count ?? -1;
        var visCount = LoadedVisAreasDat?.PrefabsList.Count ?? -1;
        if (objCount > 0)
            Logger.Debug($"Cell ({CellX},{CellY}): objects={objCount}, statobjs={statCount}, materials={matCount}, visareas={visCount}");

    }

    /// <summary>
    /// Loads AreaShape entities from entities.xml in this cell's client data.
    /// Only loads entities with EntityClass="AreaShape" for navigation-related data.
    /// </summary>
    private void LoadEntitiesXml()
    {
        if (!AppConfiguration.Instance.World.GeoDataMode)
            return;

        var cellFileName = $"{CellX:000}_{CellY:000}";
        var entitiesFile = Path.Combine("game", "worlds", Template.Name, "cells", cellFileName, "client", "entities.xml");
        if (!ClientFileManager.FileExists(entitiesFile))
            return;

        var contents = ClientFileManager.GetFileAsString(entitiesFile);
        if (string.IsNullOrWhiteSpace(contents))
            return;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(contents);

            var entityNodes = doc.SelectNodes("/Objects/Entity[@EntityClass='AreaShape']");
            if (entityNodes == null || entityNodes.Count == 0)
                return;

            for (var i = 0; i < entityNodes.Count; i++)
            {
                var node = entityNodes[i];
                var attribs = XmlHelper.ReadNodeAttributes(node);

                var shape = new CellAreaShape();

                if (attribs.TryGetValue("Name", out var name))
                    shape.Name = name;

                if (attribs.TryGetValue("Pos", out var posStr))
                    shape.Origin = XmlHelper.StringToVector3(posStr);

                // Read Area element
                var areaNode = node.SelectSingleNode("Area");
                if (areaNode == null)
                    continue;

                var areaAttribs = XmlHelper.ReadNodeAttributes(areaNode);
                shape.AreaId = XmlHelper.ReadAttribute(areaAttribs, "Id", 0);
                shape.GroupId = XmlHelper.ReadAttribute(areaAttribs, "Group", 0);
                shape.Height = XmlHelper.ReadAttribute(areaAttribs, "Height", 0f);

                // Read Points
                var pointNodes = areaNode.SelectNodes("Points/Point");
                if (pointNodes != null)
                {
                    for (var j = 0; j < pointNodes.Count; j++)
                    {
                        var pointAttribs = XmlHelper.ReadNodeAttributes(pointNodes[j]);
                        if (pointAttribs.TryGetValue("Pos", out var pointPosStr))
                            shape.Points.Add(XmlHelper.StringToVector3(pointPosStr));
                    }
                }

                if (shape.Points.Count >= 3)
                    AreaShapes.Add(shape);
            }

            if (AreaShapes.Count > 0)
                Logger.Trace($"Loaded {AreaShapes.Count} area shapes from cell {cellFileName}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error loading entities.xml for cell {cellFileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads cover.ctc from this cell's client data.
    /// Cover points are fed into the WorldTemplate spatial index for AI combat queries.
    /// </summary>
    private void LoadCoverCtc()
    {
        if (!AppConfiguration.Instance.World.GeoDataMode)
            return;
        if (!AppConfiguration.Instance.World.LoadCoverPoints)
            return;

        var cellFileName = $"{CellX:000}_{CellY:000}";
        var coverFile = Path.Combine("game", "worlds", Template.Name, "cells", cellFileName, "client", "cover.ctc");
        if (!ClientFileManager.FileExists(coverFile))
            return;

        var ctcFile = new CoverCtcFile(coverFile);
        if (ctcFile.ReadFile())
        {
            LoadedCoverCtc = ctcFile;
            // Feed any extracted cover points into the template's spatial index
            foreach (var point in ctcFile.CoverPoints)
                Template.AddCoverPoint(point);
        }
    }

    /// <summary>
    /// Extracts AABB brush bounds from parsed object.dat and stores in WorldTemplate index.
    /// </summary>
    private void ExtractBrushBounds(ObjectsFile objectsFile)
    {
        var cellOffsetX = CellX * WorldManager.CELL_SIZE;
        var cellOffsetY = CellY * WorldManager.CELL_SIZE;
        var brushCount = 0;
        const float MinBrushSize = 3f;

        foreach (var prefab in objectsFile.PrefabsList)
        {
            if (prefab is not ObjectDataType1Brush brush)
                continue;

            var posX = cellOffsetX + brush.Matrix3X4.M14;
            var posY = cellOffsetY + brush.Matrix3X4.M24;
            var posZ = brush.Matrix3X4.M34;

            var minX = posX + brush.StartPos.X;
            var maxX = posX + brush.EndPos.X;
            var minY = posY + brush.StartPos.Y;
            var maxY = posY + brush.EndPos.Y;
            var minZ = posZ + brush.StartPos.Z;
            var maxZ = posZ + brush.EndPos.Z;

            var sizeX = maxX - minX;
            var sizeY = maxY - minY;
            var sizeZ = maxZ - minZ;
            if (sizeX < MinBrushSize && sizeY < MinBrushSize)
                continue;
            if (sizeZ < 1f)
                continue;

            Template.AddBrushBounds(new BrushBounds
            {
                MinX = minX, MaxX = maxX,
                MinY = minY, MaxY = maxY,
                MinZ = minZ, MaxZ = maxZ
            });
            brushCount++;
        }

        if (brushCount > 0)
            Logger.Debug($"Loaded {brushCount} brush bounds from cell {CellX:000}_{CellY:000}");
    }
}
