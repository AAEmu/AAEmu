using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestSlave : ICommand
{
    public string[] CommandNames { get; set; } = ["testslave", "test_slave"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Spawns a test slave";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var slave = new Slave { Summoner = character, TemplateId = 73, ModelId = 1008, ObjId = ObjectIdManager.Instance.GetNextId(),
            TlId = (ushort)TlIdManager.Instance.GetNextId(),
            Faction = FactionManager.Instance.GetFaction((FactionsEnum)143),
            Level = 50
        };
        slave.Transform = character.Transform.CloneDetached(slave);
        slave.Transform.Local.AddDistanceToFront(5f);
        slave.Hp = slave.MaxHp = 190000;
        slave.Faction = character.Faction;
        slave.Template = SlaveGameData.Instance.GetSlaveTemplate(slave.TemplateId);

        slave.Spawn();
    }
}
