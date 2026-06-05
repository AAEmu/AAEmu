using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TowerDef : ICommand
{
    public string[] CommandNames { get; set; } = ["towerdef", "tower_def"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<action> <params>";
    }

    public string GetCommandHelpText()
    {
        return "Actions:\n" +
               "  list                                 list all known tower_defs\n" +
               "  start <towerDefId> [zoneGroupId]     start an event (zoneGroupId defaults to caller's zone)\n" +
               "  end <towerDefId>                     stop a running event\n" +
               "  next <towerDefId> <step>             debug: send raw wave-start packet for step\n" +
               "  goto <unitId>                        teleport to the first spawned NPC with this UnitId\n" +
               "  status                               list currently running events";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        switch (args[0].ToLower())
        {
            case "list":
                {
                    var all = TowerDefGameData.Instance.GetAllTowerDefs();
                    if (all.Count == 0)
                    {
                        CommandManager.SendNormalText(this, messageOutput, "No tower_defs loaded.");
                        return;
                    }
                    foreach (var def in all)
                    {
                        var running = TowerDefManager.Instance.IsRunning(def.Id) ? "RUNNING" : "idle";
                        CommandManager.SendNormalText(this, messageOutput,
                            $"  id={def.Id} progs={def.Progs?.Count ?? 0} forceEnd={def.ForceEndTime}s firstWave={def.FirstWaveAfter}s [{running}]");
                    }
                    return;
                }

            case "status":
                {
                    var any = false;
                    foreach (var def in TowerDefGameData.Instance.GetAllTowerDefs())
                    {
                        if (!TowerDefManager.Instance.IsRunning(def.Id))
                            continue;
                        any = true;
                        CommandManager.SendNormalText(this, messageOutput, $"  RUNNING: tower_def id={def.Id}");
                    }
                    if (!any)
                        CommandManager.SendNormalText(this, messageOutput, "No tower_def events running.");
                    return;
                }

            case "start":
                {
                    if (args.Length < 2 || !uint.TryParse(args[1], out var startId))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: towerdef start <towerDefId> [zoneGroupId]");
                        return;
                    }
                    var zoneGroupId = (ushort)((args.Length >= 3 && ushort.TryParse(args[2], out var zg))
                        ? zg
                        : character.Transform.ZoneId);

                    if (TowerDefManager.Instance.Start(startId, zoneGroupId, character.Transform.ZoneId))
                        CommandManager.SendNormalText(this, messageOutput, $"TowerDef {startId} started (zoneGroup={zoneGroupId}).");
                    else
                        CommandManager.SendErrorText(this, messageOutput, $"TowerDef {startId}: start failed (unknown id, no progs, or already running).");
                    return;
                }

            case "end":
                {
                    if (args.Length < 2 || !uint.TryParse(args[1], out var endId))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: towerdef end <towerDefId>");
                        return;
                    }
                    if (TowerDefManager.Instance.Stop(endId))
                        CommandManager.SendNormalText(this, messageOutput, $"TowerDef {endId} stopped.");
                    else
                        CommandManager.SendErrorText(this, messageOutput, $"TowerDef {endId}: not running.");
                    return;
                }

            case "next":
                {
                    // Raw debug: only sends the wave-start packet — does NOT advance manager state.
                    if (args.Length < 3 || !uint.TryParse(args[1], out var nextId) || !uint.TryParse(args[2], out var step))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: towerdef next <towerDefId> <step>");
                        return;
                    }
                    var zoneGroupId = (ushort)character.Transform.ZoneId;
                    character.SendPacket(new SCTowerDefWaveStartPacket(
                        new TowerDefKey { TowerDefId = nextId, ZoneGroupId = zoneGroupId },
                        character.Transform.ZoneId, step));
                    return;
                }

            case "goto":
                {
                    if (args.Length < 2 || !uint.TryParse(args[1], out var gotoUnitId))
                    {
                        CommandManager.SendErrorText(this, messageOutput, "Usage: towerdef goto <unitId>  (e.g. 13647 Nuia-Relikt, 13661 Harani-Relikt, 13675 Defense Flag)");
                        return;
                    }
                    var world = character.ParentWorld;
                    if (world == null)
                    {
                        CommandManager.SendErrorText(this, messageOutput, "No parent world.");
                        return;
                    }
                    Npc found = null;
                    foreach (var npc in world.GetAllNpcs())
                    {
                        if (npc?.TemplateId == gotoUnitId)
                        {
                            found = npc;
                            break;
                        }
                    }
                    if (found == null)
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"No live NPC with TemplateId={gotoUnitId} in this world. Make sure tower_def is running.");
                        return;
                    }
                    var pos = found.Transform.World.Position;
                    var regionId = found.Region?.Id;
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Teleporting to NPC {gotoUnitId} (ObjId={found.ObjId}) at ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) regionId={regionId} visible={found.IsVisible}");
                    character.ForceDismount();
                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(0, 0, pos.X, pos.Y, pos.Z + 2f, 0));
                    return;
                }

            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown tower defense action {args[0]}");
                break;
        }
    }
}
