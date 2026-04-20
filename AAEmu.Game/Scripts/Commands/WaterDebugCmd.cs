using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.World.Debug;
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
        return "reload | info [x y z] | reloadinfo [x y z] | setprobe x y z | probeoff";
    }

    public string GetCommandHelpText()
    {
        return "Water zones from object.dat per cell.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " reload — rebuild Water.Areas from Loaded cells.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " info [x y z] — snapshot at your position or coordinates.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " reloadinfo [x y z] — reload then info.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " setprobe x y z — enable autoprobe logging.\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " probeoff — disable autoprobe logging.";
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
                var loadedCells = 0;
                var clientVolumes = 0;
                var river = 0;
                var area = 0;
                var ocean = 0;
                var sector = 0;
                var likeRiver = 0;
                var likeArea = 0;
                var clientWithFlow = 0;

                var template = world.Template;
                for (var cy = 0; cy < template.CellY; cy++)
                for (var cx = 0; cx < template.CellX; cx++)
                {
                    var cell = template.Cells[cx, cy];
                    if (!cell.Loaded)
                        continue;
                    loadedCells++;

                    var list = cell.LoadedObjectDat?.PrefabsList;
                    if (list == null || list.Count == 0)
                        continue;

                    foreach (var p in list)
                    {
                        if (p is not ObjectDataType11Water w)
                            continue;

                        clientVolumes++;
                        switch (w.VolumeType)
                        {
                            case WaterObjectVolumeType.River: river++; break;
                            case WaterObjectVolumeType.Area: area++; break;
                            case WaterObjectVolumeType.Ocean: ocean++; break;
                            case WaterObjectVolumeType.Sector: sector++; break;
                        }

                        if (w.VolumeType is WaterObjectVolumeType.River or WaterObjectVolumeType.Ocean or WaterObjectVolumeType.Sector)
                            likeRiver++;
                        if (w.VolumeType is WaterObjectVolumeType.Area or WaterObjectVolumeType.Ocean or WaterObjectVolumeType.Sector)
                            likeArea++;
                        if (w.Speed != 0f)
                            clientWithFlow++;
                    }
                }

                var serverZonesWithFlow = world.Water.Areas.Count(a => a.Speed != 0f);

                CommandManager.SendNormalText(this, messageOutput,
                    $"Water reload: OceanLevel={world.Water.OceanLevel}, areas={world.Water.Areas.Count}, loadedCells={loadedCells}");
                CommandManager.SendNormalText(this, messageOutput,
                    $"Client water volumes={clientVolumes} (River={river} Area={area} Ocean={ocean} Sector={sector}), likeRiver={likeRiver}, likeArea={likeArea}, withFlow={clientWithFlow}");
                CommandManager.SendNormalText(this, messageOutput,
                    $"Server ingested zones with flow (Speed!=0): {serverZonesWithFlow}");
                return;

            case "info":
                SendInfo(this, character, world, args, messageOutput);
                return;

            case "reloadinfo":
                world.ReloadWaterFromLoadedCells();
                SendInfo(this, character, world, args, messageOutput);
                return;

            case "setprobe":
                if (!TryParseXYZ(args, 1, out var probePos))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Usage: setprobe x y z");
                    return;
                }
                WorldManager.AutoWaterProbeEnable(probePos);
                CommandManager.SendNormalText(this, messageOutput, $"AutoWaterProbe enabled at ({probePos.X:F1},{probePos.Y:F1},{probePos.Z:F1})");
                return;

            case "probeoff":
                WorldManager.AutoWaterProbeDisable();
                CommandManager.SendNormalText(this, messageOutput, "AutoWaterProbe disabled.");
                return;

            default:
                CommandManager.SendDefaultHelpText(this, messageOutput);
                return;
        }
    }

    private static void SendInfo(WaterDebugCmd cmd, Character character, WorldInstance world, string[] args, IMessageOutput messageOutput)
    {
        var pos = character.Transform.World.Position;
        if (TryParseXYZ(args, 1, out var parsed))
            pos = parsed;

        foreach (var line in WaterDebugSnapshot.Capture(world, pos, includeClientDump: true))
            CommandManager.SendNormalText(cmd, messageOutput, line);
    }

    private static bool TryParseXYZ(string[] args, int startIdx, out Vector3 pos)
    {
        pos = Vector3.Zero;
        if (args.Length < startIdx + 3)
            return false;
        if (!float.TryParse(args[startIdx], out var x))
            return false;
        if (!float.TryParse(args[startIdx + 1], out var y))
            return false;
        if (!float.TryParse(args[startIdx + 2], out var z))
            return false;
        pos = new Vector3(x, y, z);
        return true;
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
