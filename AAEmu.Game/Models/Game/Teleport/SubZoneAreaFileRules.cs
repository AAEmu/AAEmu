using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace AAEmu.Game.Models.Game.Teleport;

/// <summary>
/// Client <c>subzone_area.xml</c> under <c>level_design/zone/{zoneKey}/client/</c>.
/// <c>Area Id</c> is compact <c>sub_zones.id</c> — the same id <c>CSNotifySubZone</c> sends.
/// Polygon vertices are zone-local so they can be tested against <c>return_point.g</c> coords.
/// </summary>
public static class SubZoneAreaFileRules
{
    public readonly record struct LocalArea(uint ZoneKey, uint AreaId, IReadOnlyList<Vector2> Points);

    public static List<LocalArea> Parse(string xml, uint zoneKey)
    {
        var list = new List<LocalArea>();
        if (string.IsNullOrWhiteSpace(xml) || zoneKey == 0)
            return list;

        // Malformed XML throws so SubZoneAreaGCatalog can log the broken file path.
        var doc = XDocument.Parse(xml);

        foreach (var entity in doc.Root?.Elements("Entity") ?? [])
        {
            if (!TryReadEntityPos(entity, out var originX, out var originY))
                continue;

            var cellX = ReadIntAttr(entity, "cellX");
            var cellY = ReadIntAttr(entity, "cellY");
            var cellOffsetX = cellX * 1024f;
            var cellOffsetY = cellY * 1024f;

            foreach (var area in entity.Elements("Area"))
            {
                if (!uint.TryParse(area.Attribute("Id")?.Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var areaId) ||
                    areaId == 0)
                    continue;

                var points = new List<Vector2>();
                foreach (var point in area.Element("Points")?.Elements("Point") ?? [])
                {
                    if (!TryReadPosAttr(point.Attribute("Pos")?.Value, out var px, out var py, out _))
                        continue;
                    points.Add(new Vector2(
                        originX + cellOffsetX + px,
                        originY + cellOffsetY + py));
                }

                if (points.Count < 3)
                    continue;

                list.Add(new LocalArea(zoneKey, areaId, points));
            }
        }

        return list;
    }

    /// <summary>
    /// Even-odd point-in-polygon in zone-local XY (same test as <c>Point.IsInside</c>).
    /// </summary>
    public static bool Contains(IReadOnlyList<Vector2> polygon, float x, float y)
    {
        if (polygon == null || polygon.Count < 3)
            return false;

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];

            if (OnSegment(a, x, y, b))
                return true;

            if ((a.Y > y) != (b.Y > y) &&
                x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    public static IReadOnlyList<uint> ContainingAreaIds(
        float localX, float localY, IEnumerable<LocalArea> areasInZone)
    {
        if (areasInZone == null)
            return [];

        var ids = new List<uint>();
        foreach (var area in areasInZone)
        {
            if (Contains(area.Points, localX, localY) && !ids.Contains(area.AreaId))
                ids.Add(area.AreaId);
        }

        return ids;
    }

    private static bool TryReadEntityPos(XElement entity, out float x, out float y)
    {
        x = y = 0;
        return TryReadPosAttr(entity.Attribute("Pos")?.Value, out x, out y, out _);
    }

    private static bool TryReadPosAttr(string raw, out float x, out float y, out float z)
    {
        x = y = z = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var parts = raw.Split(',');
        if (parts.Length < 2)
            return false;
        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            return false;
        if (parts.Length >= 3)
            float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        return true;
    }

    private static int ReadIntAttr(XElement el, string name)
    {
        var raw = el.Attribute(name)?.Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static bool OnSegment(Vector2 a, float x, float y, Vector2 b)
    {
        if (MathF.Abs((b.Y - a.Y) * (x - a.X) - (b.X - a.X) * (y - a.Y)) > 1e-3f)
            return false;
        return x <= MathF.Max(a.X, b.X) + 1e-3f && x >= MathF.Min(a.X, b.X) - 1e-3f &&
               y <= MathF.Max(a.Y, b.Y) + 1e-3f && y >= MathF.Min(a.Y, b.Y) - 1e-3f;
    }
}
