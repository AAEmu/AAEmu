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

                        if (w.VolumeType == WaterObjectVolumeType.River)
                            likeRiver++;
                        if (w.VolumeType is WaterObjectVolumeType.Area or WaterObjectVolumeType.Ocean or WaterObjectVolumeType.Sector)
                            likeArea++;
                        if (w.Speed != 0f)
                            clientWithFlow++;
                    }
                }

                var areaSnapshot = world.Water.GetAreasSnapshot();
                var serverAreas = areaSnapshot.Count;
                var serverZonesWithFlow = areaSnapshot.Count(a => a.Speed != 0f);

                CommandManager.SendNormalText(this, messageOutput,
                    $"Water reload: OceanLevel={world.Water.OceanLevel}, areas={serverAreas}, loadedCells={loadedCells}");
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
}
