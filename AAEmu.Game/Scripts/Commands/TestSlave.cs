using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestSlave : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "testslave", "test_slave" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Spawns a test slave";
        }

        public void Execute(Character character, string[] args)
        {
            var slave = new Slave();
            slave.Summoner = character;
            slave.TemplateId = 73;
            slave.ModelId = 1008;
            slave.ObjId = ObjectIdManager.Instance.GetNextId();
            slave.TlId = (ushort)TlIdManager.Instance.GetNextId();
            slave.Faction = FactionManager.Instance.GetFaction(143);
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
}
