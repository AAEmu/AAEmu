using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Models.Game.Units
{
    public interface IUnit : IBaseUnit
    {
        byte Level { get; set; }
        BaseUnit CurrentTarget { get; }
        void SendPacket(GamePacket packet);
    }
}
