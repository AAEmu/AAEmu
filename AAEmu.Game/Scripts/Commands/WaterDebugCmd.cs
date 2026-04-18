using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>GM debug for water zones loaded from client cell <c>object.dat</c>.</summary>
public sealed class WaterDebugCmd : ICommand
{
    private const float OceanTol = 0.25f;

    public string[] CommandNames { get; set; } = ["waterdebug", "water_debug"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "reload | info";
    }

    public string GetCommandHelpText()
    {
        return "Water zones from object.dat per cell.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " reload — rebuild Water.Areas from Loaded cells.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " info — snapshot at your position.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var world = character.ParentWorld;
        if (world == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No world instance.");
            return;
        }

        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "reload":
                world.ReloadWaterFromLoadedCells();
                CommandManager.SendNormalText(this, messageOutput,
                    $"Water reload: OceanLevel={world.Water.OceanLevel}, areas={world.Water.Areas.Count}");
                return;

            case "info":
                SendInfo(this, character, world, messageOutput);
                return;

            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }
    }

    private static void SendInfo(WaterDebugCmd cmd, Character character, WorldInstance world, IMessageOutput messageOutput)
    {
        var pos = character.Transform.World.Position;
        var pz = pos.Z;
        var w = world.Water;
        var template = world.Template;
        var oceanTpl = template.OceanLevel;

        CommandManager.SendNormalText(cmd, messageOutput,
            $"Water.OceanLevel={w.OceanLevel} (template {oceanTpl}), areas={w.Areas.Count}");
        CommandManager.SendNormalText(cmd, messageOutput,
            $"Pos ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");

        var isW = world.IsWater(pos, out var flow);
        var flowLen = flow.Length();
        CommandManager.SendNormalText(cmd, messageOutput,
            $"IsWater={isW}, flow=({flow.X:F3},{flow.Y:F3},{flow.Z:F3}), len={flowLen:F3}");

        var surfZ = w.GetWaterSurface(pos, out var flowSurf);
        CommandManager.SendNormalText(cmd, messageOutput,
            $"GetWaterSurface Z={surfZ:F3}, flowSurf=({flowSurf.X:F3},{flowSurf.Y:F3},{flowSurf.Z:F3})");

        var depthBelow = surfZ - pz;
        CommandManager.SendNormalText(cmd, messageOutput,
            $"depthBelowSurface (surface - pos.Z)={depthBelow:F3}, IsUnderWater={character.IsUnderWater}");

        var (cx, cy) = pos.ToCellIndex();
        if (cx < 0 || cx >= template.CellX || cy < 0 || cy >= template.CellY)
        {
            CommandManager.SendNormalText(cmd, messageOutput,
                $"Cell ({cx},{cy}) — outside template grid ({template.CellX}x{template.CellY} cells)");
        }
        else
        {
            var cell = template.Cells[cx, cy];
            cell.VerifyCellLoaded();
            var obj = cell.LoadedObjectDat != null;
            CommandManager.SendNormalText(cmd, messageOutput,
                $"Cell ({cx},{cy}) Loaded={cell.Loaded}, object.dat={(obj ? "yes" : "no")}");
            SendCellClientWaterDump(cmd, cell, messageOutput);
        }

        var px = pos.X;
        var py = pos.Y;
        var bboxHits = w.Areas.Count(a => a.BoundingBox.Contains(px, py));
        CommandManager.SendNormalText(cmd, messageOutput,
            $"Areas with XY inside rough bbox: {bboxHits}");

        CommandManager.SendNormalText(cmd, messageOutput,
            "Nearest 4 water bodies (short id, full checks):");

        var ranked = w.Areas
            .Select(a => (Area: a, DistSq: DistSqPointToRect(px, py, a.BoundingBox)))
            .OrderBy(x => x.DistSq)
            .Take(4)
            .ToList();

        var anyZoneSurface = w.Areas.Exists(a => a.GetSurface(pos, out _, out _));

        foreach (var item in ranked)
        {
            var a = item.Area;
            var dist = MathF.Sqrt(item.DistSq);
            var tag = a.AreaType == WaterBodyAreaType.Polygon ? "[P]" : "[L]";
            var shortName = ShortWaterName(a.Name);
            var bb = a.BoundingBox.Contains(px, py);
            var rwExtra = a.AreaType == WaterBodyAreaType.LineArray ? $" rw={a.RiverWidth:F1}" : "";

            if (!a.GetSurface(pos, out var sPt, out var fSurf))
            {
                CommandManager.SendNormalText(cmd, messageOutput,
                    $"{tag} id={a.Id} ~ {shortName} dist={dist:F2} bbox={bb}{rwExtra} XY-in-surface=False");
                continue;
            }

            var zMin = sPt.Z - a.Depth;
            var zMax = sPt.Z;
            var vertOk = pz <= zMax && pz >= zMin;
            var fLen = fSurf.Length();
            CommandManager.SendNormalText(cmd, messageOutput,
                $"{tag} id={a.Id} ~ {shortName} dist={dist:F2} bbox={bb}{rwExtra} surfZ={sPt.Z:F3} depth={a.Depth:F3} [zMin,zMax]=[{zMin:F3},{zMax:F3}] vertOk={vertOk} flowLen={fLen:F3}");
        }

        if (pz > oceanTpl && !isW)
        {
            var oceanish = MathF.Abs(surfZ - oceanTpl) < OceanTol;
            if (oceanish && !anyZoneSurface)
                CommandManager.SendNormalText(cmd, messageOutput,
                    "Hint: surface≈ocean — no zone gave XY in GetSurface (rivers/lakes missing or XY outside strips).");
            else if (!oceanish && anyZoneSurface)
                CommandManager.SendNormalText(cmd, messageOutput,
                    "Hint: XY hit a zone surface but Z outside water slab (check vertOk / depth).");
        }
    }

    /// <summary>
    /// Raw <see cref="ObjectDataType11Water"/> from client lists for this cell — types, counts, first vertex world resolve (same rules as ingest).
    /// </summary>
    private static void SendCellClientWaterDump(WaterDebugCmd cmd, WorldCell cell, IMessageOutput messageOutput)
    {
        var offset = cell.GetCellWorldOffset();
        void DumpList(string src, List<ObjectDataBase> list)
        {
            if (list == null || list.Count == 0)
                return;

            var n = 0;
            var river = 0;
            var area = 0;
            var ocean = 0;
            var sector = 0;
            foreach (var p in list)
            {
                if (p is not ObjectDataType11Water w)
                    continue;
                n++;
                switch (w.VolumeType)
                {
                    case WaterObjectVolumeType.River: river++; break;
                    case WaterObjectVolumeType.Area: area++; break;
                    case WaterObjectVolumeType.Ocean: ocean++; break;
                    case WaterObjectVolumeType.Sector: sector++; break;
                }
            }

            if (n == 0)
                return;

            CommandManager.SendNormalText(cmd, messageOutput,
                $"Cell client [{src}] water volumes={n} (River={river} Area={area} Ocean={ocean} Sector={sector})");

            var nonOcean = 0;
            foreach (var p in list)
            {
                if (p is ObjectDataType11Water w && w.VolumeType != WaterObjectVolumeType.Ocean)
                    nonOcean++;
            }

            if (nonOcean == 0)
            {
                if (ocean > 0)
                    CommandManager.SendNormalText(cmd, messageOutput,
                        $"  [{src}] detail skipped: only Ocean ({ocean}), server ignores Ocean.");
                return;
            }

            const int maxDetail = 5;
            var printed = 0;
            foreach (var p in list)
            {
                if (printed >= maxDetail)
                    break;
                if (p is not ObjectDataType11Water w || w.VolumeType == WaterObjectVolumeType.Ocean)
                    continue;

                printed++;
                var raw1 = w.ShapePointsList.Count > 0 ? w.ShapePointsList[0] : w.StartPos;
                var mode = GuessVertexMode(raw1);
                var wxy = DebugResolveWaterVertex(offset, raw1, w.SurfaceHeight);
                var ft = $"{w.VolumeType}, sh={w.ShapePointsList.Count} ct={w.PhysicsContourPointsList.Count}" +
                         $" depth={w.Depth:F1} surf={w.SurfaceHeight:F1}";
                CommandManager.SendNormalText(cmd, messageOutput,
                    $"  [{src} #{printed}/{nonOcean}] {ft} raw1=({raw1.X:F1},{raw1.Y:F1},{raw1.Z:F1}) {mode}->XY=({wxy.X:F1},{wxy.Y:F1})");
            }

            if (nonOcean > maxDetail)
                CommandManager.SendNormalText(cmd, messageOutput,
                    $"  … +{nonOcean - maxDetail} more River/Area/Sector in [{src}]");
        }

        DumpList("object", cell.LoadedObjectDat?.PrefabsList);
    }

    /// <summary>Same XY rule as water ingest (<see cref="WaterBodies"/>).</summary>
    private static Vector3 DebugResolveWaterVertex(Vector3 cellOffset, Vector3 filePoint, float surfaceZ)
    {
        const float localBand = WorldManager.CELL_SIZE * 2f;
        var xyCellLocal = filePoint.X <= localBand && filePoint.Y <= localBand &&
                          filePoint.X >= -512f && filePoint.Y >= -512f;
        var xy = xyCellLocal ? cellOffset + filePoint : filePoint;
        return xy with { Z = surfaceZ };
    }

    /// <summary>Matches ingest heuristic for debug labels only.</summary>
    private static string GuessVertexMode(Vector3 filePoint)
    {
        const float localBand = WorldManager.CELL_SIZE * 2f;
        var xyCellLocal = filePoint.X <= localBand && filePoint.Y <= localBand &&
                          filePoint.X >= -512f && filePoint.Y >= -512f;
        return xyCellLocal ? "cellLocal+offset" : "worldXY";
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
