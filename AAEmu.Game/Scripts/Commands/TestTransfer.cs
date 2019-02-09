using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestTransfer : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("test_transter", this);
        }

        public void Execute(Character character, string[] args)
        {
            var transfer = new Transfer();
            transfer.TemplateId = 83;
            transfer.ModelId = 657;
            transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            transfer.Level = 50;
            transfer.Position = character.Position.Clone();
            transfer.Position.X += 5f; // spawn_x_offset
            transfer.Position.Y += 5f; // spawn_Y_offset
            transfer.MaxHp = transfer.Hp = 5000;
            transfer.ModelParams = new UnitCustomModelParams();
            
            transfer.Spawn();
        }
    }
}
