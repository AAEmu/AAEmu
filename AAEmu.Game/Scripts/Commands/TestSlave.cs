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
            slave.TemplateId = 6;
            slave.ModelId = 654;
            slave.ObjId = ObjectIdManager.Instance.GetNextId();
            slave.TlId = (ushort)TlIdManager.Instance.GetNextId();
            slave.Faction = FactionManager.Instance.GetFaction(143);
            slave.Level = 50;
            slave.Position = character.Position.Clone();
            slave.Position.X += 5f; // spawn_x_offset
            slave.Position.Y += 5f; // spawn_Y_offset
            slave.MaxHp = slave.Hp = 5000;
            slave.ModelParams = new UnitCustomModelParams();
            
            slave.Spawn();
        }
    }
}
