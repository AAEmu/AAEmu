using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
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
        var slave = new Slave();
        slave.Summoner = character;
        slave.TemplateId = 73;
        slave.ModelId = 1008;
        slave.ObjId = ObjectIdManager.Instance.GetNextId();
        slave.TlId = (ushort)TlIdManager.Instance.GetNextId();
        slave.Faction = FactionManager.Instance.GetFaction((FactionsEnum)143);
        slave.Level = 50;
        slave.Transform = character.Transform.CloneDetached(slave);
        slave.Transform.Local.AddDistanceToFront(5f);
        slave.Hp = slave.MaxHp = 190000;
        slave.Faction = character.Faction;
        slave.ModelParams = new UnitCustomModelParams();
        slave.Template = SlaveManager.Instance.GetSlaveTemplate(slave.TemplateId);

        slave.Spawn();
    }
}
