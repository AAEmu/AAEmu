using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.Units
{
    public interface IUnit : IBaseUnit
    {
        byte Level { get; set; }
        BaseUnit CurrentTarget { get; }
        ItemContainer Equipment { get; set; }
        void SendPacket(GamePacket packet);
        void UseSkill(uint skillId, IUnit target);
    }
}
