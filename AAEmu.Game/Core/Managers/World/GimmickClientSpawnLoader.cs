using System.Globalization;
using System.Xml;

using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Builds gimmick spawners from the client's own level data instead of a hand-maintained
/// spawn file.
/// </summary>
/// <remarks>
/// Lifts are level entities, not database rows: the <c>gimmicks</c> table only describes
/// templated gimmicks (model, physics, skill) and a lift carries template id 0, so it has no row
/// there at all. Its placement, travel extents and dwell live in
/// <c>game/worlds/{world}/cells/{cell}/client/entities.xml</c>, which is also where the
/// <c>EntityGuid</c> comes from - and that GUID is the only thing
/// <c>X2::GameClient::ClientGimmick::InitStaticEntity</c> uses to bind the server object to the
/// client's entity. Reading the same file the client reads keeps the two in step across client
/// builds.
/// </remarks>
public static class GimmickClientSpawnLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Cell grid pitch in metres; a cell directory is named by its grid index.</summary>
    private const int CellSize = 1024;

    public static List<GimmickSpawner> Load(WorldInstance world)
    {
        var result = new List<GimmickSpawner>();
        var cellsDir = Path.Combine("game", "worlds", world.Template.Name, "cells");
        var files = ClientFileManager.GetFilesInDirectory(cellsDir, "entities.xml", true);
        if (files.Count <= 0)
        {
            Logger.Warn($"No client entities.xml found under {cellsDir} for world {world.Template.Name}");
            return result;
        }

        foreach (var fileName in files)
        {
            if (!TryGetCell(fileName, out var cellX, out var cellY))
                continue;

            var contents = ClientFileManager.GetFileAsString(fileName);
            if (string.IsNullOrWhiteSpace(contents))
                continue;

            // Cheap reject before paying for the XML parse; most cells hold no gimmicks at all.
            if (!contents.Contains("bGimmick=\"1\"", StringComparison.Ordinal))
                continue;

            try
            {
                ReadCell(world, contents, cellX, cellY, result);
            }
            catch (Exception ex)
            {
                // One malformed cell must not take the world down; the rest still load.
                Logger.Error(ex, $"Failed to read gimmicks from {fileName}");
            }
        }

        return result;
    }

    /// <summary>Cell indices come from the path: <c>cells/007_008/client/entities.xml</c>.</summary>
    private static bool TryGetCell(string fileName, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        foreach (var part in fileName.Replace('\\', '/').Split('/'))
        {
            if (part.Length != 7 || part[3] != '_')
                continue;
            if (int.TryParse(part[..3], NumberStyles.None, CultureInfo.InvariantCulture, out cellX) &&
                int.TryParse(part[4..], NumberStyles.None, CultureInfo.InvariantCulture, out cellY))
                return true;
        }

        return false;
    }

    private static void ReadCell(WorldInstance world, string contents, int cellX, int cellY,
        List<GimmickSpawner> result)
    {
        var doc = new XmlDocument();
        doc.LoadXml(contents);

        var entities = doc.SelectNodes("/Mission/Objects/Entity");
        if (entities == null)
            return;

        foreach (XmlNode entity in entities)
        {
            if (entity is not XmlElement element)
                continue;
            if (element.SelectSingleNode("Properties") is not XmlElement properties)
                continue;
            if (properties.GetAttribute("bGimmick") != "1")
                continue;

            var guidText = element.GetAttribute("EntityGuid");
            if (string.IsNullOrEmpty(guidText) ||
                !long.TryParse(guidText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var entityGuid))
                continue;

            if (!TryReadVector(element.GetAttribute("Pos"), out var px, out var py, out var pz))
                continue;

            var travel = ReadTravelExtents(element);
            if (travel.Count < 2)
            {
                // No MoveEntityTo destinations: a physicalized prop rather than a lift. Spawning it
                // would hand it the elevator handler with a zero travel span and drive it to Z 0.
                continue;
            }

            var spawner = new GimmickSpawner(world)
            {
                UnitId = 0, // level entities bind by EntityGuid, not by template
                EntityGuid = entityGuid,
                BottomZ = travel[0],
                TopZ = travel[^1],
                MiddleZ = travel.Count >= 3 ? travel[1] : 0f,
                WaitTime = ReadDwellSeconds(element),
                Scale = ReadScale(element),
                Position = new WorldSpawnPosition
                {
                    X = cellX * CellSize + px,
                    Y = cellY * CellSize + py,
                    Z = pz
                }
            };

            // Rotate is authored as "W,X,Y,Z".
            if (TryReadQuaternion(element.GetAttribute("Rotate"), out var rw, out var rx, out var ry, out var rz))
            {
                spawner.RotationX = rx;
                spawner.RotationY = ry;
                spawner.RotationZ = rz;
                spawner.RotationW = rw;
            }

            result.Add(spawner);
        }
    }

    /// <summary>Distinct destination heights of the flowgraph's movement nodes, ascending.</summary>
    private static List<float> ReadTravelExtents(XmlElement entity)
    {
        var heights = new List<float>();
        var inputs = entity.SelectNodes(".//Node[@Class='Movement:MoveEntityTo']/Inputs");
        if (inputs == null)
            return heights;

        foreach (XmlNode input in inputs)
        {
            if (input is not XmlElement inputElement)
                continue;
            if (!TryReadVector(inputElement.GetAttribute("Destination"), out _, out _, out var z))
                continue;
            if (!heights.Any(existing => Math.Abs(existing - z) < 0.01f))
                heights.Add(z);
        }

        heights.Sort();
        return heights;
    }

    /// <summary>Longest <c>Time:Delay</c> in the flowgraph - the dwell at each end of the run.</summary>
    private static float ReadDwellSeconds(XmlElement entity)
    {
        var dwell = 0f;
        var inputs = entity.SelectNodes(".//Node[@Class='Time:Delay']/Inputs");
        if (inputs == null)
            return dwell;

        foreach (XmlNode input in inputs)
        {
            if (input is not XmlElement inputElement)
                continue;
            if (float.TryParse(inputElement.GetAttribute("delay"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) && value > dwell)
                dwell = value;
        }

        return dwell;
    }

    private static float ReadScale(XmlElement entity)
    {
        var text = entity.GetAttribute("Scale");
        if (string.IsNullOrEmpty(text))
            return 1f;

        // Authored either uniform or per-axis; lifts are uniform, so the first component is enough.
        var first = text.Split(',')[0];
        return float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0f
            ? scale
            : 1f;
    }

    private static bool TryReadVector(string text, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (string.IsNullOrEmpty(text))
            return false;

        var parts = text.Split(',');
        return parts.Length == 3 &&
               float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }

    private static bool TryReadQuaternion(string text, out float w, out float x, out float y, out float z)
    {
        w = x = y = z = 0f;
        if (string.IsNullOrEmpty(text))
            return false;

        var parts = text.Split(',');
        return parts.Length == 4 &&
               float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out w) &&
               float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
               float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }
}
