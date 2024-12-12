using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

public class SubZoneManager : Singleton<SubZoneManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Load()
    {
        #region LoadClientData

        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            var zonesList = WorldManager.Instance.GetZonesByWorldId(world.Id);

            foreach (var zoneId in zonesList)
            {
                #region subzone

                var worldLevelDesignDir = Path.Combine("game", "worlds", world.Name, "level_design", "zone", zoneId.ToString(), "client");
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
                        var _doc = new XmlDocument();
                        _doc.LoadXml(contents);
                        var _allSubzoneBlocks = _doc.SelectNodes("/Objects/Entity");
                        for (var i = 0; i < _allSubzoneBlocks.Count; i++)
                        {
                            var block = _allSubzoneBlocks[i];
                            var entityAttribs = XmlHelper.ReadNodeAttributes(block);

                            if (entityAttribs.TryGetValue("Name", out var blockName))
                            {
                                var cellXOffset = 0;
                                var cellYOffset = 0;
                                var template = new Area();
                                template.Name = blockName;

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
                                    var startVector = new Point();

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
                                            startVector = new Point(float.Parse(posVals[0], CultureInfo.InvariantCulture), float.Parse(posVals[1], CultureInfo.InvariantCulture), float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                        }
                                        catch
                                        {
                                            Logger.Debug("Invalid float inside Pos: " + valPos);
                                        }
                                    }

                                    var worldOrigins = ZoneManager.GetZoneOriginCell(zoneId);

                                    var cellOffset = new Point();
                                    cellOffset.X = (worldOrigins.X + cellXOffset) * 1024f;
                                    cellOffset.Y = (worldOrigins.Y + cellYOffset) * 1024f;

                                    var pointsxml = areaNode.SelectNodes("Points/Point");
                                    for (var n = 0; n < pointsxml.Count; n++)
                                    {
                                        var pointxml = pointsxml[n];
                                        var pointattribs = XmlHelper.ReadNodeAttributes(pointxml);
                                        if (pointattribs.TryGetValue("Pos", out var posString))
                                        {
                                            var posVals = posString.Split(',');
                                            if (posVals.Length != 3)
                                            {
                                                Logger.Debug("Invalid number of values inside Pos: " + posString);
                                                continue;
                                            }
                                            try
                                            {
                                                var vec = new Point(float.Parse(posVals[0], CultureInfo.InvariantCulture) + cellOffset.X, float.Parse(posVals[1], CultureInfo.InvariantCulture) + cellOffset.Y, float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                                vec.X += startVector.X;
                                                vec.Y += startVector.Y;
                                                vec.Z += startVector.Z;

                                                template._points.Add(vec);
                                            }
                                            catch
                                            {
                                                Logger.Debug("Invalid float inside Pos: " + posString);
                                            }
                                        }
                                    }

                                    if (!world.SubZones.TryGetValue(zoneId, out var value))
                                    {
                                        value = [];
                                        world.SubZones.Add(zoneId, value);
                                    }

                                    value.Add(template);
                                }
                            }
                        }
                    }
                }

                #endregion subzone

                #region housing_area

                worldLevelDesignDir = Path.Combine("game", "worlds", world.Name, "level_design", "zone", zoneId.ToString(), "client");
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
                        var _doc = new XmlDocument();
                        _doc.LoadXml(contents);
                        var _allSubzoneBlocks = _doc.SelectNodes("/Objects/Entity");
                        for (var i = 0; i < _allSubzoneBlocks.Count; i++)
                        {
                            var block = _allSubzoneBlocks[i];
                            var entityAttribs = XmlHelper.ReadNodeAttributes(block);

                            if (entityAttribs.TryGetValue("Name", out var blockName))
                            {
                                var cellXOffset = 0;
                                var cellYOffset = 0;

                                var template = new Area();
                                template.Name = blockName;

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
                                    var startVector = new Point();

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
                                            startVector = new Point(float.Parse(posVals[0], CultureInfo.InvariantCulture), float.Parse(posVals[1], CultureInfo.InvariantCulture), float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                        }
                                        catch
                                        {
                                            Logger.Debug("Invalid float inside Pos: " + valPos);
                                        }
                                    }

                                    var worldOrigins = ZoneManager.GetZoneOriginCell(zoneId);

                                    var cellOffset = new Point();
                                    cellOffset.X = (worldOrigins.X + cellXOffset) * 1024f;
                                    cellOffset.Y = (worldOrigins.Y + cellYOffset) * 1024f;

                                    var pointsxml = areaNode.SelectNodes("Points/Point");
                                    for (var n = 0; n < pointsxml.Count; n++)
                                    {
                                        var pointxml = pointsxml[n];
                                        var pointattribs = XmlHelper.ReadNodeAttributes(pointxml);
                                        if (pointattribs.TryGetValue("Pos", out var posString))
                                        {
                                            var posVals = posString.Split(',');
                                            if (posVals.Length != 3)
                                            {
                                                Logger.Debug("Invalid number of values inside Pos: " + posString);
                                                continue;
                                            }
                                            try
                                            {
                                                var vec = new Point(float.Parse(posVals[0], CultureInfo.InvariantCulture) + cellOffset.X, float.Parse(posVals[1], CultureInfo.InvariantCulture) + cellOffset.Y, float.Parse(posVals[2], CultureInfo.InvariantCulture));
                                                vec.X += startVector.X;
                                                vec.Y += startVector.Y;
                                                vec.Z += startVector.Z;
                                                template._points.Add(vec);
                                            }
                                            catch
                                            {
                                                Logger.Debug("Invalid float inside Pos: " + posString);
                                            }

                                        }
                                    }

                                    if (!world.HousingZones.TryGetValue(zoneId, out var value))
                                    {
                                        value = [];
                                        world.HousingZones.Add(zoneId, value);
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

    public List<uint> GetHousingZoneByPosition(uint worldId, float x, float y)
    {
        var zoneId = WorldManager.Instance.GetZoneId(worldId, x, y);

        var world = WorldManager.Instance.GetWorld(worldId);

        var foundHousingzones = new List<uint>();

        var found = false;

        foreach (var housezoneTemplate in world.HousingZones[zoneId])
        {
            if (Point.IsInside(housezoneTemplate._points, housezoneTemplate._points.Count, new Point(x, y, 0)))
            {
                Logger.Debug("Is in zone {0} housezone name {2}", zoneId, housezoneTemplate.Id, housezoneTemplate.Name);
                found = true;

                foundHousingzones.Add(housezoneTemplate.Id);
            }
        }

        if (found)
        {
            return foundHousingzones;
        }
        else
        {
            Logger.Debug("No housing zone found at this position!");
            return [];
        }

    }

    public List<uint> GetSubZoneByPosition(uint worldId, Vector3 pos)
    {
        return GetSubZoneByPosition(worldId, pos.X, pos.Y);
    }

    public List<uint> GetSubZoneByPosition(uint worldId, float x, float y)
    {
        var zoneId = WorldManager.Instance.GetZoneId(worldId, x, y);

        var world = WorldManager.Instance.GetWorld(worldId);

        var foundSubzones = new List<uint>();

        var found = false;
        if (world.SubZones.TryGetValue(zoneId, out var subZoneList))
        {
            foreach (var subzoneTemplate in subZoneList)
            {
                if (Point.IsInside(subzoneTemplate._points, subzoneTemplate._points.Count, new Point(x, y, 0)))
                {
                    //Logger.Debug("Is in zone {0} in subzone {1} subzone name {2}", zoneId, subzoneTemplate.Id, subzoneTemplate.Name);
                    found = true;

                    foundSubzones.Add(subzoneTemplate.Id);
                }
            }
        }

        if (!found)
            Logger.Debug($"No subzone found at this position! WorldId: {worldId}, Pos: {x} , {y}");
        return foundSubzones;
    }

}
