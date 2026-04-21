using System.Drawing;
using System.Numerics;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.World.Debug;

public static class WaterDebugSnapshot
{
    public static string FormatOneLine(Vector3 pos, bool isWater, Vector3 flow)
    {
        return $"[waterprobe] Pos ({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) IsWater={isWater} flow=({flow.X:F3},{flow.Y:F3},{flow.Z:F3}) len={flow.Length():F3}";
    }

    public static IEnumerable<string> Capture(WorldInstance world, Vector3 pos, bool includeClientDump)
    {
        var w = world.Water;
        var template = world.Template;
        var oceanTpl = template.OceanLevel;
        List<WaterBodyArea> areasSnapshot;
        lock (w._lock)
        {
            areasSnapshot = [.. w.Areas];
        }

        yield return $"[waterprobe] Water.OceanLevel={w.OceanLevel} (template {oceanTpl}), areas={areasSnapshot.Count}";
        yield return $"[waterprobe] Pos ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})";

        var isW = world.IsWater(pos, out var flow);
        yield return $"[waterprobe] IsWater={isW}, flow=({flow.X:F3},{flow.Y:F3},{flow.Z:F3}), len={flow.Length():F3}";

        var surfZ = w.GetWaterSurface(pos, out var flowSurf);
        yield return $"[waterprobe] GetWaterSurface Z={surfZ:F3}, flowSurf=({flowSurf.X:F3},{flowSurf.Y:F3},{flowSurf.Z:F3})";
        yield return $"[waterprobe] depthBelowSurface (surface - pos.Z)={(surfZ - pos.Z):F3}";

        var (cx, cy) = pos.ToCellIndex();
        if (cx < 0 || cx >= template.CellX || cy < 0 || cy >= template.CellY)
        {
            yield return $"[waterprobe] Cell ({cx},{cy}) — outside template grid ({template.CellX}x{template.CellY} cells)";
        }
        else
        {
            var cell = template.Cells[cx, cy];
            var obj = cell.LoadedObjectDat != null;
            yield return $"[waterprobe] Cell ({cx},{cy}) Loaded={cell.Loaded}, object.dat={(obj ? "yes" : "no")}";
            if (includeClientDump)
            {
                foreach (var line in CaptureCellClientWaterDump(cell))
                    yield return line;
            }
        }

        var px = pos.X;
        var py = pos.Y;
        yield return $"[waterprobe] Areas with XY inside rough bbox: {areasSnapshot.Count(a => a.BoundingBox.Contains(px, py))}";
        yield return "[waterprobe] Nearest 4 water bodies (short id, full checks):";

        var ranked = areasSnapshot
            .Select(a => (Area: a, DistSq: DistSqPointToRect(px, py, a.BoundingBox)))
            .OrderBy(x => x.DistSq)
            .Take(4)
            .ToList();

        foreach (var item in ranked)
        {
            var a = item.Area;
            var dist = MathF.Sqrt(item.DistSq);
            var tag = a.AreaType == WaterBodyAreaType.Polygon ? "[P]" : "[L]";
            var shortName = ShortWaterName(a.Name);
            var bb = a.BoundingBox.Contains(px, py);
            var rwExtra = a.AreaType == WaterBodyAreaType.LineArray ? $" rw={a.RiverWidth:F1}" : "";
            var spExtra = a.FlowSpeedSigned != 0f ? $" sp={a.FlowSpeedSigned:F1}" : "";
            var axisExtra = a.FlowSpeedSigned != 0f ? $" axis=({a.FlowAxis.X:F2},{a.FlowAxis.Y:F2})" : "";

            var polyExtra = "";
            if (a.AreaType == WaterBodyAreaType.Polygon &&
                WaterBodies.TryGetRiverLikePolygonMetrics(a.Points, out var len, out var hw, out var meanW, out var areaSqm,
                    out var aspect, out _))
            {
                polyExtra = $" poly(len={len:F0} hw={hw:F0} meanW={meanW:F0} area={areaSqm:F0} asp={aspect:F1})";
            }

            if (!a.GetSurface(pos, out var sPt, out var fSurf))
            {
                yield return $"{tag} id={a.Id} ~ {shortName} dist={dist:F2} bbox={bb}{rwExtra}{spExtra}{axisExtra}{polyExtra} XY-in-surface=False";
                continue;
            }

            var zMin = sPt.Z - a.Depth;
            var zMax = sPt.Z;
            var vertOk = pos.Z <= zMax && pos.Z >= zMin;
            yield return $"{tag} id={a.Id} ~ {shortName} dist={dist:F2} bbox={bb}{rwExtra}{spExtra}{axisExtra}{polyExtra} surfZ={sPt.Z:F3} depth={a.Depth:F3} [zMin,zMax]=[{zMin:F3},{zMax:F3}] vertOk={vertOk} flowLen={fSurf.Length():F3}";
        }
    }

    public static string CaptureOneLine(WorldInstance world, Vector3 pos)
    {
        var isW = world.IsWater(pos, out var flow);
        return FormatOneLine(pos, isW, flow);
    }

    private static IEnumerable<string> CaptureCellClientWaterDump(WorldCell cell)
    {
        var list = cell.LoadedObjectDat?.PrefabsList;
        if (list == null || list.Count == 0)
            yield break;

        var n = 0;
        var river = 0;
        var area = 0;
        var ocean = 0;
        var sector = 0;
        foreach (var p in list)
        {
            if (p is not AAEmu.Game.Models.CryEngine.Objects.ObjectDataType11Water w)
                continue;
            n++;
            switch (w.VolumeType)
            {
                case AAEmu.Game.Models.CryEngine.Objects.WaterObjectVolumeType.River: river++; break;
                case AAEmu.Game.Models.CryEngine.Objects.WaterObjectVolumeType.Area: area++; break;
                case AAEmu.Game.Models.CryEngine.Objects.WaterObjectVolumeType.Ocean: ocean++; break;
                case AAEmu.Game.Models.CryEngine.Objects.WaterObjectVolumeType.Sector: sector++; break;
            }
        }

        if (n == 0)
            yield break;

        yield return $"[waterprobe] Cell client [object] water volumes={n} (River={river} Area={area} Ocean={ocean} Sector={sector})";
    }

    private static float DistSqPointToRect(float px, float py, RectangleF r)
    {
        var nx = Math.Clamp(px, r.Left, r.Right);
        var ny = Math.Clamp(py, r.Top, r.Bottom);
        var dx = px - nx;
        var dy = py - ny;
        return dx * dx + dy * dy;
    }

    private static string ShortWaterName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "?";
        var s = name;
        if (s.StartsWith("Segment_", StringComparison.Ordinal))
            s = s["Segment_".Length..];
        if (s.StartsWith("Water_", StringComparison.Ordinal))
            s = s["Water_".Length..];
        if (s.StartsWith("WaterContour_", StringComparison.Ordinal))
            s = s["WaterContour_".Length..];
        const int max = 48;
        return s.Length <= max ? s : s[..max] + "…";
    }
}
