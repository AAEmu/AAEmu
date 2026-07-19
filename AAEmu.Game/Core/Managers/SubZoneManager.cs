using System.Globalization;
using System.Numerics;
using System.Xml;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.XML;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class SubZoneManager(IWorldManager worldManager, IZoneManager zoneManager) : Singleton<SubZoneManager>, ISubZoneManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Load()
    {
        #region LoadClientData

        var worldTemplates = worldManager.GetAllWorldTemplates();
        if (worldTemplates == null || worldTemplates.Length == 0)
        {
            return;
        }
        foreach (var worldTemplate in worldTemplates)
        {
            var zonesList = worldManager.GetZoneKeysByWorldId(worldTemplate.Id);

            foreach (var zoneKey in zonesList)
            {
                var zone = zoneManager.GetZoneByKey(zoneKey);
                if (zone is null)
                {
                    // Not done loading, or conflicting zone data?
                    var zoneName = string.Empty;
                    if (worldTemplate.XmlWorldZones.TryGetValue(zoneKey, out var xmlZone))
                    {
                        zoneName = xmlZone.Name;
                    }
                    Logger.Debug($"XML ZoneKey {zoneKey} ({zoneName}) in {worldTemplate.Name} does not exist in database (unused area)");
                    continue;
                }
                #region subzone

                var worldLevelDesignDir = Path.Combine("game", "worlds", worldTemplate.Name, "level_design", "zone", zone.ZoneKey.ToString(), "client");
                var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "subzone_area.xml", true);

                foreach (var pathFileName in pathFiles)
                {
                    var contents = ClientFileManager.GetFileAsString(pathFileName);
                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        Logger.Warn($"{pathFileName} doesn't exists or is empty.");
                    }
                    else
                    {
                        var xmlDoc = new XmlDocument();
                        xmlDoc.LoadXml(contents);
                        var allSubzoneBlocks = xmlDoc.SelectNodes("/Objects/Entity");
                        for (var i = 0; i < allSubzoneBlocks.Count; i++)
                        {
                            var block = allSubzoneBlocks[i];
                            var entityAttribs = XmlHelper.ReadNodeAttributes(block);

                            if (entityAttribs.TryGetValue("Name", out var blockName))
                            {
                                var cellXOffset = 0;
                                var cellYOffset = 0;
                                var template = new Area { Name = blockName };

                                if (entityAttribs.TryGetValue("cellX", out var cellXOffsetString))
                                {
                                    try { cellXOffset = int.Parse(cellXOffsetString); }
                                    catch { cellXOffset = 0; }
                                }

                                if (entityAttribs.TryGetValue("cellY", out var cellYOffsetString))
                                {
                                    try { cellYOffset = int.Parse(cellYOffsetString); }
                                    catch { cellYOffset = 0; }
                                }

                                var areaNodes = block.SelectNodes("Area");

                                for (var j = 0; j < areaNodes.Count; j++)
                                {
                                    var areaNode = areaNodes[j];
                                    var areaAttribs = XmlHelper.ReadNodeAttributes(areaNode);
                                    var startVector = new Vector3();

                                    // GET ID
                                    if (areaAttribs.TryGetValue("Id", out var id))
                                    {
                                        template.Id = uint.Parse(id);
                                    }

                                    // POS
                                    if (entityAttribs.TryGetValue("Pos", out var valPos))
                                    {
                                        var posVals = valPos.Split(',');
                                        if (posVals.Length != 3)
                                        {
                                            continue;
                                        }
                                        try
                                        {
                                            startVector = new Vector3(float.Parse(posVals[0], CultureInfo.InvariantCulture), float.Parse(posVals[1], CultureInfo.InvariantCulture), float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                        }
                                        catch
                                        {
                                            Logger.Debug("Invalid float inside Pos: " + valPos);
                                        }
                                    }

                                    var worldOrigins = ZoneManager.Instance.GetZoneOriginCell(zone.ZoneKey);

                                    var cellOffset = new Vector3 { X = (worldOrigins.X + cellXOffset) * 1024f, Y = (worldOrigins.Y + cellYOffset) * 1024f };

                                    var pointsXml = areaNode.SelectNodes("Points/Point");
                                    for (var n = 0; n < pointsXml.Count; n++)
                                    {
                                        var pointXml = pointsXml[n];
                                        var pointAttribs = XmlHelper.ReadNodeAttributes(pointXml);
                                        if (pointAttribs.TryGetValue("Pos", out var posString))
                                        {
                                            var posVals = posString.Split(',');
                                            if (posVals.Length != 3)
                                            {
                                                Logger.Debug("Invalid number of values inside Pos: " + posString);
                                                continue;
                                            }
                                            try
                                            {
                                                var vec = new Vector3(float.Parse(posVals[0], CultureInfo.InvariantCulture) + cellOffset.X, float.Parse(posVals[1], CultureInfo.InvariantCulture) + cellOffset.Y, float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                                vec.X += startVector.X;
                                                vec.Y += startVector.Y;
                                                vec.Z += startVector.Z;

                                                template.Points.Add(vec);
                                            }
                                            catch
                                            {
                                                Logger.Debug("Invalid float inside Pos: " + posString);
                                            }
                                        }
                                    }

                                    if (!worldTemplate.SubZones.TryGetValue(zone.Id, out var value))
                                    {
                                        value = [];
                                        worldTemplate.SubZones.Add(zone.Id, value);
                                    }

                                    value.Add(template);
                                }
                            }
                        }
                    }
                }

                #endregion subzone

                #region housing_area

                worldLevelDesignDir = Path.Combine("game", "worlds", worldTemplate.Name, "level_design", "zone", zone.ZoneKey.ToString(), "client");
                pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "housing_area.xml", true);

                foreach (var pathFileName in pathFiles)
                {
                    var contents = ClientFileManager.GetFileAsString(pathFileName);

                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        Logger.Warn($"{pathFileName} doesn't exists or is empty.");
                    }
                    else
                    {
                        var xmlDoc = new XmlDocument();
                        xmlDoc.LoadXml(contents);
                        var allSubzoneBlocks = xmlDoc.SelectNodes("/Objects/Entity");
                        for (var i = 0; i < allSubzoneBlocks.Count; i++)
                        {
                            var block = allSubzoneBlocks[i];
                            var entityAttribs = XmlHelper.ReadNodeAttributes(block);

                            if (entityAttribs.TryGetValue("Name", out var blockName))
                            {
                                var cellXOffset = 0;
                                var cellYOffset = 0;

                                var template = new Area { Name = blockName };

                                if (entityAttribs.TryGetValue("cellX", out var cellXOffsetString))
                                {
                                    try { cellXOffset = int.Parse(cellXOffsetString); }
                                    catch { cellXOffset = 0; }
                                }

                                if (entityAttribs.TryGetValue("cellY", out var cellYOffsetString))
                                {
                                    try { cellYOffset = int.Parse(cellYOffsetString); }
                                    catch { cellYOffset = 0; }
                                }

                                var areaNodes = block.SelectNodes("Area");

                                for (var j = 0; j < areaNodes.Count; j++)
                                {
                                    var areaNode = areaNodes[j];
                                    var areaAttribs = XmlHelper.ReadNodeAttributes(areaNode);
                                    var startVector = Vector3.Zero;

                                    //GET ID
                                    if (areaAttribs.TryGetValue("Id", out var id))
                                    {
                                        template.Id = uint.Parse(id);
                                    }

                                    //POS
                                    if (entityAttribs.TryGetValue("Pos", out var valPos))
                                    {
                                        var posVals = valPos.Split(',');
                                        if (posVals.Length != 3)
                                        {
                                            continue;
                                        }
                                        try
                                        {
                                            startVector = new Vector3(float.Parse(posVals[0], CultureInfo.InvariantCulture), float.Parse(posVals[1], CultureInfo.InvariantCulture), float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                        }
                                        catch
                                        {
                                            Logger.Debug("Invalid float inside Pos: " + valPos);
                                        }
                                    }

                                    var worldOrigins = ZoneManager.Instance.GetZoneOriginCell(zone.ZoneKey);

                                    var cellOffset = new Vector3 { X = (worldOrigins.X + cellXOffset) * 1024f, Y = (worldOrigins.Y + cellYOffset) * 1024f };

                                    var pointsXml = areaNode.SelectNodes("Points/Point");
                                    for (var n = 0; n < pointsXml.Count; n++)
                                    {
                                        var pointXml = pointsXml[n];
                                        var pointAttribs = XmlHelper.ReadNodeAttributes(pointXml);
                                        if (pointAttribs.TryGetValue("Pos", out var posString))
                                        {
                                            var posVals = posString.Split(',');
                                            if (posVals.Length != 3)
                                            {
                                                Logger.Debug("Invalid number of values inside Pos: " + posString);
                                                continue;
                                            }
                                            try
                                            {
                                                var vec = new Vector3(float.Parse(posVals[0], CultureInfo.InvariantCulture) + cellOffset.X, float.Parse(posVals[1], CultureInfo.InvariantCulture) + cellOffset.Y, float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                                vec.X += startVector.X;
                                                vec.Y += startVector.Y;
                                                vec.Z += startVector.Z;
                                                template.Points.Add(vec);
                                            }
                                            catch
                                            {
                                                Logger.Debug("Invalid float inside Pos: " + posString);
                                            }

                                        }
                                    }

                                    if (!worldTemplate.HousingZones.TryGetValue(zone.Id, out var value))
                                    {
                                        value = [];
                                        worldTemplate.HousingZones.Add(zone.Id, value);
                                    }

                                    value.Add(template);
                                }
                            }
                        }
                    }
                }

                #endregion housing_area
            }
        }

        #endregion
    }

    public List<uint> GetHousingZoneByPosition(WorldInstance world, float x, float y)
    {
        var zoneId = worldManager.GetZoneId(world.Template, x, y);

        var foundHousingZones = new List<uint>();

        var found = false;

        foreach (var houseZoneTemplate in world.Template.HousingZones[zoneId])
        {
            if (Point.IsInside(houseZoneTemplate.Points, houseZoneTemplate.Points.Count, new Vector3(x, y, 0)))
            {
                Logger.Debug($"Is in zone {zoneId} housezone name {houseZoneTemplate.Name} ({houseZoneTemplate.Id})");
                found = true;

                foundHousingZones.Add(houseZoneTemplate.Id);
            }
        }

        if (found)
        {
            return foundHousingZones;
        }
        else
        {
            Logger.Debug("No housing zone found at this position!");
            return [];
        }

    }

    public List<uint> GetSubZoneByPosition(WorldTemplate worldTemplate, Vector3 pos)
    {
        return GetSubZoneByPosition(worldTemplate, pos.X, pos.Y);
    }

    public List<uint> GetSubZoneByPosition(WorldTemplate worldTemplate, float x, float y)
    {
        var zoneKey = worldManager.GetZoneId(worldTemplate, x, y);
        var foundSubzones = new List<uint>();

        var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
        if (zone is null)
        {
            return foundSubzones;
        }

        var found = false;
        if (worldTemplate.SubZones.TryGetValue(zone.Id, out var subZoneList))
        {
            foreach (var subzoneTemplate in subZoneList)
            {
                if (Point.IsInside(subzoneTemplate.Points, subzoneTemplate.Points.Count, new Vector3(x, y, 0)))
                {
                    //Logger.Debug("Is in zone {0} in subzone {1} subzone name {2}", zoneId, subzoneTemplate.Id, subzoneTemplate.Name);
                    found = true;

                    foundSubzones.Add(subzoneTemplate.Id);
                }
            }
        }

        if (!found)
            Logger.Debug($"No subzone found at this position! WorldId: {worldTemplate.Id}, Pos: {x} , {y}");
        return foundSubzones;
    }

}
