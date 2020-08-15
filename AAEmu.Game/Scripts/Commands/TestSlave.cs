using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Skills;

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
            slave.TemplateId = 52;
            slave.ModelId = 657;
            slave.Template = SlaveManager.Instance.GetSlaveTemplate(slave.TemplateId);
            slave.ObjId = ObjectIdManager.Instance.GetNextId();
            slave.TlId = (ushort)TlIdManager.Instance.GetNextId();
            slave.Faction = FactionManager.Instance.GetFaction(143);
            slave.Level = 1;
            slave.Position = character.Position.Clone();
            slave.Position.X += 5f; // spawn_x_offset
            slave.Position.Y += 5f; // spawn_Y_offset
            slave.Position.Z += 100f; // spawn_Z_offset
            slave.Hp = slave.MaxHp;
            slave.Mp = slave.MaxMp;
            slave.ModelParams = new UnitCustomModelParams();
            slave.Effects.AddEffect(new Effect(slave, slave, SkillCaster.GetByType(EffectOriginType.Skill), SkillManager.Instance.GetBuffTemplate(545), null, DateTime.Now));
            slave.Spawn();
        }
    }
}
