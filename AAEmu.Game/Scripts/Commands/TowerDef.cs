using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Drives the <c>tower_defs</c> timed world events (Kraken, Leviathan, the dragon invasions) by
/// hand, for testing outside their once-a-week slot.
/// </summary>
/// <remarks>
/// <c>start</c> and <c>end</c> go through the World scheduler, which sends the WZ packet that makes
/// the zone arm the event's spawner. The earlier version of this command only sent the SC banner,
/// so the client showed the announcement and nothing ever spawned.
/// Reachable over HTTP as <c>POST /api/commands/towerdef</c>.
/// </remarks>
public class TowerDef : ICommand
{
    public string[] CommandNames { get; set; } = ["towerdef", "tower_def"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<list|start|end|wave|ui> [towerDefId] [step]";
    }

    public string GetCommandHelpText()
    {
        return "list — every scheduled world event and whether it is running\n" +
               "start <id> — fire it now (zone arms the spawner)\n" +
               "end <id> — stop it now\n" +
               "wave <id> <step> — advance to a progression step\n" +
               "ui <id> — send only the client banner, no zone effect";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var action = args[0].ToLower();

        if (action == "list")
        {
            var lines = WorldIntegration.DescribeTowerDefs?.Invoke();
            if (lines == null)
            {
                CommandManager.SendErrorText(this, messageOutput, "Tower defense scheduler is not running");
                return;
            }

            foreach (var line in lines)
                CommandManager.SendNormalText(this, messageOutput, line);
            return;
        }

        if (args.Length < 2 || !uint.TryParse(args[1], out var towerDefId))
        {
            CommandManager.SendErrorText(this, messageOutput, $"{action} needs a towerDefId");
            return;
        }

        switch (action)
        {
            case "start":
            case "end":
            {
                var result = WorldIntegration.TriggerTowerDef?.Invoke(action, towerDefId, 0);
                CommandManager.SendNormalText(this, messageOutput,
                    result ?? "Tower defense scheduler is not running");
                break;
            }
            case "wave":
            {
                if (args.Length < 3 || !uint.TryParse(args[2], out var step))
                {
                    CommandManager.SendErrorText(this, messageOutput, "wave needs a step");
                    return;
                }

                var result = WorldIntegration.TriggerTowerDef?.Invoke("wave", towerDefId, step);
                CommandManager.SendNormalText(this, messageOutput,
                    result ?? "Tower defense scheduler is not running");
                break;
            }
            case "ui":
            {
                // Client banner only — checks the UI path without arming a spawner.
                character.SendPacket(new SCTowerDefStartPacket(
                    new TowerDefKey { TowerDefId = towerDefId, ZoneGroupId = 5 },
                    character.Transform.ZoneId));
                CommandManager.SendNormalText(this, messageOutput,
                    $"Sent SCTowerDefStart {towerDefId} to {character.Name} (no zone effect)");
                break;
            }
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown tower defense action {args[0]}");
                break;
        }
    }
}
