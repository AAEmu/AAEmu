using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestMount : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("test_mount", this);
        }

        public void Execute(Character character, string[] args)
        {
            var mount = new Mount();
            mount.Template = NpcManager.Instance.GetTemplate(5431);
            mount.Faction = FactionManager.Instance.GetFaction(mount.Template.FactionId);
            mount.Name = "";
            mount.Master = character;
            mount.ModelId = mount.Template.ModelId;
            mount.ObjId = ObjectIdManager.Instance.GetNextId();
            mount.TlId = (ushort)TlIdManager.Instance.GetNextId();
            mount.Level = 1;
            mount.Position = character.Position.Clone();
            mount.Position.X += 5f; // spawn_x_offset
            mount.Position.Y += 5f; // spawn_Y_offset
            mount.MaxHp = mount.Hp = 5000;
            mount.ModelParams = new UnitCustomModelParams();

            mount.Spawn();
        }
    }
}
