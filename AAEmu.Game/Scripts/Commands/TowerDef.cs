using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
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
        return "Actions are: list, start, end, next";
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
                CommandManager.SendNormalText(this, messageOutput, $"Not implemented");
                break;
            case "start":
                if (!uint.TryParse(args[1], out var startId))
                {
                    return;
                }

                var startPacket = new SCTowerDefStartPacket(new TowerDefKey() { TowerDefId = startId, ZoneGroupId = 5 },
                    character.Transform.ZoneId);
                character.SendPacket(startPacket);
                break;
            case "end":
                if (!uint.TryParse(args[1], out var endId))
                {
                    return;
                }

                var endPacket = new SCTowerDefEndPacket(new TowerDefKey() { TowerDefId = endId, ZoneGroupId = 5 },
                    character.Transform.ZoneId);
                character.SendPacket(endPacket);
                break;
            case "next":
                if (!uint.TryParse(args[1], out var nextId))
                {
                    return;
                }

                if (!uint.TryParse(args[2], out var step))
                {
                    return;
                }

                var nextPacket = new SCTowerDefWaveStartPacket(
                    new TowerDefKey() { TowerDefId = nextId, ZoneGroupId = 5 }, character.Transform.ZoneId, step);
                character.SendPacket(nextPacket);
                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, $"Unknown tower defense action {args[0]}");
                break;
        }
    }
}
