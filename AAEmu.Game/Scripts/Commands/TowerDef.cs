using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands
{
    public class TowerDef : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register(new []{"tower_def", "td", "towerdef"}, this);
        }

        public string GetCommandLineHelp()
        {
            return "<action> <params>";
        }

        public string GetCommandHelpText()
        {
            return "Actions are: list, start, end, next";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Tower Defense] Usage: " + CommandManager.CommandPrefix + "tower_def <action> <params>");
                return;
            }

            switch (args[0])
            {
                case "list":
                    break;
                case "start":
                    if (!uint.TryParse(args[1], out var startId))
                        return;
                    var startPacket = new SCTowerDefStartPacket(new TowerDefKey()
                    {
                        TowerDefId = startId,
                        ZoneGroupId = 5
                    }, character.Position.ZoneId);
                    character.SendPacket(startPacket);
                    break;
                case "end":
                    if (!uint.TryParse(args[1], out var endId))
                        return;
                    var endPacket = new SCTowerDefEndPacket(new TowerDefKey()
                    {
                        TowerDefId = endId,
                        ZoneGroupId = 5
                    }, character.Position.ZoneId);
                    character.SendPacket(endPacket);
                    break;
                case "next":
                    if (!uint.TryParse(args[1], out var nextId))
                        return;
                    if (!uint.TryParse(args[2], out var step))
                        return;
                    var nextPacket = new SCTowerDefWaveStartPacket(new TowerDefKey()
                    {
                        TowerDefId = nextId,
                        ZoneGroupId = 5
                    }, character.Position.ZoneId, step);
                    character.SendPacket(nextPacket);
                    break;
            }
        }
    }
}
