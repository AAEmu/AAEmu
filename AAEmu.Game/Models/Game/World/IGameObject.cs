using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Models.Game.World
{
    public interface IGameObject
    {
        void BroadcastPacket(GamePacket sCOneUnitMovementPacket, bool self);
    }
}
